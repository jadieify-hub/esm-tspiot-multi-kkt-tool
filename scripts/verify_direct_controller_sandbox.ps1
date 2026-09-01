[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gateLogPath = Join-Path $repositoryRoot 'artifacts\release\direct-controller-sandbox.log'
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
$expectedLength = 14668016
$expectedSha256 = '0A25B29A39B100FE461EB3FFA06A6B18F2B474F337EFDBA9F7A9CA89740FFD0A'
$expectedThumbprint = '1CD26372850FE30F1559821CF5D318591695271A'
$ordinals = @(31, 32)
$createdServices = New-Object System.Collections.Generic.List[string]
$sandboxBase = [IO.Path]::GetFullPath((Join-Path $env:ProgramData 'KRS\MultiKKT\Sandbox'))
$sandboxRoot = [IO.Path]::GetFullPath((Join-Path $sandboxBase ([Guid]::NewGuid().ToString('N'))))

function Invoke-Sc {
    param([string[]]$Arguments)
    $output = & "$env:SystemRoot\System32\sc.exe" @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe failed ($LASTEXITCODE): $($output -join ' ')"
    }
    return $output
}

function Get-ServiceImagePath {
    param([string]$Name)
    $record = Get-CimInstance Win32_Service -Filter "Name='$Name'"
    if ($null -eq $record) { throw "Required service is missing: $Name" }
    $raw = [Environment]::ExpandEnvironmentVariables([string]$record.PathName).Trim()
    if ($raw -match '^"([^"]+)"') { return [IO.Path]::GetFullPath($Matches[1]) }
    return [IO.Path]::GetFullPath(($raw -split '\s+', 2)[0])
}

function Write-ControllerConfig {
    param([string]$Path, [int]$Ordinal)
    $grpc = 50062 + $Ordinal
    $rest = 5062 + $Ordinal
    $futureLm = 4995 + (1000 * $Ordinal)
    $logDir = Join-Path (Split-Path -Parent $Path) 'log'
    [IO.Directory]::CreateDirectory($logDir) | Out-Null
    $content = @"
settings:
    logs:
        dir: '$($logDir.Replace("'", "''"))'
        debugInfo: true
    common:
        gRPCPort: $grpc
        RESTPort: $rest
        gRPCSecured: true
        timeout:
            upd: 60
            updPolling: 1
            updCount: 10
    lmConfig:
        url: 'http://127.0.0.1'
        port: $futureLm
        version: not defined
        dbVersion: ''
    certificate:
        hosts: []
    connection:
        pingServers:
            - ya.ru
            - google.com
        inetstatus: {}
        lmstatus: {}
"@
    [IO.File]::WriteAllText($Path, $content, [Text.UTF8Encoding]::new($false))
}

function Remove-VerifiedStaleSandboxService {
    param([string]$Name, [int]$Ordinal, [string]$ExpectedBinary)
    $record = Get-CimInstance Win32_Service -Filter "Name='$Name'"
    if ($null -eq $record) { return }
    $expectedDisplay = "MultiKKT sandbox controller $Ordinal"
    $serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$Name"
    $environment = @((Get-ItemProperty -LiteralPath $serviceKey -Name Environment -ErrorAction Stop).Environment)
    $programDataEntry = @($environment | Where-Object { $_ -like 'ProgramData=*' })
    if ($record.DisplayName -ne $expectedDisplay -or
        [IO.Path]::GetFullPath(([string]$record.PathName).Trim('"')) -ne $ExpectedBinary -or
        $programDataEntry.Count -ne 1) {
        throw "Sandbox service name is occupied by an unrecognized service: $Name"
    }
    $environmentRoot = [IO.Path]::GetFullPath($programDataEntry[0].Substring('ProgramData='.Length))
    $expectedPrefix = $sandboxBase.TrimEnd('\') + '\'
    if (-not $environmentRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($environmentRoot) -ne "slot-$Ordinal") {
        throw "Sandbox service has an unrecognized profile root: $Name"
    }
    & "$env:SystemRoot\System32\sc.exe" stop $Name 2>&1 | Out-Null
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 250
        $record = Get-CimInstance Win32_Service -Filter "Name='$Name'" -ErrorAction SilentlyContinue
    } while ($null -ne $record -and [int]$record.ProcessId -ne 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($null -ne $record -and [int]$record.ProcessId -ne 0) {
        throw "Stale sandbox service did not stop: $Name"
    }
    Invoke-Sc @('delete', $Name) | Out-Null
    $deleteDeadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 250
        $record = Get-CimInstance Win32_Service -Filter "Name='$Name'" -ErrorAction SilentlyContinue
    } while ($null -ne $record -and [DateTime]::UtcNow -lt $deleteDeadline)
    if ($null -ne $record) { throw "Stale sandbox service remains registered: $Name" }
    if (Test-Path -LiteralPath $environmentRoot) {
        Remove-Item -LiteralPath $environmentRoot -Recurse -Force
    }
}

