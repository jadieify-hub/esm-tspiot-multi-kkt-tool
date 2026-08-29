# LM Gateway ESM Binding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Добавить в общую библиотеку безопасный, тестируемый и не зависящий от Windows-служб контракт привязки зарегистрированной ККТ к уже готовому контроллеру ЛМ ЧЗ через документированный API ЕСМ.

> **Дополнение 2026-08-28:** этот план фиксирует историческую реализацию PUT-only. После получения официального «Руководства по интеграции ЕСМ для разработчиков ПМСР» v1.9 текущая реализация дополнена `GET /api/v2/info`: `BindingVerified` возможен только после отдельной сверки ККТ/ИНН/endpoint; при недоступном read-back сохраняется предусмотренный планом `BindingAccepted`.

**Architecture:** `LmGatewayDiscoveryWorkflow` получает только зарегистрированные ККТ и их ИНН, чистый `LmGatewayBindingPlanner` строит план без секретов, а `LmGatewayBindingWorkflow` получает credential одной строки непосредственно перед PUT. Пароль маскируется до попадания в `ApiResponse` и затем повторно на общей границе журналирования. Потерянный HTTP-ответ не повторяется автоматически, пока идемпотентность не доказана; строка получает `RequiresAttention`. Этот этап не создает UI и службы; он формирует безопасный фундамент для второго плана.

**Tech Stack:** C# 5-compatible syntax, `net48;net8.0`, `HttpClient`, `DataContractJsonSerializer`, текущий консольный тестовый раннер без новых NuGet-зависимостей.

**Spec:** `docs/superpowers/specs/2026-08-26-multi-inn-lm-gateways-design.md`

**Toolchain prerequisite:** До первого RED выполнить приведенный ниже preflight в той же PowerShell-сессии, в которой запускаются команды плана. Нужны .NET 8 SDK, .NET Framework 4.8 Targeting/Developer Pack и Visual Studio Build Tools/MSBuild. Системный .NET Framework runtime и `csc.exe` не заменяют SDK/targeting pack для SDK-style проектов. Если preflight не проходит, реализацию не начинать и не объявлять матрицу пройденной; сначала установить недостающие компоненты либо использовать подготовленный CI-агент.

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

Все последующие вызовы MSBuild используют `& $msbuild`, а не предполагают наличие `msbuild` в обычном `PATH`.

## Global Constraints

- До начала реализации спецификация должна пройти ревью.
- Не открывать и не анализировать повторно `RollingPinForESM.zip`; не использовать его имена, тексты, ресурсы или код.
- Единственный внешний изменяющий контракт этого этапа — `PUT /api/v1/settings/lm/{escaped-id}` с полями `address`, `port`, `login`, `password`.
- Использовать внутренние имена `LmGateway*`; термин `controller` оставлять только там, где он описывает официальный компонент или внешний адрес.
- Не добавлять логику ЛМ в `BulkRegistrationWorkflow`, `ServiceRecoveryCommandBuilder` или `KktPortPairAllocator`.
- Не создавать Windows-службы, не запускать PowerShell, `sc.exe`, установщики или сторонние EXE.
- Не сохранять логин/пароль в файлы состояния. Пароль не должен попадать в UI-журнал, файловый журнал, диагностику или `ApiResponse.RequestBody`.
- Не утверждать, что PUT проверен фактически, только потому что сервер ответил 2xx. Статус называется `BindingAccepted`, а не `Verified`.
- Не повторять PUT автоматически после timeout/connection failure: запрос мог быть применен до разрыва. Без подтвержденного read-back результат называется `RequiresAttention`.
- Сохранять C# 5-совместимый стиль: явные типы, без интерполяции, `?.`, `nameof`, inline `out`, tuples и expression-bodied members.
- Все новые сообщения пользователю и журнала — на русском.
- После каждой задачи соблюдать RED → GREEN → commit. Коммиты не пушить до отдельного указания.

---

### Task 1: Зафиксировать происхождение внешнего API-контракта

**Files:**

- Create: `docs/research/2026-08-26-lm-gateway-provenance.md`

