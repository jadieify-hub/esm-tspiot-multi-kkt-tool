# LM Gateway Windows Service Provisioning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** После обязательной проверки совместимости официального контроллера добавить компактный привилегированный помощник, который безопасно создает, обновляет и удаляет отдельные Windows-службы контроллера ЛМ ЧЗ, а также завершить единый WinForms-интерфейс «ККТ → контроллер → целевой ЛМ».

**Architecture:** Чистый `LmGatewayPlanner` строит детерминированный план на ККТ, `LmGatewayLifecycleWorkflow` координирует один привилегированный batch, проверку слушателей и уже реализованную привязку ЕСМ. SCM-операции изолированы в маленьком `net48`-помощнике с типизированным named-pipe контрактом и native Windows API. Тот же защищённый EXE имеет отдельный SCM-only supervisor mode: служба с restricted SID запускает проверенный официальный контроллер дочерним процессом и заменяет только его process-local `ProgramData` на производный профиль. UI вынесен в отдельный `LmGatewayPage`; удаление разрешено только для службы, подтвержденной app-owned manifest, ImagePath и меткой в описании (либо валидным pending journal вместе с оставшимися признаками), и завершается автоматической очисткой профиля.

**Tech Stack:** C# 5-compatible syntax, WinForms, `net48` helper, `net48;net8.0` shared library, Windows SCM/Registry/IP Helper API through P/Invoke, `DataContractJsonSerializer`, existing package-free test style.

**Spec:** `docs/superpowers/specs/2026-08-26-multi-inn-lm-gateways-design.md`

**Prerequisite:** Все задачи `2026-08-26-lm-gateway-esm-binding.md` завершены, прошли ревью и находятся в текущей ветке.

## Execution amendment: private compatibility track (2026-08-27)

Этот раздел переопределяет прежние ссылки плана на единый `PublicSourceReady`:

- техническое исследование и private-реализацию открывает `TechnicalCompatibilityReady` для конкретной версии;
- `PublicSourceReady` нужен только перед переводом ветки/репозитория в public или публикацией сборки с этой функцией;
- пока publication gate не пройден, код, tests, docs и сборки с vendor-specific фактами остаются private;
- локальные raw evidence, бинарники вендора, профили и секреты не попадают в Git даже в private-режиме;
- первая исследуемая версия — `1.6.3.2`, installer SHA-256 `822e047dbef62cbdbe2cf1ae22c457f43930c574fbcf265170987b9c7eae91e7`, signer `JSC ESP`;
- каждая новая версия требует нового capability profile и повторного technical gate; wildcard-версии не поддерживаются.

**Toolchain prerequisite:** До первого RED выполнить приведенный ниже preflight в той же PowerShell-сессии. Нужны .NET 8 SDK, .NET Framework 4.8 Targeting/Developer Pack и Visual Studio Build Tools/MSBuild; для генерации инструкции также нужен уже зафиксированный проектом Python toolchain. Системный runtime/`csc.exe` не заменяет SDK/targeting pack для SDK-style helper/WinForms-проектов. Если preflight не проходит, реализацию не начинать и не объявлять локальную матрицу пройденной; сначала установить компоненты либо выполнять полный цикл на подготовленном CI/VM.

```powershell
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio Installer/vswhere is required.' }
$msbuild = & $vswhere -products * -requires Microsoft.Component.MSBuild `
  -find 'MSBuild\Current\Bin\MSBuild.exe' | Select-Object -First 1
$net48Reference = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET 8 SDK is required.' }
if (-not (dotnet --list-sdks | Select-String -Pattern '^8\.')) { throw '.NET 8 SDK is required.' }
if (-not (Test-Path -LiteralPath $net48Reference)) { throw '.NET Framework 4.8 Targeting Pack is required.' }
if (-not $msbuild -or -not (Test-Path -LiteralPath $msbuild)) { throw 'Visual Studio Build Tools/MSBuild is required.' }
```

Все последующие вызовы MSBuild используют `& $msbuild`; переменная сохраняется в рабочей PowerShell-сессии выполнения плана.

## Global Constraints

- Task 1 — жёсткий `TechnicalCompatibilityGate`. До `TechnicalCompatibilityReady` запрещено писать код регистрации дополнительной службы.
- Использовать только официальный установленный бинарный файл контроллера. Не включать его в репозиторий или релиз.
- Не использовать и не анализировать сторонний архив как источник реализации.
- Private-репозиторий содержит только compatibility summary/provenance и версионный adapter/capability code после technical gate. Raw vendor/VM characterization хранится вне рабочего дерева.
- Не применять junction/symlink/reparse points, бинарный патчинг, UPX, копирование полного штатного профиля, `taskkill`, PowerShell или `sc.exe` в продукте.
- Основное окно всегда работает без повышения прав. UAC запрашивается только на `Ensure`, `Remove` или `Cleanup` через отдельный helper.
- Имя службы, ImagePath, профиль, service account, аргументы и environment не принимаются от пользователя и вычисляются/проверяются помощником.
- Собственное имя управляемой службы: `krs-esm-lm-` + нормализованный 14-значный серийный номер ККТ.
- Штатная служба показывается только для диагностики и не назначается ККТ, не перенастраивается и не удаляется этой программой.
- Идентичность — серийный номер ККТ/ESM id. ИНН отображается и валидируется, но не является ключом службы.
- Каждая ККТ получает отдельную app-managed службу. Последовательные порты выбираются из конечных capability-approved пулов; `50063` штатной службы не переиспользуется. Helper подтверждает оба порта реальной эксклюзивной bind-проверкой непосредственно перед запуском. Целевой LM endpoint пользователь подтверждает отдельно.
- Пароль не передается helper, не записывается в manifest и не логируется.
- Удаление одной службы автоматически очищает ее профиль, manifest и все app-owned метаданные после подтвержденного исчезновения SCM/process/listeners. Если файл занят, UI предлагает `Повторить очистку`.
- Поскольку документированного API отвязки нет, удаление не меняет LM-настройку ЕСМ и всегда показывает это ограничение.
- При неизвестном состоянии не выполнять разрушительный rollback; повторно прочитать состояние и поставить `RequiresAttention`.
- Один запуск создания/обновления использует `EnsureBatch` не более чем для 32 ККТ и один UAC. Удаление остается отдельной одноэлементной операцией и отдельным UAC.
- До любой SCM-мутации создавать crash journal и брать machine-wide плюс per-KKT mutex; при старте helper сначала reconciles незавершенные операции.
- Для `1.6.3.2` разрешён только доказанный capability mode: supervisor заменяет `ProgramData` в environment block своего дочернего процесса. Machine-wide/SCM environment, произвольные environment/arguments из UI/IPC и любой fallback запрещены.
- Elevation разрешена только из не доступного обычному пользователю каталога установки и только при split-token повышении того же локального администратора. Portable/user-writable режим поддерживает только просмотр и binding-only.
- Реализация остается C# 5-compatible и без новых внешних пакетов.
- После каждой задачи RED → GREEN → commit. Не пушить и не публиковать без отдельного указания.

---

### Task 1: Compatibility gate на официальном контроллере

**Files:**

- Create: `docs/research/2026-08-26-official-lm-controller-compatibility.md`
- Modify: `docs/research/2026-08-26-lm-gateway-provenance.md`

**Environment:**

- VM requirements below apply after Step 0 verifies the exact user-supplied installer identity and records `CharacterizationReady`; the installer is never executed on the host.
- Disposable Windows 10/11 VM with PowerShell 5.1 and a clean snapshot for characterization.
- Separate Windows 7 SP1 VM only if the supported official controller version itself declares Windows 7 support; otherwise service provisioning is disabled on Windows 7 while viewing/binding remain available.
- Official controller installer obtained from the authorized delivery channel.
- No production KKT, INN, LM credentials or company network.
- Raw evidence root outside the repository: `%LOCALAPPDATA%\KRS\MultiKKT\Research\<EvidenceSetId>`, readable only by the current user and local administrators. Do not create a raw-evidence directory under the workspace.

**Interfaces:**

- Produces a private human-readable compatibility summary for exactly one official controller version plus a separate local evidence pack.
- Produces `CharacterizationReady`, then `TechnicalCompatibilityReady` or `TechnicalCompatibilityRejected`; only `TechnicalCompatibilityReady` permits Tasks 2–13.
- Tests isolation candidates in this order: official data/config switch, separate non-reparse execution path/hard link, app-owned supervisor. Junction is an experimental negative/control test only and cannot become production behavior without a separate threat model and approved spec change.
- Maintains `PublicSourcePending/Ready/Rejected` independently; this status controls publication, not VM access.
- A fact observed only in VM may be encoded solely in the private, exact-version capability profile. It is not thereby approved for public source/builds.

- [x] **Step 0: Verify the official package and open only the technical characterization gate**

Use the user-supplied `esm-lm-controller_1.6.3.2-windows-setup.exe`. Before mapping it into Sandbox, verify filename, size, SHA-256, Authenticode status, signer subject, certificate chain/code-signing EKU and file/product version. The expected installer SHA-256 is `822e047dbef62cbdbe2cf1ae22c457f43930c574fbcf265170987b9c7eae91e7`, expected signer is `JSC ESP`, and expected version is `1.6.3.2`. A mismatch is `TechnicalCompatibilityRejected`; never offer an override.

Record the four publication groups below in the compatibility summary, but do not wait for an answer before private VM characterization:

1. the exact data/config-directory switch, Windows quoting rules and per-process isolation semantics;
2. the minimum profile schema discriminator, object paths, field names/types and serialization needed for local gRPC/REST ports and target LM address/port;
3. the exact public encoding of service account, dependencies, start mode and recovery actions;
4. any vendor-specific listener/bind, executable identity or service metadata literal that will be compiled into public source or represented in public tests/fixtures.

For each group, record whether it is supported by an official public URL/permission or only by private black-box evidence. Do not commit private correspondence, personal names, email addresses, raw paths, raw schemas or attachments.

If a publication group lacks a complete basis, keep `PublicSourcePending` and the repository/build private. Set `CharacterizationReady` and continue to Step 1 only after the installer identity check passes and Windows Sandbox is available with networking disabled and a read-only installer mapping. Tasks 2–13 still remain blocked until Step 7 produces `TechnicalCompatibilityReady`.

- [x] **Step 1: Capture the clean VM baseline**

On the host, capture the reviewed repository commit before launching the VM:

```powershell
$repositoryHead = (& git rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $repositoryHead -notmatch '^[0-9a-fA-F]{40,64}$') {
  throw 'A committed repository HEAD is required before VM characterization.'
}
```

Pass that exact value to the VM baseline script as the mandatory `-RepositoryHead` parameter. Do not clone the repository or install/invoke Git inside the VM. Save and run the following block as a script in the VM; it creates one evidence set outside the repository and records the baseline:

```powershell
param(
  [Parameter(Mandatory = $true)]
  [ValidatePattern('^[0-9a-fA-F]{40,64}$')]
  [string]$RepositoryHead
)