function Wait-CloneReady {
    param([string]$Name, [int]$Ordinal, [string]$Profile)
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        Start-Sleep -Milliseconds 500
        $record = Get-CimInstance Win32_Service -Filter "Name='$Name'"
        $servicePid = if ($null -eq $record) { 0 } else { [int]$record.ProcessId }
        $grpc = 50062 + $Ordinal
        $rest = 5062 + $Ordinal
        $listeners = @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
            Where-Object { $_.LocalPort -in @($grpc, $rest) -and $_.OwningProcess -eq $servicePid })
        $certificatesReady = (Test-Path -LiteralPath (Join-Path $Profile 'server.crt') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $Profile 'server.pem') -PathType Leaf)
        if ($servicePid -gt 0 -and $listeners.Count -eq 2 -and $certificatesReady) {
            return $servicePid
        }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Sandbox controller did not become ready: $Name"
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'The direct-controller sandbox gate must run in an elevated PowerShell session.'
}

$binary = Get-ServiceImagePath 'esm-lm-controller'
$file = Get-Item -LiteralPath $binary
if ($file.Length -ne $expectedLength) { throw 'Official controller binary length is unsupported.' }
if ((Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash -ne $expectedSha256) {
    throw 'Official controller binary SHA-256 is unsupported.'
}
$signature = Get-AuthenticodeSignature -LiteralPath $binary
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $expectedThumbprint) {
    throw 'Official controller signature is unsupported.'
}
$officialProfile = Join-Path $env:ProgramData 'ESP\lmcontroller'
foreach ($caName in @('ca.crt', 'ca.pem')) {
    if (-not (Test-Path -LiteralPath (Join-Path $officialProfile $caName) -PathType Leaf)) {
        throw "Official controller CA file is missing: $caName"
    }
}
foreach ($ordinal in $ordinals) {
    $name = "esm-lm-controller-$ordinal"
    Remove-VerifiedStaleSandboxService -Name $name -Ordinal $ordinal -ExpectedBinary $binary
}

try {
    [IO.Directory]::CreateDirectory($sandboxRoot) | Out-Null
    $pids = New-Object System.Collections.Generic.List[int]
    foreach ($ordinal in $ordinals) {
        $name = "esm-lm-controller-$ordinal"
        $environmentRoot = Join-Path $sandboxRoot "slot-$ordinal"
        $profile = Join-Path $environmentRoot 'ESP\lmcontroller'
        [IO.Directory]::CreateDirectory($profile) | Out-Null
        Copy-Item -LiteralPath (Join-Path $officialProfile 'ca.crt') -Destination (Join-Path $profile 'ca.crt')
        Copy-Item -LiteralPath (Join-Path $officialProfile 'ca.pem') -Destination (Join-Path $profile 'ca.pem')
        Write-ControllerConfig -Path (Join-Path $profile 'config.yml') -Ordinal $ordinal

        Invoke-Sc @('create', $name, 'binPath=', ('"' + $binary + '"'), 'start=', 'demand', 'obj=', 'LocalSystem', 'DisplayName=', "MultiKKT sandbox controller $ordinal") | Out-Null
        $createdServices.Add($name)
        $serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$name"
        New-ItemProperty -LiteralPath $serviceKey -Name Environment -PropertyType MultiString -Value @("ProgramData=$environmentRoot") -Force | Out-Null
        Invoke-Sc @('start', $name) | Out-Null
        $pids.Add((Wait-CloneReady -Name $name -Ordinal $ordinal -Profile $profile))
    }
    if ($pids.Count -ne 2 -or $pids[0] -eq $pids[1]) {
        throw 'Sandbox controllers do not have distinct vendor processes.'
    }
    $success = "DIRECT_CONTROLLER_SANDBOX_OK`r`n" +
        [DateTime]::UtcNow.ToString('o') + "`r`n" +
        "PIDs=$($pids -join ',') ordinals=$($ordinals -join ',')`r`n"
    [IO.Directory]::CreateDirectory((Split-Path -Parent $gateLogPath)) | Out-Null
    [IO.File]::WriteAllText(
        $gateLogPath,
        $success,
        [Text.UTF8Encoding]::new($true))
    Write-Host $success.Trim()
}
finally {
    for ($createdIndex = $createdServices.Count - 1; $createdIndex -ge 0; $createdIndex--) {
        $name = $createdServices[$createdIndex]
        & "$env:SystemRoot\System32\sc.exe" stop $name 2>&1 | Out-Null
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        do {
            Start-Sleep -Milliseconds 250
            $record = Get-CimInstance Win32_Service -Filter "Name='$name'" -ErrorAction SilentlyContinue
        } while ($null -ne $record -and [int]$record.ProcessId -ne 0 -and [DateTime]::UtcNow -lt $deadline)
        & "$env:SystemRoot\System32\sc.exe" delete $name 2>&1 | Out-Null
    }
    if (Test-Path -LiteralPath $sandboxRoot) {
        $resolved = [IO.Path]::GetFullPath($sandboxRoot)
        $expectedPrefix = $sandboxBase.TrimEnd('\') + '\'
        if (-not $resolved.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unexpected sandbox path: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    if (Test-Path -LiteralPath $sandboxBase) {
        foreach ($emptyRunRoot in @(Get-ChildItem -LiteralPath $sandboxBase -Directory -Force)) {
            if (@(Get-ChildItem -LiteralPath $emptyRunRoot.FullName -Force).Count -eq 0) {
                Remove-Item -LiteralPath $emptyRunRoot.FullName -Force
            }
        }
        if (@(Get-ChildItem -LiteralPath $sandboxBase -Force).Count -eq 0) {
            Remove-Item -LiteralPath $sandboxBase -Force
        }
    }
}