**Interfaces:**

- Records: факт, источник, дата проверки, применяемое внутреннее имя и запрещенные сторонние источники.
- Does not produce executable code.

- [ ] **Step 1: Создать журнал происхождения**

Записать отдельными строками:

```text
Факт: PUT /api/v1/settings/lm/{id}
Источник: официальная инструкция CSI
Поля: address:string, port:number, login:string, password:string
Интерпретация: address/port указывают на контроллер ЛМ, gRPC-порт
Проверено: 2026-08-26
```

Добавить ссылку:

```text
https://crystals.atlassian.net/wiki/spaces/SR10SUPPORT/pages/6404898850/SetRetail10
```

Отдельно указать, что публичный источник не подтверждает API очистки LM-настройки, Windows multi-instance, конфигурацию второго локального порта и безопасную повторяемость PUT после потерянного ответа.

- [ ] **Step 2: Зафиксировать границу независимой реализации**

Указать, что сторонний архив не является источником имен, кода, UI, сценариев или конфигурационного формата; технические факты для служб будут получены только по процедуре второго плана.

- [ ] **Step 3: Проверить документ**

Run:

```powershell
rg -n "RollingPin|settings/lm|address|port|login|password|не подтверждает" docs\research\2026-08-26-lm-gateway-provenance.md
```

Expected: присутствуют источник, четыре поля, явные ограничения и запрет на заимствование; паролей или данных реальной организации нет.

- [ ] **Step 4: Commit**

```powershell
git add docs/research/2026-08-26-lm-gateway-provenance.md
git commit -m "Зафиксировать происхождение контракта ЛМ ЧЗ"
```

---

### Task 2: Маскирование секретов до постоянного журнала

**Files:**

- Create: `src/EsmTspiot.Shared/Logging/SensitiveDataMasker.cs`
- Modify: `src/EsmTspiot.Shared/Logging/DiagnosticMasker.cs`
- Modify: `src/EsmTspiot.Shared/Logging/LogFormatter.cs`
- Modify: `tests/EsmTspiot.Shared.Tests/Program.cs`

**Interfaces:**

- Produces: `SensitiveDataMasker.Mask(string text) : string`.
- `DiagnosticMasker.Mask` first delegates secret removal to `SensitiveDataMasker`, then masks paths and fiscal identifiers.
- `LogFormatter.Format` masks request body, response body and decoded message before formatting.

- [ ] **Step 1: Add four failing tests**

Register exact tests:

```csharp
Run("Sensitive masker redacts JSON credentials", SensitiveMaskerRedactsJsonCredentials);
Run("Sensitive masker redacts key value credentials", SensitiveMaskerRedactsKeyValueCredentials);
Run("Log formatter never persists reflected password", LogFormatterNeverPersistsReflectedPassword);
Run("Sensitive masker preserves ordinary fields", SensitiveMaskerPreservesOrdinaryFields);
```

Assertions:

- JSON keys `password`, `newPassword`, `token`, `secret`, `authorization`, `apiKey`, `connectionString` are replaced with `***`, case-insensitively;
- `password=secret-value` is masked;
- a password containing JSON escapes for quote, backslash, newline and Unicode is fully masked; neither raw nor serialized fragments after the first escape remain;
- the same escaped password reflected in `ResponseBody` and `DecodedMessage` does not occur in formatted output;
- `address`, `port`, `login`, `kktSerial` and normal Russian text remain unchanged.

- [ ] **Step 2: Run net8 tests and verify RED**

```powershell
dotnet run --project tests\EsmTspiot.Shared.Tests\EsmTspiot.Shared.Tests.csproj -c Release
```

Expected: compilation fails because `SensitiveDataMasker` does not exist.

- [ ] **Step 3: Implement the minimal reusable masker**

Move only credential-pattern responsibility into `SensitiveDataMasker`. Do not move fiscal-number or filesystem-path masking from `DiagnosticMasker`.

Requirements:

