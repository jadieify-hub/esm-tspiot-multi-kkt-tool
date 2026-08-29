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
    $installButton = Get-PrivateFieldValue -Instance $page -Name "_installControllerButton"
    $removeAllButton = Get-PrivateFieldValue -Instance $page -Name "_removeAllServicesButton"
    $grid = Get-PrivateFieldValue -Instance $page -Name "_grid"
    $controllerAddressBox = Get-PrivateFieldValue -Instance $page -Name "_addressTextBox"
    $controllerGrpcBox = Get-PrivateFieldValue -Instance $page -Name "_portTextBox"
    $controllerRestBox = Get-PrivateFieldValue -Instance $page -Name "_restPortTextBox"
    $automaticInstallerButton = Get-PrivateFieldValue -Instance $form -Name "_automaticSelectInstallerButton"
    $automaticInstallerPath = Get-PrivateFieldValue -Instance $form -Name "_automaticInstallerTextBox"
    $automaticSetupButton = Get-PrivateFieldValue -Instance $form -Name "_bulkRegisterButton"
    $automaticStopButton = Get-PrivateFieldValue -Instance $form -Name "_automaticStopButton"
    $automaticTab = Get-PrivateFieldValue -Instance $form -Name "_automationTab"
    $dkktPortBox = Get-PrivateFieldValue -Instance $form -Name "_dkktPortTextBox"

    if ($dkktPortBox.Text -ne "4042") {
        throw "The main form must default dkktPort to the ESM orchestrator port 4042; actual: '$($dkktPortBox.Text)'."
    }

    if ($grid.Columns.Count -lt 2 -or $grid.Columns[1].Name -ne "KktOrdinal") {
        throw "The LM KKT table must show an ordinal column immediately after selection."
    }
    $expectedNames = @(
        "SelectKkt",
        "KktOrdinal",
        "KktSerial",
        "KktInn",
        "KktSoftwarePort",
        "LmTargetAddress",
        "LmTargetPort",
        "UserStatus",
        "UserResult")
    if ($grid.Columns.Count -ne $expectedNames.Count) {
        throw "The operator-facing LM table must contain only $($expectedNames.Count) working columns; actual: $($grid.Columns.Count)."
    }
    for ($index = 0; $index -lt $expectedNames.Count; $index++) {
        if ($grid.Columns[$index].Name -ne $expectedNames[$index]) {
            throw "Unexpected LM table column $index`: '$($grid.Columns[$index].Name)'."
        }
    }
    $lmAddressColumn = $grid.Columns["LmTargetAddress"]
    $lmPortColumn = $grid.Columns["LmTargetPort"]
    if ($null -eq $lmAddressColumn) {
        throw "The LM KKT table must show the target LM CHZ address in a dedicated column."
    }
    if ($null -eq $lmPortColumn) {
        throw "The LM KKT table must show the target LM CHZ port in a dedicated column."
    }
    if ($null -ne $controllerAddressBox.Parent -or
        $null -ne $controllerGrpcBox.Parent -or
        $null -ne $controllerRestBox.Parent) {
        throw "Internal controller address, gRPC and REST fields must not be shown to the operator."
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
    $rowType.GetProperty("Login").SetValue($row, "operator", $null)
    $rowType.GetProperty("Password").SetValue($row, "field-secret", $null)
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
        "LmLogin",
        "LmPassword",
        "Validation")
    if ($parametersGrid.Columns.Count -ne $expectedParameterNames.Count) {
        throw "The automatic-setup dialog must contain only operator-facing fields."
    }
    for ($index = 0; $index -lt $expectedParameterNames.Count; $index++) {
        if ($parametersGrid.Columns[$index].Name -ne $expectedParameterNames[$index]) {
            throw "Unexpected automatic-setup column $index`: '$($parametersGrid.Columns[$index].Name)'."
        }
    }
    foreach ($name in @("KktOrdinal", "KktSerial", "KktInn", "KktSoftwarePort", "Validation")) {
        if (-not $parametersGrid.Columns[$name].ReadOnly) {
            throw "Automatic-setup identity/status column must be read-only: $name."
        }
    }
    foreach ($name in @("LmTargetAddress", "LmTargetPort", "LmLogin", "LmPassword")) {
        if ($parametersGrid.Columns[$name].ReadOnly) {
            throw "Automatic-setup operator field must be editable: $name."
        }
    }
    $automaticParametersDialog.CreateControl()
    $automaticParametersDialog.PerformLayout()
    $parametersGrid.PerformLayout()
    [System.Windows.Forms.Application]::DoEvents()
    $passwordCell = $parametersGrid.Rows[0].Cells["LmPassword"]
    if ($passwordCell.FormattedValue -eq "field-secret") {
        throw "The automatic-setup password must not be displayed as plain text."
    }
    if (-not $continueButton.Enabled) {
        throw "A complete automatic-setup row must allow the operator to continue."
    }
    $passwordCell.Value = ""
    [System.Windows.Forms.Application]::DoEvents()
    if ($continueButton.Enabled) {
        throw "Automatic setup must not continue while a KKT password is missing."
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

    if ($pathBox.Width -gt 400) {
        throw "Installer path field is too wide at 1450px page width: $($pathBox.Width)px (maximum 400px)."
    }
    if ($pathToSelectGap -lt 0 -or $pathToSelectGap -gt 12) {
        throw "Unexpected gap between installer path and Select button: ${pathToSelectGap}px."
    }
    if ($selectToInstallGap -lt 0 -or $selectToInstallGap -gt 12) {
        throw "Unexpected gap between installer buttons: ${selectToInstallGap}px."
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
