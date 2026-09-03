[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$MsiPath
)

# Elevated stand gate for the local-module MSI contour. It drives the
# production provisioner code path (base adoption + one repacked clone) through
# the Release test executable, requires the vendor MSI to stay outside the
# repository, and records the sandbox report under artifacts\release.
# It is deliberately not part of run_release_matrix.ps1: it needs the vendor
# MSI, UAC, and a machine whose regime1/yenisei1 names are free.

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gateLogPath = Join-Path $repositoryRoot 'artifacts\release\local-module-msi-sandbox.log'
$reportPath = Join-Path $repositoryRoot 'artifacts\release\local-module-msi-sandbox.report.txt'
$testExecutable = Join-Path $repositoryRoot 'tests\EsmTspiot.ServiceProvisioner.Tests\bin\Release\net48\EsmTspiot.ServiceProvisioner.Tests.exe'

trap {
    $failure = "LOCAL_MODULE_MSI_SANDBOX_FAILED`r`n" +
        [DateTime]::UtcNow.ToString('o') + "`r`n" +
        $_.Exception.ToString() + "`r`n"
    [IO.Directory]::CreateDirectory((Split-Path -Parent $gateLogPath)) | Out-Null
    [IO.File]::WriteAllText(
        $gateLogPath,
        $failure,
        [Text.UTF8Encoding]::new($true))
    Write-Error $_.Exception.Message
    exit 1
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'The local-module MSI sandbox gate must run in an elevated PowerShell session.'
}
if (-not (Test-Path -LiteralPath $testExecutable -PathType Leaf)) {
    throw "Build the Release provisioner tests before the sandbox gate: $testExecutable"
}

$msiFullPath = [IO.Path]::GetFullPath($MsiPath)
if (-not (Test-Path -LiteralPath $msiFullPath -PathType Leaf)) {
    throw "The official LM MSI was not found: $msiFullPath"
}
if ($msiFullPath.StartsWith($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The vendor MSI must stay outside the repository; point the gate at its original location.'
}

[IO.Directory]::CreateDirectory((Split-Path -Parent $reportPath)) | Out-Null
if (Test-Path -LiteralPath $reportPath) {
    Remove-Item -LiteralPath $reportPath -Force
}

$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $testExecutable
$startInfo.Arguments = '--local-module-msi-sandbox "' + $msiFullPath + '" "' + $reportPath + '"'
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$process = [Diagnostics.Process]::Start($startInfo)
$standardOutput = $process.StandardOutput.ReadToEnd()
$standardError = $process.StandardError.ReadToEnd()
$process.WaitForExit()
$exitCode = $process.ExitCode
$text = ($standardOutput.TrimEnd() + "`r`n" + $standardError.TrimEnd()).Trim()
$report = ''
if (Test-Path -LiteralPath $reportPath -PathType Leaf) {
    $report = [IO.File]::ReadAllText($reportPath).Trim()
}
if ($exitCode -ne 0) {
    throw "Production local-module MSI sandbox failed with exit code ${exitCode}:`r`n$text`r`n$report"
}
if ($report -notmatch '(?m)^SANDBOX_OK\r?$') {
    throw "The sandbox report does not end with SANDBOX_OK:`r`n$text`r`n$report"
}

$success = "LOCAL_MODULE_MSI_SANDBOX_OK`r`n" +
    [DateTime]::UtcNow.ToString('o') + "`r`n" +
    $report + "`r`n"
[IO.File]::WriteAllText(
    $gateLogPath,
    $success,
    [Text.UTF8Encoding]::new($true))
Write-Host $success.Trim()
