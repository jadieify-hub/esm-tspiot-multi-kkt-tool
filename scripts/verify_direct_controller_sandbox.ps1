[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
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

function Wait-CloneReady {
    param([string]$Name, [int]$Ordinal, [string]$Profile)
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        Start-Sleep -Milliseconds 500
        $record = Get-CimInstance Win32_Service -Filter "Name='$Name'"
        $pid = if ($null -eq $record) { 0 } else { [int]$record.ProcessId }
        $grpc = 50062 + $Ordinal
        $rest = 5062 + $Ordinal
        $listeners = @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
            Where-Object { $_.LocalPort -in @($grpc, $rest) -and $_.OwningProcess -eq $pid })
        $certificatesReady = (Test-Path -LiteralPath (Join-Path $Profile 'server.crt') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $Profile 'server.pem') -PathType Leaf)
        if ($pid -gt 0 -and $listeners.Count -eq 2 -and $certificatesReady) {
            return $pid
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
    if ($null -ne (Get-Service -Name $name -ErrorAction SilentlyContinue)) {
        throw "Sandbox service name is already occupied: $name"
    }
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
    Write-Host "DIRECT_CONTROLLER_SANDBOX_OK PIDs=$($pids -join ',') ordinals=$($ordinals -join ',')"
}
finally {
    foreach ($name in @($createdServices | Select-Object -Reverse)) {
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
}