- a small JSON string-token scanner that understands escaped quote, backslash, control and Unicode sequences for sensitive JSON fields;
- a separate compiled case-insensitive regex only for `key=value` diagnostic text;
- no full JSON parsing dependency, so malformed error bodies are still sanitized conservatively;
- null becomes empty string, matching current logging conventions;
- do not include `login` in the sensitive-key list.

- [ ] **Step 4: Apply defense in depth at both logging boundaries**

Update `DiagnosticMasker.Mask` to call `SensitiveDataMasker.Mask` before its path and identifier rules. Update `LogFormatter.Format` to sanitize all server-controlled or request-controlled strings before appending them.

- [ ] **Step 5: Run net8 and net48 tests and verify GREEN**

```powershell
dotnet run --project tests\EsmTspiot.Shared.Tests\EsmTspiot.Shared.Tests.csproj -c Release
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$files = Get-ChildItem -Recurse "src\EsmTspiot.Shared" -Filter *.cs |
  Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
  ForEach-Object { $_.FullName }
$files += (Resolve-Path "tests\EsmTspiot.Shared.Tests\Program.cs").Path
& $csc /nologo /define:NETFRAMEWORK /out:"$env:TEMP\SharedTests.exe" /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Runtime.Serialization.dll $files
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& "$env:TEMP\SharedTests.exe"
```

Expected: all 72 existing and 4 new tests pass in both targets; raw test password is absent from console output.

- [ ] **Step 6: Commit**

```powershell
git add src/EsmTspiot.Shared/Logging tests/EsmTspiot.Shared.Tests/Program.cs
git commit -m "Исключить секреты из журналов"
```

---

### Task 3: Типизированный PUT привязки ЛМ в API-клиенте

**Files:**

- Create: `src/EsmTspiot.Shared/Models/LmConnectionRequest.cs`
- Modify: `src/EsmTspiot.Shared/Models/TspiotDefaults.cs`
- Modify: `src/EsmTspiot.Shared/Services/ITspiotApiClient.cs`
- Modify: `src/EsmTspiot.Shared/Services/TspiotApiClient.cs`
- Modify: `tests/EsmTspiot.Shared.Tests/Program.cs`

**Interfaces:**

- Produces: `[DataContract] LmConnectionRequest` with `[DataMember(Name = "address")] string Address`, `port:int`, `login:string`, `password:string`.
- Produces: `ITspiotApiClient.ConfigureLmGatewayAsync(string baseUrl, string id, LmConnectionRequest request, CancellationToken cancellationToken)`.
- Adds: `TspiotDefaults.LmSettingsPath = "/api/v1/settings/lm"`.
- Extends internal HTTP send path with separate real body and log-safe body.

- [ ] **Step 1: Add three failing API-contract tests**

Register:

```csharp
Run("LM gateway API uses documented PUT contract", LmGatewayApiUsesDocumentedPutContract);
Run("LM gateway API escapes instance id", LmGatewayApiEscapesInstanceId);
Run("LM gateway API response stores redacted request", LmGatewayApiResponseStoresRedactedRequest);
```

The first test must capture the actual `HttpRequestMessage` and assert:

```text
Method: PUT
URL: http://127.0.0.1:51077/api/v1/settings/lm/00105700000001
Content-Type: application/json; charset=utf-8
Body keys exactly: address, port, login, password
port is JSON number, not string
```

Extend the existing `RecordedHttpRequest` test record with a `ContentType` field populated by `RecordingHttpHandler`; otherwise the content-type assertion is not observable.

The second passes an invalid-for-domain but useful URI test id `a/b c` and asserts the path contains `a%2Fb%20c`, proving segment escaping. Domain validation remains outside the client.

The third asserts the captured HTTP body contains the real test password while returned `ApiResponse.RequestBody` contains `"password":"***"` and not the raw value.

- [ ] **Step 2: Run tests and verify RED**

Run the net8 command from Task 2. Expected: missing request type, constant and interface method.

- [ ] **Step 3: Implement DTO and interface**

Use the same DataContract style as `AddTspiotRequest` and `RegisterTspiotRequest`. Do not add convenience `ToString` containing fields.

