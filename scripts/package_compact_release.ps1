[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $scriptRoot ".."))
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts\release"))
$stageRoot = [IO.Path]::GetFullPath((Join-Path $releaseRoot "MultiKKT-ESM-TSPioT-compact"))
$zipPath = Join-Path $releaseRoot "MultiKKT-ESM-TSPioT-compact.zip"
$outerSumsPath = Join-Path $releaseRoot "SHA256SUMS-MultiKKT-compact.txt"
$fixedTimestamp = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)

function Assert-ChildPath {
    param([string]$Parent, [string]$Child)

    $parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    $childFull = [IO.Path]::GetFullPath($Child)
    if (-not $childFull.StartsWith($parentFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe output path outside expected parent: $childFull"
    }
}

function Find-MSBuild {
    $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($found) {
            return $found
        }
    }
    throw "MSBuild was not found. Install Visual Studio Build Tools with the .NET Framework 4.8 targeting pack."
}

function Copy-RequiredFile {
    param([string]$Source, [string]$Destination)

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required build output is missing: $Source"
    }
    $destinationDirectory = Split-Path -Parent $Destination
    [IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null
    [IO.File]::Copy($Source, $Destination, $true)
}

function Get-Sha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-StageRelativePath {
    param([string]$Path)

    $prefix = $stageRoot.TrimEnd('\') + '\'
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Staged file is outside staging root: $full"
    }
    return $full.Substring($prefix.Length).Replace('\', '/')
}

Assert-ChildPath -Parent $repositoryRoot -Child $releaseRoot
Assert-ChildPath -Parent $releaseRoot -Child $stageRoot
[IO.Directory]::CreateDirectory($releaseRoot) | Out-Null
if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
[IO.Directory]::CreateDirectory($stageRoot) | Out-Null

$msbuild = Find-MSBuild
$legacyProject = Join-Path $repositoryRoot "src\EsmTspiot.Legacy.WinForms\EsmTspiot.Legacy.WinForms.csproj"
& $msbuild $legacyProject /restore /t:Rebuild "/p:Configuration=$Configuration" /p:LangVersion=5 /m /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Legacy compact build failed with exit code $LASTEXITCODE."
}

$legacyOutput = Join-Path $repositoryRoot "src\EsmTspiot.Legacy.WinForms\bin\$Configuration\net48"
$helperOutput = Join-Path $repositoryRoot "src\EsmTspiot.ServiceProvisioner\bin\$Configuration\net48"
$sourceMain = Join-Path $legacyOutput "EsmTspiot.Legacy.WinForms.exe"
$sourceShared = Join-Path $legacyOutput "EsmTspiot.Shared.dll"
$sourceHelper = Join-Path $helperOutput "EsmTspiot.ServiceProvisioner.exe"
$sourceHelperShared = Join-Path $helperOutput "EsmTspiot.Shared.dll"
$fieldGuide = Join-Path $repositoryRoot "docs\testing\2026-08-29-field-acceptance-1.6.3.2.md"
$stagedMain = Join-Path $stageRoot "MultiKKT-ESM-TSPioT.exe"
$stagedHelper = Join-Path $stageRoot "Provisioner\EsmTspiot.ServiceProvisioner.exe"

Copy-RequiredFile $sourceMain $stagedMain
Copy-RequiredFile $sourceShared (Join-Path $stageRoot "EsmTspiot.Shared.dll")
Copy-RequiredFile $fieldGuide (Join-Path $stageRoot "FIELD_TEST_1.6.3.2.md")

