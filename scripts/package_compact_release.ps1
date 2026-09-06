[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $scriptRoot ".."))
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts\release"))
$stageRoot = [IO.Path]::GetFullPath((Join-Path $releaseRoot "MultiKKT-ESM-TSPioT-compact"))
# Имя архива получает версию после сборки: без неё в каталоге лежат
# неразличимые файлы, и оператор не может понять, какой из них новый.
$zipPath = $null
$outerSumsPath = $null
$fixedTimestamp = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
. (Join-Path $scriptRoot "build_tools.ps1")

function Assert-ChildPath {
    param([string]$Parent, [string]$Child)

    $parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    $childFull = [IO.Path]::GetFullPath($Child)
    if (-not $childFull.StartsWith($parentFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe output path outside expected parent: $childFull"
    }
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

$msbuild = Get-KrsMSBuildPath
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
$sourceDtfWindowsInstaller = Join-Path $helperOutput "WixToolset.Dtf.WindowsInstaller.dll"
$sourceDtfWindowsInstallerPackage = Join-Path $helperOutput "WixToolset.Dtf.WindowsInstaller.Package.dll"
$sourceDtfCompression = Join-Path $helperOutput "WixToolset.Dtf.Compression.dll"
$sourceDtfCompressionCab = Join-Path $helperOutput "WixToolset.Dtf.Compression.Cab.dll"
$fieldGuide = Join-Path $repositoryRoot "docs\testing\2026-09-03-field-acceptance-full-contour.md"
$stagedMain = Join-Path $stageRoot "MultiKKT-ESM-TSPioT.exe"
$stagedHelper = Join-Path $stageRoot "Provisioner\EsmTspiot.ServiceProvisioner.exe"

Copy-RequiredFile $sourceMain $stagedMain
$productVersion = (Get-Item -LiteralPath $stagedMain).VersionInfo.FileVersion
if (-not ($productVersion -match "^[0-9]+(\.[0-9]+){3}$")) {
    throw "Unexpected main application version: $productVersion"
}
$zipPath = Join-Path $releaseRoot ("MultiKKT-ESM-TSPioT-" + $productVersion + ".zip")
$outerSumsPath = Join-Path $releaseRoot ("SHA256SUMS-MultiKKT-" + $productVersion + ".txt")
Assert-ChildPath $releaseRoot $zipPath
Assert-ChildPath $releaseRoot $outerSumsPath
Copy-RequiredFile $sourceShared (Join-Path $stageRoot "EsmTspiot.Shared.dll")
Copy-RequiredFile $fieldGuide (Join-Path $stageRoot "FIELD_TEST.md")
Copy-RequiredFile (Join-Path $repositoryRoot "INSTRUCTION_FOR_DUMMIES.md") (Join-Path $stageRoot "INSTRUCTION_FOR_DUMMIES.txt")
Copy-RequiredFile (Join-Path $repositoryRoot "LICENSE") (Join-Path $stageRoot "LICENSE.txt")

$helperClosure = @(
    Get-Item -LiteralPath $sourceHelper
    Get-Item -LiteralPath $sourceHelperShared
    Get-Item -LiteralPath $sourceDtfWindowsInstaller
    Get-Item -LiteralPath $sourceDtfWindowsInstallerPackage
    Get-Item -LiteralPath $sourceDtfCompression
    Get-Item -LiteralPath $sourceDtfCompressionCab
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
Мульти-ККТ в ЕСМ/ТС ПИоТ — компактная поставка, версия $productVersion

Разработчик: Руслан Керусов
Издатель и владелец: KRS

Начало работы
1. Нужны Windows, .NET Framework 4.8 и права администратора.
2. Проверьте SHA-256 ZIP по опубликованному рядом файлу контрольных сумм.
3. Распакуйте архив целиком в локальную папку и запустите MultiKKT-ESM-TSPioT.exe.
   EsmTspiot.Shared.dll и папка Provisioner должны лежать рядом с EXE.
   Подтвердите единственный запрос UAC при запуске.
4. Откройте INSTRUCTION_FOR_DUMMIES.txt: это пошаговое руководство оператора.
   В программе оно доступно через Справка -> Инструкция.
   FIELD_TEST.md — отдельный чек-лист для подготовленного физического стенда.

Обновление
Закройте MultiKKT и распакуйте новый ZIP целиком в отдельную папку.
Не смешивайте EXE, DLL и Provisioner от разных версий. Сама замена утилиты
не переустанавливает ЕСМ/ЛМ и не удаляет созданные службы.

Важно
- Для обоих режимов нужны ЕСМ/ТС ПИоТ и официальный MSI ЛМ ЧЗ от ЦРПТ.
  Для обычного режима также нужен штатно установленный ЕСП Контроллер ЛМ ЧЗ.
- Режим FMU-API пропускает контроллеры, но не ЕСМ или ЛМ ЧЗ.
  Сам FMU-API и бизнес-инициализация ЛМ настраиваются отдельно.
- Настройка завершается после установки и принятия настроек ЕСМ.
  Ожидания готовности ЛМ в конце нет; диагностика запускается отдельно.
- Настроить Frontol можно отдельной кнопкой после ручной или автоматической
  настройки. Закройте Frontol и администратор, сделайте резервную копию базы.
  Источник — кассовый Frontol.ini; меняются только адрес и порт ЕСМ.
  Используется isql.exe штатного Firebird. После записи перезапустите Frontol.
- В ZIP нет Firebird, FMU-API, вендорного MSI, Erlang runtime или данных клиента.
  Пересборка клона использует быстрое минимальное сжатие с проверкой содержимого.
- Новые сценарии перед использованием у клиента проверяйте на физическом стенде.

LICENSE.txt — условия использования программы.
THIRD-PARTY-NOTICES.txt — сведения о включённых библиотеках WiX DTF 4.0.6.
SHA256SUMS — контрольные суммы файлов внутри архива.
Официальные сборки: https://github.com/jadieify-hub/esm-tspiot-multi-kkt-tool/releases
"@
[IO.File]::WriteAllText((Join-Path $stageRoot "README.txt"), $readme, [Text.UTF8Encoding]::new($true))

$thirdPartyNotices = @"
Third-party notices

WiX Toolset Deployment Tools Foundation (DTF) 4.0.6
Files:
- WixToolset.Dtf.WindowsInstaller.dll
- WixToolset.Dtf.WindowsInstaller.Package.dll
- WixToolset.Dtf.Compression.dll
- WixToolset.Dtf.Compression.Cab.dll

Copyright (c) .NET Foundation and contributors.
License: Microsoft Reciprocal License (MS-RL)
License terms: https://licenses.nuget.org/MS-RL
Source: https://github.com/wixtoolset/wix

The listed binaries are redistributed without modification.
"@
[IO.File]::WriteAllText(
    (Join-Path $stageRoot "THIRD-PARTY-NOTICES.txt"),
    $thirdPartyNotices,
    [Text.UTF8Encoding]::new($true))

$forbiddenStage = @(Get-ChildItem -LiteralPath $stageRoot -Recurse -File | Where-Object {
    $_.Extension -in @(".msi", ".pdb", ".pfx", ".p12", ".pem", ".key", ".cer", ".crt", ".der") -or
    $_.Name -match '(?i)(esm-lm-controller|lmcontroller|regime|yenisei|erts-|epmd|nssm|InstallAutoUpdateLM|RollingPin|official-lm-profile|fixture)' -or
    $_.Length -gt 10MB
})
if ($forbiddenStage.Count -gt 0) {
    throw "Forbidden, vendor, fixture or unexpectedly large staged file: $($forbiddenStage.FullName -join ', ')"
}

$expectedExact = @(
    "EsmTspiot.Shared.dll",
    "FIELD_TEST.md",
    "INSTRUCTION_FOR_DUMMIES.txt",
    "LICENSE.txt",
    "MultiKKT-ESM-TSPioT.exe",
    "README.txt",
    "THIRD-PARTY-NOTICES.txt"
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

# Пакет требует прав администратора манифестом. Сборочный прогон идёт без
# них и не должен упираться в запрос UAC, поэтому дымовой запуск делается
# под совместимостью RunAsInvoker. Сам манифест проверяет контракт
# безопасности отдельно.
$previousCompatLayer = $env:__COMPAT_LAYER
$env:__COMPAT_LAYER = 'RunAsInvoker'
try {
    $smoke = Start-Process -FilePath $stagedMain -WorkingDirectory $stageRoot -PassThru
}
finally {
    $env:__COMPAT_LAYER = $previousCompatLayer
}
try {
    if (-not $smoke.WaitForInputIdle(10000)) {
        throw "Compact Legacy smoke did not become interactive."
    }
    # The main window is created shortly after the message loop becomes idle;
    # on a busy build machine that takes over a second, so poll instead of
    # sampling once.
    $smokeDeadline = [DateTime]::UtcNow.AddSeconds(15)
    $smokeReady = $false
    while ([DateTime]::UtcNow -lt $smokeDeadline) {
        Start-Sleep -Milliseconds 250
        $smoke.Refresh()
        if ($smoke.HasExited) {
            throw "Compact Legacy smoke exited during startup with code $($smoke.ExitCode)."
        }
        if ($smoke.Responding -and $smoke.MainWindowHandle -ne 0) {
            $smokeReady = $true
            break
        }
    }
    if (-not $smokeReady) {
        throw "Compact Legacy smoke did not open a responsive main window within 15 seconds."
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
if ($actualEntries -match '(?i)(modern|win-x64|win-x86|esm-lm-controller|lmcontroller|regime|yenisei|erts-|epmd|nssm|InstallAutoUpdateLM|\.msi$|\.pdb$)') {
    throw "ZIP contains a Modern, vendor or debug artifact."
}

$outerLine = (Get-Sha256 $zipPath) + "  " + [IO.Path]::GetFileName($zipPath)
[IO.File]::WriteAllText($outerSumsPath, $outerLine + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))

Write-Host "Compact release package created: $zipPath"
Write-Host "Archive SHA-256: $(Get-Sha256 $zipPath)"
Write-Host "Archive entries: $($actualEntries -join ', ')"