- [ ] **Step 4: Implement the client method without secret retention**

Refactor private sending logic to accept:

```csharp
SendAsync(string method, string url, string requestBody, string requestBodyForLog, CancellationToken cancellationToken)
```

Existing methods pass the same non-secret body twice. `ConfigureLmGatewayAsync` serializes once, passes the real JSON for `StringContent` and `SensitiveDataMasker.Mask(realJson)` for `ApiResponse.RequestBody`.

Do not log from inside the client. Do not mutate the caller's request. Do not catch cancellation requested by the caller.

- [ ] **Step 5: Update every fake implementation**

Add `ConfigureLmGatewayAsync` to `FakeTspiotApiClient` in the test harness. Give it an explicit response queue and record `(baseUrl, id, request)` calls so workflow ordering can be tested later.

Append the LM PUT as request index `[7]`, so existing assertions for `[0]..[6]` do not shift. Update the exact assertion to `AssertEqual(8, handler.Requests.Count, "Expected eight requests.");`; changing only the number while leaving `Expected seven requests.` is not accepted.

- [ ] **Step 6: Run net8 and net48 tests and verify GREEN**

Run both commands from Task 2. Expected: 79 tests total pass; captured real body is correct and retained log body is redacted.

- [ ] **Step 7: Commit**

```powershell
git add src/EsmTspiot.Shared/Models/LmConnectionRequest.cs src/EsmTspiot.Shared/Models/TspiotDefaults.cs src/EsmTspiot.Shared/Services/ITspiotApiClient.cs src/EsmTspiot.Shared/Services/TspiotApiClient.cs tests/EsmTspiot.Shared.Tests/Program.cs
git commit -m "Добавить безопасную привязку ЛМ в API ЕСМ"
```

---

### Task 4: Обнаружение зарегистрированных ККТ и ИНН

**Files:**

- Create: `src/EsmTspiot.Shared/Models/LmGatewayKkt.cs`
- Create: `src/EsmTspiot.Shared/Models/LmGatewayDiscovery.cs`
- Create: `src/EsmTspiot.Shared/Models/LmGatewayDiscoveryIssue.cs`
- Create: `src/EsmTspiot.Shared/Models/LmGatewayProgress.cs`
- Create: `src/EsmTspiot.Shared/Services/LmGatewayDiscoveryWorkflow.cs`
- Modify: `tests/EsmTspiot.Shared.Tests/Program.cs`

**Interfaces:**

- Produces: `LmGatewayKkt` with `InstanceId`, `KktSerial`, `KktInn`, `FnSerial`, ESM `Port`, `SoftPort`, `DkktPort`, `ServiceState`.
- Produces: `LmGatewayDiscovery.Items`, `.Issues`, `.ErrorMessage`, `.IsSuccessful`.
- Produces: `LmGatewayDiscoveryWorkflow.DiscoverAsync(string baseUrl, Action<LmGatewayProgress> progress, CancellationToken token)`.

- [ ] **Step 1: Add five failing discovery tests**

Register:

```csharp
Run("LM discovery returns registered KKT with INN", LmDiscoveryReturnsRegisteredKktWithInn);
Run("LM discovery aborts on malformed instance list", LmDiscoveryAbortsOnMalformedInstanceList);
Run("LM discovery continues after one malformed detail", LmDiscoveryContinuesAfterOneMalformedDetail);
Run("LM discovery excludes unregistered instance", LmDiscoveryExcludesUnregisteredInstance);
Run("LM discovery honors cancellation", LmDiscoveryHonorsCancellation);
```

Use the existing parsers and fake client. The happy-path test supplies two instances with different INNs and asserts stable instance-list order. The malformed-detail test records an issue for only that id and still returns the next valid KKT. An instance with no complete `regData` is not eligible for binding and gets a nonfatal issue.

- [ ] **Step 2: Run tests and verify RED**

Expected: missing discovery types.

- [ ] **Step 3: Implement models with no UI dependencies**

Collections are initialized in constructors and exposed as `IList<T>` with private setters, matching existing models. `LmGatewayDiscoveryIssue` stores only `InstanceId` and a Russian diagnostic message.

