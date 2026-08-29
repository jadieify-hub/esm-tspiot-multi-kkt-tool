# Managed Local Module Instances Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Реализовать единый цикл `ККТ -> ЕСМ -> ЛМ ЧЗ для ИНН -> контроллер ККТ` с предзаполненными адресами/портами, одним автоматическим запуском и полной очисткой.

**Architecture:** Существующие `LmGateway*` остаются слоем контроллеров и привязки ЕСМ. Новый слой `ManagedLocalModule*` группирует ККТ по ИНН, создаёт общий read-only runtime точной версии MSI и отдельные конфиги/данные/службы `yenisei` и `regime` на ИНН. `CompleteStack*` координирует canary и одну elevated session, не дублируя доменную логику в WinForms.

**Tech Stack:** C# 5-compatible syntax, WinForms, `net48` helper, `net48;net8.0` shared library, Windows Installer/SCM/process APIs через P/Invoke, `DataContractJsonSerializer`, package-free tests.

**Spec:** `docs/superpowers/specs/2026-08-29-managed-local-module-instances-design.md`

## Global Constraints

- Поддерживаемый MSI: `2.6.1-7`, ProductVersion `2.6.1`, ProductCode `{556FD8AD-43A3-4645-BC54-EBF3043ADF82}`, SHA-256 `68a9633cefc912c2c1defae40d1c8f433bb794ee66060b822f0895410ab6c5c6`.
- Один ЛМ на уникальный ИНН торговой точки; отдельный контроллер на каждую ККТ; максимум 32.
- Порты: API `4995 + 1000*N`, DB `4984 + 1000*N`, EPMD `43690 + N`, gRPC `45000 + K`, REST `15000 + K`.
- Explicit legacy gRPC `55001–55032` не перенумеровывается молча.
- Vendor-файлы не изменяются и не попадают в Git/CI/release; `nssm.exe` и `InstallAutoUpdateLM.exe` не копируются.
- Runtime read-only; все изменяемые файлы на ИНН лежат только в защищённом app-owned `ProgramData`.
- Запрещены reparse points, batch/PowerShell/`cmd.exe`/`sc.exe`, общий `taskkill`, пользовательские command/environment поля.
- Одна автоматическая операция использует один UAC и один хэшированный IPC-сеанс; секреты не входят в plan/manifest/log.
- Удаляются только повторно подтверждённые app-owned объекты; runtime — только при нуле фактических ссылок.
- Shared/helper остаются C# 5-compatible, без новых NuGet-пакетов.
- Каждая задача заканчивается RED → GREEN → полная затронутая матрица → отдельный commit.

---

### Task 1: Группировка по ИНН, стабильные номера и миграция портов

**Files:**
- Create: `src/EsmTspiot.Shared/Models/ManagedKktAssignment.cs`
- Create: `src/EsmTspiot.Shared/Models/ManagedLocalModuleAssignment.cs`
- Create: `src/EsmTspiot.Shared/Models/ManagedLocalModulePlan.cs`
- Create: `src/EsmTspiot.Shared/Models/ManagedLocalModulePlanItem.cs`
- Create: `src/EsmTspiot.Shared/Models/ManagedLocalModulePorts.cs`
- Create: `src/EsmTspiot.Shared/Services/ManagedLocalModulePlanner.cs`
- Modify: `src/EsmTspiot.Shared/Services/LmGatewayDraftDefaults.cs`
- Modify: `src/EsmTspiot.Shared/Services/LmGatewayDraftSettingsStore.cs`
- Test: `tests/EsmTspiot.Shared.Tests/Program.cs`
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-08-26-multi-inn-lm-gateways-design.md`

**Interfaces:** Produces `ManagedLocalModulePlanner.Build(IList<LmGatewayKkt>, IList<ManagedKktAssignment>, IList<ManagedLocalModuleAssignment>, IList<TcpListenerSnapshotItem>)`. Saved KKT owns `K`; saved unique-INN module owns `N`, instance-id and API/DB/EPMD ports.

- [x] **Step 1: Write failing tests** registering `ManagedLmPlannerGroupsKktByInn`, `ManagedLmPlannerAssignsStableOrdinals`, `ManagedLmPlannerBlocksOccupiedPorts`, `LmDefaultsUse45000GrpcPool`, `LmDraftStorePreservesLegacyGrpc`.
- [x] **Step 2: Run RED:** `dotnet run --project tests\EsmTspiot.Shared.Tests\EsmTspiot.Shared.Tests.csproj -c Release`; new symbols/default expectations must fail.
- [x] **Step 3: Implement the exact entry point:**

```csharp
public static ManagedLocalModulePlan Build(
    IList<LmGatewayKkt> kkts,
    IList<ManagedKktAssignment> savedKkts,
    IList<ManagedLocalModuleAssignment> savedModules,
    IList<TcpListenerSnapshotItem> listeners)
