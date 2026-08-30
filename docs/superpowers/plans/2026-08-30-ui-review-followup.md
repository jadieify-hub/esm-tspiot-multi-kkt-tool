# LM UI Review Follow-up Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Вернуть оператору документированную привязку контроллера к ЕСМ, сделать видимыми осиротевшие управляемые комплекты и устранить подтверждённые проблемы интерфейса перед полевой проверкой.

**Architecture:** Существующий безопасный `LmGatewayBindingWorkflow` остаётся единственной реализацией PUT/read-back. WinForms получает отдельный короткоживущий диалог учётных данных и отдельный partial-файл привязки; пароль не хранится в полях основной страницы и очищается после операции. Таблица строится как объединение строк ЕСМ и подтверждённого app-owned inventory, поэтому локальный комплект не исчезает после удаления ККТ из ЕСМ.

**Tech Stack:** C# 5 / .NET Framework 4.8 WinForms, .NET 8 shared tests, PowerShell UI/safety gates, MSBuild.

**Spec:** `docs/superpowers/specs/2026-08-26-multi-inn-lm-gateways-design.md`, `docs/superpowers/specs/2026-08-29-managed-local-module-instances-design.md`

## Global Constraints

- Сохранять совместимость production-исходников Legacy/helper с C# 5.
- Не сохранять логин и пароль в профиле, manifest, журнале или состоянии основной формы.
- Использовать только документированный `PUT /api/v1/settings/lm/{id}` и существующий защищённый read-back `/api/v2/info`.
- Не изменять vendor-файлы, штатные службы и правила владения app-owned компонентами.
- Компактный ZIP остаётся единственной сборкой по умолчанию и не содержит vendor-бинарников.

---

### Task 1: Shared-контракты привязки и отображения

**Files:**
- Modify: `tests/EsmTspiot.Shared.Tests/Program.cs`
- Modify: `src/EsmTspiot.Shared/Services/LmGatewayBindingSession.cs`
- Modify: `src/EsmTspiot.Shared/Services/CanonicalLmPlanHasher.cs`
- Create: `src/EsmTspiot.Shared/Services/KktServiceStateFormatter.cs`
- Modify: `src/EsmTspiot.Shared/Services/InstructionFileSelector.cs`

**Interfaces:**
- Produces: `LmGatewayBindingSession.BuildPlanFor(string)`.
- Produces: `CanonicalLmPlanHasher.IsWellFormedSha256(string)`.
- Produces: `KktServiceStateFormatter.ToDisplayText(string)`.
- Produces: `InstructionFileSelector.SelectAvailable(string)`.

- [x] **Step 1:** Добавить тесты: точечный план содержит только заданную ККТ; неизвестная ККТ даёт пустой план; SHA-256 принимает ровно 64 hex; состояния ЕСМ переводятся, неизвестные сохраняются; инструкция выбирает PDF, а при его отсутствии — `FIELD_TEST*.md`.
- [x] **Step 2:** Запустить `dotnet run --project tests\EsmTspiot.Shared.Tests\EsmTspiot.Shared.Tests.csproj -c Release` и подтвердить ожидаемый FAIL из-за отсутствующих API.
- [x] **Step 3:** Реализовать минимальные shared-методы без новых зависимостей и синтаксиса новее C# 5.
- [x] **Step 4:** Повторить net8-тесты и системный net48-прогон из `.github/workflows/ci.yml` до PASS.

### Task 2: Короткоживущая привязка ЕСМ

**Files:**
- Create: `src/EsmTspiot.WinForms.Shared/LmGatewayBindingDialog.cs`
- Create: `src/EsmTspiot.WinForms.Shared/LmGatewayPage.Binding.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmGatewayPage.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmGatewayPage.Layout.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmGatewayPage.Services.cs`
- Modify: `src/EsmTspiot.Legacy.WinForms/EsmTspiot.Legacy.WinForms.csproj`
- Modify: `src/EsmTspiot.Modern.WinForms/EsmTspiot.Modern.WinForms.csproj`
- Modify: `scripts/verify_lm_safety.ps1`
- Modify: `scripts/verify_ui_layout.ps1`

