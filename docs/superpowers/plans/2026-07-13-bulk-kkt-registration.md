# Experimental Bulk KKT Registration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Добавить отдельную экспериментальную кнопку, которая находит все новые ККТ, назначает им последовательные пары портов и независимо выполняет POST/PUT для каждой с полным итоговым журналом.

**Architecture:** Чистый `BulkKktRegistrationPlanner` в общей библиотеке строит детерминированный план из ответов `/instances/info` и `/dkktList`; он не обращается к сети и тестируется отдельно. `MainForm` только получает ответы API, показывает единое подтверждение, последовательно исполняет готовый план через существующий `TspiotApiClient` и формирует итог. Существующие четыре обработчика не меняются.

**Tech Stack:** C# 5-compatible syntax, WinForms, .NET Framework 4.x system compiler, `HttpClient`, existing console test harness.

## Global Constraints

- Существующие четыре кнопки, их обработчики и обычный пошаговый сценарий не изменяются.
- Первая пара портов равна `50401/51401`, каждая следующая увеличивается на 1.
- `port=50401, softPort=0` резервирует пару `50401/51401`.
- Ошибка одной ККТ не останавливает остальные.
- Массовая операция не запускает PowerShell и не восстанавливает службы автоматически.
- Сохранить совместимость с Windows 7-11 и 32/64-разрядными системами; не добавлять внешние зависимости.

---

### Task 1: Планировщик ККТ и портов

**Files:**
- Create: `src/EsmTspiot.Shared/Models/BulkKktRegistrationItem.cs`
- Create: `src/EsmTspiot.Shared/Models/BulkKktRegistrationPlan.cs`
- Create: `src/EsmTspiot.Shared/Services/BulkKktRegistrationPlanner.cs`
- Modify: `tests/EsmTspiot.Shared.Tests/Program.cs`

**Interfaces:**
- Consumes: `DkktDeviceInfo`, `KktInstanceInfo`, `TspiotFormInput`, `ValidationResult`, `TspiotInputValidator.ValidatePut(input, true)`.
- Produces: `BulkKktRegistrationPlanner.Build(string baseUrl, string dkktPort, IList<DkktDeviceInfo> devices, IList<KktInstanceInfo> instances) : BulkKktRegistrationPlan`.
- Produces: `BulkKktRegistrationPlan.Items`, `BulkKktRegistrationPlan.ExistingDevices`.
- Produces: each `BulkKktRegistrationItem` contains `Device`, `Input`, and `Validation`.

- [ ] **Step 1: Add failing planner tests**

Register and implement tests in the existing harness for these exact behaviors:

```csharp
Run("Bulk planner assigns first sequential port pairs", BulkPlannerAssignsFirstSequentialPortPairs);
Run("Bulk planner reserves implicit first soft port", BulkPlannerReservesImplicitFirstSoftPort);
Run("Bulk planner skips occupied pair indexes", BulkPlannerSkipsOccupiedPairIndexes);
Run("Bulk planner excludes existing devices", BulkPlannerExcludesExistingDevices);
Run("Bulk planner marks invalid device data", BulkPlannerMarksInvalidDeviceData);
```

The first test builds two devices with no instances and asserts `50401/51401` and `50402/51402`. The second supplies an existing instance with `port=50401, softPort=0` and asserts the new device receives `50402/51402`. The third occupies `50401` and `51403` and asserts the next two assignments are pairs 2 and 4. The fourth asserts an existing serial appears only in `ExistingDevices`. The fifth supplies an invalid FN and asserts `item.Validation.IsValid == false`.

- [ ] **Step 2: Run tests and verify RED**

