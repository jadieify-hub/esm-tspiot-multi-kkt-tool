[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipPackage
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $scriptRoot ".."))
. (Join-Path $scriptRoot "build_tools.ps1")

function Invoke-Checked {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$FailureMessage
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage Exit code: $LASTEXITCODE."
    }
}

$msbuild = Get-KrsMSBuildPath
$csc = Get-KrsNetFrameworkCscPath
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) `
    ("esm-tspiot-release-matrix-" + [Guid]::NewGuid().ToString("N"))
[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

try {
    Push-Location $repositoryRoot
    try {
        Invoke-Checked "dotnet.exe" @(
            "run", "--project",
            "tests\EsmTspiot.Shared.Tests\EsmTspiot.Shared.Tests.csproj",
            "-c", $Configuration
        ) "Shared net8 tests failed."

        $sharedSources = @(Get-ChildItem -LiteralPath "src\EsmTspiot.Shared" `
            -Recurse -Filter *.cs -File | Where-Object {
                $_.FullName -notmatch '\\obj\\|\\bin\\'
            } | ForEach-Object { $_.FullName })
        $sharedSources += (Resolve-Path `
            "tests\EsmTspiot.Shared.Tests\Program.cs").Path
        $sharedNet48Exe = Join-Path $temporaryRoot "SharedTests-net48.exe"
        $cscArguments = @(
            "/nologo",
            "/define:NETFRAMEWORK",
            "/out:$sharedNet48Exe",
            "/r:System.Net.Http.dll",
            "/r:System.Web.Extensions.dll",
            "/r:System.Runtime.Serialization.dll"
        ) + $sharedSources
        Invoke-Checked $csc $cscArguments "Shared net48 compilation failed."
        Invoke-Checked $sharedNet48Exe @() "Shared net48 tests failed."

        Invoke-Checked "dotnet.exe" @(
            "run", "--project",
            "tests\EsmTspiot.Operator.Tests\EsmTspiot.Operator.Tests.csproj",
            "-f", "net8.0-windows", "-c", $Configuration
        ) "Operator net8 tests failed."
        Invoke-Checked "dotnet.exe" @(
            "run", "--project",
            "tests\EsmTspiot.Operator.Tests\EsmTspiot.Operator.Tests.csproj",
            "-f", "net48", "-c", $Configuration
        ) "Operator net48 tests failed."

        Invoke-Checked $msbuild @(
            "tests\EsmTspiot.ServiceProvisioner.Tests\EsmTspiot.ServiceProvisioner.Tests.csproj",
            "/restore", "/t:Rebuild", "/p:Configuration=$Configuration",
            "/m", "/v:minimal"
        ) "Provisioner test build failed."
        Invoke-Checked `
            "tests\EsmTspiot.ServiceProvisioner.Tests\bin\$Configuration\net48\EsmTspiot.ServiceProvisioner.Tests.exe" `
            @() "Provisioner tests failed."

        Invoke-Checked $msbuild @(
            "src\EsmTspiot.ServiceProvisioner\EsmTspiot.ServiceProvisioner.csproj",
            "/restore", "/t:Rebuild", "/p:Configuration=$Configuration",
            "/p:LangVersion=5", "/m", "/v:minimal"
        ) "Provisioner C# 5 build failed."
        Invoke-Checked $msbuild @(
            "src\EsmTspiot.Legacy.WinForms\EsmTspiot.Legacy.WinForms.csproj",
            "/restore", "/t:Rebuild", "/p:Configuration=$Configuration",
            "/p:LangVersion=5", "/m", "/v:minimal"
        ) "Legacy C# 5 build failed."

        Invoke-Checked "powershell.exe" @(
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            "scripts\verify_lm_safety.ps1"
        ) "LM safety gate failed."
        Invoke-Checked "powershell.exe" @(
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            "scripts\verify_ui_layout.ps1"
        ) "UI gate failed."
        Invoke-Checked "dotnet.exe" @(
            "build",
            "src\EsmTspiot.Modern.WinForms\EsmTspiot.Modern.WinForms.csproj",
            "-c", $Configuration
        ) "Modern build failed."

        if (-not $SkipPackage) {
            Invoke-Checked "powershell.exe" @(
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                "scripts\package_compact_release.ps1",
                "-Configuration", $Configuration
            ) "Compact release package failed."
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

Write-Host "RELEASE_MATRIX_OK"
