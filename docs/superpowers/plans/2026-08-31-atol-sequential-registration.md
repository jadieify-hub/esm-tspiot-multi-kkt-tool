# ATOL Sequential Registration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Автоматически регистрировать несколько USB/VCOM ККТ АТОЛ разных ИНН, открывая штатную сессию только с одной кассой за раз, а затем создавать её ЛМ ЧЗ и контроллер.

**Architecture:** Чистый координатор в `EsmTspiot.Shared` управляет снимками `dkktList`, границами сессий и ожиданием подтверждения ЕСМ через абстракцию подключения. Операторский адаптер в `EsmTspiot.WinForms.Shared` перечисляет `MI_00` VCOM и динамически вызывает установленный API ДТО; helper и поставка остаются свободны от DLL АТОЛ. `MainForm` выбирает старый одно-ИНН поток либо новый последовательный поток и передаёт регистрацию в существующую complete-stack канарейку.

**Tech Stack:** C# 5-compatible syntax, `net48;net8.0`, WinForms, Registry API, reflection over installed `Atol.Drivers10.Fptr.dll`, existing package-free console tests and PowerShell gates.

**Spec:** `docs/superpowers/specs/2026-08-31-atol-sequential-registration-design.md`

## Global Constraints

- Не фильтровать и не подменять ответы ЕСМ; использовать только реальные сессии API ДТО.
- Не добавлять vendor DLL, NuGet-пакеты, helper/IPC-поля или восьмой файл в компактный ZIP.
- Все общие и операторские исходники должны собираться системным C# 5 путём Legacy.
- Рабочий endpoint привязки остаётся `/api/v1/settings/lm/{id}`.
- После любой ошибки или отмены все принадлежащие приложению сессии закрываются.
- Push не выполнять.

---

### Task 1: Контракты пустого dkktList и выбор режима

**Files:**
- Modify: `tests/EsmTspiot.Shared.Tests/Program.cs`
- Modify: `src/EsmTspiot.Shared/Services/DkktListParser.cs`
- Create: `src/EsmTspiot.Shared/Services/AutomaticRegistrationModeSelector.cs`
- Create: `src/EsmTspiot.Shared/Models/AutomaticRegistrationMode.cs`
- Modify: `src/EsmTspiot.Shared/Models/BulkRegistrationDiscovery.cs`

**Interfaces:**
- Produces: `DkktListParser.TryParse(ApiResponse, out IList<DkktDeviceInfo>)`.
- Produces: `AutomaticRegistrationModeSelector.Select(BulkRegistrationDiscovery)` returning `ExistingSessions` or `SequentialVcom`.
- Produces: `BulkRegistrationDiscovery.ObservedDevices` as a read-only mutable list populated by discovery.

- [ ] **Step 1: Write failing shared tests.** Add literal fixtures proving: `204` with empty/whitespace body is an empty list; `204` with body and `200` with empty body fail; a one-INN pending plan chooses `ExistingSessions`; all-registered multi-INN chooses `ExistingSessions`; empty or pending multi-INN discovery chooses `SequentialVcom`.
- [ ] **Step 2: Run `dotnet run --project tests\EsmTspiot.Shared.Tests\EsmTspiot.Shared.Tests.csproj -c Release` and confirm compile/test failure because the overload, enum, selector and observed collection do not exist.**
- [ ] **Step 3: Implement the minimal parser overload and selector.** Normalize INNs with `Trim`, ignore invalid/empty INNs for the distinct-count only after planner validation, and never infer a device not present in `ObservedDevices`.
- [ ] **Step 4: Refactor `BulkRegistrationWorkflow.DiscoverAsync` to populate `ObservedDevices` and use the response-aware parser without changing existing one-INN outcomes.**
- [ ] **Step 5: Run shared net8 and the exact system-csc net48 block from CI; both pass.**

### Task 2: Подтверждение регистрации после PUT и формулировки

**Files:**
- Modify: `tests/EsmTspiot.Shared.Tests/Program.cs`
- Modify: `src/EsmTspiot.Shared/Services/BulkRegistrationWorkflow.cs`
- Modify: `src/EsmTspiot.Shared/Services/TspiotErrorDecoder.cs`
- Modify: `src/EsmTspiot.Shared/Models/BulkKktRegistrationResult.cs`

**Interfaces:**
- `BulkRegistrationWorkflow` keeps its public API; the injected delay drives both transient retries and post-PUT polling.
- A successful PUT yields `Registered`/`RecoveredRegistration` only after a matching registered detail response.

