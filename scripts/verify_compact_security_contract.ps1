[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$StageRoot
)

$ErrorActionPreference = 'Stop'

if ([IntPtr]::Size -ne 4) {
    $x86PowerShell = Join-Path $env:WINDIR 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path -LiteralPath $x86PowerShell -PathType Leaf)) {
        throw '32-bit PowerShell is required to inspect the x86 compact main executable.'
    }
    & $x86PowerShell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath `
        -StageRoot $StageRoot
    exit $LASTEXITCODE
}

$stage = [IO.Path]::GetFullPath($StageRoot).TrimEnd('\')
$mainPath = Join-Path $stage 'MultiKKT-ESM-TSPioT.exe'
$helperPath = Join-Path $stage 'Provisioner\EsmTspiot.ServiceProvisioner.exe'

foreach ($path in @($mainPath, $helperPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Compact security-contract file is missing: $path"
    }
}

$provisionerRoot = Join-Path $stage 'Provisioner'
$expectedProvisionerFiles = @(
    'EsmTspiot.ServiceProvisioner.exe',
    'EsmTspiot.Shared.dll',
    'WixToolset.Dtf.Compression.Cab.dll',
    'WixToolset.Dtf.Compression.dll',
    'WixToolset.Dtf.WindowsInstaller.Package.dll',
    'WixToolset.Dtf.WindowsInstaller.dll'
) | Sort-Object
$actualProvisionerFiles = @(
    Get-ChildItem -LiteralPath $provisionerRoot -File |
        ForEach-Object Name |
        Sort-Object
)
if (($expectedProvisionerFiles -join "`n") -ne
    ($actualProvisionerFiles -join "`n")) {
    throw "Compact helper closure differs from the reviewed exact file set. Expected: $($expectedProvisionerFiles -join ', '); actual: $($actualProvisionerFiles -join ', ')."
}

$expectedDtfHashes = @{
    'WixToolset.Dtf.WindowsInstaller.dll' = 'CDD7F34DDA1180F21205543C8EE836C5BE66060D94441172366BCE078E1CDB87'
    'WixToolset.Dtf.WindowsInstaller.Package.dll' = '3DE9CAB111A102040C6B4E17FE26690AFCDF37E8F9D82055A479CEF0DECA803C'
    'WixToolset.Dtf.Compression.dll' = '7D1C9C7B18D95A5AD80E250E4B185F47FAE6C921048355AE6D53FAB10BF19525'
    'WixToolset.Dtf.Compression.Cab.dll' = '06971F82041E80DAAC87CCEFFC1F88CC7D663470E9295E487554447A5AE43435'
}
foreach ($name in $expectedDtfHashes.Keys) {
    $path = Join-Path $provisionerRoot $name
    $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedDtfHashes[$name]) {
        throw "Packaged DTF binary differs from the reviewed 4.0.6 dependency: $name"
    }
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($path)
    if ($info.FileVersion -ne '4.0.6.0') {
        throw "Packaged DTF binary has an unexpected file version: $name ($($info.FileVersion))."
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

$closureField = $integrityType.GetField(
    'ExpectedClosureSha256',
    [Reflection.BindingFlags]'Static,NonPublic')
$expectedClosure = [string]$closureField.GetRawConstantValue()
$closureEntries = @($expectedClosure -split '\|' | Where-Object { $_ })
$expectedClosureNames = @($expectedProvisionerFiles |
    Where-Object { $_ -ne 'EsmTspiot.ServiceProvisioner.exe' } |
    Sort-Object)
$actualClosureNames = @($closureEntries |
    ForEach-Object { ($_ -split '=', 2)[0] } |
    Sort-Object)
if (($expectedClosureNames -join "`n") -ne ($actualClosureNames -join "`n")) {
    throw "The packaged main executable embeds an unexpected helper closure list: $($actualClosureNames -join ', ')."
}
foreach ($entry in $closureEntries) {
    $parts = $entry -split '=', 2
    $closurePath = Join-Path $provisionerRoot $parts[0]
    $closureHash = (Get-FileHash -LiteralPath $closurePath -Algorithm SHA256).Hash
    if ($closureHash -ne $parts[1]) {
        throw "The packaged main executable does not trust the packaged helper file: $($parts[0])"
    }
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

# Программа правит службы, ставит MSI и меняет правила брандмауэра. Права
# администратора она запрашивает манифестом при старте, чтобы оператору не
# приходилось помнить про «запуск от имени администратора». Проверяем не
# исходник, а то, что действительно зашито в поставляемый файл.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class MultiKktEmbeddedManifest
{
    private const uint LoadLibraryAsDataFile = 0x00000002;
    private const int ManifestResourceId = 1;
    private const int ManifestResourceType = 24;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryExW(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr data);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);

    public static string Read(string path)
    {
        IntPtr module = LoadLibraryExW(path, IntPtr.Zero, LoadLibraryAsDataFile);
        if (module == IntPtr.Zero)
        {
            throw new InvalidOperationException("Cannot open the image: " + path);
        }

        try
        {
            IntPtr info = FindResourceW(
                module,
                new IntPtr(ManifestResourceId),
                new IntPtr(ManifestResourceType));
            if (info == IntPtr.Zero)
            {
                return string.Empty;
            }

            uint size = SizeofResource(module, info);
            IntPtr data = LockResource(LoadResource(module, info));
            if (data == IntPtr.Zero || size == 0)
            {
                return string.Empty;
            }

            byte[] bytes = new byte[size];
            Marshal.Copy(data, bytes, 0, (int)size);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            FreeLibrary(module);
        }
    }
}
'@

$mainManifest = [MultiKktEmbeddedManifest]::Read($mainPath)
if ([string]::IsNullOrWhiteSpace($mainManifest)) {
    throw 'The packaged main executable has no embedded application manifest.'
}
if ($mainManifest -notmatch 'level\s*=\s*"requireAdministrator"') {
    throw 'The packaged main executable does not request administrator rights at startup.'
}
if ($mainManifest -match 'uiAccess\s*=\s*"true"') {
    throw 'The packaged main executable must not request uiAccess.'
}

Write-Host 'Compact main/helper security contract passed.'