Run:

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
& $csc /nologo /define:NETFRAMEWORK /target:exe /out:artifacts\SharedTests.exe /reference:System.Net.Http.dll /reference:System.Runtime.Serialization.dll /reference:System.Web.Extensions.dll src\EsmTspiot.Shared\Logging\*.cs src\EsmTspiot.Shared\Models\*.cs src\EsmTspiot.Shared\Services\*.cs src\EsmTspiot.Shared\Validation\*.cs tests\EsmTspiot.Shared.Tests\Program.cs
```

Expected: compilation fails because `BulkKktRegistrationPlanner` and plan models do not exist.

- [ ] **Step 3: Implement minimal plan models**

Create models with read/write properties:

```csharp
public sealed class BulkKktRegistrationItem
{
    public DkktDeviceInfo Device { get; set; }
    public TspiotFormInput Input { get; set; }
    public ValidationResult Validation { get; set; }
}

public sealed class BulkKktRegistrationPlan
{
    public BulkKktRegistrationPlan()
    {
        Items = new List<BulkKktRegistrationItem>();
        ExistingDevices = new List<DkktDeviceInfo>();
    }

    public IList<BulkKktRegistrationItem> Items { get; private set; }
    public IList<DkktDeviceInfo> ExistingDevices { get; private set; }
}
```

- [ ] **Step 4: Implement minimal deterministic planner**

`Build` must:

```csharp
HashSet<string> existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
HashSet<int> occupiedIndexes = new HashSet<int>();
```

For every instance, calculate `port - 50400` and `softPort - 51400` and reserve only positive indexes from 1 through 1000; values outside these two supported sequences do not reserve an index. Devices whose trimmed serial is in `existingIds` go to `ExistingDevices`. Other devices receive the lowest positive unoccupied index, then an input with `Port = (50400 + index).ToString()`, `SoftPort = (51400 + index).ToString()`, common `baseUrl` and `dkktPort`; call `ValidatePut(input, true)` and store the result.

- [ ] **Step 5: Run shared tests and verify GREEN**

Run the compile command from Step 2 and then:

```powershell
.\artifacts\SharedTests.exe
```

Expected: all existing and five new shared tests pass.

### Task 2: Итоговые статусы массовой операции

**Files:**
- Create: `src/EsmTspiot.Shared/Models/BulkKktRegistrationResult.cs`
- Create: `src/EsmTspiot.Shared/Models/BulkKktRegistrationStatus.cs`
- Modify: `tests/EsmTspiot.Shared.Tests/Program.cs`

**Interfaces:**
- Consumes: serial number and status assigned by the WinForms orchestrator.
- Produces: enum values `Registered`, `AlreadyExists`, `InvalidData`, `AddFailed`, `RegistrationFailed` and result fields `KktSerial`, `Status`, `Details`.

- [ ] **Step 1: Add failing result-summary test**

Add a test that constructs one result of every status and calls a static summary formatter:

```csharp
string summary = BulkKktRegistrationResult.FormatSummary(results);
AssertContains(summary, "Зарегистрировано: 1");
AssertContains(summary, "Уже существует: 1");
AssertContains(summary, "Некорректные данные: 1");
AssertContains(summary, "Ошибок добавления: 1");
AssertContains(summary, "Создано без регистрации: 1");
```

- [ ] **Step 2: Run tests and verify RED**

Run the shared-test compile command. Expected: compilation fails because result types do not exist.

- [ ] **Step 3: Implement result and formatter**

Create the enum and a result model with a static `FormatSummary(IList<BulkKktRegistrationResult> results)` that counts every enum value and returns the five Russian lines above. Keep this presentation helper independent of WinForms.

- [ ] **Step 4: Run shared tests and verify GREEN**

Compile and run `artifacts\SharedTests.exe`. Expected: all tests pass.

### Task 3: Экспериментальная кнопка и последовательное выполнение

**Files:**
- Modify: `src/EsmTspiot.WinForms.Shared/MainForm.cs`

**Interfaces:**
- Consumes: `BulkKktRegistrationPlanner.Build`, existing `_client.GetInstancesAsync`, `_client.GetDkktListAsync`, `_client.AddInstanceAsync`, `_client.RegisterInstanceAsync`, `AppendResponse`, and request factories in `TspiotInputValidator`.
- Produces: new handler `RegisterAllKktsExperimentalAsync()` and one new `_bulkRegisterButton`.

- [ ] **Step 1: Add the isolated UI control without changing existing controls**

Declare `_bulkRegisterButton`, configure it with text `ЭКСПЕРИМЕНТ: добавить и зарегистрировать все ККТ`, add it as a new full-width third row in the existing action grid, and add it to `_actionButtons` through the existing `ConfigureButton` path. Increase only the action panel row count/height required for this button; preserve the texts and event handlers of the four existing buttons.

- [ ] **Step 2: Implement discovery and all-or-nothing preflight**

The new handler reads only `BaseUrl` and `dkktPort` from the form for shared settings. It validates the base URL and dkkt port, performs and logs both GET requests, aborts before POST when either response fails, parses both responses, then calls the planner.

If `plan.Items.Count == 0`, log existing devices and show `Новых ККТ для добавления не найдено.` without mutation.

- [ ] **Step 3: Build and show one confirmation**

Build text containing every existing device as skipped and every planned item as:

```text
kktSerial=...; fnSerial=...; kktInn=...; port=...; softPort=...; dkktPort=...
```

Include validation errors and warnings per item. Show one `MessageBoxButtons.YesNo` warning. Return without POST/PUT when the user chooses No.

- [ ] **Step 4: Execute every valid item independently**

For invalid items, append an `InvalidData` result and continue. For valid items:

```csharp
ApiResponse addResponse = await _client.AddInstanceAsync(
    item.Input.BaseUrl,
    TspiotInputValidator.CreateAddRequest(item.Input));
