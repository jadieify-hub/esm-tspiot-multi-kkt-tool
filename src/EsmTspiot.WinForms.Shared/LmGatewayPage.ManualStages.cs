using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    // Manual mode of the cash-register contour: the same idempotent stages the
    // automatic run executes, exposed one at a time. Every stage re-reads ESM
    // first, so repeating a stage after a partial failure is always safe.
    public sealed partial class LmGatewayPage
    {
        // Порядок шагов повторяет автомат: ЛМ ЧЗ поднимается раньше
        // контроллеров, иначе контроллер стартует без своего локального
        // модуля и ЕСМ отвергает привязку.
        private enum ManualContourStage
        {
            LocalModules = 1,
            Controllers = 2,
            Binding = 3,
            Readback = 4
        }

        private readonly Button _ensureControllersButton = new Button();
        private readonly Button _ensureLocalModulesButton = new Button();
        private readonly Button _bindAllEsmButton = new Button();
        private readonly Button _readbackEsmButton = new Button();
        private readonly List<string> _lastAutomaticLocalModuleLines =
            new List<string>();

        private Control BuildManualStagePanel()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true,
                Margin = new Padding(0, 2, 0, 0)
            };
            Label caption = new Label
            {
                Text = "Ручной режим — те же шаги по отдельности; повтор безопасен:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 8, 4)
            };
            ConfigureButton(_ensureLocalModulesButton, "Шаг 1: ЛМ ЧЗ");
            _ensureLocalModulesButton.Tag = "EnsureLocalModules";
            _ensureLocalModulesButton.Click += async delegate
            {
                await RunManualStageAsync(ManualContourStage.LocalModules);
            };
            ConfigureButton(_ensureControllersButton, "Шаг 2: контроллеры");
            _ensureControllersButton.Tag = "EnsureControllers";
            _ensureControllersButton.Click += async delegate
            {
                await RunManualStageAsync(ManualContourStage.Controllers);
            };
            ConfigureButton(_bindAllEsmButton, "Шаг 3: привязка к ЕСМ");
            _bindAllEsmButton.Tag = "BindReadyEsm";
            _bindAllEsmButton.Click += async delegate
            {
                await RunManualStageAsync(ManualContourStage.Binding);
            };
            ConfigureButton(_readbackEsmButton, "Шаг 4: проверить по ЕСМ");
            _readbackEsmButton.Tag = "ReadbackEsm";
            _readbackEsmButton.Click += async delegate
            {
                await RunManualStageAsync(ManualContourStage.Readback);
            };
            panel.Controls.Add(caption);
            panel.Controls.Add(_ensureLocalModulesButton);
            panel.Controls.Add(_ensureControllersButton);
            panel.Controls.Add(_bindAllEsmButton);
            panel.Controls.Add(_readbackEsmButton);
            return panel;
        }

        private void UpdateManualStageActionState(bool idle, bool hasKkts)
        {
            bool mutation = idle && _helperAvailable && hasKkts;
            _ensureControllersButton.Enabled = mutation;
            _ensureLocalModulesButton.Enabled = mutation;
            _bindAllEsmButton.Enabled = idle && hasKkts;
            _readbackEsmButton.Enabled = idle && hasKkts;
            string unavailable = !_helperAvailable
                ? _helperUnavailableReason
                : !hasKkts
                    ? "В таблице нет зарегистрированных ККТ."
                    : null;
            _serviceToolTip.SetToolTip(
                _ensureControllersButton,
                unavailable ?? "Создать или проверить независимый контроллер " +
                    "для каждой ККТ; подтверждение UAC.");
            _serviceToolTip.SetToolTip(
                _ensureLocalModulesButton,
                unavailable ?? "Установить или проверить ЛМ ЧЗ для каждого ИНН " +
                    "из выбранного MSI; автозапуск включается; подтверждение UAC.");
            _serviceToolTip.SetToolTip(
                _bindAllEsmButton,
                hasKkts
                    ? "Передать ЕСМ адрес уже созданного контроллера каждой ККТ; " +
                        "контроллеры не пересоздаются, UAC не требуется."
                    : "В таблице нет зарегистрированных ККТ.");
            _serviceToolTip.SetToolTip(
                _readbackEsmButton,
                hasKkts
                    ? "Прочитать /api/v2/info каждой ККТ и сверить с планом " +
                        "контура; без UAC."
                    : "В таблице нет зарегистрированных ККТ.");
        }

        private async Task RunManualStageAsync(ManualContourStage stage)
        {
            await RunOperationAsync(
                async delegate(CancellationToken cancellation)
                {
                    await RefreshCoreAsync(cancellation);
                    _session.SelectAll();
                    IList<LmGatewayKkt> kkts = CopySessionKkts();
                    if (kkts.Count == 0)
                        throw new InvalidOperationException(
                            "В ЕСМ нет зарегистрированных ККТ для этого шага.");
                    LocalModuleMsiOperatorInventorySnapshot inventory =
                        new LocalModuleMsiOperatorInventoryReader().Read();
                    LocalModuleMsiPlan plan = LocalModuleMsiPlanner.Build(
                        kkts,
                        inventory.Assignments,
                        inventory.BaseInventory,
                        null,
                        inventory.Listeners);
                    Log("=== Ручной шаг " +
                        ((int)stage).ToString(CultureInfo.InvariantCulture) +
                        ": " + DescribeManualStage(stage) + " ===\r\n");
                    if (stage == ManualContourStage.Controllers)
                        await EnsureControllersManuallyAsync(
                            kkts, plan, cancellation).ConfigureAwait(true);
                    else if (stage == ManualContourStage.LocalModules)
                        await EnsureLocalModulesManuallyAsync(
                            kkts, plan, inventory, cancellation).ConfigureAwait(true);
                    else if (stage == ManualContourStage.Binding)
                        await BindControllersManuallyAsync(
                            kkts, plan, cancellation).ConfigureAwait(true);
                    else
                        await ReadbackManuallyAsync(
                            kkts, plan, cancellation).ConfigureAwait(true);
                },
                "Ручной режим: " + DescribeManualStage(stage) + "...");
        }

        private static string DescribeManualStage(ManualContourStage stage)
        {
            if (stage == ManualContourStage.Controllers)
                return "проверка и создание контроллеров";
            if (stage == ManualContourStage.LocalModules)
                return "установка и проверка ЛМ ЧЗ";
            if (stage == ManualContourStage.Binding)
                return "привязка контроллеров к ЕСМ";
            return "контрольное чтение ЕСМ";
        }

        private async Task EnsureControllersManuallyAsync(
            IList<LmGatewayKkt> kkts,
            LocalModuleMsiPlan plan,
            CancellationToken cancellation)
        {
            RequireUsableLocalModulePlan(plan);
            DirectControllerSetupOutcome controllers =
                await EnsureDirectControllersFromHostAsync(
                    kkts,
                    plan.CreateTargetApiPortMap(),
                    cancellation).ConfigureAwait(true);
            FinalizeDirectControllerOutcome(controllers);
            await RefreshAfterManualStageAsync(_statusLabel.Text, cancellation)
                .ConfigureAwait(true);
        }

        private async Task EnsureLocalModulesManuallyAsync(
            IList<LmGatewayKkt> kkts,
            LocalModuleMsiPlan plan,
            LocalModuleMsiOperatorInventorySnapshot inventory,
            CancellationToken cancellation)
        {
            if (_localModuleInstallerSelection == null)
                throw new InvalidOperationException(
                    "Выберите официальный MSI ЛМ ЧЗ (кнопка «Выбрать MSI...»).");
            RequireUsableLocalModulePlan(plan);
            string volume = ResolveInstallVolume(plan, inventory);
            if (!string.IsNullOrEmpty(volume))
            {
                plan = LocalModuleMsiPlanner.Build(
                    kkts,
                    inventory.Assignments,
                    inventory.BaseInventory,
                    volume,
                    inventory.Listeners);
                RequireUsableLocalModulePlan(plan);
            }
            _lastAutomaticLocalModuleLines.Clear();
            bool ready = await EnsureMsiLocalModulesAsync(
                plan,
                inventory,
                cancellation).ConfigureAwait(true);
            string summary = ready
                ? "ЛМ ЧЗ установлены и проверены: " +
                    _lastAutomaticLocalModulesReady.ToString(
                        CultureInfo.InvariantCulture) +
                    "; автозапуск служб включён."
                : "ЛМ ЧЗ: готовы " +
                    _lastAutomaticLocalModulesReady.ToString(
                        CultureInfo.InvariantCulture) +
                    ", отложены или с ошибкой " +
                    _lastAutomaticLocalModulesFailed.ToString(
                        CultureInfo.InvariantCulture) +
                    "; см. журнал.";
            await RefreshAfterManualStageAsync(summary, cancellation)
                .ConfigureAwait(true);
        }

        // Привязка не пересоздаёт контроллеры: она берёт то, что уже стоит на
        // машине по данным инвентаризации, и передаёт ЕСМ их адреса. Иначе
        // каждая привязка тянула бы за собой полную стадию контроллеров — с UAC
        // и многоминутным ожиданием, хотя менять на диске нечего.
        private async Task BindControllersManuallyAsync(
            IList<LmGatewayKkt> kkts,
            LocalModuleMsiPlan plan,
            CancellationToken cancellation)
        {
            DirectControllerSetupOutcome controllers =
                BuildInventoryControllerOutcome(kkts, plan);
            await BindReadyDirectControllersAsync(
                kkts,
                controllers,
                cancellation).ConfigureAwait(true);
            FinalizeDirectControllerOutcome(controllers);
            await RefreshAfterManualStageAsync(_statusLabel.Text, cancellation)
                .ConfigureAwait(true);
        }

        private DirectControllerSetupOutcome BuildInventoryControllerOutcome(
            IList<LmGatewayKkt> kkts,
            LocalModuleMsiPlan plan)
        {
            DirectControllerOperatorInventoryReader reader =
                new DirectControllerOperatorInventoryReader();
            DirectControllerPlan controllerPlan = plan != null && plan.IsValid
                ? reader.BuildPlan(kkts, plan.CreateTargetApiPortMap())
                : reader.BuildPlan(kkts);
            for (int index = 0;
                index < controllerPlan.ValidationMessages.Count;
                index++)
                Log("План контроллеров: " + SensitiveDataMasker.Mask(
                    controllerPlan.ValidationMessages[index]) + "\r\n");
            DirectControllerSetupOutcome controllers =
                new DirectControllerSetupOutcome();
            controllers.ExpectedKktCount = kkts.Count;
            for (int index = 0; index < kkts.Count; index++)
            {
                LmGatewayKkt kkt = kkts[index];
                DirectControllerAssignment assignment = kkt == null
                    ? null
                    : controllerPlan.FindBySerial(kkt.KktSerial);
                // План выдаёт назначение и для ККТ без службы — это
                // намерение создать контроллер, а не факт его наличия.
                if (assignment == null ||
                    !controllerPlan.IsInstalled(kkt.KktSerial))
                {
                    controllers.FailedCount++;
                    controllers.Messages.Add(
                        "ККТ " + (kkt == null ? string.Empty : kkt.KktSerial) +
                        ": контроллер не найден, привязывать нечего; " +
                        "выполните «Шаг 2: контроллеры».");
                    continue;
                }
                controllers.ReadyAssignments[kkt.KktSerial] = assignment;
                controllers.ReadyCount++;
            }
            return controllers;
        }

        /// <summary>
        /// Ручной шаг 4 подтвердил контур целиком. Автомат сообщает об этом
        /// сам, а ручной путь оставался немым: оператор, собравший контур по
        /// шагам, доходил до конца без единого слова благодарности.
        /// </summary>
        internal event Action ContourConfirmed;

        private async Task ReadbackManuallyAsync(
            IList<LmGatewayKkt> kkts,
            LocalModuleMsiPlan plan,
            CancellationToken cancellation)
        {
            // Сверка идёт по тем же контроллерам, что и привязка: по стоящим
            // на машине службам, а не по плану. Иначе ЕСМ, хранящий адрес
            // ещё не созданного контроллера, «подтверждал» контур.
            DirectControllerSetupOutcome expected =
                BuildInventoryControllerOutcome(kkts, plan);
            for (int index = 0; index < expected.Messages.Count; index++)
                Log(expected.Messages[index] + "\r\n");
            FullAutomaticLocalSetupOutcome outcome =
                new FullAutomaticLocalSetupOutcome();
            bool confirmed = await ReadbackContourAsync(
                kkts,
                expected,
                outcome,
                cancellation).ConfigureAwait(true) && expected.FailedCount == 0;
            _statusLabel.Text = (confirmed
                ? "ЕСМ подтвердил контур: подтверждено "
                : "ЕСМ не подтвердил контур полностью: подтверждено ") +
                outcome.EsmVerifiedCount.ToString(CultureInfo.InvariantCulture) +
                "; ЛМ ждёт инициализации: " +
                outcome.EsmLocalModulePendingCount.ToString(
                    CultureInfo.InvariantCulture) +
                "; требуется проверка: " +
                outcome.EsmAttentionCount.ToString(CultureInfo.InvariantCulture) +
                (expected.FailedCount > 0
                    ? "; контроллер не создан: " +
                        expected.FailedCount.ToString(CultureInfo.InvariantCulture)
                    : string.Empty) +
                (confirmed ? "." : "; см. журнал.");
            if (confirmed && ContourConfirmed != null)
            {
                ContourConfirmed();
            }
        }

        private async Task RefreshAfterManualStageAsync(
            string summary,
            CancellationToken cancellation)
        {
            string result = summary ?? string.Empty;
            await RefreshCoreAsync(cancellation);
            _statusLabel.Text = result;
        }

        private static void RequireUsableLocalModulePlan(LocalModuleMsiPlan plan)
        {
            if (plan == null || !plan.IsValid)
                throw new InvalidOperationException(
                    "Не удалось построить план ЛМ ЧЗ: " + (plan == null
                        ? "план отсутствует"
                        : string.Join("; ", plan.ValidationMessages)));
        }

        // Reads /api/v2/info of every KKT and compares the LM binding ESM
        // reports with the controller endpoints the contour expects. Runs
        // without UAC; nothing is mutated.
        private async Task<bool> ReadbackContourAsync(
            IList<LmGatewayKkt> kkts,
            DirectControllerSetupOutcome controllers,
            FullAutomaticLocalSetupOutcome outcome,
            CancellationToken cancellation)
        {
            outcome.EsmLines.Clear();
            outcome.EsmVerifiedCount = 0;
            outcome.EsmLocalModulePendingCount = 0;
            outcome.EsmAttentionCount = 0;
            if (kkts == null || kkts.Count == 0 || controllers == null ||
                controllers.ReadyAssignments.Count == 0)
            {
                Log("Контрольное чтение ЕСМ пропущено: нет готовых " +
                    "контроллеров для сверки.\r\n");
                return false;
            }
            string baseUrl = ValidateAndReadBaseUrl();
            _statusLabel.Text = "Контрольное чтение настроек ЕСМ...";
            IList<LmGatewayReadbackObservation> readback =
                await _readbackWorkflow.ReadAllAsync(
                    baseUrl,
                    kkts,
                    delegate(LmGatewayKkt kkt)
                    {
                        DirectControllerAssignment assignment;
                        if (kkt == null || !controllers.ReadyAssignments.TryGetValue(
                                (kkt.KktSerial ?? string.Empty).Trim(),
                                out assignment))
                            return null;
                        return LmContourReadbackPolicy.ExpectedTarget(assignment);
                    },
                    delegate(int current, int total, string serial)
                    {
                        PostToUi(delegate
                        {
                            _statusLabel.Text = "Контрольное чтение ЕСМ " +
                                current.ToString(CultureInfo.InvariantCulture) +
                                "/" + total.ToString(CultureInfo.InvariantCulture) +
                                ": " + serial + "...";
                        });
                    },
                    cancellation).ConfigureAwait(true);
            bool acceptable = readback.Count == kkts.Count;
            for (int index = 0; index < readback.Count; index++)
            {
                LmGatewayReadbackObservation observation = readback[index];
                LmContourReadbackState state =
                    LmContourReadbackPolicy.Classify(observation);
                if (state == LmContourReadbackState.Verified)
                    outcome.EsmVerifiedCount++;
                else if (state == LmContourReadbackState.LocalModuleNotInitialized)
                    outcome.EsmLocalModulePendingCount++;
                else
                    outcome.EsmAttentionCount++;
                if (!LmContourReadbackPolicy.IsAcceptable(state))
                    acceptable = false;
                string line = SensitiveDataMasker.Mask(
                    LmContourReadbackPolicy.Describe(observation));
                outcome.EsmLines.Add(line);
                Log(line + "\r\n");
                // При расхождении показываем сам ответ ЕСМ: иначе строка
                // сверки ничем не проверяется.
                if (state != LmContourReadbackState.Verified &&
                    !string.IsNullOrWhiteSpace(observation.InfoResponseBody))
                    Log("    Ответ /api/v2/info: " +
                        observation.InfoResponseBody + Environment.NewLine);
            }
            return acceptable;
        }

        private static string DescribeLocalModuleResult(
            LocalModuleMsiPlan plan,
            LocalModuleMsiProvisioningItemResult item,
            bool ready)
        {
            string inn = item.Inn ?? string.Empty;
            LocalModuleMsiAssignment assignment = plan == null
                ? null
                : plan.FindByInn(inn);
            string role = assignment == null
                ? "ЛМ"
                : assignment.CloneOrdinal == 0
                    ? "базовый ЛМ"
                    : "клон ЛМ " + assignment.CloneOrdinal.ToString(
                        CultureInfo.InvariantCulture);
            int apiPort = assignment == null ? item.ApiPort : assignment.ApiPort;
            return "ИНН " + inn + ": " + role + ", API " +
                apiPort.ToString(CultureInfo.InvariantCulture) +
                (ready
                    ? " — готов, автозапуск служб включён; "
                    : " — " + item.Status.ToString() + "; ") +
                SensitiveDataMasker.Mask(item.Message ?? string.Empty);
        }
    }
}
