[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$StageRoot
)

$ErrorActionPreference = 'Stop'
$stage = [IO.Path]::GetFullPath($StageRoot).TrimEnd('\')
$mainPath = Join-Path $stage 'MultiKKT-ESM-TSPioT.exe'
$helperPath = Join-Path $stage 'Provisioner\EsmTspiot.ServiceProvisioner.exe'

foreach ($path in @($mainPath, $helperPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Compact security-contract file is missing: $path"
    }
}

$mainAssembly = [Reflection.Assembly]::LoadFrom($mainPath)
$integrityType = $mainAssembly.GetType(
    'EsmTspiot.WinForms.Shared.ProvisionerIntegrity',
    $true)
$integrityField = $integrityType.GetField(
    'ExpectedSha256',
    [Reflection.BindingFlags]'Static,NonPublic')
$expectedHelperHash = [string]$integrityField.GetRawConstantValue()
$actualHelperHash = (Get-FileHash -LiteralPath $helperPath -Algorithm SHA256).Hash
if ($expectedHelperHash -ne $actualHelperHash) {
    throw 'The packaged main executable does not trust the packaged helper hash.'
}

$mainInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($mainPath)
$helperInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($helperPath)
if ($mainInfo.CompanyName -ne 'KRS' -or
    $helperInfo.CompanyName -ne 'KRS' -or
    [string]::IsNullOrWhiteSpace($mainInfo.ProductName) -or
    $mainInfo.ProductName -ne $helperInfo.ProductName -or
    [string]::IsNullOrWhiteSpace($mainInfo.FileVersion) -or
    $mainInfo.FileVersion -ne $helperInfo.FileVersion -or
    $mainInfo.ProductVersion -ne $helperInfo.ProductVersion) {
    throw 'The packaged main and helper product metadata differ.'
}

$clientType = $mainAssembly.GetType(
    'EsmTspiot.WinForms.Shared.LmServiceProvisionerClient',
    $true)
$createServer = $clientType.GetMethod(
    'CreateServer',
    [Reflection.BindingFlags]'Static,NonPublic')
$pipeName = [string]('multikkt-contract-' + [Guid]::NewGuid().ToString('N'))
$pipe = $createServer.Invoke(
    $null,
    [object[]]@($pipeName))
try {
    if ($null -eq $pipe) {
        throw 'The packaged main did not create its protected named pipe.'
    }
}
finally {
    if ($null -ne $pipe) {
        $pipe.Dispose()
    }
}

$helperAssembly = [Reflection.Assembly]::LoadFrom($helperPath)
$peerReaderType = $helperAssembly.GetType(
    'EsmTspiot.ServiceProvisioner.NativePeerEvidenceReader',
    $true)
$allowedMain = $peerReaderType.GetMethod(
    'IsAllowedMainImage',
    [Reflection.BindingFlags]'Static,NonPublic')
$outsideMainPath = [string](Join-Path (Split-Path -Parent $stage) 'outside.exe')
$acceptsStagedMain = [bool]$allowedMain.Invoke(
    $null,
    [object[]]@([string]$mainPath, [string]$stage))
$acceptsOutsideMain = [bool]$allowedMain.Invoke(
    $null,
    [object[]]@($outsideMainPath, [string]$stage))
if (-not $acceptsStagedMain -or $acceptsOutsideMain) {
    throw 'The packaged helper and main executable disagree about the application directory.'
}

Write-Host 'Compact main/helper security contract passed.'
