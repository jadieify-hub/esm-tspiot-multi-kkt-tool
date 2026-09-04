[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $scriptRoot ".."))

function Invoke-CheckedSearch {
    param(
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string[]]$Paths
    )

    try {
        foreach ($match in @(Select-String -Pattern $Pattern -LiteralPath $Paths -AllMatches -ErrorAction Stop)) {
            [pscustomobject]@{
                Path = $match.Path
                Line = [int]$match.LineNumber
                Text = $match.Line.TrimEnd("`r", "`n")
            }
        }
    }
    catch {
        throw "Select-String failed: $($_.Exception.Message)"
    }
}

function Test-SearchWrapper {
    $testRoot = Join-Path ([IO.Path]::GetTempPath()) ("KrsLmSafety-" + [Guid]::NewGuid().ToString("N"))
    [IO.Directory]::CreateDirectory($testRoot) | Out-Null
    try {
        $clean = Join-Path $testRoot "clean.txt"
        $hit = Join-Path $testRoot "hit.txt"
        [IO.File]::WriteAllText($clean, "ordinary text", [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($hit, "forbidden-marker", [Text.UTF8Encoding]::new($false))
        if (@(Invoke-CheckedSearch -Pattern "forbidden-marker" -Paths @($hit)).Count -ne 1) {
            throw "Search wrapper self-test did not report a hit."
        }
        if (@(Invoke-CheckedSearch -Pattern "forbidden-marker" -Paths @($clean)).Count -ne 0) {
            throw "Search wrapper self-test inverted a clean result."
        }
        $toolErrorObserved = $false
        try {
            Invoke-CheckedSearch -Pattern "[" -Paths @($clean) | Out-Null
        }
        catch {
            $toolErrorObserved = $true
        }
        if (-not $toolErrorObserved) {
            throw "Search wrapper self-test swallowed a tool error."
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

function Get-ProductionSourceFiles {
    param([Parameter(Mandatory = $true)][string]$Root)

    $files = New-Object System.Collections.Generic.List[string]
    foreach ($relativeRoot in @(
        "src\EsmTspiot.ServiceProvisioner",
        "src\EsmTspiot.WinForms.Shared",
        "src\EsmTspiot.Shared")) {
        $sourceRoot = Join-Path $Root $relativeRoot
        if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
            continue
        }
        foreach ($file in @(Get-ChildItem $sourceRoot -Recurse -Filter *.cs)) {
            if ($file.FullName -notmatch '[\\/](bin|obj)[\\/]') {
                $files.Add($file.FullName)
            }
        }
    }
    $provisionerTarget = Join-Path $Root "build\EsmTspiot.Provisioner.targets"
    if (Test-Path -LiteralPath $provisionerTarget -PathType Leaf) {
        $files.Add($provisionerTarget)
    }
    return @($files | Sort-Object -Unique)
}

function Test-AllowedForbiddenHit {
    param($Hit)

    $relative = Get-RelativeRepositoryPath $Hit.Path
    if ($relative -eq "src/EsmTspiot.Shared/Services/ServiceRecoveryCommandBuilder.cs") {
        return $Hit.Text -match '^\s*builder\.AppendLine\("        sc\.exe delete \$serviceName \| Out-Host"\);\s*$'
    }
    if ($relative -eq "src/EsmTspiot.WinForms.Shared/MainForm.cs") {
        return $Hit.Text -match '^\s*startInfo\.FileName = "powershell\.exe";\s*$'
    }
    if ($relative -eq "src/EsmTspiot.ServiceProvisioner/LegacyOwnedProcessTerminator.cs") {
        return $Hit.Text -match '^\s*if \(!TerminateProcess\(process, 0x4B52534C\)\)\s*$' -or
            $Hit.Text -match '^\s*private static extern bool TerminateProcess\(IntPtr process, uint exitCode\);\s*$'
    }
    return $false
}

function Test-ProductionSourceDiscovery {
    $testRoot = Join-Path ([IO.Path]::GetTempPath()) ("KrsLmSourceScope-" + [Guid]::NewGuid().ToString("N"))
    [IO.Directory]::CreateDirectory($testRoot) | Out-Null
    try {
        $expected = @(
            "src/EsmTspiot.ServiceProvisioner/FutureHelper.cs",
            "src/EsmTspiot.Shared/Services/FutureCapability.cs",
            "src/EsmTspiot.WinForms.Shared/FutureWorkflow.cs",
            "build/EsmTspiot.Provisioner.targets")
        foreach ($relative in $expected) {
            $path = Join-Path $testRoot $relative.Replace('/', '\')
            [IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
            [IO.File]::WriteAllText($path, "fixture", [Text.UTF8Encoding]::new($false))
        }
        foreach ($relative in @(
            "src/EsmTspiot.Shared/bin/Ignored.cs",
            "src/EsmTspiot.WinForms.Shared/obj/Ignored.cs")) {
            $path = Join-Path $testRoot $relative.Replace('/', '\')
            [IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
            [IO.File]::WriteAllText($path, "fixture", [Text.UTF8Encoding]::new($false))
        }
        $actual = @(Get-ProductionSourceFiles -Root $testRoot | ForEach-Object {
            $_.Substring($testRoot.TrimEnd('\').Length + 1).Replace('\', '/')
        })
        if (@(Compare-Object ($expected | Sort-Object) ($actual | Sort-Object)).Count -ne 0) {
            throw "Production source discovery does not cover the complete source fixture."
        }
    }
    finally {
        if (Test-Path -LiteralPath $testRoot) {
            Remove-Item -LiteralPath $testRoot -Recurse -Force
        }
    }
}

function Test-AllowedSecretHit {
    param($Hit)

    $relative = Get-RelativeRepositoryPath $Hit.Path
    $text = $Hit.Text

    if ($relative -eq "src/EsmTspiot.Shared/Logging/SensitiveDataMasker.cs") {
        return $text -match '"(password|newPassword|pass|token|secret|authorization|apiKey)"'
    }
    if ($relative -eq "src/EsmTspiot.Shared/Models/LmGatewayCredentials.cs" -or
        $relative -eq "src/EsmTspiot.Shared/Models/LmConnectionRequest.cs") {
        return $text -match 'Password|DataMember\(Name = "password"\)'
    }
    if ($relative -eq "src/EsmTspiot.Shared/Services/LmGatewayBindingWorkflow.cs") {
        return $text -match 'credentials\.Password|Password = credentials\.Password|timeout\.Token'
    }
    if ($relative -eq "src/EsmTspiot.WinForms.Shared/LmGatewayCredentialDefaults.cs") {
        return $text -match '^\s*Password\s*=\s*SupportedLocalModulePackageIdentity\.ApiPassword\s*$'
    }
    if ($relative -eq "src/EsmTspiot.ServiceProvisioner/LocalModuleConfigurationInspector.cs") {
        # Reviewed: the inspector only checks that the vendor installer wrote
        # non-empty [api] login/password; values are never logged or copied.
        return $text -match '"api\.password"|^\s*string password;$|^\s*password\.Length == 0\)$|\[api\] login/password|write \[api\] login and password'
    }
    if ($relative -eq "src/EsmTspiot.Shared/Services/LmGatewayReadbackWorkflow.cs") {
        return $text -match 'timeout\.Token'
    }
    if ($relative -eq "src/EsmTspiot.WinForms.Shared/LmGatewayBindingDialog.cs") {
        return $text -match 'Password\s*=\s*_passwordTextBox\.Text|_passwordTextBox\.Text\s*=\s*credentials\.Password|credentials\.Password\s*=\s*string\.Empty'
    }
    if ($relative -eq "src/EsmTspiot.WinForms.Shared/LmGatewayPage.Binding.cs") {
        return $text -match 'delegate\(CancellationToken token\)|ExecuteSelectedBindingAsync\(plan, credentials, token\)|credentials\.Password\s*=\s*string\.Empty|Password\s*=\s*credentials\.Password'
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
        return $text -match 'CancellationToken token|_cancellation\.Token'
    }
    if ($relative -eq "src/EsmTspiot.WinForms.Shared/LmGatewayPage.Services.cs") {
        return $text -match 'CancellationToken token|\btoken\b'
    }
    return $false
}

Test-SearchWrapper
Test-ProductionSourceDiscovery

# Donation UI is deliberately offline. Keep its only external value as inert
# text/QR data and reject APIs that could turn the dialog into a network or
# browser integration later.
$supportDialogPath = Join-Path $repositoryRoot (
    "src\EsmTspiot.WinForms.Shared\SupportDevelopmentDialog.cs")
$supportQrPath = Join-Path $repositoryRoot (
    "src\EsmTspiot.WinForms.Shared\Assets\support-cloudtips-qr.png")
if (-not (Test-Path -LiteralPath $supportDialogPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $supportQrPath -PathType Leaf)) {
    throw "The offline support dialog and its pinned QR asset must both exist."
}
$supportDialogSource = [IO.File]::ReadAllText($supportDialogPath)
$supportForbiddenPattern = 'System\.Net|HttpClient|WebClient|WebRequest|Socket|TcpClient|Dns\.|Process\.Start|ProcessStartInfo|WebBrowser|Timer'
if ($supportDialogSource -match $supportForbiddenPattern) {
    throw "The support dialog must not contain network, browser, process-launch or timer APIs."
}
$expectedSupportUrl = "https://pay.cloudtips.ru/p/53698013"
$supportUrls = @([regex]::Matches(
    $supportDialogSource,
    'https://[^"\s]+') | ForEach-Object Value)
if ($supportUrls.Count -ne 1 -or $supportUrls[0] -ne $expectedSupportUrl) {
    throw "The support dialog must contain only the approved CloudTips URL."
}
$expectedSupportQrHash =
    "fa50f1d7549ae49f0ee15bd834c35866acbcbef725d7d5ca6eb257929b622bfc"
$actualSupportQrHash = (Get-FileHash -LiteralPath $supportQrPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualSupportQrHash -ne $expectedSupportQrHash) {
    throw "The embedded support QR differs from the user-provided PNG."
}

$gatewayPageServicesPath = Join-Path $repositoryRoot "src\EsmTspiot.WinForms.Shared\LmGatewayPage.Services.cs"
$gatewayPageServices = [IO.File]::ReadAllText($gatewayPageServicesPath)
$gatewayPagePath = Join-Path $repositoryRoot "src\EsmTspiot.WinForms.Shared\LmGatewayPage.cs"
$gatewayPage = [IO.File]::ReadAllText($gatewayPagePath)
$gatewayPageMsiPath = Join-Path $repositoryRoot (
    "src\EsmTspiot.WinForms.Shared\LmGatewayPage.MsiLocalModules.cs")
$gatewayPageMsi = [IO.File]::ReadAllText($gatewayPageMsiPath)
$bindingPagePath = Join-Path $repositoryRoot "src\EsmTspiot.WinForms.Shared\LmGatewayPage.Binding.cs"
$bindingDialogPath = Join-Path $repositoryRoot "src\EsmTspiot.WinForms.Shared\LmGatewayBindingDialog.cs"
if (-not (Test-Path -LiteralPath $bindingPagePath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $bindingDialogPath -PathType Leaf)) {
    throw "The documented ESM binding must have a focused page coordinator and one-time credential dialog."
}
$bindingPage = [IO.File]::ReadAllText($bindingPagePath)
$bindingDialog = [IO.File]::ReadAllText($bindingDialogPath)
$credentialDefaultsPath = Join-Path $repositoryRoot (
    "src\EsmTspiot.WinForms.Shared\LmGatewayCredentialDefaults.cs")
$legacyProject = [IO.File]::ReadAllText((Join-Path $repositoryRoot (
    "src\EsmTspiot.Legacy.WinForms\EsmTspiot.Legacy.WinForms.csproj")))
$modernProject = [IO.File]::ReadAllText((Join-Path $repositoryRoot (
    "src\EsmTspiot.Modern.WinForms\EsmTspiot.Modern.WinForms.csproj")))
$helperProject = [IO.File]::ReadAllText((Join-Path $repositoryRoot (
    "src\EsmTspiot.ServiceProvisioner\EsmTspiot.ServiceProvisioner.csproj")))
if (-not (Test-Path -LiteralPath $credentialDefaultsPath -PathType Leaf) -or
    $legacyProject -notmatch 'LmGatewayCredentialDefaults\.cs' -or
    $modernProject -notmatch 'LmGatewayCredentialDefaults\.cs' -or
    $helperProject -match 'LmGatewayCredentialDefaults|EsmTspiot\.WinForms\.Shared') {
    throw "Standard ESM credentials must be compiled only into the two WinForms applications, never into the privileged helper."
}
if ($legacyProject -notmatch 'SupportDevelopmentDialog\.cs' -or
    $modernProject -notmatch 'SupportDevelopmentDialog\.cs' -or
    $legacyProject -notmatch 'support-cloudtips-qr\.png.*LogicalName="EsmTspiot\.WinForms\.Shared\.Assets\.support-cloudtips-qr\.png"' -or
    $modernProject -notmatch 'support-cloudtips-qr\.png.*LogicalName="EsmTspiot\.WinForms\.Shared\.Assets\.support-cloudtips-qr\.png"' -or
    $helperProject -match 'SupportDevelopmentDialog|support-cloudtips-qr') {
    throw "The support dialog and pinned QR must be embedded only in both operator applications."
}
if ($gatewayPage -match '_loginTextBox|_passwordTextBox|LmGatewayCredentials|StoreCredentials') {
    throw "The operator LM page must not retain hidden credential controls or credential state."
}
if ($bindingPage -match 'Dictionary\s*<\s*string\s*,\s*LmGatewayCredentials|private\s+.*LmGatewayCredentials' -or
    $bindingPage -notmatch '(?s)finally.*?credentials\.Login\s*=\s*string\.Empty.*?credentials\.Password\s*=\s*string\.Empty') {
    throw "ESM binding credentials must remain one-operation values and be cleared in finally."
}
if ($bindingDialog -notmatch 'UseSystemPasswordChar\s*=\s*true') {
    throw "The one-time ESM binding dialog must mask its password field."
}
if ($gatewayPageServices -notmatch '(?s)private async Task StartAutomaticSetupAsync\(\).*?RunFullAutomaticLocalSetupFromHostAsync') {
    throw "The LM-tab automatic entry point must use the controller/MSI local-module workflow."
}
$automaticRequestCount = ([regex]::Matches(
    $gatewayPageMsi, 'new LmServiceProvisioningBatchRequest')).Count
$automaticHashCount = ([regex]::Matches(
    $gatewayPageMsi, 'request\.PlanHash = CanonicalLmPlanHasher\.Compute\(request\)')).Count
if ($automaticRequestCount -lt 1 -or $automaticHashCount -ne $automaticRequestCount) {
    throw "Every automatic helper request must be hashed before elevation."
}
if ($gatewayPageServices -match '(?s)FixedTimeEqualsHex\(\s*item\.ManifestFingerprint\.Sha256,\s*item\.ManifestFingerprint\.Sha256\)' -or
    $gatewayPageServices -match '(?s)FixedTimeEqualsHex\(\s*managed\.ManagedStateFingerprint\.Sha256,\s*managed\.ManagedStateFingerprint\.Sha256\)') {
    throw "A fingerprint must be validated for SHA-256 syntax, not compared with itself."
}
# This narrow list is intentional only for LM-specific structure and secret
# checks. Forbidden implementation patterns and e-mail addresses use the full
# production source set assembled by Get-ProductionSourceFiles below.
$productionFiles = New-Object System.Collections.Generic.List[string]
$productionFiles.AddRange([string[]]@(Get-ChildItem (Join-Path $repositoryRoot "src\EsmTspiot.ServiceProvisioner") -Recurse -Filter *.cs | ForEach-Object FullName))
$productionFiles.AddRange([string[]]@(Get-ChildItem (Join-Path $repositoryRoot "src\EsmTspiot.WinForms.Shared") -Filter "Lm*.cs" | ForEach-Object FullName))
$productionFiles.Add((Join-Path $repositoryRoot "src\EsmTspiot.WinForms.Shared\LocalModuleInstallerPicker.cs"))
$productionFiles.Add((Join-Path $repositoryRoot "src\EsmTspiot.WinForms.Shared\ManagedLocalModuleInventoryReader.cs"))
$productionFiles.Add((Join-Path $repositoryRoot "src\EsmTspiot.WinForms.Shared\ProvisionerProcessLauncher.cs"))
$productionFiles.AddRange([string[]]@(Get-ChildItem (Join-Path $repositoryRoot "src\EsmTspiot.Shared\Models") -Filter "Lm*.cs" | ForEach-Object FullName))
$productionFiles.AddRange([string[]]@(Get-ChildItem (Join-Path $repositoryRoot "src\EsmTspiot.Shared\Services") -Filter "Lm*.cs" | ForEach-Object FullName))
$productionFiles.AddRange([string[]]@(Get-ChildItem (Join-Path $repositoryRoot "src\EsmTspiot.Shared\Services") -Filter "ManagedLocalModule*.cs" | ForEach-Object FullName))
$productionFiles.Add((Join-Path $repositoryRoot "src\EsmTspiot.Shared\Services\SupportedLocalModulePackageIdentity.cs"))
$productionFiles.AddRange([string[]]@(Get-ChildItem (Join-Path $repositoryRoot "src\EsmTspiot.Shared\Validation") -Filter "Lm*.cs" | ForEach-Object FullName))
$productionFiles.AddRange([string[]]@(Get-ChildItem (Join-Path $repositoryRoot "src\EsmTspiot.Shared\Logging") -Filter *.cs | ForEach-Object FullName))
$productionFiles.Add((Join-Path $repositoryRoot "src\EsmTspiot.Shared\Services\TspiotApiClient.cs"))
$productionFiles.Add((Join-Path $repositoryRoot "build\EsmTspiot.Provisioner.targets"))
$production = @($productionFiles | Sort-Object -Unique)
$allProduction = @(Get-ProductionSourceFiles -Root $repositoryRoot)

$forbiddenPattern = 'RollingPin|taskkill(?:\.exe)?|sc\.exe|powershell\.exe|cmd\.exe|\bjunction\b|\bUPX\b|relaxed_command_check|TerminateProcess|Process\.Kill|\.Kill\('
$forbidden = @(Invoke-CheckedSearch -Pattern $forbiddenPattern -Paths $allProduction)
$forbidden = @($forbidden | Where-Object { -not (Test-AllowedForbiddenHit $_) })
if ($forbidden.Count -gt 0) {
    $details = $forbidden | ForEach-Object { "$(Get-RelativeRepositoryPath $_.Path):$($_.Line):$($_.Text)" }
    throw "Forbidden LM implementation pattern found:`n$($details -join [Environment]::NewLine)"
}

$emailPattern = '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}'
$emailHits = @(Invoke-CheckedSearch -Pattern $emailPattern -Paths $allProduction)
$unexpectedEmails = @($emailHits | Where-Object {
    $relativePath = Get-RelativeRepositoryPath $_.Path
    -not (
        $relativePath -eq 'src/EsmTspiot.ServiceProvisioner/ControllerCapabilityProfile.cs' -and
        $_.Text -match '(?i)\bE=it@ao-esp\.ru\b')
})
if ($unexpectedEmails.Count -gt 0) {
    $details = $unexpectedEmails | ForEach-Object { "$(Get-RelativeRepositoryPath $_.Path):$($_.Line):$($_.Text)" }
    throw "Unexpected e-mail address found in LM production code:`n$($details -join [Environment]::NewLine)"
}

$secretPattern = '\b(newPassword|password|pass|token|secret|apiKey|authorization)\b'
$secretHits = @(Invoke-CheckedSearch -Pattern $secretPattern -Paths $production)
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
    $_ -match '(?i)\.(exe|dll|msi|beam|boot|ez|pdb|pfx|p12|pem|key|cer|crt|der)$' -or
    $_ -match '(?i)(esm-lm-controller|RollingPin|regime|yenisei|erts-|epmd|nssm|InstallAutoUpdateLM).*\.(zip|7z|rar)$' -or
    $_ -match '(?i)(^|/)(regime|yenisei|erts-[^/]+)(/|$)'
})
if ($forbiddenTracked.Count -gt 0) {
    throw "Tracked binary, credential or vendor artifact found:`n$($forbiddenTracked -join [Environment]::NewLine)"
}

Write-Host "LM safety verification passed: full production forbidden/e-mail scan clean, LM secret hits allowlisted, no tracked vendor/binary artifacts."
