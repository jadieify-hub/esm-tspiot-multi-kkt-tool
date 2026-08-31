param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path $repoRoot (
    "src\EsmTspiot.Legacy.WinForms\bin\{0}\net48\EsmTspiot.Legacy.WinForms.exe" -f $Configuration)

if (-not (Test-Path -LiteralPath $appPath -PathType Leaf)) {
    throw "Legacy WinForms build not found: $appPath"
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$assembly = [System.Reflection.Assembly]::LoadFrom($appPath)
$formType = $assembly.GetType("EsmTspiot.WinForms.Shared.MainForm", $true)
$flags = [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic
$allInstanceFlags = $flags -bor [System.Reflection.BindingFlags]::Public
$allStaticFlags = [System.Reflection.BindingFlags]::Static -bor
    [System.Reflection.BindingFlags]::NonPublic -bor
    [System.Reflection.BindingFlags]::Public

function Get-PrivateFieldValue {
    param(
        [object]$Instance,
        [string]$Name
    )

    $field = $Instance.GetType().GetField($Name, $flags)
    if ($null -eq $field) {
        throw "Private field not found: $Name"
    }
    return $field.GetValue($Instance)
}

function Set-ObjectPropertyValue {
    param(
        [object]$Instance,
        [string]$Name,
        [object]$Value
    )

    $property = $Instance.GetType().GetProperty($Name, $allInstanceFlags)
    if ($null -eq $property) {
        throw "Property not found: $Name"
    }
    $property.SetValue($Instance, $Value, $null)
}

function Get-Utf8Text {
    param([string]$Base64)
    return [System.Text.Encoding]::UTF8.GetString(
        [Convert]::FromBase64String($Base64))
}

function Get-DescendantControls {
    param([System.Windows.Forms.Control]$Root)
    foreach ($child in $Root.Controls) {
        $child
        Get-DescendantControls -Root $child
    }
}

function Invoke-ClipboardWriteWithRetry {
    param(
        [scriptblock]$Operation,
        [string]$Description
    )

    $lastError = $null
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            & $Operation
            return
        }
        catch [System.Runtime.InteropServices.ExternalException] {
            $lastError = $_.Exception
            Start-Sleep -Milliseconds 50
        }
    }

    throw "$Description Clipboard remained unavailable: $($lastError.Message)"
}