- [ ] **Step 4: Implement sequential discovery**

Algorithm:

1. `GetInstancesAsync` once;
2. fail before per-instance calls if HTTP or `InstanceInfoParser.TryParse` fails;
3. for each listed instance call `GetInstanceAsync` with the same cancellation token;
4. require successful response, valid `InstanceDetailsParser` result, `HasCompleteRegistrationData`, and equality of trimmed `instance.Id` with `regData.kktSerial`;
5. add invalid/mismatching details to `Issues`, never to `Items`;
6. preserve source order and report progress without credentials.

Do not call `/dkktList`: it describes physically detected devices, not necessarily successfully registered instances.

- [ ] **Step 5: Run net8 and net48 tests and verify GREEN**

Expected: 84 tests pass. Cancellation propagates as `OperationCanceledException`; it is not converted into a connection failure.

- [ ] **Step 6: Commit**

```powershell
git add src/EsmTspiot.Shared/Models/LmGatewayKkt.cs src/EsmTspiot.Shared/Models/LmGatewayDiscovery.cs src/EsmTspiot.Shared/Models/LmGatewayDiscoveryIssue.cs src/EsmTspiot.Shared/Models/LmGatewayProgress.cs src/EsmTspiot.Shared/Services/LmGatewayDiscoveryWorkflow.cs tests/EsmTspiot.Shared.Tests/Program.cs
git commit -m "Добавить обнаружение ККТ для контроллеров ЛМ"
```

---

### Task 5: Чистая валидация и план привязки

**Files:**

- Create: `src/EsmTspiot.Shared/Models/LmGatewayBindingInput.cs`
- Create: `src/EsmTspiot.Shared/Models/LmGatewayBindingItem.cs`
- Create: `src/EsmTspiot.Shared/Models/LmGatewayBindingPlan.cs`
- Create: `src/EsmTspiot.Shared/Validation/LmGatewayInputValidator.cs`
- Create: `src/EsmTspiot.Shared/Services/LmGatewayBindingPlanner.cs`
- Modify: `tests/EsmTspiot.Shared.Tests/Program.cs`

**Interfaces:**

- `LmGatewayBindingInput`: `KktSerial`, `KktInn`, `ControllerAddress`, `ControllerGrpcPort`. It deliberately has no login/password.
- `LmGatewayInputValidator.ValidateBinding(input) : ValidationResult`.
- `LmGatewayBindingPlanner.Build(LmGatewayDiscovery discovery, IList<LmGatewayBindingInput> inputs) : LmGatewayBindingPlan`.
- Each plan item contains a discovered KKT, a copied normalized input and validation; no formatter emits password.

- [ ] **Step 1: Add six failing planner tests**

Register:

```csharp
Run("LM binding planner matches by KKT identity", LmBindingPlannerMatchesByKktIdentity);
Run("LM binding planner keeps different INNs separate", LmBindingPlannerKeepsDifferentInnsSeparate);
Run("LM binding planner requires one input per KKT", LmBindingPlannerRequiresOneInputPerKkt);
Run("LM binding planner rejects duplicate local endpoints", LmBindingPlannerRejectsDuplicateLocalEndpoints);
Run("LM binding planner rejects invalid loopback and port", LmBindingPlannerRejectsInvalidLoopbackAndPort);
Run("LM binding plan cannot contain credentials", LmBindingPlanCannotContainCredentials);
```

Required behavior:

- match by exact normalized 14-digit KKT serial, not by INN;
- two KKT with different INNs remain two rows even if endpoint fields otherwise match;
- only `127.0.0.1`, `localhost` and IPv6 loopback are accepted at the validation layer, then normalized to `127.0.0.1` for the request;
- port range `1..65535`;
- each controller endpoint unique across the plan;
- duplicate or missing input is an item-level blocking error;
- reflection over plan/input models finds no `Password`, `Token`, `Secret` or credential object; plan summaries therefore cannot retain a password.

- [ ] **Step 2: Run tests and verify RED**

