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

$form = $null
$automaticParametersDialog = $null
try {
    $form = [Activator]::CreateInstance($formType)
    $page = Get-PrivateFieldValue -Instance $form -Name "_lmGatewayPage"

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
    $removeAllButton = Get-PrivateFieldValue -Instance $page -Name "_removeAllServicesButton"
    $grid = Get-PrivateFieldValue -Instance $page -Name "_grid"
    $automaticInstallerButton = Get-PrivateFieldValue -Instance $form -Name "_automaticSelectInstallerButton"
    $automaticInstallerPath = Get-PrivateFieldValue -Instance $form -Name "_automaticInstallerTextBox"
    $automaticSetupButton = Get-PrivateFieldValue -Instance $form -Name "_bulkRegisterButton"
    $automaticStopButton = Get-PrivateFieldValue -Instance $form -Name "_automaticStopButton"
    $automaticTab = Get-PrivateFieldValue -Instance $form -Name "_automationTab"
    $dkktPortBox = Get-PrivateFieldValue -Instance $form -Name "_dkktPortTextBox"

    if ($dkktPortBox.Text -ne "4042") {
        throw "The main form must default dkktPort to the ESM orchestrator port 4042; actual: '$($dkktPortBox.Text)'."
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

    $sharedAssembly = [System.Reflection.Assembly]::LoadFrom(
        (Join-Path (Split-Path -Parent $appPath) "EsmTspiot.Shared.dll"))
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
        @("State", "Требуется очистка"),
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
    $selectToInstallGap = $installButton.Left - $selectButton.Right
    $localPathToSelectGap = $selectLocalModuleButton.Left - $localModulePathBox.Right

    if ($pathBox.Width -gt 400) {
        throw "Installer path field is too wide at 1450px page width: $($pathBox.Width)px (maximum 400px)."
    }
    if ($pathToSelectGap -lt 0 -or $pathToSelectGap -gt 12) {
        throw "Unexpected gap between installer path and Select button: ${pathToSelectGap}px."
    }
    if ($selectToInstallGap -lt 0 -or $selectToInstallGap -gt 12) {
        throw "Unexpected gap between installer buttons: ${selectToInstallGap}px."
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

    Write-Host (
        ("UI layout OK: wide path={0}px, path/select gap={1}px, select/install gap={2}px; " +
        "normal button right={3}/{4}px; automatic path={5}px.") -f
        $pathBox.Width, $pathToSelectGap, $selectToInstallGap,
        $installButton.Right, $page.ClientSize.Width, $automaticInstallerPath.Width)
}
finally {
    if ($null -ne $automaticParametersDialog) {
        $automaticParametersDialog.Dispose()
    }
    if ($null -ne $form) {
        $form.Dispose()
    }
}