**Interfaces:**
- Consumes: `LmGatewayBindingSession.BuildPlanFor(string)` and `LmGatewayBindingWorkflow.ExecuteAsync(...)`.
- Produces: действие `Привязать к ЕСМ` только для выбранной строки ЕСМ с готовым управляемым контроллером.

- [x] **Step 1:** Расширить UI/safety-гейты проверками наличия действия, маскированного пароля, отсутствия credential-state на основной странице и узкого allowlist для краткоживущего диалога.
- [x] **Step 2:** Собрать Legacy и запустить оба гейта; подтвердить ожидаемый FAIL до реализации.
- [x] **Step 3:** Добавить диалог и partial-координатор, передающий копию credentials непосредственно в существующий workflow и очищающий локальные ссылки в `finally`.
- [x] **Step 4:** Повторить Legacy, UI и safety-проверки до PASS.

### Task 3: Осиротевшие комплекты

**Files:**
- Modify: `src/EsmTspiot.WinForms.Shared/LmGatewayPage.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmGatewayPage.Services.cs`
- Modify: `scripts/verify_ui_layout.ps1`

**Interfaces:**
- Produces: объединённая таблица ЕСМ + app-owned inventory; orphan row имеет `Нет в ЕСМ` и остаётся доступной для точечного удаления/очистки.

- [x] **Step 1:** Добавить headless-проверку строки inventory, отсутствующей в `_session.Rows`, и подтвердить FAIL.
- [x] **Step 2:** Дополнить `FillRows` синтетическими строками только из подтверждённого removable inventory, сохраняя ИНН, ordinal, softPort и endpoint из managed manifest.
- [x] **Step 3:** Запустить UI-гейт до PASS и убедиться, что штатная служба не появляется как orphan.

### Task 4: Точечные исправления интерфейса

**Files:**
- Modify: `src/EsmTspiot.WinForms.Shared/MainForm.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/KktDeletionConfirmationDialog.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmGatewayRemovalDialog.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmGatewayPage.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmGatewayPage.Layout.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmGatewayPage.Services.cs`
- Modify: `src/EsmTspiot.WinForms.Shared/LmAutomaticSetupDialog.cs`
- Modify: `scripts/verify_ui_layout.ps1`

**Interfaces:**
- Consumes: `KktServiceStateFormatter.ToDisplayText` и `InstructionFileSelector.SelectAvailable`.
- Produces: read-only журнал, рабочая локальная инструкция, прокручиваемая busy-таблица, короткий заголовок `Порт ПО`, русские состояния и безопасный default `Нет`.

- [x] **Step 1:** Добавить UI-проверки свойств формы и подтвердить FAIL на текущей сборке.
- [x] **Step 2:** Внести минимальные свойства/тексты и использовать `MessageBoxDefaultButton.Button2` во всех Yes/No, меняющих систему.
- [x] **Step 3:** Сделать автоматический первый refresh немодальным, оставив модальную ошибку у явной кнопки `Обновить`.
- [x] **Step 4:** Запустить shared-тесты и UI-гейт до PASS.

### Task 5: Полная верификация и поставка

**Files:**
- Modify if required: `README.md`, `INSTRUCTION_FOR_DUMMIES.md`
- Regenerate ignored artifact: `artifacts/release/MultiKKT-ESM-TSPioT-compact.zip`

- [x] **Step 1:** Запустить shared net8/net48, helper 108+, safety/UI gates, Legacy C# 5 и Modern Release.
- [x] **Step 2:** Запустить `scripts/package_compact_release.ps1`, проверить состав ZIP и SHA-256.
- [x] **Step 3:** Выполнить `git diff --check`, аудит diff и проверку отсутствия vendor/credential-артефактов.
- [x] **Step 4:** Создать локальный коммит; push и релиз не выполнять без отдельной команды пользователя.