Expected: missing planner and models.

- [ ] **Step 3: Implement the dedicated validator**

Do not expand the already large `TspiotInputValidator` with controller-specific rules. Reuse only its public primitive validation if it avoids duplication without coupling to KKT `port/softPort/dkktPort`.

- [ ] **Step 4: Implement deterministic planner**

Build a serial-keyed dictionary using `StringComparer.Ordinal`. Copy and normalize all strings; never retain a reference to mutable UI input. Preserve discovery order. Mark duplicates on every affected row, not only the second one. Do not make any HTTP or filesystem calls.

- [ ] **Step 5: Run net8 and net48 tests and verify GREEN**

Expected: 90 tests pass.

- [ ] **Step 6: Commit**

```powershell
git add src/EsmTspiot.Shared/Models/LmGatewayBindingInput.cs src/EsmTspiot.Shared/Models/LmGatewayBindingItem.cs src/EsmTspiot.Shared/Models/LmGatewayBindingPlan.cs src/EsmTspiot.Shared/Validation/LmGatewayInputValidator.cs src/EsmTspiot.Shared/Services/LmGatewayBindingPlanner.cs tests/EsmTspiot.Shared.Tests/Program.cs
git commit -m "Добавить планирование привязок контроллеров ЛМ"
```

---

### Task 6: Последовательный workflow привязки с короткоживущими credentials

**Files:**

- Create: `src/EsmTspiot.Shared/Models/LmGatewayBindingStatus.cs`
- Create: `src/EsmTspiot.Shared/Models/LmGatewayBindingResult.cs`
- Create: `src/EsmTspiot.Shared/Models/LmGatewayBindingOutcome.cs`
- Create: `src/EsmTspiot.Shared/Models/LmGatewayBindingProgress.cs`
- Create: `src/EsmTspiot.Shared/Models/LmGatewayCredentials.cs`
- Create: `src/EsmTspiot.Shared/Services/LmGatewayBindingWorkflow.cs`
- Modify: `tests/EsmTspiot.Shared.Tests/Program.cs`

**Interfaces:**

- Statuses: `BindingAccepted`, `Invalid`, `BindingFailed`, `Cancelled`, `RequiresAttention`.
- Produces: `ExecuteAsync(string baseUrl, LmGatewayBindingPlan plan, Func<string, LmGatewayCredentials> credentialProvider, Action<LmGatewayBindingProgress> progress, CancellationToken token)`.
- Uses: `ITspiotApiClient.ConfigureLmGatewayAsync` only.

- [ ] **Step 1: Add six failing workflow tests**

Register:

```csharp
Run("LM binding workflow sends items sequentially", LmBindingWorkflowSendsItemsSequentially);
Run("LM binding workflow skips invalid item and continues", LmBindingWorkflowSkipsInvalidItemAndContinues);
Run("LM binding workflow marks lost response for attention", LmBindingWorkflowMarksLostResponseForAttention);
Run("LM binding workflow does not retry permanent HTTP error", LmBindingWorkflowDoesNotRetryPermanentHttpError);
Run("LM binding workflow preserves partial results on cancellation", LmBindingWorkflowPreservesPartialResultsOnCancellation);
Run("LM binding workflow progress never contains password", LmBindingWorkflowProgressNeverContainsPassword);
```

The happy path asserts exact call order by KKT serial and that `credentialProvider` is called only immediately before its row. A timeout/connection failure produces one call and `RequiresAttention`; it is not repeated because the server may already have applied the request. A 4xx/5xx response is recorded once as permanent. Cancellation after one success records the completed row and marks untouched rows `Cancelled` without requesting their credentials.

- [ ] **Step 2: Run tests and verify RED**

Expected: missing workflow types.

- [ ] **Step 3: Implement outcome and redacted progress**

`LmGatewayBindingProgress` may contain an `ApiResponse` only because its request body is already redacted by Task 3. It must not contain the input object, credentials or credential provider. `LmGatewayBindingResult.Details` uses decoded status and never request JSON.

- [ ] **Step 4: Implement workflow**