- [ ] **Step 1: Write failing tests** for an unregistered→registered response sequence, a 60-second-equivalent timeout using zero-delay injection, mismatched trimmed identity, and an HTTP-success PUT whose state never becomes registered. Update the 1026 expectation to include the sequential automatic-mode instruction and summary expectation to `Регистрация не завершена (экземпляр существует)`.
- [ ] **Step 2: Run shared net8 and confirm the new tests fail because PUT success is currently accepted immediately and old wording remains.**
- [ ] **Step 3: Add post-PUT polling.** Use 30 attempts and the existing two-second delay, require `IsRegistered`, `HasCompleteRegistrationData`, and trimmed serial/FN/INN equality. A mismatch stops immediately as inspection failure; a timeout is `RegistrationFailed` and never authorizes local components.
- [ ] **Step 4: Update existing successful-workflow fixtures to enqueue a matching registered detail so they test the stronger contract rather than bypass it.**
- [ ] **Step 5: Run shared net8/net48; all tests pass with increased counts.**

### Task 3: Чистый последовательный координатор

**Files:**
- Create: `src/EsmTspiot.Shared/Models/KktConnectionPort.cs`
- Create: `src/EsmTspiot.Shared/Models/KktConnectionIdentity.cs`
- Create: `src/EsmTspiot.Shared/Models/SequentialKktRegistrationTarget.cs`
- Create: `src/EsmTspiot.Shared/Models/SequentialKktDiscovery.cs`
- Create: `src/EsmTspiot.Shared/Services/IKktConnectionProvider.cs`
- Create: `src/EsmTspiot.Shared/Services/SequentialKktRegistrationCoordinator.cs`
- Modify: `tests/EsmTspiot.Shared.Tests/Program.cs`

**Interfaces:**
- `IKktConnectionProvider.EnumeratePorts()` returns deterministic `KktConnectionPort` values.
- `IKktConnectionProvider.OpenAsync(port, token)` returns `IKktConnectionLease` with a verified `KktConnectionIdentity` and `Dispose()` ownership.
- `SequentialKktRegistrationCoordinator.DiscoverAsync(baseUrl, progress, token)` returns targets containing the actual `DkktDeviceInfo` captured while one port is open.
- `ExecuteWithTargetAsync(target, registration, progress, token)` keeps only that target open while the registration delegate runs and disposes it before returning.
- `VerifyAllAsync(discovery, progress, token)` opens all mapped ports read-only, verifies every serial and disposes in reverse order.

- [ ] **Step 1: Add fakes and failing tests** for changing `dkktList` responses, external-session detection, target timeout, deterministic VCOM→serial→device mapping, disposal before return, cancellation between targets without rollback callbacks, and failure during final verification closing every lease.
- [ ] **Step 2: Run shared net8 and confirm compile failure on the new interfaces/types.**
- [ ] **Step 3: Implement discovery with three empty-preflight polls, ten-second target isolation polling, exact serial comparison, duplicate serial/port rejection, progress containing poll count/visible INNs/count, and `finally` disposal.**
- [ ] **Step 4: Implement registration and final-verification boundaries without any process manipulation or vendor knowledge.**
- [ ] **Step 5: Run shared net8/net48 and mutate the fake so a missing dispose or permissive multi-device predicate makes at least one test fail; restore and pass.**

### Task 4: Операторский адаптер установленного API ДТО

**Files:**
- Create: `src/EsmTspiot.WinForms.Shared/AtolDriverRuntimeLocator.cs`
- Create: `src/EsmTspiot.WinForms.Shared/AtolVcomEnumerator.cs`
- Create: `src/EsmTspiot.WinForms.Shared/AtolFptrConnectionProvider.cs`
- Create: `tests/EsmTspiot.Operator.Tests/EsmTspiot.Operator.Tests.csproj`
- Create: `tests/EsmTspiot.Operator.Tests/Program.cs`
- Modify: `src/EsmTspiot.Legacy.WinForms/EsmTspiot.Legacy.WinForms.csproj`
- Modify: `src/EsmTspiot.Modern.WinForms/EsmTspiot.Modern.WinForms.csproj`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- `AtolDriverRuntimeLocator.Resolve()` returns verified wrapper/native paths whose files live under one installed ATOL root and whose native PE machine matches the current process.
- `AtolVcomEnumerator.EnumeratePorts()` returns unique numeric-sorted `VID_2912...MI_00` ports only.
- `AtolFptrConnectionProvider` implements the shared provider through reflection and never references/redistributes a vendor assembly at compile time.

- [ ] **Step 1: Create the operator test runner with failing tests** for uninstall/fallback path selection, PE x86/x64 matching, filtering out `MI_02`, numeric COM ordering, duplicate removal, generated DTO settings using exact COM and `AutoReconnect=false`, and lease close/destroy on normal/error paths.
- [ ] **Step 2: Run operator net8/net48 and confirm compile failure because adapter classes do not exist.**
- [ ] **Step 3: Implement runtime location** by scanning 32/64-bit uninstall views for display name `Драйвер ККТ v.10`, then safe `Program Files` fallbacks; canonicalize paths and reject wrapper/native files outside the chosen root.
- [ ] **Step 4: Implement VCOM enumeration** from `HKLM\SYSTEM\CurrentControlSet\Enum\USB`, accepting only hardware IDs with `VID_2912` and `MI_00` plus a validated `COM[1-9][0-9]*` `PortName`.
- [ ] **Step 5: Implement the reflection adapter** for `Fptr(string)`, constants, `setSettings`, `open`, status query, serial/model/version reads, `close`, and `destroy`. Translate vendor errors into Russian operator messages without exposing arbitrary paths or data.
- [ ] **Step 6: Link the three files into Legacy and Modern only, set compact Legacy to x86, add both operator test targets to CI, and prove helper csproj has no ATOL source/reference.**
- [ ] **Step 7: Run operator net8/net48, Legacy C# 5 and Modern builds; all pass without vendor DLL present in the test output.**