$form = $null
$automaticParametersDialog = $null
$bindingDialog = $null
$removeAllDialog = $null
$kktDeletionDialog = $null
$primaryKktDeletionDialog = $null
$supportDevelopmentDialog = $null
$supportDialogCloseTimer = $null
try {
    $form = [Activator]::CreateInstance($formType)
    $page = Get-PrivateFieldValue -Instance $form -Name "_lmGatewayPage"
    $workspaceTabs = Get-PrivateFieldValue -Instance $form -Name "_workspaceTabs"
    $automationTab = Get-PrivateFieldValue -Instance $form -Name "_automationTab"
    $manualKktTab = Get-PrivateFieldValue -Instance $form -Name "_manualKktTab"
    $instancesTab = Get-PrivateFieldValue -Instance $form -Name "_instancesTab"
    $lmGatewayTab = Get-PrivateFieldValue -Instance $form -Name "_lmGatewayTab"
    $logTab = Get-PrivateFieldValue -Instance $form -Name "_logTab"

    $expectedTabOrder = @(
        $automationTab,
        $manualKktTab,
        $instancesTab,
        $lmGatewayTab,
        $logTab)
    if ($workspaceTabs.TabPages.Count -ne $expectedTabOrder.Count) {
        throw "The workspace must expose exactly five task tabs."
    }
    for ($tabIndex = 0; $tabIndex -lt $expectedTabOrder.Count; $tabIndex++) {
        if (-not [object]::ReferenceEquals(
            $workspaceTabs.TabPages[$tabIndex],
            $expectedTabOrder[$tabIndex])) {
            throw "Automatic setup must be first, followed by the uninterrupted manual workflow and the log."
        }
    }
    if (-not [object]::ReferenceEquals(
        $workspaceTabs.SelectedTab,
        $automationTab)) {
        throw "Automatic setup must be the default landing tab."
    }
    $expectedAutomationTitle = Get-Utf8Text(
        "0JDQstGC0L7QvNCw0YLQuNGH0LXRgdC60LDRjyDQvdCw0YHRgtGA0L7QudC60LA=")
    if ($automationTab.Text -ne $expectedAutomationTitle) {
        throw "The primary tab needs an action-oriented automatic-setup title."
    }
    $atolText = Get-Utf8Text("0JDQotCe0Js=")
    $bindingStem = Get-Utf8Text("0L/RgNC40LLRj9C3")
    $bindingFutureStem = Get-Utf8Text("0L/RgNC40LLRj9C2")
    $automaticHint = @(Get-DescendantControls -Root $automationTab |
        Where-Object {
            $_ -is [System.Windows.Forms.Label] -and
            $_.Text.Contains($atolText) -and
            ($_.Text.Contains($bindingStem) -or
                $_.Text.Contains($bindingFutureStem))
        })
    if ($automaticHint.Count -ne 1) {
        throw "The automatic page must visibly explain ATOL discovery and ESM binding."
    }

    $helpTitle = Get-Utf8Text("0KHQv9GA0LDQstC60LA=")
    $supportTitle = Get-Utf8Text(
        "0J/QvtC00LTQtdGA0LbQsNGC0Ywg0YDQsNC30YDQsNCx0L7RgtC60YM=")
    $helpMenu = @($form.MainMenuStrip.Items | Where-Object {
        $_ -is [System.Windows.Forms.ToolStripMenuItem] -and
        $_.Text -eq $helpTitle
    })
    if ($helpMenu.Count -ne 1) {
        throw "The main menu must contain one Help menu."
    }
    $supportMenuItem = @($helpMenu[0].DropDownItems | Where-Object {
        $_ -is [System.Windows.Forms.ToolStripMenuItem] -and
        $_.Text -eq $supportTitle
    })
    if ($supportMenuItem.Count -ne 1) {
        throw "Help must contain one Support Development command."
    }

    $supportDialogType = $assembly.GetType(
        "EsmTspiot.WinForms.Shared.SupportDevelopmentDialog", $true)
    $script:supportDialogObserved = $false
    $supportDialogCloseTimer = New-Object System.Windows.Forms.Timer
    $supportDialogCloseTimer.Interval = 25
    $supportDialogCloseTimer.add_Tick({
        foreach ($openForm in [System.Windows.Forms.Application]::OpenForms) {
            if ($openForm.GetType() -eq $supportDialogType) {
                $script:supportDialogObserved = $true
                $openForm.Close()
                $supportDialogCloseTimer.Stop()
                break
            }
        }
    })
    $supportDialogCloseTimer.Start()
    $supportMenuItem[0].PerformClick()
    $supportDialogCloseTimer.Stop()
    if (-not $script:supportDialogObserved) {
        throw "The Support Development menu command must open its dialog."
    }

    $supportDevelopmentDialog = [Activator]::CreateInstance(
        $supportDialogType)
    $supportDevelopmentDialog.CreateControl()
    $supportDevelopmentDialog.PerformLayout()
    foreach ($control in @(Get-DescendantControls $supportDevelopmentDialog)) {
        $control.PerformLayout()
    }
    $supportDevelopmentDialog.Show()
    [System.Windows.Forms.Application]::DoEvents()
    $supportControls = @(Get-DescendantControls $supportDevelopmentDialog)
    $supportPictures = @($supportControls | Where-Object {
        $_ -is [System.Windows.Forms.PictureBox]
    })
    if ($supportPictures.Count -ne 1 -or
        $null -eq $supportPictures[0].Image -or
        $supportPictures[0].Image.Width -ne 296 -or
        $supportPictures[0].Image.Height -ne 296 -or
        $supportPictures[0].Width -ne 296 -or
        $supportPictures[0].Height -ne 296) {
        throw "The support dialog must display the embedded 296px QR image at 1:1 scale."
    }
    $supportUrl = "https://pay.cloudtips.ru/p/53698013"
    if (@($supportControls | Where-Object {
            $_.Text -eq $supportUrl
        }).Count -ne 1) {
        throw "The support dialog must show the exact donation URL as text."
    }
    $copyLinkText = Get-Utf8Text(
        "0KHQutC+0L/QuNGA0L7QstCw0YLRjCDRgdGB0YvQu9C60YM=")
    $copyLinkButtons = @($supportControls | Where-Object {
            $_ -is [System.Windows.Forms.Button] -and
            $_.Text -eq $copyLinkText
        })
    if ($copyLinkButtons.Count -ne 1) {
        throw "The support dialog must expose one Copy Link button."
    }
    $clipboardBefore = [System.Windows.Forms.Clipboard]::GetDataObject()
    try {
        Invoke-ClipboardWriteWithRetry -Description "Before copy-link test." -Operation {
            [System.Windows.Forms.Clipboard]::Clear()
        }
        $copied = $false
        for ($attempt = 1; $attempt -le 10; $attempt++) {
            $copyLinkButtons[0].PerformClick()
            try {
                if ([System.Windows.Forms.Clipboard]::GetText() -eq $supportUrl) {
                    $copied = $true
                    break
                }
            }
            catch [System.Runtime.InteropServices.ExternalException] {
            }
            Start-Sleep -Milliseconds 50
        }
        if (-not $copied) {
            throw "Copy Link must place the exact donation URL on the clipboard."
        }
    }
    finally {
        if ($null -ne $clipboardBefore) {
            Invoke-ClipboardWriteWithRetry -Description "After copy-link test." -Operation {
                [System.Windows.Forms.Clipboard]::SetDataObject(
                    $clipboardBefore, $true)
            }
        }
        else {
            Invoke-ClipboardWriteWithRetry -Description "After copy-link test." -Operation {
                [System.Windows.Forms.Clipboard]::Clear()
            }
        }
    }
    $offlineNotice = Get-Utf8Text(
        "0J7RgtGB0LrQsNC90LjRgNGD0LnRgtC1INC60L7QtCDRgtC10LvQtdGE0L7QvdC+0LwuINCf0YDQvtCz0YDQsNC80LzQsCDQv9GA0Lgg0Y3RgtC+0Lwg0L3QuNC60YPQtNCwINC90LUg0L7QsdGA0LDRidCw0LXRgtGB0Y8g0Lgg0L3QuNGH0LXQs9C+INC90LUg0L/QtdGA0LXQtNCw0ZHRgi4=")
    if (@($supportControls | Where-Object {
            $_.Text -eq $offlineNotice
        }).Count -ne 1) {
        throw "The support dialog must state that the program sends nothing."
    }
    $closeText = Get-Utf8Text("0JfQsNC60YDRi9GC0Yw=")
    if ($supportDevelopmentDialog.FormBorderStyle -ne
            [System.Windows.Forms.FormBorderStyle]::FixedDialog -or
        $null -eq $supportDevelopmentDialog.AcceptButton -or
        $supportDevelopmentDialog.AcceptButton.Text -ne $closeText -or
        $null -eq $supportDevelopmentDialog.CancelButton -or
        $supportDevelopmentDialog.CancelButton.Text -ne $closeText) {
        throw "The support dialog must be fixed and close by Enter or Escape."
    }

    $completionMessageMethod = $formType.GetMethod(
        "BuildAutomaticSetupCompletionMessage", $allStaticFlags)
    if ($null -eq $completionMessageMethod) {
        throw "Automatic setup needs one testable completion-message formatter."
    }
    $outcomeType = $completionMessageMethod.GetParameters()[0].ParameterType
    $emptyOutcome = [Activator]::CreateInstance($outcomeType)
    $donationLine = Get-Utf8Text(
        "0J/RgNC+0LPRgNCw0LzQvNCwINC/0L7QvNC+0LPQu9CwPyDQn9C+0LTQtNC10YDQttCw0YLRjCDRgNCw0LfRgNCw0LHQvtGC0LrRgzog0LzQtdC90Y4g0KHQv9GA0LDQstC60LAu")
    $fullSuccessMessage = $completionMessageMethod.Invoke(
        $null, @($emptyOutcome, "stack", $true, $false))
    $partialMessage = $completionMessageMethod.Invoke(
        $null, @($emptyOutcome, "stack", $true, $true))
    $failedMessage = $completionMessageMethod.Invoke(
        $null, @($emptyOutcome, "stack", $false, $false))
    if (-not $fullSuccessMessage.EndsWith($donationLine) -or
        $partialMessage.Contains($donationLine) -or
        $failedMessage.Contains($donationLine)) {
        throw "The support hint must be the final line of full-success results only."
    }

    # Test the responsive layout directly without displaying a window or starting discovery.
    $page.Dock = [System.Windows.Forms.DockStyle]::None
    $page.Size = [System.Drawing.Size]::new(1450, 512)
    $page.CreateControl()
    $page.PerformLayout()
    foreach ($control in $page.Controls) {
        $control.PerformLayout()
    }
    [System.Windows.Forms.Application]::DoEvents()

    $pathBox = Get-PrivateFieldValue -Instance $page -Name "_installerPathTextBox"
    $selectButton = Get-PrivateFieldValue -Instance $page -Name "_selectInstallerButton"
    $localModulePathBox = Get-PrivateFieldValue -Instance $page -Name "_localModuleInstallerPathTextBox"
    $selectLocalModuleButton = Get-PrivateFieldValue -Instance $page -Name "_selectLocalModuleInstallerButton"
    $installButton = Get-PrivateFieldValue -Instance $page -Name "_installControllerButton"
    $bindButton = Get-PrivateFieldValue -Instance $page -Name "_bindButton"
    $removeAllButton = Get-PrivateFieldValue -Instance $page -Name "_removeAllServicesButton"
    $grid = Get-PrivateFieldValue -Instance $page -Name "_grid"
    $automaticInstallerButton = Get-PrivateFieldValue -Instance $form -Name "_automaticSelectInstallerButton"
    $automaticInstallerPath = Get-PrivateFieldValue -Instance $form -Name "_automaticInstallerTextBox"
    $automaticSetupButton = Get-PrivateFieldValue -Instance $form -Name "_bulkRegisterButton"
    $automaticStopButton = Get-PrivateFieldValue -Instance $form -Name "_automaticStopButton"
    $automaticTab = Get-PrivateFieldValue -Instance $form -Name "_automationTab"
    $baseUrlBox = Get-PrivateFieldValue -Instance $form -Name "_baseUrlTextBox"
    $dkktPortBox = Get-PrivateFieldValue -Instance $form -Name "_dkktPortTextBox"
    $logTextBox = Get-PrivateFieldValue -Instance $form -Name "_logTextBox"
    $automaticStatus = Get-PrivateFieldValue -Instance $form -Name "_automationStatusLabel"
    $officialControllerStatus = Get-PrivateFieldValue -Instance $page -Name "_officialControllerStatusLabel"
    $manualInstancesGrid = Get-PrivateFieldValue -Instance $form -Name "_manualInstancesGrid"
    $instancesGrid = Get-PrivateFieldValue -Instance $form -Name "_instancesGrid"
    $nextPortPairLabel = Get-PrivateFieldValue -Instance $form -Name "_nextPortPairLabel"
    $registerButton = Get-PrivateFieldValue -Instance $form -Name "_registerButton"

    if ($dkktPortBox.Text -ne "4042") {
        throw "The main form must default dkktPort to the ESM orchestrator port 4042; actual: '$($dkktPortBox.Text)'."
    }
    $expectedManualPortLabels = @(
        (Get-Utf8Text("0J/QvtGA0YIg0YHQu9GD0LbQsdGLIChwb3J0KQ==")),
        (Get-Utf8Text(
            "0J/QvtGA0YIg0LrQsNGB0YHQvtCy0L7Qs9C+INCf0J4gKHNvZnRQb3J0KQ==")))
    $manualLabelTexts = @(Get-DescendantControls -Root $manualKktTab |
        Where-Object { $_ -is [System.Windows.Forms.Label] } |
        ForEach-Object { $_.Text })
    foreach ($expectedManualPortLabel in $expectedManualPortLabels) {
        if ($expectedManualPortLabel -notin $manualLabelTexts) {
            throw "The manual form must name both ports in operator and API terms."
        }
    }
    $manualNextStepField = $formType.GetField(
        "_manualNextStepLabel",
        $flags)
    if ($null -eq $manualNextStepField) {
        throw "The manual three-step flow needs a visible bridge to LM setup."
    }
    $manualNextStep = $manualNextStepField.GetValue($form)
    $expectedManualNextStep = Get-Utf8Text(
        "0J/QvtGB0LvQtSDRiNCw0LPQsCAzINC/0LXRgNC10LnQtNC40YLQtSDQvdCwINCy0LrQu9Cw0LTQutGDIMKr0JvQnCDQp9CXwrsg4oCUINGC0LDQvCDRgdC+0LfQtNCw0Y7RgtGB0Y8g0LrQvtC90YLRgNC+0LvQu9C10YAg0Lgg0LvQvtC60LDQu9GM0L3Ri9C5INC80L7QtNGD0LvRjC4=")
    if ($manualNextStep.Text -ne $expectedManualNextStep) {
        throw "The manual flow does not explain the next LM setup step."
    }
    $expectedUncheckedPortText = Get-Utf8Text(
        "0J/QvtGA0YLRiyDQsiDRhNC+0YDQvNC1IOKAlCDQv9C+INGD0LzQvtC70YfQsNC90LjRjjsg0L7QsdC90L7QstC40YLQtSDRgdC/0LjRgdC+0Log0LTQu9GPINC/0YDQvtCy0LXRgNC60Lgg0LfQsNC90Y/RgtC+0YHRgtC4Lg==")
    if ($nextPortPairLabel.Text -ne $expectedUncheckedPortText) {
        throw "Default manual ports must be labelled as unchecked defaults."
    }
    $expectedServicePortHeader = Get-Utf8Text(
        "0J/QvtGA0YIg0YHQu9GD0LbQsdGL")
    $expectedCashSoftwarePortHeader = Get-Utf8Text(
        "0J/QvtGA0YIg0J/Qng==")
    foreach ($instancesDisplayGrid in @($manualInstancesGrid, $instancesGrid)) {
        if ($instancesDisplayGrid.Columns[2].HeaderText -ne
                $expectedServicePortHeader -or
            $instancesDisplayGrid.Columns[3].HeaderText -ne
                $expectedCashSoftwarePortHeader) {
            throw "KKT tables must use consistent operator-facing port names."
        }
    }
    $manualKktTab.Size = [System.Drawing.Size]::new(748, 512)
    $manualRoot = $manualKktTab.Controls[0]
    $manualRoot.Size = $manualKktTab.ClientSize
    $manualRoot.PerformLayout()
    foreach ($manualControl in $manualRoot.Controls) {
        $manualControl.PerformLayout()
    }
    [System.Windows.Forms.Application]::DoEvents()
    if ($manualRoot.DisplayRectangle.Width -gt $manualRoot.ClientSize.Width -or
        $registerButton.Right -gt $manualRoot.ClientSize.Width) {
        throw "The manual workflow requires horizontal scrolling at the normal window width."
    }
    if (-not $logTextBox.ReadOnly) {
        throw "The visible execution log must be read-only."
    }
    $expectedAutomaticStatus = Get-Utf8Text(
        "0KHRgtCw0YLRg9GBOiDQvdGD0LbQvdGLINC+0LHQsCDQv9Cw0LrQtdGC0LA=")
    if ($automaticStatus.Text -ne $expectedAutomaticStatus) {
        throw "Automatic mode must initially state that both official packages are required."
    }
    $unusedByManagedText = Get-Utf8Text(
        "0L3QtSDQuNGB0L/QvtC70YzQt9GD0LXRgtGB0Y8=")
    if (-not $officialControllerStatus.Text.Contains($unusedByManagedText)) {
        throw "The base controller status must explain that managed kits do not reuse it."
    }

    $expectedPackageLabels = @(
        (Get-Utf8Text(
            "0KPRgdGC0LDQvdC+0LLRidC40Log0LrQvtC90YLRgNC+0LvQu9C10YDQsCDQm9CcINCn0Jc=")),
        (Get-Utf8Text(
            "TVNJINC70L7QutCw0LvRjNC90L7Qs9C+INC80L7QtNGD0LvRjyDQp9CX")),
        (Get-Utf8Text(
            "0JrQvtC80L/Qu9C10LrRgiA9INC60L7QvdGC0YDQvtC70LvQtdGAINCa0JrQoiArINCb0Jwg0KfQlyDQtNC70Y8g0LXRkSDQmNCd0J07INCa0JrQoiDQvtC00L3QvtCz0L4g0JjQndCdINC40YHQv9C+0LvRjNC30YPRjtGCINC+0LHRidC40Lkg0JvQnC4=")))
    $lmPageLabelTexts = @(Get-DescendantControls -Root $page |
        Where-Object { $_ -is [System.Windows.Forms.Label] } |
        ForEach-Object { $_.Text })
    foreach ($expectedPackageLabel in $expectedPackageLabels) {
        if ($expectedPackageLabel -notin $lmPageLabelTexts) {
            throw "The LM package panel is missing a permanent operator label."
        }
    }
    $setupHintField = $page.GetType().GetField(
        "_setupActionHintLabel",
        $flags)
    if ($null -eq $setupHintField) {
        throw "The LM package action needs a visible availability reason."
    }
    $setupActionHint = $setupHintField.GetValue($page)
    if ([string]::IsNullOrWhiteSpace($setupActionHint.Text)) {
        throw "The LM package action availability reason is empty."
    }
    $selectionHintField = $page.GetType().GetField(
        "_selectionActionHintLabel",
        $flags)
    if ($null -eq $selectionHintField) {
        throw "Disabled row actions need a visible availability reason."
    }
    $selectionActionHint = $selectionHintField.GetValue($page)
    if ([string]::IsNullOrWhiteSpace($selectionActionHint.Text)) {
        throw "The row-action availability reason is empty."
    }

    if ($grid.Columns.Count -lt 1 -or $grid.Columns[0].Name -ne "KktOrdinal") {
        throw "The LM KKT table must start with the stable KKT ordinal."
    }
    $expectedNames = @(
        "KktOrdinal",
        "KktSerial",
        "KktInn",
        "KktSoftwarePort",
        "LmEndpoint",
        "LmState",
        "EsmLinkState")
    if ($grid.Columns.Count -ne $expectedNames.Count) {
        throw "The operator-facing LM table must contain only $($expectedNames.Count) working columns; actual: $($grid.Columns.Count)."
    }
    for ($index = 0; $index -lt $expectedNames.Count; $index++) {
        if ($grid.Columns[$index].Name -ne $expectedNames[$index]) {
            throw "Unexpected LM table column $index`: '$($grid.Columns[$index].Name)'."
        }
    }
    if ($null -eq $grid.Columns["LmEndpoint"]) {
        throw "The LM KKT table must show the LM CHZ address and port."
    }
    $expectedSoftwarePortHeader = Get-Utf8Text("0J/QvtGA0YIg0J/Qng==")
    $softwarePortColumn = $grid.Columns["KktSoftwarePort"]
    if ($softwarePortColumn.HeaderText -ne $expectedSoftwarePortHeader -or
        [string]::IsNullOrWhiteSpace($softwarePortColumn.ToolTipText)) {
        throw "The software-port column needs a short header and an explanatory tooltip."
    }

    $page.GetType().GetField("_running", $flags).SetValue($page, $true)
    $page.GetType().GetMethod("UpdateActionState", $flags).Invoke(
        $page, $null) | Out-Null
    if (-not $grid.Enabled) {
        throw "The read-only LM table must remain scrollable while an operation is running."
    }
    $page.GetType().GetField("_running", $flags).SetValue($page, $false)
    $page.GetType().GetMethod("UpdateActionState", $flags).Invoke(
        $page, $null) | Out-Null

    # A managed stack can outlive its ESM row after an interrupted or partial
    # cleanup. It must remain visible and individually removable.
    $snapshotType = $assembly.GetType(
        "EsmTspiot.WinForms.Shared.ManagedLocalModuleInventorySnapshot",
        $true)
    $managedItemType = $assembly.GetType(
        "EsmTspiot.WinForms.Shared.ManagedLocalModuleInventoryItem",
        $true)
    $orphanSnapshot = [Activator]::CreateInstance($snapshotType, $true)
    $orphanItem = [Activator]::CreateInstance($managedItemType, $true)
    $orphanSerial = "00105700009999"
    Set-ObjectPropertyValue $orphanItem "KktSerial" $orphanSerial
    Set-ObjectPropertyValue $orphanItem "Inn" "1234567894"
    Set-ObjectPropertyValue $orphanItem "KktOrdinal" 9
    Set-ObjectPropertyValue $orphanItem "SoftwarePort" 51409
    Set-ObjectPropertyValue $orphanItem "Endpoint" "127.0.0.1:5995"
    Set-ObjectPropertyValue $orphanItem "State" "Running"

    $fingerprintProperty = $managedItemType.GetProperty(
        "ManagedStateFingerprint", $allInstanceFlags)
    $fingerprint = [Activator]::CreateInstance($fingerprintProperty.PropertyType)
    Set-ObjectPropertyValue $fingerprint "Sha256" ("A" * 64)
    Set-ObjectPropertyValue $orphanItem "ManagedStateFingerprint" $fingerprint

    $assignmentProperty = $managedItemType.GetProperty(
        "KktAssignment", $allInstanceFlags)
    $assignment = [Activator]::CreateInstance($assignmentProperty.PropertyType)
    Set-ObjectPropertyValue $assignment "KktSerial" $orphanSerial
    Set-ObjectPropertyValue $assignment "KktInn" "1234567894"
    Set-ObjectPropertyValue $assignment "KktOrdinal" 9
    Set-ObjectPropertyValue $assignment "GrpcPort" 55009
    Set-ObjectPropertyValue $assignment "RestPort" 15009
    Set-ObjectPropertyValue $orphanItem "KktAssignment" $assignment

    $orphanSnapshot.GetType().GetProperty(
        "Items", $allInstanceFlags).GetValue(
            $orphanSnapshot, $null).Add($orphanItem)
    $page.GetType().GetField(
        "_managedLocalModuleInventory", $flags).SetValue(
            $page, $orphanSnapshot)
    $page.GetType().GetField(
        "_serviceInventory", $flags).GetValue($page).Clear()
    $page.GetType().GetMethod("FillRows", $flags).Invoke(
        $page, [object[]]@("")) | Out-Null

    $expectedMissingEsmText = -join @(
        [char]0x041D, [char]0x0435, [char]0x0442, [char]0x0020,
        [char]0x0432, [char]0x0020, [char]0x0415, [char]0x0421,
        [char]0x041C)
    if ($grid.Rows.Count -ne 1 -or
        $grid.Rows[0].Cells["KktSerial"].Value -ne $orphanSerial -or
        $grid.Rows[0].Cells["EsmLinkState"].Value -ne $expectedMissingEsmText) {
        throw "A managed stack missing from ESM must stay visible with a clear missing-ESM state."
    }
    $orphanContext = $grid.Rows[0].Tag
    if ($null -ne $orphanContext.GetType().GetProperty(
            "SessionRow", $allInstanceFlags).GetValue($orphanContext, $null) -or
        $null -eq $orphanContext.GetType().GetProperty(
            "Inventory", $allInstanceFlags).GetValue($orphanContext, $null)) {
        throw "A managed stack missing from ESM must remain individually removable."
    }
    $orphanInnProperty = $orphanContext.GetType().GetProperty(
        "KktInn", $allInstanceFlags)
    if ($null -eq $orphanInnProperty -or
        $orphanInnProperty.GetValue($orphanContext, $null) -ne "1234567894") {
        throw "An orphaned managed stack must retain its INN for guarded removal."
    }

    $page.GetType().GetField(
        "_helperAvailable", $flags).SetValue($page, $false)
    $page.GetType().GetField(
        "_helperUnavailableReason", $flags).SetValue(
            $page, "helper unavailable")
    $page.GetType().GetMethod("UpdateActionState", $flags).Invoke(
        $page, $null) | Out-Null
    $expectedOrphanRemovalUnavailable = Get-Utf8Text(
        "0JrQmtCiINC+0YLRgdGD0YLRgdGC0LLRg9C10YIg0LIg0JXQodCcOyDRg9C00LDQu9C10L3QuNC1INC90LXQtNC+0YHRgtGD0L/QvdC+")
    if (-not $selectionActionHint.Text.Contains(
            $expectedOrphanRemovalUnavailable)) {
        throw "A selected orphaned kit must visibly explain why removal is unavailable."
    }

    $emptySnapshot = [Activator]::CreateInstance($snapshotType, $true)
    $page.GetType().GetField(
        "_managedLocalModuleInventory", $flags).SetValue(
            $page, $emptySnapshot)

    $session = Get-PrivateFieldValue -Instance $page -Name "_session"
    $session.Rows.Clear()
    $sessionRowType = $session.Rows.GetType().GetGenericArguments()[0]
    $kktType = $sessionRowType.GetProperty(
        "Kkt", $allInstanceFlags).PropertyType
    $sessionRow = [Activator]::CreateInstance($sessionRowType)
    $sessionKkt = [Activator]::CreateInstance($kktType)
    $sessionSerial = "00105700008888"
    Set-ObjectPropertyValue $sessionKkt "KktSerial" $sessionSerial
    Set-ObjectPropertyValue $sessionKkt "KktInn" "1234567894"
    Set-ObjectPropertyValue $sessionKkt "SoftPort" "51408"
    Set-ObjectPropertyValue $sessionRow "Kkt" $sessionKkt
    $session.Rows.Add($sessionRow)
    $page.GetType().GetMethod("FillRows", $flags).Invoke(
        $page, [object[]]@($sessionSerial)) | Out-Null

    $expectedUncreatedKitHint = Get-Utf8Text(
        "0JrQvtC80L/Qu9C10LrRgiDRjdGC0L7QuSDQmtCa0KIg0LXRidGRINC90LUg0YHQvtC30LTQsNC9Lg==")
    if ($selectionActionHint.Text -ne $expectedUncreatedKitHint) {
        throw "A selected registered KKT without a kit must explain the next action."
    }

    $serviceInventory = $page.GetType().GetField(
        "_serviceInventory", $flags).GetValue($page)
    $inventoryType = $serviceInventory.GetType().GetGenericArguments()[0]
    $inventoryItem = [Activator]::CreateInstance($inventoryType)
    Set-ObjectPropertyValue $inventoryItem "KktSerial" $sessionSerial
    $roleProperty = $inventoryType.GetProperty("Role", $allInstanceFlags)
    Set-ObjectPropertyValue $inventoryItem "Role" (
        [Enum]::Parse($roleProperty.PropertyType, "Managed"))
    Set-ObjectPropertyValue $inventoryItem "IsRunning" $true
    Set-ObjectPropertyValue $inventoryItem "IsReady" $true
    $serviceInventory.Add($inventoryItem)
    $page.GetType().GetMethod("FillRows", $flags).Invoke(
        $page, [object[]]@($sessionSerial)) | Out-Null

    $expectedReadyRemovalUnavailable = Get-Utf8Text(
        "0JrQvtC80L/Qu9C10LrRgiDQs9C+0YLQvtCyOiDQv9GA0LjQstGP0LfQutCwINC6INCV0KHQnCDQtNC+0YHRgtGD0L/QvdCwOyDRg9C00LDQu9C10L3QuNC1INC90LXQtNC+0YHRgtGD0L/QvdC+")
    if (-not $selectionActionHint.Text.Contains(
            $expectedReadyRemovalUnavailable)) {
        throw "A ready kit must not promise removal when the helper is unavailable."
    }

    $session.Rows.Clear()
    $serviceInventory.Clear()
    $page.GetType().GetMethod("FillRows", $flags).Invoke(
        $page, [object[]]@("")) | Out-Null

    $expectedBindText = -join @(
        [char]0x041F, [char]0x0440, [char]0x0438, [char]0x0432,
        [char]0x044F, [char]0x0437, [char]0x0430, [char]0x0442,
        [char]0x044C, [char]0x0020, [char]0x043A, [char]0x0020,
        [char]0x0415, [char]0x0421, [char]0x041C)
    if ($bindButton.Text -ne $expectedBindText -or
        $bindButton.Tag -ne "BindSelectedEsm") {
        throw "The LM page must expose one explicit selected-row ESM binding action."
    }
    foreach ($removedField in @(
        "_addressTextBox",
        "_portTextBox",
        "_restPortTextBox",
        "_loginTextBox",
        "_passwordTextBox",
        "_targetAddressTextBox",
        "_targetPortTextBox",
        "_saveDraftButton",
        "_editorGroup")) {
        if ($null -ne $page.GetType().GetField($removedField, $flags)) {
            throw "Obsolete internal or credential field remains on the operator page: $removedField."
        }
    }

    $bindingDialogType = $assembly.GetType(
        "EsmTspiot.WinForms.Shared.LmGatewayBindingDialog",
        $true)
    $credentialDefaultsType = $assembly.GetType(
        "EsmTspiot.WinForms.Shared.LmGatewayCredentialDefaults",
        $true)
    $credentialFactory = $credentialDefaultsType.GetMethod(
        "Create",
        [System.Reflection.BindingFlags]::Static -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $firstDefaults = $credentialFactory.Invoke($null, $null)
    $secondDefaults = $credentialFactory.Invoke($null, $null)
    if ($firstDefaults.Login -ne "admin" -or
        $firstDefaults.Password -ne "admin" -or
        [object]::ReferenceEquals($firstDefaults, $secondDefaults)) {
        throw "Every ESM binding must receive a fresh admin/admin credential object."
    }
    $firstDefaults.Login = ""
    $firstDefaults.Password = ""
    $secondDefaults.Login = ""
    $secondDefaults.Password = ""
    $bindingDialogConstructor = $bindingDialogType.GetConstructors(
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)[0]
    $bindingArguments = New-Object object[] 3
    $bindingArguments[0] = "00105700000001"
    $bindingArguments[1] = "1234567894"
    $bindingArguments[2] = "127.0.0.1:45001"
    $bindingDialog = $bindingDialogConstructor.Invoke($bindingArguments)
    $bindingLogin = Get-PrivateFieldValue -Instance $bindingDialog -Name "_loginTextBox"
    $bindingPassword = Get-PrivateFieldValue -Instance $bindingDialog -Name "_passwordTextBox"
    $bindingConfirm = Get-PrivateFieldValue -Instance $bindingDialog -Name "_confirmButton"
    if (-not $bindingPassword.UseSystemPasswordChar) {
        throw "The one-time ESM binding password must be masked."
    }
    if ($bindingLogin.Text -ne "admin" -or
        $bindingPassword.Text -ne "admin") {
        throw "The one-time ESM binding dialog must prefill the deployed admin/admin credentials."
    }
    if (-not $bindingConfirm.Enabled) {
        throw "The prefilled ESM binding action must be ready without repetitive operator input."
    }
    $bindingLogin.Text = "operator"
    $bindingPassword.Text = "temporary-secret"
    if (-not $bindingConfirm.Enabled) {
        throw "The ESM binding action must accept complete one-time credentials."
    }
    $credentialsProperty = $bindingDialogType.GetProperty(
        "Credentials",
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)
    $credentials = $credentialsProperty.GetValue($bindingDialog, $null)
    if ($credentials.Login -ne "operator" -or
        $credentials.Password -ne "temporary-secret") {
        throw "The ESM binding dialog did not return the entered one-time credentials."
    }

    $sharedAssembly = [System.Reflection.Assembly]::LoadFrom(
        (Join-Path (Split-Path -Parent $appPath) "EsmTspiot.Shared.dll"))

    $instanceType = $sharedAssembly.GetType(
        "EsmTspiot.Shared.Models.KktInstanceInfo",
        $true)
    $instance = [Activator]::CreateInstance($instanceType)
    $instance.Id = "00105700000001"
    $instance.Port = "50401"
    $instance.SoftPort = "51401"
    $instance.ServiceState = "Running"
    $deletionDialogType = $assembly.GetType(
        "EsmTspiot.WinForms.Shared.KktDeletionConfirmationDialog",
        $true)
    $deletionConstructor = $deletionDialogType.GetConstructor(@($instanceType))
    $kktDeletionDialog = $deletionConstructor.Invoke(@($instance))
    $kktDeletionDialog.CreateControl()
    $kktDeletionDialog.PerformLayout()
    foreach ($control in @(Get-DescendantControls $kktDeletionDialog)) {
        $control.PerformLayout()
    }
    [System.Windows.Forms.Application]::DoEvents()
    $deleteButton = Get-PrivateFieldValue -Instance $kktDeletionDialog -Name "_deleteButton"
    $cancelDeleteButton = Get-PrivateFieldValue -Instance $kktDeletionDialog -Name "_cancelButton"
    if ($cancelDeleteButton.Left -le $deleteButton.Left) {
        throw "The destructive KKT-delete action must be left of Cancel in the RTL button row."
    }
    $expectedRunningState = $sharedAssembly.GetType(
        "EsmTspiot.Shared.Services.KktServiceStateFormatter",
        $true).GetMethod("ToDisplayText").Invoke($null, @("Running"))
    $deletionText = (@(Get-DescendantControls $kktDeletionDialog) |
        Where-Object { $_ -is [System.Windows.Forms.Label] } |
        ForEach-Object { $_.Text }) -join "`n"
    if ($deletionText.Contains("Running") -or
        -not $deletionText.Contains($expectedRunningState)) {
        throw "The KKT deletion dialog must localize the service state."
    }

    $primaryDeletionConstructor = $deletionDialogType.GetConstructor(
        [Type[]]@($instanceType, [bool]))
    $primaryKktDeletionDialog = $primaryDeletionConstructor.Invoke(@($instance, $true))
    $primaryConfirmation = Get-PrivateFieldValue -Instance $primaryKktDeletionDialog -Name "_confirmationTextBox"
    $primaryDeleteButton = Get-PrivateFieldValue -Instance $primaryKktDeletionDialog -Name "_deleteButton"
    $primaryDeletionText = (@(Get-DescendantControls $primaryKktDeletionDialog) |
        Where-Object { $_ -is [System.Windows.Forms.Label] } |
        ForEach-Object { $_.Text }) -join "`n"
    if ($primaryConfirmation.MaxLength -ne 14 -or
        -not $primaryDeletionText.Contains("14")) {
        throw "Primary KKT deletion must visibly require the full 14-digit serial."
    }
    $primaryConfirmation.Text = "0001"
    if ($primaryDeleteButton.Enabled) {
        throw "A four-digit suffix must not authorize primary KKT deletion."
    }
    $primaryConfirmation.Text = "00105700000001"
    if (-not $primaryDeleteButton.Enabled) {
        throw "The exact primary KKT serial must enable the deletion action."
    }

    $rowType = $assembly.GetType(
        "EsmTspiot.WinForms.Shared.LmAutomaticSetupDialogRow",
        $true)
    $genericListType = [System.Collections.Generic.List``1].MakeGenericType($rowType)
    $rows = [Activator]::CreateInstance($genericListType)
    $row = [Activator]::CreateInstance($rowType, $true)
    $rowType.GetProperty("Ordinal").SetValue($row, 1, $null)
    $rowType.GetProperty("KktSerial").SetValue($row, "00105700000001", $null)
    $rowType.GetProperty("KktInn").SetValue($row, "1234567894", $null)
    $rowType.GetProperty("SoftwarePort").SetValue($row, "51401", $null)
    $rowType.GetProperty("TargetAddress").SetValue($row, "127.0.0.1", $null)
    $rowType.GetProperty("TargetPort").SetValue($row, "5995", $null)
    $rows.Add($row)
    $dialogType = $assembly.GetType(
        "EsmTspiot.WinForms.Shared.LmAutomaticSetupDialog",
        $true)
    $constructor = $dialogType.GetConstructors(
        [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic)[0]
    $constructorArguments = New-Object object[] 1
    $constructorArguments[0] = $rows
    $automaticParametersDialog = $constructor.Invoke($constructorArguments)
    $parametersGrid = Get-PrivateFieldValue -Instance $automaticParametersDialog -Name "_grid"
    $continueButton = Get-PrivateFieldValue -Instance $automaticParametersDialog -Name "_continueButton"
    $expectedParameterNames = @(
        "KktOrdinal",
        "KktSerial",
        "KktInn",
        "KktSoftwarePort",
        "LmTargetAddress",
        "LmTargetPort",
        "Validation")
    if ($parametersGrid.Columns.Count -ne $expectedParameterNames.Count) {
        throw "The automatic-setup dialog must contain only operator-facing fields."
    }
    for ($index = 0; $index -lt $expectedParameterNames.Count; $index++) {
        if ($parametersGrid.Columns[$index].Name -ne $expectedParameterNames[$index]) {
            throw "Unexpected automatic-setup column $index`: '$($parametersGrid.Columns[$index].Name)'."
        }
    }
    foreach ($name in @("KktOrdinal", "KktSerial", "KktInn", "KktSoftwarePort", "LmTargetAddress", "Validation")) {
        if (-not $parametersGrid.Columns[$name].ReadOnly) {
            throw "Automatic-setup identity/status column must be read-only: $name."
        }
    }
    foreach ($name in @("LmTargetPort")) {
        if ($parametersGrid.Columns[$name].ReadOnly) {
            throw "Automatic-setup operator field must be editable: $name."
        }
    }
    if ($parametersGrid.Columns["KktSoftwarePort"].HeaderText -ne
            $expectedSoftwarePortHeader -or
        [string]::IsNullOrWhiteSpace(
            $parametersGrid.Columns["KktSoftwarePort"].ToolTipText)) {
        throw "The automatic dialog needs the same concise software-port heading and hint."
    }

    # The pre-UAC batch recheck must reject a change in either displayed
    # fingerprint, even when the other fingerprint still matches.
    $fingerprintType = $sharedAssembly.GetType(
        "EsmTspiot.Shared.Models.LmManifestFingerprint",
        $true)
    $inventoryItemType = $sharedAssembly.GetType(
        "EsmTspiot.Shared.Models.LmServiceInventoryItem",
        $true)
    $confirmationType = $sharedAssembly.GetType(
        "EsmTspiot.Shared.Models.LmRemovalConfirmation",
        $true)
    $portsType = $sharedAssembly.GetType(
        "EsmTspiot.Shared.Models.LmGatewayPorts",
        $true)
    $roleType = $sharedAssembly.GetType(
        "EsmTspiot.Shared.Models.LmServiceRole",
        $true)
    $legacyFingerprint = [Activator]::CreateInstance($fingerprintType)
    $legacyFingerprint.Sha256 = ("a" * 64)
    $displayedManagedFingerprint = [Activator]::CreateInstance($fingerprintType)
    $displayedManagedFingerprint.Sha256 = ("b" * 64)
    $changedManagedFingerprint = [Activator]::CreateInstance($fingerprintType)
    $changedManagedFingerprint.Sha256 = ("c" * 64)
    $portsConstructor = $portsType.GetConstructor(@([int], [int]))
    $ports = $portsConstructor.Invoke(@([int]45001, [int]15001))

    $inventoryItem = [Activator]::CreateInstance($inventoryItemType)
    $inventoryItem.KktSerial = "00105700000001"
    $inventoryItem.ServiceName = "krs-esm-lm-00105700000001"
    $inventoryItem.Role = [Enum]::Parse($roleType, "Managed")
    $inventoryItem.Ports = $ports
    $inventoryItem.ManifestFingerprint = $legacyFingerprint
    $inventoryItem.ManagedStateFingerprint = $changedManagedFingerprint
    $inventoryListType = [System.Collections.Generic.List``1].MakeGenericType(
        $inventoryItemType)
    $inventoryList = [Activator]::CreateInstance($inventoryListType)
    $inventoryList.Add($inventoryItem)

    $removeAllType = $assembly.GetType(
        "EsmTspiot.WinForms.Shared.LmGatewayRemoveAllDialog",
        $true)
    $removeAllConstructor = $removeAllType.GetConstructors(
        [System.Reflection.BindingFlags]::Instance -bor
        [System.Reflection.BindingFlags]::NonPublic)[0]
    $removeAllArguments = New-Object object[] 1
    $removeAllArguments[0] = $inventoryList
    $removeAllDialog = $removeAllConstructor.Invoke($removeAllArguments)
    $serviceList = @(Get-DescendantControls $removeAllDialog) |
        Where-Object {
            $_ -is [System.Windows.Forms.TextBox] -and
            $_.Multiline -and $_.ReadOnly
        } | Select-Object -First 1
    if ($null -eq $serviceList -or $serviceList.TabStop) {
        throw "The read-only remove-all service list must not steal keyboard focus."
    }

    $confirmation = [Activator]::CreateInstance($confirmationType)
    $confirmation.KktSerial = $inventoryItem.KktSerial
    $confirmation.GrpcPort = 45001
    $confirmation.RestPort = 15001
    $confirmation.ManifestFingerprint = $legacyFingerprint
    $confirmation.ManagedStateFingerprint = $displayedManagedFingerprint
    $confirmation.RetainedEsmWarningAccepted = $true
    $confirmationListType = [System.Collections.Generic.List``1].MakeGenericType(
        $confirmationType)
    $confirmationList = [Activator]::CreateInstance($confirmationListType)
    $confirmationList.Add($confirmation)

    $staticFlags = [System.Reflection.BindingFlags]::Static -bor
        [System.Reflection.BindingFlags]::NonPublic
    $lmStateFormatter = $page.GetType().GetMethod(
        "GetLmStateText", $staticFlags)
    $emptyLmStateArguments = New-Object object[] 2
    $controllerOnlyArguments = New-Object object[] 2
    $controllerOnlyArguments[0] = $inventoryItem
    $emptyLmState = $lmStateFormatter.Invoke($null, $emptyLmStateArguments)
    $controllerOnlyState = $lmStateFormatter.Invoke(
        $null, $controllerOnlyArguments)
    if ($emptyLmState -ne $controllerOnlyState) {
        throw "The LM state must use one unambiguous not-created label."
    }
    $esmStateFormatter = $page.GetType().GetMethod(
        "GetEsmLinkStateText", $staticFlags)
    $expectedUnboundState = Get-Utf8Text(
        "0J3QtSDQv9GA0LjQstGP0LfQsNC90LA=")
    $unboundStateArguments = New-Object object[] 1
    if ($esmStateFormatter.Invoke($null, $unboundStateArguments) -ne
            $expectedUnboundState) {
        throw "A KKT without binding evidence must be labelled as not bound."
    }
    $batchMatcher = $page.GetType().GetMethod(
        "RemovalBatchStillMatches",
        $staticFlags)
    if ($null -eq $batchMatcher) {
        throw "Pre-UAC removal-batch matcher was not found."
    }
    $matcherArguments = New-Object object[] 3
    $matcherArguments[0] = $confirmationList
    $matcherArguments[1] = $inventoryList
    $matcherArguments[2] = $null
    if ([bool]$batchMatcher.Invoke($null, $matcherArguments)) {
        throw "The pre-UAC batch recheck accepted a changed managed-state fingerprint."
    }

    # The object-copy helper must receive the already observed confirmation
    # explicitly; a generic clone may not silently turn false into true.
    $localModuleSelectionType = $sharedAssembly.GetType(
        "EsmTspiot.Shared.Models.LocalModuleInstallerSelection",
        $true)
    $unconfirmedSelection = [Activator]::CreateInstance(
        $localModuleSelectionType)
    $unconfirmedSelection.LicenseNoticeAccepted = $false
    $selectionCopy = $page.GetType().GetMethod(
        "CopyLocalModuleInstallerForCompleteSetup",
        $staticFlags)
    if ($null -eq $selectionCopy -or
        $selectionCopy.GetParameters().Count -ne 2) {
        throw "The complete-setup selection copy must receive explicit license confirmation."
    }
    $copyArguments = New-Object object[] 2
    $copyArguments[0] = $unconfirmedSelection
    $copyArguments[1] = $false
    $notAccepted = $selectionCopy.Invoke($null, $copyArguments)
    if ($notAccepted.LicenseNoticeAccepted) {
        throw "The complete-setup selection copy silently elevated license confirmation."
    }
    $copyArguments[1] = $true
    $accepted = $selectionCopy.Invoke($null, $copyArguments)
    if (-not $accepted.LicenseNoticeAccepted) {
        throw "The explicit license confirmation was not carried into the helper request."
    }

    # When an old controller manifest and a managed LM cleanup journal describe
    # the same KKT, the combined row must expose CleanupPending instead of the
    # stale controller status.
    $inventoryItem.ManagedStateFingerprint = $null
    $inventoryItem.Status = [Enum]::Parse(
        $sharedAssembly.GetType(
            "EsmTspiot.Shared.Models.LmServiceProvisioningStatus",
            $true),
        "Succeeded")
    $managedSnapshotType = $assembly.GetType(
        "EsmTspiot.WinForms.Shared.ManagedLocalModuleInventorySnapshot",
        $true)
    $managedItemType = $assembly.GetType(
        "EsmTspiot.WinForms.Shared.ManagedLocalModuleInventoryItem",
        $true)
    $managedSnapshot = [Activator]::CreateInstance($managedSnapshotType, $true)
    $managedItem = [Activator]::CreateInstance($managedItemType, $true)
    foreach ($entry in @(
        @("KktSerial", $inventoryItem.KktSerial),
        @("State", (Get-Utf8Text(
            "0KLRgNC10LHRg9C10YLRgdGPINC+0YfQuNGB0YLQutCw"))),
        @("CleanupPending", $true),
        @("ManagedStateFingerprint", $displayedManagedFingerprint))) {
        $managedItemType.GetProperty($entry[0], $flags).SetValue(
            $managedItem,
            $entry[1],
            $null)
    }
    $managedSnapshotType.GetProperty("Items", $flags).GetValue(
        $managedSnapshot,
        $null).Add($managedItem)
    $removableMatcher = $page.GetType().GetMethod(
        "GetRemovableManagedItems",
        $staticFlags)
    if ($null -eq $removableMatcher) {
        throw "Combined managed-removal inventory builder was not found."
    }
    $removableArguments = New-Object object[] 2
    $removableArguments[0] = $inventoryList
    $removableArguments[1] = $managedSnapshot
    $combined = $removableMatcher.Invoke($null, $removableArguments)
    $cleanupPending = [Enum]::Parse(
        $sharedAssembly.GetType(
            "EsmTspiot.Shared.Models.LmServiceProvisioningStatus",
            $true),
        "CleanupPending")
    if ($combined.Count -ne 1 -or $combined[0].Status -ne $cleanupPending) {
        throw "The combined LM row hid CleanupPending behind a stale controller status."
    }

    $automaticParametersDialog.CreateControl()
    $automaticParametersDialog.PerformLayout()
    $parametersGrid.PerformLayout()
    [System.Windows.Forms.Application]::DoEvents()
    if (-not $continueButton.Enabled) {
        throw "A complete automatic-setup row must allow the operator to continue."
    }
    if ($installButton.Tag -ne "AutomaticSetup") {
        throw "The installer action must expose the single automatic setup command."
    }
    $expectedCreateKitsText = Get-Utf8Text(
        "0KHQvtC30LTQsNGC0YwgLyDQvtCx0L3QvtCy0LjRgtGMINC60L7QvNC/0LvQtdC60YLRiw==")
    if ($installButton.Text -ne $expectedCreateKitsText) {
        throw "The LM-tab action must explain that existing kits are updated safely."
    }
    if ($removeAllButton.Tag -ne "RemoveAllManaged") {
        throw "The LM controller page must expose the protected remove-all command."
    }
    if ($automaticInstallerButton.Tag -ne "ControllerInstallerPicker") {
        throw "Automatic mode must expose controller installer selection before start."
    }
    if ($automaticSetupButton.Tag -ne "EndToEndAutomaticSetup") {
        throw "Automatic mode must expose the end-to-end setup command."
    }
    if ($automaticStopButton.Tag -ne "CancelEndToEndAutomaticSetup") {
        throw "Automatic mode must preserve a safe cancellation action."
    }

    $automaticTab.Size = [System.Drawing.Size]::new(748, 512)
    $automaticRoot = $automaticTab.Controls[0]
    $automaticRoot.Size = $automaticTab.ClientSize
    $automaticRoot.PerformLayout()
    $automaticGroup = $automaticRoot.Controls[0]
    $automaticGroup.PerformLayout()
    $automaticCommands = $automaticGroup.Controls[0]
    $automaticCommands.PerformLayout()
    [System.Windows.Forms.Application]::DoEvents()
    if ($automaticInstallerButton.Right -gt $automaticCommands.ClientSize.Width) {
        throw "Automatic-mode installer controls overflow the normal page width."
    }
    if ($automaticInstallerPath.Width -lt 150) {
        throw "Automatic-mode installer path is too narrow: $($automaticInstallerPath.Width)px."
    }
    if ($automaticSetupButton.Right -gt $automaticCommands.ClientSize.Width) {
        throw "Automatic-mode setup button overflows the normal page width."
    }
    if ($automaticStopButton.Right -gt $automaticCommands.ClientSize.Width) {
        throw "Automatic-mode stop button overflows the normal page width."
    }

    $pathToSelectGap = $selectButton.Left - $pathBox.Right
    $localPathToSelectGap = $selectLocalModuleButton.Left - $localModulePathBox.Right

    if ($pathBox.Width -gt 400) {
        throw "Installer path field is too wide at 1450px page width: $($pathBox.Width)px (maximum 400px)."
    }
    if ($pathToSelectGap -lt 0 -or $pathToSelectGap -gt 12) {
        throw "Unexpected gap between installer path and Select button: ${pathToSelectGap}px."
    }
    if ($localModulePathBox.Width -gt 400) {
        throw "Local-module MSI path field is too wide: $($localModulePathBox.Width)px."
    }
    if ($localPathToSelectGap -lt 0 -or $localPathToSelectGap -gt 12) {
        throw "Unexpected gap between local-module path and Select button: ${localPathToSelectGap}px."
    }

    $page.Size = [System.Drawing.Size]::new(748, 512)
    $page.PerformLayout()
    foreach ($control in $page.Controls) {
        $control.PerformLayout()
    }
    [System.Windows.Forms.Application]::DoEvents()
    if ($installButton.Right -gt $page.ClientSize.Width) {
        throw (
            "Installer controls do not fit the normal page width: button right={0}px, page={1}px." -f
            $installButton.Right, $page.ClientSize.Width)
    }
    if ($removeAllButton.Right -gt $removeAllButton.Parent.ClientSize.Width) {
        throw "The remove-all action overflows its toolbar at normal page width."
    }
    $visibleGridWidth = 0
    foreach ($column in $grid.Columns) {
        if ($column.Visible) {
            $visibleGridWidth += $column.Width
        }
    }
    if ($visibleGridWidth -gt $grid.ClientSize.Width) {
        throw "The operator-facing LM table requires horizontal resizing: columns=${visibleGridWidth}px, grid=$($grid.ClientSize.Width)px."
    }

    # The first read-only LM refresh belongs to the LM page. It must not raise
    # the host-wide busy event that disables unrelated manual tabs.
    $baseUrlBox.Text = "http://127.0.0.1:1"
    $silentRefresh = $page.RefreshIfNeededAsync()
    $hostBusyDuringRefresh = [bool]$formType.GetField(
        "_busy",
        $flags).GetValue($form)
    $refreshDeadline = [DateTime]::UtcNow.AddSeconds(10)
    while (-not $silentRefresh.IsCompleted -and
        [DateTime]::UtcNow -lt $refreshDeadline) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 10
    }
    if (-not $silentRefresh.IsCompleted) {
        throw "The first read-only LM refresh did not complete within ten seconds."
    }
    $silentRefresh.GetAwaiter().GetResult() | Out-Null
    if ($hostBusyDuringRefresh) {
        throw "The first read-only LM refresh entered the host-wide busy state."
    }

    $mainFormSource = [IO.File]::ReadAllText(
        (Join-Path $repoRoot "src\EsmTspiot.WinForms.Shared\MainForm.cs"))
    $pageSource = [IO.File]::ReadAllText(
        (Join-Path $repoRoot "src\EsmTspiot.WinForms.Shared\LmGatewayPage.cs"))
    $pageServicesSource = [IO.File]::ReadAllText(
        (Join-Path $repoRoot "src\EsmTspiot.WinForms.Shared\LmGatewayPage.Services.cs"))
    if ($mainFormSource -notmatch 'InstructionFileSelector\.SelectAvailable') {
        throw "Help must use the packaged Markdown field guide when no PDF exists."
    }
    if ([regex]::Matches(
            $mainFormSource,
            'MessageBoxButtons\.YesNo,\s*MessageBoxIcon\.Warning,\s*MessageBoxDefaultButton\.Button2').Count -ne 3) {
        throw "Every destructive or warning Yes/No prompt must default to No."
    }
    if ($pageServicesSource -notmatch '(?s)_completeStackProvisioner\.RunAsync.*?async delegate\(\s*int index,\s*LmServiceProvisioningItemResult item,.*?ExecuteCompleteAutomaticBindingAsync.*?LmGatewayCredentialDefaults\.Create') {
        throw "Each prepared KKT, including the canary, must be bound before the complete automatic workflow advances."
    }
    if ($pageServicesSource -notmatch '(?s)await RefreshCoreAsync\(CancellationToken\.None\).*?_session\.ApplyOutcomeFallback\(automaticBindingOutcome\).*?FillRows\(null\).*?AreCompleteAutomaticBindingRowsSuccessful') {
        throw "Automatic binding outcomes must remain visible after the final ESM readback refresh."
    }
    if ($pageServicesSource -notmatch '(?s)bool finalRefreshSucceeded\s*=\s*false.*?finalRefreshSucceeded\s*=\s*true.*?if \(finalRefreshSucceeded\).*?AreCompleteAutomaticBindingRowsSuccessful') {
        throw "A best-effort final refresh must refine success only when the readback itself completed."
    }

    Write-Host (
        ("UI layout OK: wide path={0}px, path/select gap={1}px; " +
        "normal button right={2}/{3}px; automatic path={4}px.") -f
        $pathBox.Width, $pathToSelectGap,
        $installButton.Right, $page.ClientSize.Width, $automaticInstallerPath.Width)
}
finally {
    if ($null -ne $supportDialogCloseTimer) {
        $supportDialogCloseTimer.Dispose()
    }
    if ($null -ne $supportDevelopmentDialog) {
        $supportDevelopmentDialog.Dispose()
    }
    if ($null -ne $kktDeletionDialog) {
        $kktDeletionDialog.Dispose()
    }
    if ($null -ne $primaryKktDeletionDialog) {
        $primaryKktDeletionDialog.Dispose()
    }
    if ($null -ne $removeAllDialog) {
        $removeAllDialog.Dispose()
    }
    if ($null -ne $bindingDialog) {
        $bindingDialog.Dispose()
    }
    if ($null -ne $automaticParametersDialog) {
        $automaticParametersDialog.Dispose()
    }
    if ($null -ne $form) {
        $form.Dispose()
    }
}