```

Preserve valid assignments, allocate `K/N` from `1..32`, group exact normalized INN, reject identity drift and all port collisions without shifting.
- [x] **Step 4: Set `GrpcPortFirst=45001`, `GrpcPortLast=45032`; treat only persisted explicit `550xx` as legacy override.**
- [x] **Step 5: Run net8 and the exact net48 system-compiler block from `.github/workflows/ci.yml`; all tests pass.**
- [x] **Step 6: Commit:** record the completed controller baseline and Task 1 planning together as the verified implementation checkpoint that existed in this working tree.

---

### Task 2: Проверка MSI и защищённый IPC-контракт

**Files:**
- Create: `src/EsmTspiot.Shared/Models/LocalModuleInstallerSelection.cs`
- Create: `src/EsmTspiot.Shared/Models/ManagedLocalModuleProvisioningItemRequest.cs`
- Create: `src/EsmTspiot.Shared/Models/ManagedProvisioningSessionMessage.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/WindowsInstallerPackageReader.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModulePackageVerifier.cs`
- Modify: `src/EsmTspiot.Shared/Models/LmServiceOperation.cs`
- Modify: `src/EsmTspiot.Shared/Models/LmServiceProvisioningBatchRequest.cs`
- Modify: `src/EsmTspiot.Shared/Services/CanonicalLmPlanHasher.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/ProvisioningRequestValidator.cs`
- Test: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:** `VerifyAndLock(LocalModuleInstallerSelection)` returns a locked `VerifiedLocalModulePackage`. Session kinds are exactly `SessionReady`, `ExecuteItem`, `ItemResult`, `Finish`, `CancelAfterCurrentItem`, with monotonically increasing sequence.

- [ ] **Step 1: Write failing tests** for exact package, substituted source, wrong ProductCode/version/signer, duplicate INN, unknown message kind, repeated sequence, reflected secret/path/command property and plan-hash mismatch.
- [ ] **Step 2: Run RED:** build/run `tests/EsmTspiot.ServiceProvisioner.Tests`; new verifier/protocol tests fail.
- [ ] **Step 3: Implement read-only `_Property` inspection** using `MsiOpenDatabaseW`, view/execute/fetch and `MsiRecordGetStringW`; never run install actions during inspection.
- [ ] **Step 4: Require exact metadata + WinTrust + size/hash after reopening without sharing. Hash operation, package identities, KKT/INN, K/N, ports and sequence in ordinal order.**
- [ ] **Step 5: Run helper tests and `scripts/verify_lm_safety.ps1`; both pass.**
- [ ] **Step 6: Commit:** `git commit -m "Добавить проверку MSI ЛМ и защищённый контракт"`.

---

### Task 3: Exact-version capability и изолированная конфигурация

**Files:**
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleCapabilityProfile.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleConfiguration.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleConfigurationWriter.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/ErlangChildStartPlan.cs`
- Create: `tests/EsmTspiot.ServiceProvisioner.Tests/Fixtures/local-module-2.6.1-sanitized.json`
- Test: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:** `Resolve("2.6.1")` yields exact boot/required-file/fingerprint contract. `Build(...)` returns two `local.ini`, two `vm.args`, two `sys.config`. `ErlangChildStartPlan` contains only derived executable, working dir, fixed tokens and environment.

- [ ] **Step 1: Write failing tests** `LocalModuleConfigsIsolateEveryMutablePath`, `LocalModuleStartPlansShareOnlyReadOnlyRuntime`, `LocalModuleConfigRejectsAmbiguousTemplate`, `LocalModuleConfigsContainNoCredential`.
- [ ] **Step 2: Run helper tests and verify RED.**
- [ ] **Step 3: Generate complete typed files, never unrestricted search/replace. Build only this command shape:**

```text
erl.exe -boot <fixed-boot> -args_file <profile-vm.args> -epmd <fixed-epmd.exe> -config <profile-sys.config>
```

Environment is derived `ERL_LIBS`, `ERL_EPMD_PORT`, `ERL_EPMD_ADDRESS=127.0.0.1`, restricted `PATH`, absolute query-server commands. Every mutable path normalizes below instance root.
- [ ] **Step 4: Run helper tests and `msbuild ...ServiceProvisioner.csproj /p:LangVersion=5`; pass.**
- [ ] **Step 5: Commit:** `git commit -m "Добавить изолированную конфигурацию ЛМ ЧЗ"`.

---

