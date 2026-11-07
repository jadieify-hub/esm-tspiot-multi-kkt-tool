# AntdUI в рабочей MultiKKT Implementation Plan

> **For agentic workers:** использовать superpowers:executing-plans, самостоятельная реализация и одно независимое финальное ревью.

**Goal:** непубличный рабочий ZIP 11.3.29.0-test с оформлением предпросмотра 02.
**Architecture:** общий набор цветов/оформления и узкие компоненты шагов/навигации;
сохраняются реальные workflow, DataGridView и TextBox, без второго состояния касс.
**Tech Stack:** WinForms, AntdUI 2.4.18, net48 x86 / net8 Windows, C#5 совместимость общих исходников.
**Spec:** ../specs/2026-11-07-antdui-working.md

## Global Constraints

- Ветка codex/antdui-working-11.3.29 от 449be9e, worktree lm-server.
- Не изменять основную папку с чужими правками; туда копируется только готовый архив.
- Windows 10/11; без публикации/push/merge и без мутаций живого оборудования.
- Полные serial, COM 72 px, сырой черновик и границы применения сохраняются.
- Helper trust/ownership/подтверждения удаления не ослабляются.

## Review Focus

- Выбор режима/вкладки при busy не запускает посторонний процесс.
- Возврат с обзора или журнала не теряет pending редактор/невалидный ввод.
- Переход из обзора на настройку сохраняет выбранную фактическую ККТ.
- Маленькое окно и несколько ИНН не прячут основную кнопку и удалённые поля.
- AntdUI.dll присутствует только в операторской поставке, лицензия приложена.

## Task 1: Общая оболочка и мастер

Files: новый OperatorTheme.cs, SetupStepStrip.cs, MainForm.Appearance.cs; MainForm.cs/.Wizard.cs/.ManualDiscovery.cs; оба csproj; WizardFlowCheck.cs и verify_ui_layout.ps1.
- [x] RED: проверка нового выбора режима/навигации и отдельного обзора; существующие сценарии остаются.
- [x] AntdUI-кнопки и реальная навигация, карточки шагов, оформление настоящей таблицы и полей.
- [x] GREEN: поиск/регистрация выбранной, авто, смена строк, Stop/X и destination checks.

## Task 2: ЛМ и обзор

Files: LmSetupEditor.cs, LmGatewayPage.Layout.cs и частичные классы с кнопками; проверки редактора и обзора.
- [x] Общий оформленный выбор размещения и поля; прежние имена, валидация и session credentials.
- [x] Обзор реальных компонентов, отдельная диагностика, служебные операции вторичны.
- [x] Проверки сырого черновика, endpoint credentials, частичных итогов и удалённых компонентов проходят.

## Task 3: Проверка и поставка

Files: AssemblyInfo main/helper, package_compact_release.ps1, README, инструкция 11.3.29.0-test.
- [x] Версии, README и поставка с AntdUI.dll/Apache-2.0, без UI-зависимости у helper.
- [x] RELEASE_MATRIX_OK и актуальные изображения основных состояний.
- [x] Независимое финальное ревью, исправления существенных замечаний.
- [x] Упаковка, проверка состава/сумм и запуск распакованного ZIP; копия в artifacts/test.

## Ledger

- Пользователь уже выбрал оформление и поручил рабочую реализацию; повторное утверждение не требуется.
- Ruling: адаптировать нативные DataGridView и TextBox к палитре, сохранив обработчики и accessibility; не создавать зеркальную AntdUI-таблицу и второй набор данных.
- Ruling: текущий изолированный worktree используется повторно; основная рабочая папка не меняется.
- RED: AntdWorkingCheck отверг старый radio-selector; WizardFlowCheck с проверкой всех родительских границ обнаружил скрытый редактор. Логи artifacts/test/ui-v29-red.log и ui-v29-clipping-red.log.
- GREEN: RELEASE_MATRIX_OK (artifacts/test/release-matrix-v29.log): shared/operator net8+net48, 137 provisioner, C#5, safety, UI и Modern без предупреждений. После финального уточнения высоты карточки повторно прошли ui-v29-final.log и build-modern-v29-final.log.
- Ruling: OperatorCard измеряет единственный дочерний layout, потому что AntdUI.Panel не рассчитывает предпочтительный размер содержимого для AutoSize; без этого редактор схлопывался.
- Ruling: AntdUI-кнопки используют AutoSizeMode.Width без MinimumWidth; библиотечный GetPreferredSize не учитывает MinimumWidth и создавал наложение в RTL footer. Добавлена проверка пересечения соседних controls.
- Аппаратные операции и масштаб Windows 125/150% здесь не проверяются: нет подключённой ККТ, рабочий стол заблокирован. Изображения получаются DrawToBitmap из реальных controls с тестовыми транспортами.
- Astra final review: Critical нет; Important про клавиатуру и Minor про обновление списка касс ИНН исправлены. OperatorSegments получает Tab/Left/Right/Home/End и видимый фокус. RED ui-v29-keyboard-red.log, GREEN — WM_KEYDOWN в AntdWorkingCheck/LmEditorCheck, включая запрет изменения disabled режима.
- Наблюдавшееся замещение инструкции мастера статусом обновления обзора исправлено условием _mainOperationRunning; регрессия воспроизведена в ui-v29-message-red.log и проверена при возврате из обзора/журнала.
- Измерение карточки выполняется без ограничения ширины: native TableLayoutPanel с процентными колонками давал лишние 89–120 px пустой высоты при ограниченном GetPreferredSize. Измерение подтверждено диагностикой lm-layout-measure.log и снимками двух ИНН; дополнительные GrowAndShrink настройки оказались ненужными и удалены.
- Упаковка: 17 файлов, AntdUI только рядом с оператором; main/helper 11.3.29.0-test. Compact security contract, все внутренние SHA256 и отдельный запуск распакованного ZIP прошли. Архив 2 068 960 байт; SHA256 dbdbc26381e9736e0c66e8b5b1005e94405c576f2d962285aa0e74af697890e9.
- Готовый ZIP, внешние суммы и инструкция скопированы в D:/dev/Пиот 2 ККТ/artifacts/test. Копия ZIP совпадает по хешу. Публикации/push/merge нет; чужие изменения основной папки не тронуты.
- Ограничение проверки: clipboard занят внешним процессом, поэтому взаимодействие Copy Link пропущено; остальные проверки поддержки и маскирования выполнены.
