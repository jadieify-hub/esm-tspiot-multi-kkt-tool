[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gateLogPath = Join-Path $repositoryRoot 'artifacts\release\direct-controller-sandbox.log'
$testExecutable = Join-Path $repositoryRoot 'tests\EsmTspiot.ServiceProvisioner.Tests\bin\Release\net48\EsmTspiot.ServiceProvisioner.Tests.exe'

trap {
    $failure = "DIRECT_CONTROLLER_SANDBOX_FAILED`r`n" +
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
    throw 'The direct-controller sandbox gate must run in an elevated PowerShell session.'
}
if (-not (Test-Path -LiteralPath $testExecutable -PathType Leaf)) {
    throw "Build the Release provisioner tests before the sandbox gate: $testExecutable"
}

$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $testExecutable
$startInfo.Arguments = '--direct-controller-sandbox'
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
if ($exitCode -ne 0) {
    throw "Production direct-controller sandbox failed with exit code ${exitCode}:`r`n$text"
}
if ($text -notmatch '(?m)^DIRECT_CONTROLLER_PRODUCTION_SANDBOX_OK\r?$') {
    throw "The test executable did not run the production sandbox mode:`r`n$text"
}

$success = "DIRECT_CONTROLLER_SANDBOX_OK`r`n" +
    [DateTime]::UtcNow.ToString('o') + "`r`n" +
    $text.Trim() + "`r`n"
[IO.Directory]::CreateDirectory((Split-Path -Parent $gateLogPath)) | Out-Null
[IO.File]::WriteAllText(
    $gateLogPath,
    $success,
    [Text.UTF8Encoding]::new($true))
Write-Host $success.Trim()