### Task 5: Интеграция в автоматическую настройку

**Files:**
- Modify: `src/EsmTspiot.Shared/Services/BulkRegistrationWorkflow.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/MainForm.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmGatewayPage.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmGatewayPage.Services.cs`
- Modify: `scripts/verify_ui_layout.ps1`
- Modify: `scripts/verify_lm_safety.ps1`
- Modify: `tests/EsmTspiot.Shared.Tests/Program.cs`

**Interfaces:**
- `BulkRegistrationWorkflow.DiscoverFromDevicesAsync(...)` builds the existing plan from sequentially observed real devices.
- `MainForm` uses existing sessions for one-INN/all-registered discovery and a retryable sequential preflight otherwise.
- The complete-stack `prepareItem` delegate returns only after the target lease is disposed; helper/controller setup therefore cannot overlap the POS VCOM session.

- [ ] **Step 1: Add failing tests/UI-gate checks** for discovery from supplied devices, no sequential prompt in one-INN/all-registered mode, correct Retry/Cancel external-holder text, correct `/api/v1/settings/lm` constant, operator-only adapter compilation, and controller setup beginning after lease disposal.
- [ ] **Step 2: Run shared/UI gates and confirm the expected RED failures.**
- [ ] **Step 3: Refactor discovery** so both live-response and supplied-device paths share one planner/inspection implementation without duplicating registration logic.
- [ ] **Step 4: Wire `MainForm`** to create the provider/coordinator, select the mode, show external-holder Retry/Cancel without killing processes, wrap each registration delegate in `ExecuteWithTargetAsync`, and run final all-port verification only after every complete-stack row succeeds.
- [ ] **Step 5: Record exact stop reasons.** Registration failure names the control KKT; local-component or binding failure names that stage. Replace the generic unattempted text with the captured reason.
- [ ] **Step 6: Change `Зарегистрированных ККТ` to `Экземпляров в ЕСМ`, update automatic status/hint text to explain temporary closure of cash software, and keep the five-tab layout unchanged.**
- [ ] **Step 7: Extend safety/UI gates** to scan the ATOL files, prove no vendor binary/reference/helper inclusion, preserve seven ZIP entries, and exercise the mode selector/visible diagnostics through behavior rather than source-only checks where possible.
- [ ] **Step 8: Run shared, operator, helper, Legacy, Modern and both gates; pass.**

### Task 6: Документация, полевая проверка и релизный артефакт

**Files:**
- Modify: `README.md`
- Modify: `docs/testing/2026-08-29-field-acceptance-1.6.3.2.md`
- Modify: `INSTRUCTION_FOR_DUMMIES.md`
- Modify: `docs/superpowers/plans/2026-08-31-atol-sequential-registration.md`
- Generated: `artifacts/release/MultiKKT-ESM-TSPioT-compact.zip`

**Interfaces:** Компактный ZIP остаётся переносимым пакетом из семи файлов; API ДТО является проверяемой внешней предпосылкой машины.

- [ ] **Step 1: Document prerequisites and flow:** закрыть Frontol/«Тест драйвера» только когда программа это обнаружила, использовать USB/VCOM `MI_00`, не выбирать `USB:auto`, ручные вкладки остаются fallback.
- [ ] **Step 2: Add field checks:** две ККТ разных ИНН; session open→clean dkktList→POST/PUT→registered read-back→session closed→LM/controller; controller succeeds while POS VCOM is closed; cancellation after first KKT; foreign holder diagnostic; final all-KKT read-only verification.
- [ ] **Step 3: Run final matrix:** shared net8/net48, operator net8/net48, helper 111+, helper and Legacy C# 5, Modern zero warnings, safety/UI gates and `git diff --check`.
- [ ] **Step 4: Run `scripts/package_compact_release.ps1`, verify exactly seven entries, no ATOL DLL, and record size/SHA-256.**
- [ ] **Step 5: Review changed code for duplicate workflow logic, unsafe process handling, missing disposal, stale wording and C# 5 violations; fix under RED→GREEN if needed.**
- [ ] **Step 6: Mark this plan’s completed steps, commit all source/tests/docs locally with no push, and leave the original worktree clean.**