AppendResponse(addResponse);
```

On POST failure append `AddFailed` and continue. On POST success execute the existing registration request factory and client call. Append `Registered` for PUT success or `RegistrationFailed` for PUT failure. Do not call `PrepareServiceRecovery` from this handler.

- [ ] **Step 5: Log and show the final summary**

Append one line per result with serial, status and details, append `BulkKktRegistrationResult.FormatSummary(results)`, keep the existing log auto-scroll behavior, and show one final information/warning message depending on whether every new valid item registered successfully.

- [ ] **Step 6: Compile both WinForms targets with the legacy compiler**

Run:

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
& $csc /nologo /define:NETFRAMEWORK /target:winexe /platform:anycpu /out:"artifacts\legacy-csc\MultiККТEsmTspiot-compact8-experimental-all-kkt.exe" /win32icon:"src\EsmTspiot.WinForms.Shared\Assets\app.ico" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Net.Http.dll /reference:System.Runtime.Serialization.dll /reference:System.Web.Extensions.dll src\EsmTspiot.Shared\Logging\*.cs src\EsmTspiot.Shared\Models\*.cs src\EsmTspiot.Shared\Services\*.cs src\EsmTspiot.Shared\Validation\*.cs src\EsmTspiot.WinForms.Shared\MainForm.cs src\EsmTspiot.WinForms.Shared\Program.cs
```

Expected: exit code 0 and the named EXE exists.

### Task 4: Финальная регрессия и артефакт

**Files:**
- Verify: `artifacts/SharedTests.exe`
- Verify: `artifacts/legacy-csc/MultiККТEsmTspiot-compact8-experimental-all-kkt.exe`

**Interfaces:**
- Consumes: completed shared library and WinForms UI.
- Produces: verified executable for user testing.

- [ ] **Step 1: Run all shared tests**

Run `.\artifacts\SharedTests.exe`. Expected: all tests pass with exit code 0.

- [ ] **Step 2: Verify executable metadata and launchability**

Use `Get-Item` to verify the artifact exists and has nonzero length. Start it, wait until the main window appears, record its title and dimensions, then close only that launched process.

- [ ] **Step 3: Inspect source scope**

Run `git diff -- src tests docs/superpowers/plans/2026-07-13-bulk-kkt-registration.md` and confirm that existing button labels and handlers are unchanged and no PowerShell recovery call was added to the bulk handler.
