[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $scriptRoot ".."))

function Invoke-CheckedRg {
    param(
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string[]]$Paths
    )

    $output = @(& rg --json -n -i -e $Pattern -- $Paths 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -gt 1) {
        throw "rg failed with exit code $exitCode`: $($output -join [Environment]::NewLine)"
    }
    if ($exitCode -eq 1) {
        return @()
    }

    foreach ($line in $output) {
        $event = $line.ToString() | ConvertFrom-Json
        if ($event.type -eq "match") {
            [pscustomobject]@{
                Path = $event.data.path.text
                Line = [int]$event.data.line_number
                Text = $event.data.lines.text.TrimEnd("`r", "`n")
            }
        }
    }
}

function Test-RgWrapper {
    $testRoot = Join-Path ([IO.Path]::GetTempPath()) ("KrsLmSafety-" + [Guid]::NewGuid().ToString("N"))
    [IO.Directory]::CreateDirectory($testRoot) | Out-Null
    try {
        $clean = Join-Path $testRoot "clean.txt"
        $hit = Join-Path $testRoot "hit.txt"
        [IO.File]::WriteAllText($clean, "ordinary text", [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($hit, "forbidden-marker", [Text.UTF8Encoding]::new($false))
        if (@(Invoke-CheckedRg -Pattern "forbidden-marker" -Paths @($hit)).Count -ne 1) {
            throw "rg wrapper self-test did not report a hit."
        }
        if (@(Invoke-CheckedRg -Pattern "forbidden-marker" -Paths @($clean)).Count -ne 0) {
            throw "rg wrapper self-test inverted a clean result."
        }
        $toolErrorObserved = $false
        try {
            Invoke-CheckedRg -Pattern "[" -Paths @($clean) | Out-Null
        }
        catch {
            $toolErrorObserved = $true
        }
        if (-not $toolErrorObserved) {
            throw "rg wrapper self-test swallowed a tool error."
        }
    }
    finally {
        if (Test-Path -LiteralPath $testRoot) {
            Remove-Item -LiteralPath $testRoot -Recurse -Force
        }
    }
}

function Get-RelativeRepositoryPath {
    param([string]$Path)

    $full = [IO.Path]::GetFullPath($Path)
    $prefix = $repositoryRoot.TrimEnd('\') + '\'
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the repository: $full"
    }
    return $full.Substring($prefix.Length).Replace('\', '/')
}

function Test-AllowedSecretHit {
    param($Hit)

    $relative = Get-RelativeRepositoryPath $Hit.Path
    $text = $Hit.Text

    if ($relative -eq "src/EsmTspiot.Shared/Logging/SensitiveDataMasker.cs") {
        return $text -match '"(password|newPassword|token|secret|authorization|apiKey)"'
    }
    if ($relative -eq "src/EsmTspiot.Shared/Models/LmGatewayCredentials.cs" -or
        $relative -eq "src/EsmTspiot.Shared/Models/LmConnectionRequest.cs") {
        return $text -match 'Password|DataMember\(Name = "password"\)'
    }
    if ($relative -eq "src/EsmTspiot.Shared/Services/LmGatewayBindingWorkflow.cs") {
        return $text -match 'credentials\.Password|Password = credentials\.Password'
    }
    if ($relative -eq "src/EsmTspiot.ServiceProvisioner/OfficialLmProfileAdapter.cs") {
        return $text -match 'forbidden secret field|lower\.IndexOf\("(password|secret|token|apikey)"'
    }
    if ($relative -eq "src/EsmTspiot.ServiceProvisioner/WindowsServiceApi.cs") {
        return $text -match '^\s*string password[,);]'
    }
    if ($relative -eq "src/EsmTspiot.ServiceProvisioner/RestrictedServiceSid.cs" -or
        $relative -eq "src/EsmTspiot.WinForms.Shared/ProvisionerProcessLauncher.cs") {
        return $text -match '\btoken\b|\.Token\b'
    }
    if ($relative -eq "src/EsmTspiot.ServiceProvisioner/NamedPipeProvisioningChannel.cs" -or
        $relative -eq "src/EsmTspiot.ServiceProvisioner/LmGatewaySupervisorService.cs") {
        return $text -match '(peer|service) token'
    }
    if ($relative -eq "src/EsmTspiot.WinForms.Shared/LmGatewayPage.cs") {
        return $text -match 'CancellationToken token|_cancellation\.Token|\bpassword\b|\.Password\b|_passwordTextBox'
    }
    if ($relative -eq "src/EsmTspiot.WinForms.Shared/LmGatewayPage.Services.cs") {
        return $text -match 'CancellationToken token|\btoken\b'
    }
    return $false
}

Test-RgWrapper

$productionFiles = New-Object System.Collections.Generic.List[string]
$productionFiles.AddRange([string[]]@(Get-ChildItem (Join-Path $repositoryRoot "src\EsmTspiot.ServiceProvisioner") -Recurse -Filter *.cs | ForEach-Object FullName))
$productionFiles.AddRange([string[]]@(Get-ChildItem (Join-Path $repositoryRoot "src\EsmTspiot.WinForms.Shared") -Filter "Lm*.cs" | ForEach-Object FullName))
$productionFiles.Add((Join-Path $repositoryRoot "src\EsmTspiot.WinForms.Shared\ProvisionerProcessLauncher.cs"))
$productionFiles.AddRange([string[]]@(Get-ChildItem (Join-Path $repositoryRoot "src\EsmTspiot.Shared\Models") -Filter "Lm*.cs" | ForEach-Object FullName))
$productionFiles.AddRange([string[]]@(Get-ChildItem (Join-Path $repositoryRoot "src\EsmTspiot.Shared\Services") -Filter "Lm*.cs" | ForEach-Object FullName))
$productionFiles.AddRange([string[]]@(Get-ChildItem (Join-Path $repositoryRoot "src\EsmTspiot.Shared\Validation") -Filter "Lm*.cs" | ForEach-Object FullName))
$productionFiles.AddRange([string[]]@(Get-ChildItem (Join-Path $repositoryRoot "src\EsmTspiot.Shared\Logging") -Filter *.cs | ForEach-Object FullName))
$productionFiles.Add((Join-Path $repositoryRoot "src\EsmTspiot.Shared\Services\TspiotApiClient.cs"))
$productionFiles.Add((Join-Path $repositoryRoot "build\EsmTspiot.Provisioner.targets"))
$production = @($productionFiles | Sort-Object -Unique)

$forbiddenPattern = 'RollingPin|taskkill(?:\.exe)?|sc\.exe|powershell\.exe|cmd\.exe|\bjunction\b|\bUPX\b|TerminateProcess|Process\.Kill|\.Kill\('
$forbidden = @(Invoke-CheckedRg -Pattern $forbiddenPattern -Paths $production)
if ($forbidden.Count -gt 0) {
    $details = $forbidden | ForEach-Object { "$(Get-RelativeRepositoryPath $_.Path):$($_.Line):$($_.Text)" }
    throw "Forbidden LM implementation pattern found:`n$($details -join [Environment]::NewLine)"
}

$secretPattern = '\b(newPassword|password|token|secret|apiKey|authorization)\b'
$secretHits = @(Invoke-CheckedRg -Pattern $secretPattern -Paths $production)
$unexpectedSecrets = @($secretHits | Where-Object { -not (Test-AllowedSecretHit $_) })
if ($unexpectedSecrets.Count -gt 0) {
    $details = $unexpectedSecrets | ForEach-Object { "$(Get-RelativeRepositoryPath $_.Path):$($_.Line):$($_.Text)" }
    throw "Unreviewed secret-like production symbol found:`n$($details -join [Environment]::NewLine)"
}

$tracked = @(& git -C $repositoryRoot ls-files)
if ($LASTEXITCODE -ne 0) {
    throw "git ls-files failed."
}
$forbiddenTracked = @($tracked | Where-Object {
    $_ -match '(?i)\.(exe|dll|pdb|pfx|p12|pem|key|cer|crt|der)$' -or
    $_ -match '(?i)(esm-lm-controller|lmcontroller\.exe|RollingPin)'
})
if ($forbiddenTracked.Count -gt 0) {
    throw "Tracked binary, credential or vendor artifact found:`n$($forbiddenTracked -join [Environment]::NewLine)"
}

Write-Host "LM safety verification passed: forbidden patterns absent, secret hits allowlisted, no tracked vendor/binary artifacts."