For a valid item request one short-lived `LmGatewayCredentials` immediately before transport, validate nonempty login/password, then create a new `LmConnectionRequest` with:

```text
address = normalized local controller address
port = local controller gRPC port
login/password = short-lived credential object
```

Call PUT sequentially. Treat a 2xx response as `BindingAccepted`; do not synthesize `Verified`. Do not retry any result automatically. An `IsConnectionFailure` response or cancellation after send with unknown result marks the current row `RequiresAttention`; untouched rows become `Cancelled` only when cancellation was requested.

- [ ] **Step 5: Drop short-lived secret references**

After a row is complete, release workflow references to `LmGatewayCredentials` and `LmConnectionRequest`. Do not claim that immutable .NET strings were securely zeroed. The enforceable guarantees are: credentials are requested per row, never enter the plan/progress/result, are not persisted, and are not logged.

- [ ] **Step 6: Run net8 and net48 tests and verify GREEN**

Expected: 96 tests pass; no test secret occurs in formatted progress or result text.

- [ ] **Step 7: Commit**

```powershell
git add src/EsmTspiot.Shared/Models/LmGatewayBindingStatus.cs src/EsmTspiot.Shared/Models/LmGatewayBindingResult.cs src/EsmTspiot.Shared/Models/LmGatewayBindingOutcome.cs src/EsmTspiot.Shared/Models/LmGatewayBindingProgress.cs src/EsmTspiot.Shared/Models/LmGatewayCredentials.cs src/EsmTspiot.Shared/Services/LmGatewayBindingWorkflow.cs tests/EsmTspiot.Shared.Tests/Program.cs
git commit -m "Добавить последовательную привязку ККТ к контроллерам ЛМ"
```

---

### Task 7: Регрессия первого этапа и review checkpoint

**Files:**

- Verify: `src/EsmTspiot.Shared/**`
- Verify: `src/EsmTspiot.WinForms.Shared/**`
- Verify: `tests/EsmTspiot.Shared.Tests/Program.cs`
- Verify: `docs/research/2026-08-26-lm-gateway-provenance.md`

**Interfaces:**

- Produces no new behavior; proves that Phase 1 is safe to use from Phase 2.

- [ ] **Step 1: Run the complete matrix**

```powershell
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
& $msbuild src\EsmTspiot.Legacy.WinForms\EsmTspiot.Legacy.WinForms.csproj /restore /p:Configuration=Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build src\EsmTspiot.Modern.WinForms\EsmTspiot.Modern.WinForms.csproj -c Release
```

Expected: 96/96 shared tests pass in net8 and net48; Legacy and Modern compile with zero errors. No UI behavior has changed yet.

- [ ] **Step 2: Search for secret leakage and forbidden coupling**

```powershell
rg -n "RequestBody|password|LmGateway|ServiceRecovery|PowerShell|sc\.exe|RollingPin" src tests docs/research
```

Review every hit and confirm:

- real password occurs only in the in-memory HTTP serialization path and test fixtures;
- no result/log/progress formatter exposes it;
- no `LmGateway` service references recovery builder, shell or third-party names;
- API field names remain exactly the documented external contract.

- [ ] **Step 3: Perform architectural review**

Reviewer checkpoint:

- no duplicate JSON serialization;
- discovery and planner are pure enough to test;
- KKT identity is not replaced with INN identity;
- API 2xx is called `Accepted`, not `Verified`;
- `ITspiotApiClient` fake and all callers remain coherent;
- no Windows-specific dependency entered `EsmTspiot.Shared`;
- no user-visible README claim is changed before the final feature exists.

- [ ] **Step 4: Record review outcome**

Append a dated `Phase 1 review` section to `docs/research/2026-08-26-lm-gateway-provenance.md` with commit hash, test counts and any rejected design idea. Do not begin the service plan until all blocking comments are resolved.

- [ ] **Step 5: Commit review record**

```powershell
git add docs/research/2026-08-26-lm-gateway-provenance.md
git commit -m "Зафиксировать ревью привязки контроллеров ЛМ"
```