$evidenceSetId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N')
$evidenceRoot = Join-Path $env:LOCALAPPDATA ('KRS\MultiKKT\Research\' + $evidenceSetId)
New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null
$evidenceAcl = New-Object System.Security.AccessControl.DirectorySecurity
$evidenceAcl.SetAccessRuleProtection($true, $false)
$inheritance = [System.Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
$propagation = [System.Security.AccessControl.PropagationFlags]::None
$fullControl = [System.Security.AccessControl.FileSystemRights]::FullControl
@([System.Security.Principal.WindowsIdentity]::GetCurrent().Name, 'BUILTIN\Administrators', 'NT AUTHORITY\SYSTEM') | ForEach-Object {
  $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($_, $fullControl, $inheritance, $propagation, 'Allow')
  [void]$evidenceAcl.AddAccessRule($rule)
}
Set-Acl -LiteralPath $evidenceRoot -AclObject $evidenceAcl
Get-Service | Sort-Object Name | Select-Object Name,Status,StartType | Export-Csv (Join-Path $evidenceRoot 'services-before.csv') -NoTypeInformation
Get-ChildItem -LiteralPath "$env:ProgramData" -Force | Select-Object FullName,LastWriteTime | Export-Csv (Join-Path $evidenceRoot 'programdata-before.csv') -NoTypeInformation
$evidenceIndex = [ordered]@{
  EvidenceSetId = $evidenceSetId
  CreatedUtc = [DateTime]::UtcNow.ToString('o')
  RepositoryHead = $RepositoryHead.ToLowerInvariant()
  RepositoryHeadSource = 'HostSupplied'
  Items = @(
    [ordered]@{ EvidenceId = 'baseline-services'; Method = 'Get-Service sorted export'; File = 'services-before.csv' },
    [ordered]@{ EvidenceId = 'baseline-programdata'; Method = 'ProgramData top-level listing'; File = 'programdata-before.csv' }
  )
}
$evidenceIndex | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidenceRoot 'evidence-index.json') -Encoding UTF8
```

Verify the resulting DACL has only the current user, local administrators and `SYSTEM`. Append one indexed item for every later command/output, then take a VM snapshot. Do not execute any third-party comparison utility. Nothing under `$evidenceRoot` is copied into Git.

- [x] **Step 2: Install only the official controller and capture identity**

After interactive installation identify only service IDs present in the before/after diff. Do not select a service by a broad name regex. For each new service record:

```powershell
$beforeNames = @(Import-Csv (Join-Path $evidenceRoot 'services-before.csv') | ForEach-Object { $_.Name })
$afterServices = @(Get-WmiObject Win32_Service)
$newServices = @($afterServices | Where-Object { $beforeNames -notcontains $_.Name })
if ($newServices.Count -ne 1) { throw 'Нужна ручная проверка состава новых служб установщика' }
$officialService = $newServices[0]
$officialService | Select-Object Name,DisplayName,PathName,StartMode,StartName,State | Format-List
$imagePathMatch = [regex]::Match($officialService.PathName, '^\s*"(?<exe>[^\"]+\.exe)"(?:\s|$)|^\s*(?<exe>[^\s\"]+\.exe)(?:\s|$)', 'IgnoreCase')
if (-not $imagePathMatch.Success) { throw 'Не удалось однозначно разобрать ImagePath' }
$binaryPath = $imagePathMatch.Groups['exe'].Value
Get-Item -LiteralPath $binaryPath | Select-Object FullName,Length,@{n='FileVersion';e={$_.VersionInfo.FileVersion}}
Get-AuthenticodeSignature -LiteralPath $binaryPath | Select-Object Status,StatusMessage,@{n='Subject';e={$_.SignerCertificate.Subject}},@{n='Thumbprint';e={$_.SignerCertificate.Thumbprint}}
$sha256 = [System.Security.Cryptography.SHA256]::Create()
$stream = [System.IO.File]::OpenRead($binaryPath)
try { ([BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() } finally { $stream.Dispose(); $sha256.Dispose() }
```

The parser accepts a correctly quoted executable or an unquoted path without whitespace. An unquoted executable path containing whitespace or any ambiguous command line produces `Gate: FAIL`. Record exact service details, resolved root, DACLs, account, dependencies, start/recovery configuration and raw signer output only in the local evidence pack. The private summary may contain version, PE product/architecture, signer identity, SHA-256, high-level trust conclusion and evidence IDs/digests, but no raw SCM/path/DACL dump. Any unprivileged write/modify access to the binary or its ancestors produces FAIL. Do not commit the vendor binary or raw evidence.

- [x] **Step 3: Determine official configuration behavior**

Start and stop only the official service, then diff filesystem and service registry state. Store sanitized raw diffs, observed relative/absolute paths, registry names, field paths/types/defaults and raw CLI output only in the local evidence pack. The private summary contains evidence IDs/digests and conclusions, not raw output or internal schema. Never record certificate/private-key contents, tokens, passwords, organization identifiers or a full production config dump anywhere.

Check in this order:

1. official documentation bundled with the installer;
2. `--help` or equivalent self-documenting CLI output of the official binary in the VM;
3. filesystem/registry writes produced by the official service itself.

Bundled documentation, self-documenting CLI output and observed runtime behavior may support the private exact-version capability profile. Mark every fact with provenance class `OfficialPublic`, `VendorSelfDocumented` or `PrivateBlackBox`. Only `OfficialPublic` or a recorded publication permission may later cross into public source/builds.

Confirm where local gRPC, local REST and target LM address/port are represented. Separately prove that the controller profile itself needs no login/password/token beyond credentials sent to ESM through the documented PUT. If the controller requires any additional per-profile secret, mark the technical gate failed rather than extending the privileged protocol with credentials. Facts supported only by observation are tagged `PrivateBlackBox` and keep the implementation private.

- [x] **Step 4: Prove the exact-version isolation mode**

First test an official document/self-documenting CLI data/config-directory argument. If absent, characterize whether configuration is resolved from the launched executable path or working directory. Then test a separate non-reparse path/hard link and, independently, an app-owned supervisor. Do not patch or unpack the binary, copy secrets/full profiles or carry a test mechanism into production by assumption. A junction may be used only as a final VM control to distinguish path resolution behavior; it cannot produce `TechnicalCompatibilityReady` under the current spec.

Result for `1.6.3.2`: CLI has no data/config switch; process-local `ProgramData` is the only selected discriminator and `ALLUSERSPROFILE` is ignored. Two simultaneous terminal-mode child processes used separate profiles, local listeners and target LM endpoints in two clean runs. A third control started from config only and independently generated CA/server certificates and keys. Production therefore uses only an app-owned supervisor that sets the single pinned environment key internally; it never writes SCM-registry or machine-wide environment.

For the test service use an obvious temporary name not equal to the product's future name, a new empty data directory and the same verified binary. Create it only inside the VM. Confirm all of the following:

- the second service generates or accepts its own profile without reading/writing the first profile;
- each service can listen on a distinct gRPC and REST port;
- changing target LM address/port in the second profile does not alter the first;
- both can run simultaneously;
- uninstalling/stopping the test service leaves the official service operational;
- no junction, copied secret or shared mutable machine-wide key is involved;
- the only environment override is the pinned process-local `ProgramData` set by the app-owned supervisor;
- each production instance uses a restricted unique service SID and the effective profile DACL prevents the second managed service from reading the first profile; this security property is re-proven by the supervisor integration tests before release.

- [x] **Step 5: Prove process/listener ownership**

For both services record service PID and TCP listener PID. A pass requires the listener to belong either to the service PID or to a signed child process whose path and parent relationship can be checked deterministically. Record the readiness evidence available without real credentials: expected listeners plus any official local health/status endpoint. Characterization confirmed both listener families on the expected child PID and independent target LM attempts. Dynamic/excluded port enumeration and the product's `ExclusiveAddressUse=true` probe are Windows implementation tests in Task 7, not vendor-gate assumptions. The official base ports are not part of the managed pools.

- [x] **Step 6: Repeat once from a second clean VM instance**

Do not revert or discard the first VM while its local evidence pack is the only copy. Create a second disposable VM from the same clean base snapshot/clone, use a new `EvidenceSetId`, and repeat the selected isolation procedure from the written notes. A second successful run must produce the same relative paths, schema, port behavior and process ownership. Before disposing either VM, copy both evidence roots to access-controlled host storage outside the repository, re-hash after transfer and retain the VM-local packs until the transferred digests match.

- [x] **Step 7: Write the capability profile and gate decision**

The private summary committed to Git must include only:

- `TechnicalCompatibilityReady` or `TechnicalCompatibilityRejected`, independent publication status, date, supported official version and Windows versions;
- public vendor version, SHA-256, signer identity and high-level trust conclusion;
- links to official public sources and the provenance class of each relied-on fact;
- high-level invariants: the selected exact-version supervisor mode is proven, profiles and service identities are isolated, no production junction/arbitrary environment fallback is used, required listener families are supported;
- target LM authentication conclusion without credentials or internal config paths;
- unsupported conditions, known limitations and two-run conclusion without raw output;
- `EvidenceId` plus SHA-256 for each relied-on local evidence item and a digest of the sorted local `SHA256SUMS` manifest.

The local access-controlled evidence pack must include exact binary/service paths, SCM account/dependencies/start/recovery, SDDL/DACL, raw filesystem/registry diffs, internal paths/schema/field names, exact port pools/endpoints/dual-stack behavior, health observations, process trees, sanitized CLI/installer output, VM/snapshot metadata and per-item hashes. It remains outside the repository and is never attached to a public release.

Pass the technical gate only when every technical requirement in spec section 9 is independently reproducible twice for the exact installer version. A failed publication basis leaves `PublicSourcePending` but does not change the technical result. On technical failure, stop the plan and report the exact missing capability; do not invent a fallback.

- [x] **Step 8: Hash local evidence, update private provenance and commit only the summary**

Generate a sorted `SHA256SUMS` inside `$evidenceRoot`, add its own digest plus referenced `EvidenceId` values to the private summary/provenance, then verify no raw evidence path is inside the worktree. Review the staged diff for absolute user/VM paths, SIDs, account names, internal registry/config paths and raw command output; any hit blocks the commit.

```powershell
$repoRoot = (git rev-parse --show-toplevel).Trim()
$evidenceFull = (Resolve-Path -LiteralPath $evidenceRoot).Path
if ($evidenceFull.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Raw evidence must be outside the repository.' }

$sumsPath = Join-Path $evidenceRoot 'SHA256SUMS.txt'
Get-ChildItem -LiteralPath $evidenceRoot -File -Recurse |
  Where-Object { $_.FullName -ne $sumsPath } |
  Get-FileHash -Algorithm SHA256 |
  Sort-Object Path |
  ForEach-Object { $_.Hash.ToLowerInvariant() + '  ' + $_.Path } |
  Set-Content -LiteralPath $sumsPath -Encoding UTF8
$evidenceManifestDigest = (Get-FileHash -LiteralPath $sumsPath -Algorithm SHA256).Hash.ToLowerInvariant()
# Manually place only $evidenceManifestDigest and referenced EvidenceId values in the private summary.

git add docs/research/2026-08-26-official-lm-controller-compatibility.md docs/research/2026-08-26-lm-gateway-provenance.md
git diff --cached --check
git commit -m "Проверить совместимость контроллера ЛМ"
```

---

### Task 2: Чистый план локальных портов и управляемых служб

**Files:**

- Create: `src/EsmTspiot.Shared/Models/LmGatewayPorts.cs`
- Create: `src/EsmTspiot.Shared/Models/LmGatewayTarget.cs`
- Create: `src/EsmTspiot.Shared/Models/LmServiceRole.cs`
- Create: `src/EsmTspiot.Shared/Models/LmServiceInventoryItem.cs`
- Create: `src/EsmTspiot.Shared/Models/TcpListenerSnapshotItem.cs`
- Create: `src/EsmTspiot.Shared/Models/TcpPortRange.cs`
- Create: `src/EsmTspiot.Shared/Models/LmManagedPortPolicy.cs`
- Create: `src/EsmTspiot.Shared/Models/LmGatewayDraft.cs`
- Create: `src/EsmTspiot.Shared/Models/LmGatewayPlanAction.cs`
- Create: `src/EsmTspiot.Shared/Models/LmGatewayPlanItem.cs`
- Create: `src/EsmTspiot.Shared/Models/LmGatewayPlan.cs`
- Create: `src/EsmTspiot.Shared/Models/ManagedLmServiceSpec.cs`
- Create: `src/EsmTspiot.Shared/Services/LmServiceIdentity.cs`
- Create: `src/EsmTspiot.Shared/Services/LmGatewayPlanner.cs`
- Modify: `src/EsmTspiot.Shared/Validation/LmGatewayInputValidator.cs`
- Modify: `tests/EsmTspiot.Shared.Tests/Program.cs`

**Interfaces:**

- `LmServiceIdentity.CreateName(string kktSerial) : string` returns exactly `krs-esm-lm-<14 digits>` or throws `ArgumentException`.
- `LmGatewayPlanner.Build(discovery, drafts, inventory, portPolicy, tcpListeners) : LmGatewayPlan`.
- `ManagedLmServiceSpec` contains KKT identity, local ports and target LM endpoint, but no login/password and no arbitrary service/path values.

- [x] **Step 1: Add eight failing tests**

Register:

```csharp
Run("LM service identity is deterministic and independent", LmServiceIdentityIsDeterministicAndIndependent);
Run("LM service identity rejects unsafe serial", LmServiceIdentityRejectsUnsafeSerial);
Run("LM gateway planner never adopts official base service", LmGatewayPlannerNeverAdoptsOfficialBaseService);
Run("LM gateway planner allocates sequential local ports", LmGatewayPlannerAllocatesSequentialLocalPorts);
Run("LM gateway planner keeps owned and skips foreign listener", LmGatewayPlannerKeepsOwnedAndSkipsForeignListener);
Run("LM gateway planner preserves matching managed assignment", LmGatewayPlannerPreservesMatchingManagedAssignment);
Run("LM gateway planner rejects unsafe target and all port conflicts", LmGatewayPlannerRejectsUnsafeTargetAndAllPortConflicts);
Run("Managed LM service spec contains no credentials", ManagedLmServiceSpecContainsNoCredentials);
```

Use three KKT with three different INNs in the primary happy-path fixture. Assert three managed roles and unique pairs. A service marked `VerifiedOfficial` appears only in inventory/occupied ports and never becomes a plan item. Rows with equal INN also remain individual KKT rows.

- [x] **Step 2: Run shared tests and verify RED**

Run both Phase 1 test commands. Expected: missing plan types.

- [x] **Step 3: Implement strict identity and value models**

`LmGatewayTarget` contains only target `Address` and integer `Port`. Credentials remain in the short-lived Phase 1 credential provider. Target validation accepts only a plain IPv4, IPv6 or DNS hostname: reject scheme, userinfo, path, query, fragment, whitespace/CRLF, control characters and ambiguous Unicode. Normalize all loopback representations before conflict checks. `ManagedLmServiceSpec` receives its `ServiceName` and profile identity from `LmServiceIdentity`, never from UI text.

- [x] **Step 4: Implement deterministic allocation**

Rules:

1. sort eligible KKT by ordinal KKT serial for assignment stability;
2. keep an existing valid assignment for the same serial when any listeners on those ports are proven to belong to that managed service; a foreign/ambiguous owner blocks the row instead of silently reallocating a running service;
3. never assign the verified official base service; reserve all of its observed ports;
4. scan the finite gRPC and REST pools frozen in `LmManagedPortPolicy`, increasing each candidate by one;
5. reserve both numbers if either is claimed by another inventory row, foreign/ambiguous TCP listener or an earlier plan item;
6. reject user override outside the capability-approved pools, duplicate across either column, or conflict with a normalized loopback target endpoint;
7. a draft with validation errors must not consume an automatically allocated pair for the next valid row.

Do not call `KktPortPairAllocator`; its `504xx/514xx` domain is unrelated.

- [x] **Step 5: Produce explicit plan actions**

Actions:

- `CreateManagedService`;
- `UpdateManagedService`;
- `StartManagedService`;
- `BindReadyService`;
- `NoChange`;
- `Blocked`.

Each plan item has separate service and binding validation. Plan formatters show target address/port and local ports but never accept or show password.

- [x] **Step 6: Run net8 and net48 tests and verify GREEN**

Expected: all previous tests plus 8 new tests pass in both frameworks.

- [x] **Step 7: Commit**

```powershell
git add src/EsmTspiot.Shared/Models src/EsmTspiot.Shared/Services/LmServiceIdentity.cs src/EsmTspiot.Shared/Services/LmGatewayPlanner.cs src/EsmTspiot.Shared/Validation/LmGatewayInputValidator.cs tests/EsmTspiot.Shared.Tests/Program.cs
git commit -m "Добавить планирование экземпляров контроллера ЛМ"
```

---

### Task 3: Типизированный протокол привилегированного помощника

**Files:**

- Create: `src/EsmTspiot.Shared/Models/LmServiceOperation.cs`
- Create: `src/EsmTspiot.Shared/Models/LmServiceProvisioningItemRequest.cs`
- Create: `src/EsmTspiot.Shared/Models/LmServiceProvisioningBatchRequest.cs`
- Create: `src/EsmTspiot.Shared/Models/LmServiceProvisioningStatus.cs`
- Create: `src/EsmTspiot.Shared/Models/LmServiceProvisioningItemResult.cs`
- Create: `src/EsmTspiot.Shared/Models/LmServiceProvisioningBatchResult.cs`
- Create: `src/EsmTspiot.Shared/Models/LmManifestFingerprint.cs`
- Create: `src/EsmTspiot.Shared/Models/LmRemovalConfirmation.cs`
- Create: `src/EsmTspiot.Shared/Models/LmCleanupConfirmation.cs`
- Create: `src/EsmTspiot.Shared/Models/LmControllerInstallerSelection.cs`
- Create: `src/EsmTspiot.Shared/Models/LmControllerInstallResult.cs`
- Create: `src/EsmTspiot.Shared/Services/CanonicalLmPlanHasher.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/EsmTspiot.ServiceProvisioner.csproj`
- Create: `src/EsmTspiot.ServiceProvisioner/Program.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/NamedPipeProvisioningChannel.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/ProvisioningRequestValidator.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/Properties/AssemblyInfo.cs`
- Create: `tests/EsmTspiot.ServiceProvisioner.Tests/EsmTspiot.ServiceProvisioner.Tests.csproj`
- Create: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`
- Modify: `EsmTspiotTool.sln`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**

- Helper command line: `--pipe <32 hex chars> --operation <32 hex chars>` only; no paths or JSON appear in command line.
- Operations: `InstallControllerVersion`, `EnsureBatch`, `RemoveManaged` and `CleanupManaged` only. Install contains exactly one selected installer; ensure contains 1–32 items; remove/cleanup contain exactly one.
- Exit codes: `0` typed result sent, `2` invalid/authentication request, `3` operation failed, `4` unsupported controller/version.
- Request schema version: integer `1`.

- [x] **Step 1: Add helper protocol tests before implementation**

Register in the new helper test runner:

```csharp
Run("Provisioning protocol accepts bounded ensure batch", ProvisioningProtocolAcceptsBoundedEnsureBatch);
Run("Provisioning protocol rejects oversized or duplicate batch", ProvisioningProtocolRejectsOversizedOrDuplicateBatch);
Run("Provisioning protocol rejects unknown schema or operation", ProvisioningProtocolRejectsUnknownSchemaOrOperation);
Run("Provisioning protocol rejects unsafe item", ProvisioningProtocolRejectsUnsafeItem);
Run("Provisioning pipe authenticates exact protected peer images", ProvisioningPipeAuthenticatesExactProtectedPeerImages);
Run("Provisioning protocol rejects plan hash mismatch", ProvisioningProtocolRejectsPlanHashMismatch);
Run("Provisioning protocol exposes no credentials paths or commands", ProvisioningProtocolExposesNoCredentialsPathsOrCommands);
Run("Installer operation accepts only one verified setup selection", InstallerOperationAcceptsOnlyOneVerifiedSetupSelection);
Run("Installer operation rejects stale or substituted source file", InstallerOperationRejectsStaleOrSubstitutedSourceFile);
```

- [x] **Step 2: Run helper build and verify RED**

```powershell
& $msbuild tests\EsmTspiot.ServiceProvisioner.Tests\EsmTspiot.ServiceProvisioner.Tests.csproj /restore /p:Configuration=Release
```

Expected: project or types do not exist.

- [x] **Step 3: Create compact net48 projects**

Helper project properties and reference:

```xml
<OutputType>Exe</OutputType>
<TargetFramework>net48</TargetFramework>
<PlatformTarget>AnyCPU</PlatformTarget>
<GenerateAssemblyInfo>false</GenerateAssemblyInfo>
```

```xml
<ProjectReference Include="..\EsmTspiot.Shared\EsmTspiot.Shared.csproj" />
```

Helper-test project properties and reference:

```xml
<OutputType>Exe</OutputType>
<TargetFramework>net48</TargetFramework>
<ProjectReference Include="..\..\src\EsmTspiot.ServiceProvisioner\EsmTspiot.ServiceProvisioner.csproj" />
```

Expose only deliberate public test seams or add `InternalsVisibleTo("EsmTspiot.ServiceProvisioner.Tests")` in the helper AssemblyInfo. Inherit KRS metadata, add both projects to the solution with Debug/Release mappings, and add no NuGet packages.

In the same commit add CI steps that build and run helper tests immediately after shared tests. Check `$LASTEXITCODE` after `msbuild` and after the test EXE separately so a later command cannot hide failure.

- [x] **Step 4: Implement strict request/result transport**

Every request contains only:

- schema version;
- operation and `operationId`;
- initiating Windows SID;
- SHA-256 of the canonical redacted plan/confirmation shown in UI;
- the operation-specific payload described below.

`EnsureBatch` contains 1–32 items with KKT serial, local gRPC/REST ports and target LM address/port. `RemoveManaged` and `CleanupManaged` contain one immutable confirmation projection. `InstallControllerVersion` contains exactly one user-selected source path plus the filename, byte length, SHA-256, file/product version and signer identity already shown in UI. The source path is allowed only for this operation; it is never accepted as an ImagePath/profile path, command-line argument or service field.

For `InstallControllerVersion`, the hash covers exact displayed file metadata and the warning that all managed instances will be stopped and moved to `VersionVerificationPending`. For `EnsureBatch`, the hash covers the exact selected rows shown in the plan dialog. For `RemoveManaged`, one canonical confirmation includes KKT serial, derived service name, local ports, manifest fingerprint and the retained-ESM warning. For `CleanupManaged`, it includes KKT serial, observed manifest fingerprint and displayed `CleanupPending` state. The helper reloads local state and rejects a stale/mismatched confirmation before install/stop/delete/cleanup.

Except for the strictly scoped installer source path above, the request does not contain service name, binary path, config path, username, password, environment, shell text or arbitrary arguments. Both sides derive service name from KKT serial when constructing the canonical confirmation; only its hash is transmitted. The helper recalculates all derived values, canonicalizes the request, checks the confirmation hash, limits count, and rejects duplicate KKT or ports before SCM work. Results are per item so one failure does not hide the other outcomes.

The unelevated UI creates exactly one one-shot named-pipe server with an unguessable GUID, `maxNumberOfServerInstances: 1` (never `MaxAllowedServerInstances`) and a DACL for its exact user SID, `SYSTEM` and `Administrators`. The same `operationId` may not create a second server instance; failure to obtain the unique pipe name aborts before UAC. The helper connects as client, obtains the pipe-server PID through `GetNamedPipeServerProcessId`, reads that process token SID and image path, and requires the SID to equal both request `InitiatingSid` and the non-elevated/elevated identity of the same split-token local administrator. The server image must be the exact main EXE in the parent protected product directory with expected filename/product/company/version and no reparse component. Standard-user over-the-shoulder elevation with another account and same-SID wrong-image server both fail before the working payload is read.

UI obtains the client PID with `GetNamedPipeClientProcessId` and accepts only the exact PID returned by `Process.Start`, high integrity, same SID, canonical `Provisioner\EsmTspiot.ServiceProvisioner.exe` path, protected DACL, expected metadata and the helper SHA-256 embedded in main. A same-user process that wins the pipe race is disconnected before receiving the batch. The authentication test above contains explicit subcases for wrong server image, helper from a writable path, wrong client PID, `maxNumberOfServerInstances != 1` and an attempted second server for the same operation.

Messages are length-prefixed and at most 1 MiB. After mutual authentication, UI sends exactly one execute request and may send one typed `CancelAfterCurrentItem` control message with matching `operationId` and monotonic sequence. Helper sends typed per-item progress/results and one final result. No arbitrary message kind, request/result file or TOCTOU path exists.

- [x] **Step 5: Implement deterministic exit behavior**

Even on operational failure, send a redacted per-item batch result when the authenticated pipe remains connected. `Program.Main` contains only parse args → mutually authenticate pipe peers → read/validate/hash → dispatch/control-loop → send result → exit; no SCM logic. Explicit cancel or UI disconnect sets `CancelAfterCurrentItem`: helper finishes and reconciles only the current item, marks untouched items `Cancelled`, writes final machine-side state and is never force-killed.

- [x] **Step 6: Run helper tests and verify GREEN**

```powershell
& $msbuild tests\EsmTspiot.ServiceProvisioner.Tests\EsmTspiot.ServiceProvisioner.Tests.csproj /restore /p:Configuration=Release
& "tests\EsmTspiot.ServiceProvisioner.Tests\bin\Release\net48\EsmTspiot.ServiceProvisioner.Tests.exe"
```

Expected: 9/9 helper tests pass. Shared test matrix still passes.

- [x] **Step 7: Commit**

```powershell
git add .github/workflows/ci.yml EsmTspiotTool.sln src/EsmTspiot.Shared/Models/LmServiceOperation.cs src/EsmTspiot.Shared/Models/LmServiceProvisioningItemRequest.cs src/EsmTspiot.Shared/Models/LmServiceProvisioningBatchRequest.cs src/EsmTspiot.Shared/Models/LmServiceProvisioningStatus.cs src/EsmTspiot.Shared/Models/LmServiceProvisioningItemResult.cs src/EsmTspiot.Shared/Models/LmServiceProvisioningBatchResult.cs src/EsmTspiot.Shared/Models/LmManifestFingerprint.cs src/EsmTspiot.Shared/Models/LmRemovalConfirmation.cs src/EsmTspiot.Shared/Models/LmCleanupConfirmation.cs src/EsmTspiot.Shared/Services/CanonicalLmPlanHasher.cs src/EsmTspiot.ServiceProvisioner tests/EsmTspiot.ServiceProvisioner.Tests
git commit -m "Добавить протокол помощника служб ЛМ"
```

---

### Task 4: Проверка официального бинарного файла и app-owned manifest

**Files:**

- Create: `src/EsmTspiot.ServiceProvisioner/ControllerCapabilityProfile.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/OfficialControllerLocator.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/OfficialControllerInstallerVerifier.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/IFileTrustVerifier.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/WinTrustVerifier.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/ManagedServiceManifest.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/ManagedServiceManifestStore.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/ProvisioningOperationJournal.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/ProvisioningOperationJournalStore.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/PathSafety.cs`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:**

- `OfficialControllerLocator.ResolveVerifiedBinary()` returns a typed immutable record or an error; no user path input.
- `OfficialControllerInstallerVerifier.VerifyStageAndLock(selection)` is the only helper boundary that accepts a user path. It opens the source without write/delete sharing, verifies exact metadata, copies it into an administrator-only staging root while the source handle remains locked, re-verifies the staged file and returns a disposable locked artifact.
- `ManagedServiceManifestStore.Read/Write/Delete` operates only in `%ProgramData%\KRS\MultiKKT\Inventory\<derived-service-name>`.
- Profile paths are separate under `%ProgramData%\KRS\MultiKKT\Profiles\<derived-service-name>` and never exposed to unelevated inventory readers.
- Operation journals live under a third protected `Operations` root and are the crash-recovery source before/through SCM mutation.
- `PathSafety` rejects reparse points at every existing path component.

- [x] **Step 1: Add trust and manifest tests**

Register:

```csharp
Run("Official controller locator enforces protected allowed root", OfficialControllerLocatorEnforcesProtectedAllowedRoot);
Run("Official controller locator enforces full product trust", OfficialControllerLocatorEnforcesFullProductTrust);
Run("Official installer verifier locks verifies and stages atomically", OfficialInstallerVerifierLocksVerifiesAndStagesAtomically);
Run("Official installer verifier rejects filename signer version or hash mismatch", OfficialInstallerVerifierRejectsFilenameSignerVersionOrHashMismatch);
Run("Manifest path is derived only from KKT serial", ManifestPathIsDerivedOnlyFromKktSerial);
Run("Manifest and profile stores reject reparse points", ManifestAndProfileStoresRejectReparsePoints);
Run("Manifest is atomic credential free and projects cleanup state", ManifestIsAtomicCredentialFreeAndProjectsCleanupState);
Run("Manifest ownership mismatch blocks mutation", ManifestOwnershipMismatchBlocksMutation);
```

Use fake filesystem/trust boundaries where practical. Tests must not require an installed controller or administrator rights.

- [x] **Step 2: Run helper tests and verify RED**

Expected: missing trust and manifest types.

- [x] **Step 3: Encode only the passed capability profile**

Transcribe only facts accepted by `TechnicalCompatibilityReady` for the exact version—controller version, allowed installation root/relative executable, PE architecture/product identity, code-signing EKU, valid SHA-256, Authenticode signer, fixed profile/config contract, the single `ProgramData` environment key and supervisor terminal-mode contract—into `ControllerCapabilityProfile`. Tag each fact with its provenance class. A `PrivateBlackBox` fact keeps the branch/build private. Supporting another version requires a new independently reviewed profile; do not accept «any signed file» or a wildcard publisher.

- [x] **Step 4: Implement path and trust checks**

Requirements:

- canonical absolute path under the fixed official install root;
- no alternate data streams or reparse components;
- valid Authenticode chain at verification time;
- exact allowed signer identity for the capability profile;
- code-signing EKU, exact product/architecture, supported version and SHA-256;
- DACL of every install-root ancestor and binary denies unprivileged write/modify;
- repeat the canonical path/reparse/DACL/hash/signature check immediately before `CreateService`/start to narrow replacement races;
- no fallback search in current directory, PATH, temp or user profile.

For installer selection, require basename `esm-lm-controller_<version>-windows-setup.exe`, a regular non-reparse file, exact size/hash/version/signing identity from `ControllerCapabilityProfile`, and a valid Authenticode chain/code-signing EKU. Never execute from Downloads/Desktop directly. Copy through the locked source handle to `%ProgramData%\KRS\MultiKKT\InstallerStaging\<operationId>`, whose complete ancestor chain is administrator/SYSTEM-write only; verify the staged copy again, launch only that path, and delete the staging directory after the child exits. A locked residue becomes app-owned `CleanupPending`, not an untracked file.

- [x] **Step 5: Implement manifest store and ACL**

Create physically separate roots. Inventory-manifest DACL: `SYSTEM` and `Builtin Administrators` full, initiating user SID read-only, no `Builtin Users`/`Authenticated Users`. Its nonsecret state projection contains `LocalLifecycleState`, `OperationId`, `LastCleanupErrorClass` and `UpdatedUtc`, while the authoritative journal remains administrators/SYSTEM only. Profile DACL grants runtime access to the unique per-instance service SID and management access to administrators/SYSTEM. Configure `SERVICE_SID_TYPE_RESTRICTED`; the supervisor and child inherit the restricted token, so the normal `SYSTEM` grant and restricted service-SID grant must both pass. A peer service SID is absent and cannot read the profile. UI/initiating user cannot read profiles. Split immutable config from writable runtime state/logs when the controller supports it. Writes use temp + flush + atomic replace. On every operation validate ACL and reparse status of every existing component.

- [x] **Step 6: Run helper tests and verify GREEN**

Expected: 17/17 helper tests pass.

- [x] **Step 7: Commit**

```powershell
git add src/EsmTspiot.ServiceProvisioner tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs
git commit -m "Проверять официальный контроллер и профили служб"
```

---

### Task 5: Узкий адаптер Windows SCM

**Files:**

- Create: `src/EsmTspiot.ServiceProvisioner/IWindowsServiceApi.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/WindowsServiceApi.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/WindowsServiceRecord.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/SafeServiceHandle.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/ServiceSecurityDescriptor.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LmGatewaySupervisorService.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LmControllerChildProcess.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/RestrictedServiceSid.cs`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:**

- Read operations: query existence, configuration, state, PID, description, dependencies and start mode.
- Mutations: create, update fixed configuration, set typed description marker, start, stop and delete.
- No method accepts shell text or an unvalidated registry path.

- [x] **Step 1: Add contract tests using a fake SCM**

Register:

```csharp
Run("SCM adapter derives service name internally", ScmAdapterDerivesServiceNameInternally);
Run("SCM adapter uses exact verified image path", ScmAdapterUsesExactVerifiedImagePath);
Run("SCM adapter enforces restrictive service DACL", ScmAdapterEnforcesRestrictiveServiceDacl);
Run("SCM adapter never force kills process", ScmAdapterNeverForceKillsProcess);
Run("SCM handles are disposed on every failure", ScmHandlesAreDisposedOnEveryFailure);
Run("SCM configures restricted service SID", ScmConfiguresRestrictedServiceSid);
Run("SCM image path targets only protected supervisor mode", ScmImagePathTargetsOnlyProtectedSupervisorMode);
Run("Supervisor replaces only child ProgramData", SupervisorReplacesOnlyChildProgramData);
Run("Supervisor rejects caller supplied environment and arguments", SupervisorRejectsCallerSuppliedEnvironmentAndArguments);
Run("Supervisor stops child gracefully without process kill", SupervisorStopsChildGracefullyWithoutProcessKill);
```

Static/source assertion for the fourth test is acceptable: helper production sources must contain no `taskkill`, `Kill(`, `sc.exe`, `powershell` or `cmd.exe` call.

- [x] **Step 2: Run tests and verify RED**

Expected: missing SCM abstraction.

- [x] **Step 3: Implement native SCM calls**

Use Unicode APIs and `SafeHandle`:

- `OpenSCManagerW`;
- `OpenServiceW` / `CreateServiceW`;
- `QueryServiceConfigW` / `QueryServiceStatusEx`;
- `ChangeServiceConfigW` / `ChangeServiceConfig2W`;
- `StartServiceW`;
- `ControlService`;
- `DeleteService`;
- `CloseServiceHandle` through `SafeServiceHandle`.

Request the minimum access mask for each operation. Do not grant interactive-user control over the resulting service ACL.

- [x] **Step 4: Apply only the isolation mode selected by Task 1**

Build ImagePath only from the protected provisioner executable plus the internally derived `--supervise <service-name>` mode. The supervisor re-derives service/profile identity, verifies its restricted service SID/token and exact capability profile, then launches the verified official binary in terminal mode. It builds a fresh child environment from the service environment and replaces only the pinned `ProgramData` key with the derived profile root. Do not write per-service SCM environment or accept caller-provided paths, environment keys or vendor arguments. Unit-test Windows command-line quoting, spaces and trailing backslashes.

On service stop, the supervisor sends the proven graceful console-control signal, waits a bounded interval and reports failure if the child remains. It never calls `Process.Kill`, `TerminateProcess`, `taskkill` or closes the job as a kill mechanism. Listener readiness accepts only the verified child PID whose parent is the supervisor PID.

- [x] **Step 5: Mirror only verified official service facts**

Service account, dependencies, start mode and recovery actions come from the exact-version `ControllerCapabilityProfile`, not from UI or discovery of an arbitrary similarly named service. If any required value was not proven by the technical gate, stop before this task; if it is `PrivateBlackBox`, retain the private-only publication status. After creation set and re-read an explicit service-object DACL: `SYSTEM` and Administrators receive only required management rights; operator/initiating SID gets no `CHANGE_CONFIG`, `WRITE_DAC`, `DELETE`, `START` or `STOP`. Do not silently elevate privileges beyond the official service configuration verified in Task 1.

- [x] **Step 6: Run helper tests and verify GREEN**

Expected: 27/27 helper tests pass; helper builds as AnyCPU net48 without external packages.

- [x] **Step 7: Commit**

```powershell
git add src/EsmTspiot.ServiceProvisioner tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs
git commit -m "Добавить безопасный адаптер Windows SCM"
```

---

### Task 6: Изолированный профиль и атомарная конфигурация контроллера

**Files:**

- Create: `src/EsmTspiot.ServiceProvisioner/OfficialLmProfileAdapter.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LmProfileConfiguration.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/AtomicFileWriter.cs`
- Create: `tests/EsmTspiot.ServiceProvisioner.Tests/Fixtures/official-lm-profile-sanitized.json`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:**

- `PrepareEmptyProfile(spec)` creates only the derived app-owned profile.
- `CreateOrLoadConfiguration` uses only the exact-version data-directory/config contract proven in Task 1.
- `ApplyConfiguration` changes only gRPC port, REST port and target LM address/port fields named in the capability profile.
- `ReadConfiguration` returns typed values for reconciliation.

- [x] **Step 1: Create a sanitized schema fixture from the passed exact-version capability profile**

The fixture contains only the minimum valid shape proven by Task 1, with dummy addresses/ports and no copied tokens, certificates, private keys, organization identifiers, values or comments from the observed profile. Add the evidence ID and provenance class. If any field is `PrivateBlackBox`, the fixture and compiled build remain private; never paste the production file wholesale.

- [x] **Step 2: Add profile-adapter tests**

Register:

```csharp
Run("LM profile adapter changes only supported fields", LmProfileAdapterChangesOnlySupportedFields);
Run("LM profile adapter rejects ambiguous schema", LmProfileAdapterRejectsAmbiguousSchema);
Run("LM profile adapter preserves unknown nonsecret fields", LmProfileAdapterPreservesUnknownNonsecretFields);
Run("LM profile adapter writes atomically", LmProfileAdapterWritesAtomically);
Run("LM profile adapter never clones official profile", LmProfileAdapterNeverClonesOfficialProfile);
Run("LM profile adapter detects unsupported controller version", LmProfileAdapterDetectsUnsupportedControllerVersion);
```

- [x] **Step 3: Run tests and verify RED**

Expected: missing profile adapter.

- [x] **Step 4: Implement exactly one proven schema adapter**

Do not add heuristic key search. Require the expected object path, field types and schema discriminator from the exact-version capability profile recorded in Task 1. If schema differs, return `UnsupportedController` before changing a service.

For a new profile, independently generate the minimum supported schema from the exact-version capability profile; never copy the full official profile, CA, certificate or key. Write only local ports and target address/port—never credentials. The controller must generate unique CA/server material on first start; readiness fails if expected generated artifacts are missing or shared with another managed profile. For an owned existing profile, patch the supported fields only while every service/child/listener PID is stopped, then replace the config atomically.

- [x] **Step 5: Run helper tests and verify GREEN**

Expected: 33/33 helper tests pass.

- [x] **Step 6: Commit**

```powershell
git add src/EsmTspiot.ServiceProvisioner tests/EsmTspiot.ServiceProvisioner.Tests
git commit -m "Добавить изолированную конфигурацию контроллера ЛМ"
```

---

### Task 7: Восстанавливаемое batch-создание и обновление службы

**Files:**

- Create: `src/EsmTspiot.ServiceProvisioner/LmServiceProvisioner.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LmServiceOwnershipVerifier.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LmServiceReadinessProbe.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/TcpListenerOwnerReader.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/Program.cs`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:**

- `LmServiceProvisioner.EnsureBatch(request) : LmServiceProvisioningBatchResult`.
- `LmServiceProvisioner.InstallControllerVersion(request) : LmControllerInstallResult`.
- Probe checks service state, service PID and port-owner PID according to Task 1 capability profile.
- No HTTP credentials are involved.

- [x] **Step 1: Add ensure transaction tests**

Register:

```csharp
Run("Ensure creates profile service and listeners in order", EnsureCreatesProfileServiceAndListenersInOrder);
Run("Ensure is no op for matching ready service", EnsureIsNoOpForMatchingReadyService);
Run("Ensure starts matching stopped service", EnsureStartsMatchingStoppedService);
Run("Ensure safely updates owned mismatched service", EnsureSafelyUpdatesOwnedMismatchedService);
Run("Ensure blocks unknown existing service", EnsureBlocksUnknownExistingService);
Run("Ensure rechecks and holds exact exclusive endpoints until start", EnsureRechecksAndHoldsExactExclusiveEndpointsUntilStart);
Run("Ensure rejects listener owned by another process", EnsureRejectsListenerOwnedByAnotherProcess);
Run("Ensure leaves failed new service stopped for diagnosis", EnsureLeavesFailedNewServiceStoppedForDiagnosis);
Run("Ensure writes manifest only after confirmed stages", EnsureWritesManifestOnlyAfterConfirmedStages);
Run("Ensure journal recovers every simulated crash stage", EnsureJournalRecoversEverySimulatedCrashStage);
Run("Ensure serializes concurrent ensure remove and cleanup", EnsureSerializesConcurrentMutations);
Run("Ensure batch continues failures and honors cancel boundary", EnsureBatchContinuesFailuresAndHonorsCancelBoundary);
Run("Ensure batch rejects stale operation result", EnsureBatchRejectsStaleOperationResult);
Run("Install version marks every managed instance verification pending before launch", InstallVersionMarksEveryManagedInstanceVerificationPendingBeforeLaunch);
Run("Install version never runs a substituted or unlocked installer", InstallVersionNeverRunsSubstitutedOrUnlockedInstaller);
Run("Install version leaves services stopped when verification fails", InstallVersionLeavesServicesStoppedWhenVerificationFails);
Run("Ensure clears version pending only after recreated artifacts are ready", EnsureClearsVersionPendingOnlyAfterRecreatedArtifactsAreReady);
```

- [x] **Step 2: Run tests and verify RED**

Expected: missing provisioner/probe.

- [x] **Step 3: Implement the selected-installer version transition**

For `InstallControllerVersion`, validate the canonical confirmation, acquire the machine-wide mutex, call `VerifyStageAndLock`, enumerate only app-owned manifests, and atomically write `VersionVerificationPending` to every managed instance before stopping anything. Stop each managed service normally and verify its service/child/listener PIDs exited; never kill a process. Launch the staged signed installer visibly with no guessed silent/vendor arguments and wait for its exit. The official base service remains owned by the installer.

After exit, re-resolve the installed binary and require exact version, hash, product, architecture, signer and protected path from the selected capability profile. Delete the protected staged copy. Success records the new machine controller version but keeps every old managed instance in `VersionVerificationPending`; the next `EnsureBatch` recreates any version-dependent execution/profile artifact and clears that state for one KKT only after readiness succeeds. Cancellation, installer failure, identity mismatch or staging-cleanup failure never restarts managed services automatically. Removal/cleanup remain available from the saved ownership manifest.

- [x] **Step 4: Implement ownership verification**

Require operation journal/manifest + service marker + exact derived name + verified ImagePath + matching KKT serial. A pending journal alone is not sufficient to delete a service, but it permits deterministic reconciliation with the other ownership facts. The official base service has a separate read-only verification path and is never passed to `EnsureBatch`.

- [x] **Step 5: Implement ensure state machine**

Exact order for new service:

1. authenticate and validate the whole batch/hash, then acquire a machine-wide operation mutex;
2. for each KKT in stable serial order acquire a per-KKT mutex and reconcile any pending journal;
3. reject an unsupported machine version; accept `VersionVerificationPending` only as an explicit recreate/update action, then repeat official binary path/DACL/hash/signature checks;
4. re-read listeners, then bind both managed ports to the exact capability-profile endpoints with `ExclusiveAddressUse=true`, no `ReuseAddress` and explicit IPv6-only/dual-stack mode; keep every probe socket while preparing the profile and creating the stopped service;
5. atomically create `Preparing` operation journal before any profile/SCM mutation;
6. prepare isolated profile and atomically generate/apply the exact-version capability configuration;
7. create supervisor service stopped with marker, restricted service SID and restrictive service DACL, updating journal stage after success;
8. release the two probe sockets immediately before `StartService`, then start supervisor; supervisor launches the verified child with the derived process-local `ProgramData`;
9. wait bounded time for `Running` and both listeners;
10. verify supervisor/child parentage, exact child identity, restricted profile access and listener ownership;
11. atomically write ready manifest, set journal terminal, then remove the completed journal;
12. release per-KKT mutex and continue the next batch item even after an item-level failure.

For an owned existing service reconcile current state first. A matching ready service verifies that its current listeners have the expected owner and returns `Unchanged` without trying to bind over itself. A stopped service or config update writes `Updating`, stops normally if needed, waits for service/child/listener PIDs to exit, takes the same exclusive probe sockets, updates atomically, releases them immediately before start, then starts and probes. A race after socket release is a normal item-level readiness failure: never kill the new owner and never perform ESM PUT. Unknown state returns `RequiresAttention` without delete/recreate. Concurrent `EnsureBatch`/`RemoveManaged`/`CleanupManaged` cannot pass the mutex boundary.

The two renamed tests above contain subcases for occupied IPv4, IPv6, dual-stack, OS-excluded candidate and a race immediately after probe release. Cancellation before the first item changes nothing; cancellation received during an item completes/reconciles that item only, marks every untouched item `Cancelled`, and returns a partial typed result. Pipe loss follows the same boundary.

- [x] **Step 6: Implement conservative failure behavior**

If a just-created service fails readiness, request a normal stop, keep the service/profile, retain a terminal failure journal for reconciliation, and return exact stage. Do not delete automatically. If SCM result is ambiguous, re-query; if still ambiguous, return `RequiresAttention`. Tests inject a crash after profile creation, `CreateService`, start and readiness, then prove the next run never creates an unowned orphan.

- [x] **Step 7: Run helper tests and verify GREEN**

Expected: 50/50 helper tests pass.

- [x] **Step 8: Commit**

```powershell
git add src/EsmTspiot.ServiceProvisioner tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs
git commit -m "Добавить создание экземпляров контроллера ЛМ"
```

---

### Task 8: Безопасное удаление управляемой службы

**Files:**

- Create: `src/EsmTspiot.ServiceProvisioner/LmServiceRemovalWorkflow.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/LmServiceProvisioner.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/Program.cs`
- Modify: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:**

- `LmServiceRemovalWorkflow.RemoveManaged(request) : LmServiceProvisioningItemResult` and `CleanupManaged(request)`.
- Accepts one KKT identity; derives every target and uses the same mutex/journal boundary.
- Result distinguishes `RemovedLocalArtifactsBindingRetained`, `CleanupPending`, `MarkedForDelete`, `RemovalBlocked`, `RequiresAttention`.

- [x] **Step 1: Add ten removal tests**

Register:

```csharp
Run("Remove deletes only fully owned freshly confirmed service", RemoveDeletesOnlyFullyOwnedFreshlyConfirmedService);
Run("Remove blocks official base service", RemoveBlocksOfficialBaseService);
Run("Remove blocks service on marker mismatch", RemoveBlocksServiceOnMarkerMismatch);
Run("Remove blocks service on image mismatch", RemoveBlocksServiceOnImageMismatch);
Run("Remove stops before delete and waits", RemoveStopsBeforeDeleteAndWaits);
Run("Remove deletes profile manifest and app metadata", RemoveDeletesProfileManifestAndAppMetadata);
Run("Remove reports marked for delete until SCM absence", RemoveReportsMarkedForDeleteUntilScmAbsence);
Run("Remove does not claim ESM binding was cleared", RemoveDoesNotClaimEsmBindingWasCleared);
Run("Remove projects cleanup pending across restart and supports retry", RemoveProjectsCleanupPendingAcrossRestartAndSupportsRetry);
Run("Remove remains possible after vendor binary update", RemoveRemainsPossibleAfterVendorBinaryUpdate);
```

- [x] **Step 2: Run tests and verify RED**

Expected: missing removal workflow/status behavior.

- [x] **Step 3: Implement exact removal preconditions**

Acquire the machine-wide and per-KKT mutex and reconcile pending journals first. Then all must match:

- derived app-owned service name;
- valid manifest or pending operation journal for the KKT plus all remaining ownership facts;
- own typed service-description marker;
- exact canonical ImagePath/profile switch recorded in the manifest and still confined to the protected official/app-owned roots;
- managed profile path without reparse components.

If the service exists but any ownership/path/confirmation-fingerprint check fails, return `RemovalBlocked`; do not stop it. The first removal test includes a stale/mismatched confirmation hash subcase and proves no stop call occurs. Deletion must remain possible when the official executable was legitimately updated or removed: creation/start still require the exact supported capability profile, but removal never executes that binary and therefore validates the stored canonical path and ownership boundary instead of requiring the old file hash/version.

- [x] **Step 4: Implement complete local removal**

For an owned service:

1. atomically write journal state `Deleting` and query actual state/PID;
2. request normal stop if needed;
3. wait for service PID, allowed child PID and listener PID to exit;
4. call `DeleteService`, then close every helper handle to the service;
5. bounded-poll `OpenService`; distinguish `MarkedForDelete`, `AccessDenied`, timeout and exact `ERROR_SERVICE_DOES_NOT_EXIST`;
6. only after exact absence write journal state `Cleaning`, then atomically update the still-present manifest projection to `CleanupPending` with operation/error metadata;
7. re-resolve profile/inventory paths from KKT serial, verify absolute roots, DACL and no reparse component;
8. recursively delete only the derived profile and any other app-owned metadata explicitly named by the capability profile; verify them absent, then delete manifest;
9. remove operation journal last;
10. return `RemovedLocalArtifactsBindingRetained` and the Russian warning that ESM settings were not cleared.

Never call `taskkill`, delete the official binary or touch the base service. Never recursively delete a caller-supplied or unresolved path.

- [x] **Step 5: Handle interrupted cleanup**

If SCM is already absent but a valid manifest or `Deleting/Cleaning` journal and ownership artifacts remain, continue only cleanup. A valid manifest plus absent SCM is conservatively projected as `CleanupPending` even after a crash before the state update. If a file is locked or deletion cannot be confirmed, retain both manifest projection and journal and return `CleanupPending`; after UI restart a later `CleanupManaged` repeats all SCM/manifest/journal/path/ACL/reparse checks and cleanup. The test closes/reopens the inventory reader between failure and retry. If neither service nor valid manifest/journal exists, return `RemovalBlocked`, not success.

- [x] **Step 6: Run helper tests and verify GREEN**

Expected: 60/60 helper tests pass. Source scan shows no forced-process termination or shell command.

- [x] **Step 7: Commit**

```powershell
git add src/EsmTspiot.ServiceProvisioner tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs
git commit -m "Добавить удаление управляемых служб ЛМ"
```

---

### Task 9: Unelevated inventory, helper client and local readiness adapter

**Files:**

- Create: `src/EsmTspiot.Shared/Services/ILmServiceProvisioner.cs`
- Create: `src/EsmTspiot.Shared/Services/ILmGatewayProbe.cs`
- Create: `src/EsmTspiot.WinForms.Shared/LmServiceProvisionerClient.cs`
- Create: `src/EsmTspiot.WinForms.Shared/LmServiceInventoryReader.cs`
- Create: `src/EsmTspiot.WinForms.Shared/LmGatewayProbe.cs`
- Create: `src/EsmTspiot.WinForms.Shared/ProvisionerProcessLauncher.cs`
- Create: `build/EsmTspiot.Provisioner.targets`
- Modify: `src/EsmTspiot.Legacy.WinForms/EsmTspiot.Legacy.WinForms.csproj`
- Modify: `src/EsmTspiot.Modern.WinForms/EsmTspiot.Modern.WinForms.csproj`
- Modify: `tests/EsmTspiot.Shared.Tests/Program.cs`

**Interfaces:**

- `ILmServiceProvisioner.InstallControllerVersionAsync(selection, operationId, confirmationHash, token)`, `EnsureBatchAsync(items, operationId, planHash, token)`, `RemoveAsync(LmRemovalConfirmation confirmation, token)` and `CleanupAsync(LmCleanupConfirmation confirmation, token)`. Only install accepts one selected source path under the strict verifier contract from Task 4. Each other confirmation contains a new operation ID, KKT serial, observed manifest fingerprint and canonical hash of the exact dialog/inventory projection; it contains no arbitrary path or command.
- `ILmGatewayProbe.ProbeAsync(serviceSpec, token)` returns typed service/listener readiness without credentials.
- Windows adapters remain outside `EsmTspiot.Shared`.

- [x] **Step 1: Add shared interface contract tests**

Register:

```csharp
Run("LM provisioner contract exposes no arbitrary command", LmProvisionerContractExposesNoArbitraryCommand);
Run("LM probe result separates service and listener state", LmProbeResultSeparatesServiceAndListenerState);
Run("LM provisioning progress contains no credentials", LmProvisioningProgressContainsNoCredentials);
```

- [x] **Step 2: Run tests and verify RED**

Expected: missing interfaces/results.

- [x] **Step 3: Implement helper invocation**

`LmServiceProvisionerClient`:

1. verifies the current user has a split local-administrator token and the app/helper directory is not writable by unprivileged SIDs; otherwise returns binding-only capability before UAC;
2. creates a GUID named-pipe server with the exact DACL and one `operationId`;
3. resolves the helper only at fixed `<app-root>\Provisioner\EsmTspiot.ServiceProvisioner.exe` and verifies expected product/company/version plus the helper SHA-256 embedded in main by the build target;
4. computes the canonical hash from the exact selected plan, removal dialog or cleanup projection; remove/cleanup include the observed manifest fingerprint and get a new operation ID;
5. launches with `Verb = "runas"`, `UseShellExecute = true` and only `--pipe <guid> --operation <guid>`;
6. authenticates the connected helper process and sends one schema-1 batch;
7. waits asynchronously without blocking UI and accepts only a result with matching `operationId`/plan hash;
8. closes the pipe and drops credential-free batch references.

Cancellation before UAC/helper connection aborts the whole request. After mutual pipe authentication the client sends `CancelAfterCurrentItem` and continues reading until the current item is reconciled and the helper returns partial results; pipe loss has the same helper-side effect. It never kills the elevated process. Further mutations remain blocked until final result or inventory/journal reconciliation determines actual state.

- [x] **Step 4: Implement read-only inventory**

Read only inventory manifests permitted to the initiating SID; never read profile directories or protected journals. Query SCM with read-only access. A manifest marked `CleanupPending`, or any valid managed manifest whose SCM service is absent, is classified `CleanupPending` so cleanup remains visible after restart. Classify rows as:

- `VerifiedOfficial`;
- `Managed`;
- `Removed`;
- `CleanupPending`;
- `Unknown`;
- `Missing`.

A name match alone never yields `Managed`. Do not request UAC for `Обновить`.

- [x] **Step 5: Implement readiness probe**

Use the same capability ownership rules as helper, but read-only. Confirm service state and both local ports; map listener PID with the Windows IP Helper API. The UI may use a non-authoritative socket/listener snapshot for planning, but the helper's exclusive bind immediately before mutation is the authority. Do not parse localized `netsh` output, send LM credentials or perform a production code-check request.

- [x] **Step 6: Link shared WinForms source files into both targets**

Add explicit `<Compile Include=... Link=...>` entries for all new Windows adapter files to both WinForms projects. Import `build/EsmTspiot.Provisioner.targets` in both apps. The target:

- builds the helper before WinForms;
- computes helper SHA-256;
- generates `$(IntermediateOutputPath)ProvisionerIntegrity.g.cs` before compile and includes it in main;
- after `Build`, copies helper EXE and its net48 `EsmTspiot.Shared.dll` into `$(TargetDir)Provisioner\` for F5/local smoke;
- after `Publish`, copies the same helper closure into `$(PublishDir)Provisioner\`; the fixed subdirectory prevents its net48 `EsmTspiot.Shared.dll` from colliding with the Modern app's net8 assembly;
- fails if helper/main product version or KRS company metadata differ.

The embedded hash is an integrity check, while protected-directory ACL is the trust boundary; an external editable hash file is not used.

- [x] **Step 7: Run shared tests and compile both WinForms targets**

```powershell
dotnet run --project tests\EsmTspiot.Shared.Tests\EsmTspiot.Shared.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $msbuild src\EsmTspiot.Legacy.WinForms\EsmTspiot.Legacy.WinForms.csproj /restore /p:Configuration=Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build src\EsmTspiot.Modern.WinForms\EsmTspiot.Modern.WinForms.csproj -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$publishSmoke = Join-Path $env:TEMP ("MultiKKT-modern-publish-" + [Guid]::NewGuid().ToString("N"))
dotnet publish src\EsmTspiot.Modern.WinForms\EsmTspiot.Modern.WinForms.csproj -c Release -o $publishSmoke
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (-not (Test-Path -LiteralPath (Join-Path $publishSmoke 'Provisioner\EsmTspiot.ServiceProvisioner.exe'))) { throw 'Helper missing from PublishDir' }
if (-not (Test-Path -LiteralPath (Join-Path $publishSmoke 'Provisioner\EsmTspiot.Shared.dll'))) { throw 'Helper net48 dependency missing from PublishDir' }
```

Also run `dotnet publish` for Modern into a clean temporary publish directory and assert `Provisioner\EsmTspiot.ServiceProvisioner.exe` plus its net48 `EsmTspiot.Shared.dll` exist there and match the embedded helper hash. Expected: 107 shared tests pass; Legacy and Modern build/publish with zero errors; each build/publish output contains the correct isolated helper closure. No UI entry point exists yet.

- [x] **Step 8: Commit**

```powershell
git add build/EsmTspiot.Provisioner.targets src/EsmTspiot.Shared/Services/ILmServiceProvisioner.cs src/EsmTspiot.Shared/Services/ILmGatewayProbe.cs src/EsmTspiot.WinForms.Shared src/EsmTspiot.Legacy.WinForms/EsmTspiot.Legacy.WinForms.csproj src/EsmTspiot.Modern.WinForms/EsmTspiot.Modern.WinForms.csproj tests/EsmTspiot.Shared.Tests/Program.cs
git commit -m "Подключить помощник служб к приложению"
```

---

### Task 10: Общий lifecycle workflow создания, привязки и удаления

**Files:**

- Create: `src/EsmTspiot.Shared/Models/LmGatewayLifecycleStatus.cs`
- Create: `src/EsmTspiot.Shared/Models/LmGatewayLifecycleResult.cs`
- Create: `src/EsmTspiot.Shared/Models/LmGatewayLifecycleOutcome.cs`
- Create: `src/EsmTspiot.Shared/Models/LmGatewayLifecycleProgress.cs`
- Create: `src/EsmTspiot.Shared/Services/LmGatewayLifecycleWorkflow.cs`
- Create: `src/EsmTspiot.Shared/Services/LmGatewayRemovalWorkflow.cs`
- Modify: `tests/EsmTspiot.Shared.Tests/Program.cs`

**Interfaces:**

- Creation order: one `EnsureBatch` UAC → per-item probe → per-item ESM binding.
- Removal order: explicit UI confirmation → one helper remove → automatic profile/metadata cleanup → inventory refresh; no ESM unbind call.
- Results preserve separate service and binding statuses.

- [ ] **Step 1: Add lifecycle tests**

Register:

```csharp
Run("LM lifecycle ensures probes then binds", LmLifecycleEnsuresProbesThenBinds);
Run("LM lifecycle never binds failed service", LmLifecycleNeverBindsFailedService);
Run("LM lifecycle continues after one KKT failure", LmLifecycleContinuesAfterOneKktFailure);
Run("LM lifecycle retries binding without reprovisioning", LmLifecycleRetriesBindingWithoutReprovisioning);
Run("LM lifecycle preserves partial outcome on cancellation", LmLifecyclePreservesPartialOutcomeOnCancellation);
Run("LM lifecycle reconciles unknown result before mutation", LmLifecycleReconcilesUnknownResultBeforeMutation);
Run("LM removal workflow removes one selected managed service", LmRemovalWorkflowRemovesOneSelectedManagedService);
Run("LM removal workflow blocks batch and official removal", LmRemovalWorkflowBlocksBatchAndOfficialRemoval);
Run("LM removal outcome warns that ESM binding remains", LmRemovalOutcomeWarnsThatEsmBindingRemains);
```

- [ ] **Step 2: Run tests and verify RED**

Expected: missing lifecycle workflows.

- [ ] **Step 3: Implement creation orchestration**

For all valid selected plan items:

1. exclude blocked rows but retain explicit `Blocked` results for them;
2. send every create/update/start spec in one `EnsureBatchAsync` call and one UAC;
3. merge per-item helper results by KKT serial and reject stale operation/hash results;
4. after each successful/unchanged item call `ProbeAsync` again from the unelevated process;
5. only when gRPC and REST ownership/readiness are confirmed, build the Phase 1 secret-free plan and request that row's short-lived credentials for binding;
6. record service stage and `BindingAccepted`/failure separately;
7. continue the remaining valid KKT after an item failure.

UAC cancellation happens before the batch starts and marks all pending mutations cancelled. Cancellation after helper connection sends `CancelAfterCurrentItem`: lifecycle preserves completed/current reconciled outcomes, marks untouched rows `Cancelled`, never kills helper and waits for or later reconciles the same `operationId` before allowing another mutation.

- [ ] **Step 4: Implement binding-only repair path**

For a ready controller whose prior PUT failed, `RetryBindingAsync` probes again and invokes Phase 1 workflow without calling helper. This path requires no UAC.

- [ ] **Step 5: Implement removal orchestration**

Accept exactly one selected managed inventory record and the canonical hash of the confirmation the user actually accepted. Call helper `RemoveAsync`; when it returns `CleanupPending`, expose `CleanupAsync` with a new operation ID and hash of the visible pending projection as `Повторить очистку`. Read inventory/probe afterward. A changed manifest fingerprint makes the request stale and forces refresh/reconfirmation. Return `RemovedLocalArtifactsBindingRetained` only when SCM, profile, manifest and all app-owned metadata absence are confirmed. Do not call any PUT/DELETE settings endpoint.

- [ ] **Step 6: Run net8 and net48 tests and verify GREEN**

Expected: 116 shared tests pass in both frameworks.

- [ ] **Step 7: Commit**

```powershell
git add src/EsmTspiot.Shared/Models/LmGatewayLifecycle* src/EsmTspiot.Shared/Services/LmGatewayLifecycleWorkflow.cs src/EsmTspiot.Shared/Services/LmGatewayRemovalWorkflow.cs tests/EsmTspiot.Shared.Tests/Program.cs
git commit -m "Добавить управление жизненным циклом контроллеров ЛМ"
```

---

### Task 11: Вкладка «Контроллеры ЛМ ЧЗ» и удаление из интерфейса

**Состояние на 2026-08-27:** безопасный binding-only срез реализован до service gate на базе `LmGatewayBindingSession`, `LmGatewayPage`, Phase 1 discovery/planner/workflow и документированного PUT ЕСМ. Он показывает только зарегистрированные ККТ, хранит drafts/credentials в памяти текущего сеанса и не заявляет read-back. При выполнении Task 11 существующую страницу нужно расширить inventory/helper-функциями, а не заменять; создание, обновление, удаление служб и vendor-specific профиль добавляются только после `TechnicalCompatibilityReady`, а до `PublicSourceReady` остаются private.

**Files:**

- Create: `src/EsmTspiot.WinForms.Shared/LmGatewayPage.cs`
- Create: `src/EsmTspiot.WinForms.Shared/LmGatewayPlanDialog.cs`
- Create: `src/EsmTspiot.WinForms.Shared/LmGatewayRemovalDialog.cs`
- Create: `src/EsmTspiot.WinForms.Shared/LmControllerInstallerPicker.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/MainForm.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/BulkRegistrationDialog.cs`
- Modify: `src/EsmTspiot.Legacy.WinForms/EsmTspiot.Legacy.WinForms.csproj`
- Modify: `src/EsmTspiot.Modern.WinForms/EsmTspiot.Modern.WinForms.csproj`

**Interfaces:**

- New main tab: `Контроллеры ЛМ ЧЗ`.
- `LmGatewayPage` receives base-URL provider, API/workflows, Windows adapters and `Action<string>` logger; it does not reach into private MainForm controls.
- Delete action is enabled only for exactly one verified managed row.
- `LmControllerInstallerPicker.SelectAndInspect(owner)` returns an in-memory `LmControllerInstallerSelection`; the helper remains the authoritative verifier.

- [ ] **Step 1: Build the page layout as a separate UserControl**

Use a resizable `TableLayoutPanel`:

1. installer row with read-only path field, `Выбрать…`, `Установить / проверить версию` and a compact status showing filename, version, signer and shortened SHA-256;
2. toolbar with `Обновить`, `Подготовить план`, `Создать / обновить выбранные`, `Повторить привязку к ЕСМ`, `Удалить службу` and contextual `Повторить очистку`;
3. read-only status grid occupying remaining height;
4. selected-row editor with target LM address/port, login/password, local gRPC/REST ports;
5. status/help line.

Grid columns exactly follow spec section 6.1. Password textbox uses `UseSystemPasswordChar = true`; grid and status never display it.

The picker filter is `esm-lm-controller_*-windows-setup.exe`. Selection performs a non-authoritative read-only preflight and displays a clear mismatch; it never launches the file. Keep the full source path only in the current page session, do not add it to settings/logs/manifest, and clear it after install, cancel or form disposal. `Создать / обновить` remains disabled until the installed controller version is authoritatively verified by the helper.

- [ ] **Step 2: Implement refresh and in-memory editing**

`Обновить` performs Phase 1 KKT discovery and read-only service inventory/probe. It never requests UAC and never reads profile directories. Draft/credentials live only for the current form session. The plan stores no credentials; the page keeps a short-lived credential provider keyed by KKT serial and removes each reference after binding/cancel/close.

`Установить / проверить версию` shows one final confirmation with selected metadata and the exact number of managed services that will be stopped. It sends `InstallControllerVersion` through one helper/UAC operation. After success refresh inventory: rows stay `VersionVerificationPending` until the user explicitly runs create/update for them; do not chain an automatic mass restart.

Do not persist credentials to settings, registry, manifest or logs.

- [ ] **Step 3: Implement plan confirmation dialog**

Show each KKT/INN, action, service role/name, local ports, target LM and validation. The dialog shows only `Credentials: заданы/не заданы`, never a password placeholder/value. Rows with blocking errors are automatically unchecked and remain visible as `Blocked`; the user may run the remaining valid selected rows. Start is enabled when at least one selected row is valid. UAC is requested only after the final start button.

- [ ] **Step 4: Implement sequential execution and cancellation UI**

Reuse the progress/cancellation UX pattern of `BulkRegistrationDialog`, not its business logic. Send all valid create/update rows in one helper batch/UAC, then show current KKT and stage while probing/binding. Closing during work sends `CancelAfterCurrentItem`; explain that the current KKT will finish safely and remaining KKT will not start. Never kill helper; the page remains mutation-locked until operation reconciliation. Already completed actions are not rolled back.

- [ ] **Step 5: Implement removal dialog and button**

Enable `Удалить службу` only when:

- exactly one row is selected;
- role is `Managed`;
- full ownership check succeeded;
- no create/update operation is running.

Dialog displays KKT, INN, service, ports and the warnings from spec, including irreversible deletion of the local profile/metadata and retained ESM binding. Require exact entry of the 14-digit KKT serial. Hash this exact immutable projection together with its manifest fingerprint; after confirmation run only that one remove operation. If inventory changed, close the dialog, refresh and require confirmation again. On full local success show:

```text
Служба и локальные данные удалены. Настройка связи в ЕСМ не очищена и может по-прежнему ссылаться на этот порт.
```

For `CleanupPending`, replace delete with `Повторить очистку`; the app performs cleanup itself and never tells the user to find a folder manually. For official/unknown rows keep both mutation buttons disabled and show the reason. Do not offer «удалить все».

- [ ] **Step 6: Connect the two product parts without automatic elevation**

Add `public bool GoToLmGatewaysRequested { get; private set; }` and a post-completion button `_goToLmGatewaysButton` with text `Перейти к контроллерам ЛМ ЧЗ`. Show it only when outcome has at least one successfully registered/recovered row and the operation is no longer running. Its click sets the property, sets `DialogResult = OK` and closes. MainForm owns exactly one `LmGatewayPage`, selects its tab and calls refresh when the property is true. If the user closes normally, no behavior changes. Never start provisioning directly from bulk registration.

- [ ] **Step 7: Link UI files into both WinForms projects**

Add explicit compile links for the page and two dialogs to Legacy and Modern csproj files.

- [ ] **Step 8: Compile both targets**

```powershell
& $msbuild src\EsmTspiot.Legacy.WinForms\EsmTspiot.Legacy.WinForms.csproj /restore /p:Configuration=Release
dotnet build src\EsmTspiot.Modern.WinForms\EsmTspiot.Modern.WinForms.csproj -c Release
```

Expected: zero errors. MainForm instantiates the page exactly once. The initial 780×580 window shows all tab labels; new page scrolls on a smaller working area and does not clip its grid/actions.

- [ ] **Step 9: Manual UI smoke without mutations**

After the final build, rename the helper only inside that output folder and launch the app without rebuilding:

- KKT/INN discovery and binding-only controls remain visible;
- create/delete buttons explain that helper is unavailable;
- no UAC appears on refresh or tab switch;
- log contains no typed password;
- existing manual, instances, automation and log tabs behave unchanged.

Repeat from a user-writable extracted folder: refresh and binding-only remain available, while create/delete/cleanup are disabled before UAC with an instruction to place the verified compact package under a protected administrator-owned installation directory.

- [ ] **Step 10: Commit**

```powershell
git add src/EsmTspiot.WinForms.Shared src/EsmTspiot.Legacy.WinForms/EsmTspiot.Legacy.WinForms.csproj src/EsmTspiot.Modern.WinForms/EsmTspiot.Modern.WinForms.csproj
git commit -m "Добавить интерфейс контроллеров ЛМ ЧЗ"
```

---

### Task 12: CI, компактная поставка и документация

**Files:**

- Create: `scripts/package_compact_release.ps1`
- Create: `scripts/verify_lm_safety.ps1`
- Modify: `.github/workflows/ci.yml`
- Modify: `README.md`
- Modify: `INSTRUCTION_FOR_DUMMIES.md`
- Modify: `scripts/generate_instruction_pdf.py`

**Interfaces:**

- CI builds/runs shared tests, helper tests, Legacy, Modern and helper.
- Default release asset is a compact ZIP containing the runnable set; no 60 MB self-contained assets by default.

- [ ] **Step 1: Extend CI**

Add after shared tests:

```powershell
& $msbuild tests\EsmTspiot.ServiceProvisioner.Tests\EsmTspiot.ServiceProvisioner.Tests.csproj /restore /p:Configuration=Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& "tests\EsmTspiot.ServiceProvisioner.Tests\bin\Release\net48\EsmTspiot.ServiceProvisioner.Tests.exe"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $msbuild src\EsmTspiot.ServiceProvisioner\EsmTspiot.ServiceProvisioner.csproj /restore /p:Configuration=Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

Keep the existing net8/net48 shared matrix and both WinForms builds. Implement and invoke `scripts/verify_lm_safety.ps1` in CI. It scopes forbidden scans to the new LM/helper production files because the repository already has an unrelated reviewed PowerShell recovery feature. Its `rg` wrapper treats exit `0` as a forbidden hit/failure, `1` as clean, and propagates `>1` as a tool error. The forbidden set is `RollingPin`, `taskkill`, `sc.exe`, `powershell.exe`, `cmd.exe`, junction/UPX/process kill or vendor-binary redistribution.

The same script performs a default-deny secret scan: only exact reviewed file+symbol patterns in request transport, short-lived credential DTO/input and masking code are allowed; a new `password/newPassword/token/secret/apiKey/authorization` production hit fails. It also fails on a tracked executable/library, private key/certificate bundle or recognized vendor artifact. Tests prove hit/no-hit/tool-error behavior so the scan itself cannot silently invert success.

- [ ] **Step 2: Implement deterministic compact packaging**

Package a clean staging directory with this exact layout:

- root compact Legacy main EXE;
- root `EsmTspiot.Shared.dll` built for net48 and required by the Legacy app;
- `Provisioner\EsmTspiot.ServiceProvisioner.exe`;
- `Provisioner\EsmTspiot.Shared.dll` built for net48 plus any additional helper dependency outputs explicitly discovered from the helper build (no vendor binaries);
- `README.txt` describing that the official controller must already be installed;
- `SHA256SUMS` for every shipped file.

The README gives one safe installation route for service operations: an administrator extracts the verified ZIP into `C:\Program Files\KRS\MultiKKT`; inherited ACL must deny ordinary-user write. From Downloads/Desktop/other user-writable folders the app deliberately offers only viewing and binding-only. The script copies dependencies from clean Release output directories, never from the repository root, verifies the exact layout above, and rejects an official controller binary, credential file, test fixture or PDB. It fails if helper/main versions, embedded helper hash or KRS author/company metadata differ. A VM smoke launches the Legacy EXE from the staging folder before the ZIP is accepted, catching a missing `EsmTspiot.Shared.dll`/helper dependency.

Do not publish Modern self-contained x86/x64 by default. They remain CI-only unless explicitly requested.

- [ ] **Step 3: Update README honestly**

Replace the current blanket statement that PUT settings is never called with precise behavior:

- generic `PUT /api/v1/settings/{id}` remains unused;
- documented `PUT /api/v1/settings/lm/{id}` is called only after validation/confirmation and ready controller probe;
- passwords are never persisted/logged;
- supported controller version comes from the capability profile;
- only app-managed services can be removed;
- deleting a service automatically removes its local profile/metadata, but does not clear ESM binding;
- `CleanupPending` is resolved by the in-app `Повторить очистку`, not manual folder search;
- service mutations require extraction to a protected administrator-owned directory;
- compact ZIP is the default download.

- [ ] **Step 4: Update beginner instruction and regenerate PDF**

Add two explicit scenarios:

1. register several KKT in ESM;
2. for each row enter its LM address/port and credentials, review local ports, create/bind;
3. how to retry only a failed binding;
4. how to remove one managed service, how automatic local cleanup works and what remains only in ESM;
5. how to distinguish official and managed services;
6. recovery when status is `RequiresAttention`.

Regenerate the PDF with the existing script and visually inspect every page. Screenshots must be made from this product, not copied from another application.

- [ ] **Step 5: Run package verification**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\package_compact_release.ps1
Get-ChildItem artifacts\release -Recurse | Select-Object FullName,Length
Get-ChildItem artifacts\release -File -Recurse | Get-FileHash -Algorithm SHA256
```

Expected: one compact ZIP plus documentation/checksums as defined by the release process; no approximately 60 MB Modern binary and no vendor controller executable.

Add the packaging command and archive-content verification as a CI step. Check its exit code and inspect the ZIP file list in CI, so a release package cannot diverge from locally tested output.

- [ ] **Step 6: Commit**

```powershell
git add .github/workflows/ci.yml scripts README.md INSTRUCTION_FOR_DUMMIES.md
git commit -m "Подготовить компактную поставку контроллеров ЛМ"
```

---

### Task 13: Полная верификация, VM-сценарий и финальное ревью

**Files:**

- Verify: all changed source, tests, docs and release staging.
- Modify: `docs/research/2026-08-26-official-lm-controller-compatibility.md` with final implementation evidence.
- Modify: `docs/research/2026-08-26-lm-gateway-provenance.md` with final review hashes.

**Interfaces:**

- Produces evidence, not new behavior.

- [ ] **Step 1: Run all automated checks from a clean tree**

```powershell
git status --short
dotnet run --project tests\EsmTspiot.Shared.Tests\EsmTspiot.Shared.Tests.csproj -c Release
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$files = Get-ChildItem -Recurse "src\EsmTspiot.Shared" -Filter *.cs |
  Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
  ForEach-Object { $_.FullName }
$files += (Resolve-Path "tests\EsmTspiot.Shared.Tests\Program.cs").Path
& $csc /nologo /define:NETFRAMEWORK /out:"$env:TEMP\SharedTests.exe" /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Runtime.Serialization.dll $files
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& "$env:TEMP\SharedTests.exe"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $msbuild tests\EsmTspiot.ServiceProvisioner.Tests\EsmTspiot.ServiceProvisioner.Tests.csproj /restore /p:Configuration=Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& "tests\EsmTspiot.ServiceProvisioner.Tests\bin\Release\net48\EsmTspiot.ServiceProvisioner.Tests.exe"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $msbuild src\EsmTspiot.Legacy.WinForms\EsmTspiot.Legacy.WinForms.csproj /restore /p:Configuration=Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build src\EsmTspiot.Modern.WinForms\EsmTspiot.Modern.WinForms.csproj -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$finalPublishSmoke = Join-Path $env:TEMP ("MultiKKT-final-publish-" + [Guid]::NewGuid().ToString("N"))
dotnet publish src\EsmTspiot.Modern.WinForms\EsmTspiot.Modern.WinForms.csproj -c Release -o $finalPublishSmoke
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (-not (Test-Path -LiteralPath (Join-Path $finalPublishSmoke 'Provisioner\EsmTspiot.ServiceProvisioner.exe'))) { throw 'Helper missing from final PublishDir' }
if (-not (Test-Path -LiteralPath (Join-Path $finalPublishSmoke 'Provisioner\EsmTspiot.Shared.dll'))) { throw 'Helper net48 dependency missing from final PublishDir' }
& $msbuild src\EsmTspiot.ServiceProvisioner\EsmTspiot.ServiceProvisioner.csproj /restore /p:Configuration=Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\package_compact_release.ps1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

Expected: 116 shared tests and 60 helper tests pass, zero compile/package errors. If review added tests, record the larger exact counts.

- [ ] **Step 2: Run the same fail-fast static safety gate as CI**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify_lm_safety.ps1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

Expected:

- no forbidden implementation mechanism;
- every secret-related production hit belongs to request transport/masking and never manifest/log/result;
- only project-owned release binaries are produced, not tracked; no vendor binary or private key is committed.

- [ ] **Step 3: Execute the end-to-end VM acceptance scenario**

From a clean supported VM snapshot:

1. extract the verified compact package as administrator under `C:\Program Files\KRS\MultiKKT`, verify inherited protected ACL, then start the app unelevated;
2. choose the exact supported `esm-lm-controller_*-windows-setup.exe` in the page and confirm displayed version/signer/hash;
3. run `Установить / проверить версию`, confirm one UAC, protected staging cleanup and the verified official base controller;
4. use a fake/test ESM and three synthetic KKT records with different INNs;
5. confirm the verified base controller is displayed read-only and cannot be assigned;
6. create three managed services with distinct gRPC/REST pairs from the approved pools and distinct dummy target LM endpoints; prove the helper rejects a deliberately occupied or OS-excluded candidate during its bind check;
7. confirm one UAC sequence and per-item results;
8. verify three independent profiles and listeners;
9. verify three correct PUT bodies at fake ESM and absence of passwords in all app logs/files;
10. rerun create/update and confirm no duplicate services and `NoChange` results;
11. select and run the same verified installer again as an update rehearsal; confirm all managed rows become stopped `VersionVerificationPending` before installer launch and are not restarted automatically;
12. explicitly run create/update and confirm each successfully verified row returns to `ServiceReady`, while removal remains available before that action;
13. simulate one port conflict and confirm only its row is blocked;
14. simulate one PUT failure, confirm service remains `ReadyNotBound`, then retry binding without UAC;
15. remove one managed service from UI by typing its KKT serial;
16. confirm service, profile, manifest and app-owned metadata absent, other two services/listeners unchanged, and UI says ESM binding remains;
17. inject a locked profile file, confirm `CleanupPending`, unlock it and finish via `Повторить очистку` without manual folder deletion;
18. confirm delete disabled for official base and unknown service;
19. inject crashes after installer staging, version-pending publication, profile/create/start and prove the next launch reconciles journals without orphan/duplicate services;
20. start concurrent install/ensure/remove attempts and prove mutex serialization;
21. reboot VM and confirm remaining service autostart behavior matches capability profile.

Use no production credentials or real organization data.

If Task 1 declared Windows 7 supported by this controller version, repeat helper start, named-pipe batch, service creation/removal and compact Legacy UI smoke on Windows 7 SP1. If the vendor does not support it, verify service actions are disabled there while existing KKT and binding-only functions remain operational.

- [ ] **Step 4: Review maintainability and regression risks**

Reviewer checklist:

- no service logic leaked into `MainForm`, bulk registration or recovery builder;
- no duplicated service-name/path/port derivation between main and helper without helper-side revalidation;
- helper API cannot express an arbitrary command, path, registry key or service;
- capability profile is version-pinned and unsupported versions fail closed;
- manifest ownership is necessary but not sufficient for deletion;
- official base is read-only;
- successful removal automatically cleans only the strictly derived profile/metadata, and interrupted cleanup is retryable in-app;
- lifecycle status separates service readiness from HTTP acceptance;
- errors/cancellation are resumable and do not trigger guessed rollback;
- same sources compile for Legacy and Modern;
- packaging remains compact and excludes vendor code;
- no raw evidence, vendor binary or production secret is tracked; `PrivateBlackBox` facts are version-scoped and keep repository/build visibility private;
- documentation matches actual behavior and limitations.

- [ ] **Step 5: Record evidence and commit review result**

Append to the private summary/provenance:

- implementation commit hashes;
- exact supported official controller version/hash/signature;
- test counts and build commands;
- VM run date and only opaque evidence IDs/digests, not snapshot ID or raw output;
- any known limitation;
- reviewer decision.

Append full VM/snapshot identifiers, raw final logs, SDDL/SCM/config/process evidence and their hashes only to the access-controlled local evidence pack. Rebuild its sorted `SHA256SUMS`, publish the new manifest digest in the summary, and verify the staged diff contains no raw evidence.

Then:

```powershell
git add docs/research/2026-08-26-official-lm-controller-compatibility.md docs/research/2026-08-26-lm-gateway-provenance.md
git commit -m "Зафиксировать проверку контроллеров ЛМ"
```

- [ ] **Step 6: Stop before release**

Do not bump version, tag, push, make repository changes, publish a GitHub Release or change visibility until the user explicitly approves the reviewed implementation and release contents.