$helperClosure = @(
    Get-Item -LiteralPath $sourceHelper
    Get-Item -LiteralPath $sourceHelperShared
)
foreach ($file in $helperClosure) {
    Copy-RequiredFile $file.FullName (Join-Path $stageRoot ("Provisioner\" + $file.Name))
}

if ((Get-Sha256 $sourceShared) -ne (Get-Sha256 $sourceHelperShared)) {
    throw "Main and helper use different net48 EsmTspiot.Shared.dll files."
}

$contractScript = Join-Path $scriptRoot 'verify_compact_security_contract.ps1'
if (-not (Test-Path -LiteralPath $contractScript -PathType Leaf)) {
    throw "Compact main/helper security contract script is missing: $contractScript"
}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $contractScript -StageRoot $stageRoot
if ($LASTEXITCODE -ne 0) {
    throw "Compact main/helper security contract failed with exit code $LASTEXITCODE."
}

$readme = @"
Multi-KKT for ESM/TS PIOT - compact package

Publisher and owner: KRS
Author: Ruslan Kerusov

1. Verify the SHA-256 checksums before use.
2. To manage Windows services, an administrator must extract the complete archive to:
   C:\Program Files\KRS\MultiKKT
   Inherited ACLs must not grant ordinary users write access.
3. From Downloads, Desktop or another user-writable directory, the application deliberately permits only viewing and manual ESM binding.
4. The official LM controller and its installer are not included. Obtain esm-lm-controller_*-windows-setup.exe from a licensed PIOT distribution, then select it on the LM Controllers tab.
5. This archive contains no vendor binaries, credentials or controller profiles.
6. Before a real installation, follow FIELD_TEST_1.6.3.2.md.

Run: MultiKKT-ESM-TSPioT.exe
"@
[IO.File]::WriteAllText((Join-Path $stageRoot "README.txt"), $readme, [Text.UTF8Encoding]::new($true))

$forbiddenStage = @(Get-ChildItem -LiteralPath $stageRoot -Recurse -File | Where-Object {
    $_.Extension -in @(".pdb", ".pfx", ".p12", ".pem", ".key", ".cer", ".crt", ".der") -or
    $_.Name -match '(?i)(esm-lm-controller|lmcontroller|RollingPin|official-lm-profile|fixture)' -or
    $_.Length -gt 10MB
})
if ($forbiddenStage.Count -gt 0) {
    throw "Forbidden, vendor, fixture or unexpectedly large staged file: $($forbiddenStage.FullName -join ', ')"
}

$expectedExact = @(
    "EsmTspiot.Shared.dll",
    "FIELD_TEST_1.6.3.2.md",
    "MultiKKT-ESM-TSPioT.exe",
    "README.txt"
)
$expectedExact += @($helperClosure | ForEach-Object { "Provisioner/" + $_.Name })
$actualBeforeSums = @(Get-ChildItem -LiteralPath $stageRoot -Recurse -File | ForEach-Object { Get-StageRelativePath $_.FullName } | Sort-Object)
$unexpected = @($actualBeforeSums | Where-Object { $_ -notin $expectedExact })
$missing = @($expectedExact | Where-Object { $_ -notin $actualBeforeSums })
if ($unexpected.Count -gt 0 -or $missing.Count -gt 0) {
    throw "Compact layout mismatch. Missing: $($missing -join ', '); unexpected: $($unexpected -join ', ')."
}

$sumLines = New-Object System.Collections.Generic.List[string]
foreach ($file in @(Get-ChildItem -LiteralPath $stageRoot -Recurse -File | Sort-Object FullName)) {
    $sumLines.Add((Get-Sha256 $file.FullName) + "  " + (Get-StageRelativePath $file.FullName))
}
[IO.File]::WriteAllLines((Join-Path $stageRoot "SHA256SUMS"), $sumLines, [Text.UTF8Encoding]::new($false))

$smoke = Start-Process -FilePath $stagedMain -WorkingDirectory $stageRoot -PassThru
try {
    if (-not $smoke.WaitForInputIdle(10000)) {
        throw "Compact Legacy smoke did not become interactive."
    }
    Start-Sleep -Milliseconds 500
    $smoke.Refresh()
    if ($smoke.HasExited -or -not $smoke.Responding -or $smoke.MainWindowHandle -eq 0) {
        throw "Compact Legacy smoke did not open a responsive main window."
    }
}
finally {
    if (-not $smoke.HasExited) {
        $null = $smoke.CloseMainWindow()
        if (-not $smoke.WaitForExit(3000)) {
            Stop-Process -Id $smoke.Id -Force
        }
    }
}

foreach ($file in @(Get-ChildItem -LiteralPath $stageRoot -Recurse -File)) {
    $file.LastWriteTimeUtc = $fixedTimestamp.UtcDateTime
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in @(Get-ChildItem -LiteralPath $stageRoot -Recurse -File | Sort-Object FullName)) {
        $entryName = Get-StageRelativePath $file.FullName
        $entry = $zip.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = $fixedTimestamp
        $input = [IO.File]::OpenRead($file.FullName)
        $output = $entry.Open()
        try {
            $input.CopyTo($output)
        }
        finally {
            $output.Dispose()
            $input.Dispose()
        }
    }
}
finally {
    $zip.Dispose()
}

$expectedEntries = @(Get-ChildItem -LiteralPath $stageRoot -Recurse -File | ForEach-Object { Get-StageRelativePath $_.FullName } | Sort-Object)
$readZip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $actualEntries = @($readZip.Entries | ForEach-Object FullName | Sort-Object)
}
finally {
    $readZip.Dispose()
}
if (($expectedEntries -join "`n") -ne ($actualEntries -join "`n")) {
    throw "ZIP content differs from the verified compact staging directory."
}
if ($actualEntries -match '(?i)(modern|win-x64|win-x86|esm-lm-controller|lmcontroller|\.pdb$)') {
    throw "ZIP contains a Modern, vendor or debug artifact."
}

$outerLine = (Get-Sha256 $zipPath) + "  " + [IO.Path]::GetFileName($zipPath)
[IO.File]::WriteAllText($outerSumsPath, $outerLine + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

Write-Host "Compact release package created: $zipPath"
Write-Host "Archive SHA-256: $(Get-Sha256 $zipPath)"
Write-Host "Archive entries: $($actualEntries -join ', ')"