### Task 4: Общий runtime, manifests и crash recovery

**Files:**
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleRuntimeManifest.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleInstanceManifest.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/ManagedKktStackManifest.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleManifestStore.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleRuntimeInstaller.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleOperationJournalStore.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/PathSafety.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/ServiceSecurityDescriptor.cs`
- Test: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:** Runtime root `%ProgramFiles%\KRS\MultiKKT\LocalModuleRuntime\<version-id>`; instance root `%ProgramData%\KRS\MultiKKT\LocalModules\<instance-id>`. `CountRuntimeReferences` independently scans verified instance manifests.

- [ ] **Step 1: Write failing tests** for wrapper/updater exclusion, three ownership levels, zero-reference delete and injected crash after every mutation boundary.
- [ ] **Step 2: Run helper tests and verify RED.**
- [ ] **Step 3: Implement fixed extraction:** `%SystemRoot%\System32\msiexec.exe /a <locked-msi> /qn TARGETDIR=<protected-stage> /l*v <protected-log>`; reject nonzero exit, invalid structure, reparse paths and writable final runtime.
- [ ] **Step 4: Implement atomic schema-versioned manifests/journals; derive all roots again in helper. Recovery completes idempotently or returns `CleanupPending`, never adopts unknown files.**
- [ ] **Step 5: Run helper tests/safety and commit:** `git commit -m "Добавить общий runtime и manifest ЛМ"`.

---

### Task 5: Общий service host и точный process ownership

**Files:**
- Create: `src/EsmTspiot.ServiceProvisioner/ManagedChildServiceHost.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/ManagedChildProcess.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleServiceIdentity.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/LocalModuleServiceOwnershipVerifier.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/EpmdInstanceController.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/LmGatewaySupervisorService.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/LmControllerChildProcess.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/ProvisionerCommandLine.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/WindowsServiceApi.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/TcpListenerOwnerReader.cs`
- Test: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:** `ManagedChildServiceHost.Run(serviceName)` loads only owned manifest. Controller retains empty vendor args; LM mode is only `--supervise-local-module <validated-service-name>`, with role/start plan re-derived from manifest. Ownership combines SCM/SID, supervisor/parent/child PID, exact executable/hash/config tokens/EPMD env/nonce/listener PID.

- [ ] **Step 1: Write failing tests** for controller invariant, manifest-only LM args, distinguishing shared `erl.exe` children, explicit EPMD port and blocked `epmd -kill` with live nodes.
- [ ] **Step 2: Run helper tests and verify RED.**
- [ ] **Step 3: Extract only process handles, environment block, graceful stop and parent/child observation into common machinery; keep separate typed factories.**
- [ ] **Step 4: Create DB/API SCM services with restricted SID/DACL and API dependency on DB. Start DB/listener → API/listener; stop API → DB → `epmd -names -port N` → conditional `epmd -kill -port N`.**
- [ ] **Step 5: Run helper tests/C# 5 and commit:** `git commit -m "Добавить службы управляемых ЛМ ЧЗ"`.

---

### Task 6: Idempotent lifecycle, canary, cleanup и update guard

**Files:**
- Create: `src/EsmTspiot.ServiceProvisioner/ManagedLocalModuleProvisioner.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/ManagedLocalModuleRemovalWorkflow.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/ManagedLocalModuleUpdateWorkflow.cs`
- Create: `src/EsmTspiot.ServiceProvisioner/CompleteStackProvisioningSession.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/Program.cs`
- Modify: `src/EsmTspiot.ServiceProvisioner/WindowsLmProvisioningPlatform.cs`
- Modify: `src/EsmTspiot.Shared/Models/LmServiceProvisioningStatus.cs`
- Test: `tests/EsmTspiot.ServiceProvisioner.Tests/Program.cs`

**Interfaces:** `Ensure(item, session)` returns after owned DB/API listeners. Session locks both packages once and accepts only prehashed item indexes. Remove KKT retains referenced same-INN LM; remove-all then deletes zero-reference runtimes. Different version is `VersionVerificationPending` until exact migration profile exists.

- [ ] **Step 1: Write failing tests** for canary ordering/failure stop, same-INN idempotency, retaining shared LM, full cleanup, `CleanupPending`, and unknown-version guard.
- [ ] **Step 2: Run helper tests and verify RED.**
- [ ] **Step 3: Implement journaled states** `Created`, `RuntimeReady`, `ProfileReady`, `DbReady`, `ApiReady`, `ControllerReady`, `BindingPending`, `Completed`, `CleanupPending`, `RequiresAttention`.
- [ ] **Step 4: Implement factual cleanup by re-reading manifests/SCM/PIDs/listeners. Same-version repair preserves data; new version never stops existing processes without exact capability/migration.**
- [ ] **Step 5: Run helper/C# 5/safety and commit:** `git commit -m "Добавить жизненный цикл полного комплекта ККТ"`.

---

### Task 7: Операторский UI и один automatic flow

**Files:**
- Create: `src/EsmTspiot.WinForms.Shared/LocalModuleInstallerPicker.cs`
- Create: `src/EsmTspiot.WinForms.Shared/CompleteStackProvisionerClient.cs`
- Create: `src/EsmTspiot.WinForms.Shared/ManagedLocalModuleDetailsDialog.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmGatewayPage.Layout.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmGatewayPage.Services.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmGatewayPage.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmAutomaticSetupDialog.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/MainForm.cs`
- Modify: `src/EsmTspiot.Shared/Services/AutomaticConfigurationCoordinator.cs`
- Modify: `src/EsmTspiot.Shared/Services/LmAutomaticSetupCoordinator.cs`
- Test: `tests/EsmTspiot.Shared.Tests/Program.cs`
- Modify: `scripts/verify_ui_layout.ps1`

**Interfaces:** Tab `ЛМ ЧЗ`; two compact installer selectors. Main grid is exactly `№ ККТ`, serial, INN, cash-software port, LM endpoint, LM state, ESM-link state. `RunSessionAsync(plan, onReadyItem, cancellation)` owns one helper/pipe/UAC.

- [ ] **Step 1: Write failing tests** for full-chain canary, one helper session, repeated same-INN endpoint and exact operator-grid columns.
- [ ] **Step 2: Run shared tests and verify RED.**
- [ ] **Step 3: Rebuild the tab within initial width. Prefill loopback/deterministic ports; hide DB/EPMD/gRPC/REST/service/hash/path fields in details/log.**
- [ ] **Step 4: Implement one-UAC session. UI registers requested canary, sends only its preplanned index, waits for LM/controller, binds/read-backs, then advances. Canary failure aborts and leaves remaining KKT unregistered.**
- [ ] **Step 5: Connect one-action full cleanup and one `Повторить очистку` state.**
- [ ] **Step 6: Run shared tests, Legacy C# 5, Modern build and `verify_ui_layout.ps1`; commit:** `git commit -m "Добавить автоматическую настройку полного комплекта"`.

---

### Task 8: Full matrix, clean VM и acceptance package

**Files:**
- Create: `docs/testing/managed-local-module-vm-checklist.md`
- Create: `scripts/build_managed_lm_acceptance.ps1`
- Modify: `.github/workflows/ci.yml`
- Modify: `scripts/package_compact_release.ps1`
- Modify: `scripts/verify_compact_security_contract.ps1`
- Modify: `README.md`
- Modify: `INSTRUCTION_FOR_DUMMIES.md`

**Interfaces:** Acceptance ZIP contains KRS app/helper/scripts/checklist only. CI explicitly builds helper with `/p:LangVersion=5`. VM covers one KKT, three different INNs, two KKT sharing INN, reboot, remove-all, second reboot and reinstall.

- [ ] **Step 1: Harden gates** against `regime-*.msi`, `RollingPinForLM`, `yenisei`, `erts-*`, `epmd.exe`, `nssm.exe`, updater, raw databases/profiles and personal paths.
- [ ] **Step 2: Run full local matrix:** shared net8/net48, helper tests and C# 5, Legacy C# 5, Modern, LM safety, UI layout, compact package. Every command exits 0.
- [ ] **Step 3: In clean VM verify** two INN groups from one read-only runtime, separate mutable state/EPMD/listeners, one-UAC flow, reboot autostart, shared-INN single-KKT removal, remove-all, second reboot and reinstall.
- [ ] **Step 4: With owner present run one real-KKT canary**, then remaining KKT. Do not persist credentials/tokens/unmasked organization data.
- [ ] **Step 5: Review duplication, boundaries, cancellation, recovery, ownership, no forced kill, UI width and cleanup. Record exact test counts/package hash and commit:** `git commit -m "Подготовить тестирование управляемых ЛМ ЧЗ"`.

## Self-review

- Spec 1–7 maps to Tasks 1–2; config/manifest/services to Tasks 3–5; canary/lifecycle/update to Task 6; UI to Task 7; VM/publication/package to Task 8.
- No wildcard version, vendor redistribution, binary patch, shared mutable config or silent port migration remains.
- `LmGateway*` means controller/binding, `ManagedLocalModule*` means LM/runtime, `CompleteStack*` means orchestration.
- Production support is exact for `2.6.1-7`; another MSI cannot mutate instances without its exact capability and migration pair.
