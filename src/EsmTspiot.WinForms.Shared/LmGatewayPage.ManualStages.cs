using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Button = AntdUI.Button;
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
            ConfigureButton(_readbackEsmButton, "Диагностика выбранной ККТ");
            _readbackEsmButton.Tag = "ReadbackEsm";
            _readbackEsmButton.Click += async delegate
            {
                await DiagnoseSelectedKktAsync();
            };

            panel.Controls.Add(_ensureLocalModulesButton);
            panel.Controls.Add(_ensureControllersButton);
            panel.Controls.Add(_bindAllEsmButton);
            return panel;
        }

        private void UpdateManualStageActionState(bool idle, bool hasKkts)
        {
            bool mutation = idle && _helperAvailable && hasKkts;
            _ensureControllersButton.Enabled = mutation && !FmuApiMode;
            _ensureLocalModulesButton.Enabled = mutation;
            _bindAllEsmButton.Enabled = idle && hasKkts && !FmuApiMode;

            string unavailable = !_helperAvailable
                ? _helperUnavailableReason
                : !hasKkts
                    ? "В таблице нет зарегистрированных ККТ."
                    : null;
            _serviceToolTip.SetToolTip(
                _ensureControllersButton,
                FmuApiMode ? FmuControllerNotApplicable :
                    unavailable ?? "Создать или проверить независимый контроллер " +
                        "для каждой ККТ; подтверждение UAC.");
            _serviceToolTip.SetToolTip(
                _ensureLocalModulesButton,
                unavailable ?? "Проверить или установить общий локальный ЛМ для каждого ИНН без готового адреса ЛМ.");
            _serviceToolTip.SetToolTip(
                _bindAllEsmButton,
                FmuApiMode ? FmuControllerNotApplicable : hasKkts
                    ? "Передать ЕСМ адрес уже созданного контроллера каждой ККТ; " +
                        "контроллеры не пересоздаются, UAC не требуется."
                    : "В таблице нет зарегистрированных ККТ.");
        }

        private async Task RunManualStageAsync(ManualContourStage stage)
        {
            if (FmuApiMode && stage != ManualContourStage.LocalModules)
            {
                _statusLabel.Text = FmuControllerNotApplicable;
                return;
            }
            await RunOperationAsync(
                async delegate(CancellationToken cancellation)
                {
                    await RefreshCoreAsync(cancellation);
                    IList<LmGatewayKkt> kkts = CopyRegisteredSessionKkts();
                    if (kkts.Count == 0)
                        throw new InvalidOperationException(
                            "В ЕСМ нет зарегистрированных ККТ для этого шага.");
                    IList<LmGatewayKkt> localLmKkts = SelectLocalLmKkts(kkts);
                    LocalModuleMsiOperatorInventorySnapshot inventory = localLmKkts.Count == 0
                        ? new LocalModuleMsiOperatorInventorySnapshot()
                        : CreateLocalModuleInventoryReader().Read();
                    LocalModuleMsiPlan plan = localLmKkts.Count == 0
                        ? new LocalModuleMsiPlan()
                        : LocalModuleMsiPlanner.Build(
                        localLmKkts,
                        inventory.Assignments,
                        inventory.BaseInventory,
                        null,
                        inventory.Listeners, ReservedExistingLocalApiPorts());
                    Log("=== Ручной шаг " +
                        ((int)stage).ToString(CultureInfo.InvariantCulture) +
                        ": " + DescribeManualStage(stage) + " ===\r\n");
                    if (stage == ManualContourStage.Controllers)
                        await EnsureControllersManuallyAsync(
                            kkts, plan, cancellation).ConfigureAwait(true);
                    else if (stage == ManualContourStage.LocalModules)
                        await EnsureLocalModulesManuallyAsync(
                            localLmKkts, plan, inventory, cancellation).ConfigureAwait(true);
                    else if (stage == ManualContourStage.Binding)
                        await BindControllersManuallyAsync(
                            kkts, plan, cancellation).ConfigureAwait(true);
                    else
                        await ReadbackManuallyAsync(
                            kkts, plan, inventory, cancellation).ConfigureAwait(true);
                },
                "Ручной режим: " + DescribeManualStage(stage) + "...");
        }

        private void ConfirmManualControllerScope(string title, IList<DirectControllerAssignment> targets)
        {
            if (targets.Count > 0 && MessageBox.Show(_serviceOperationsWindow,
                DescribeManualControllerScope(targets) + "\r\nПродолжить?", title,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                throw new OperationCanceledException();
        }

        private static string DescribeManualControllerScope(IList<DirectControllerAssignment> targets)
        {
            var inns = new HashSet<string>(StringComparer.Ordinal);
            var details = new StringBuilder();
            foreach (DirectControllerAssignment target in targets)
            {
                inns.Add(target.KktInn);
                details.Append("ККТ ").Append(target.KktSerial).Append(" · ИНН ").Append(target.KktInn)
                    .Append(" · ЛМ ").AppendLine(LocalModuleTargetAddress.Key(
                        target.TargetLocalModuleAddress, target.TargetLocalModulePort));
            }
            return "ККТ: " + targets.Count + "; ИНН: " + inns.Count + ".\r\n\r\n" + details;
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
                    CreateControllerTargetPorts(plan),
                    cancellation, confirmManualScope: true).ConfigureAwait(true);
            FinalizeDirectControllerOutcome(controllers);
            await RefreshAfterManualStageAsync(_statusLabel.Text, cancellation)
                .ConfigureAwait(true);
        }

        private async Task EnsureLocalModulesManuallyAsync(
            IList<LmGatewayKkt> localLmKkts,
            LocalModuleMsiPlan plan,
            LocalModuleMsiOperatorInventorySnapshot inventory,
            CancellationToken cancellation)
        {
            RequireUsableLocalModulePlan(plan);
            if (plan.Assignments.Count == 0)
            {
                await RefreshAfterManualStageAsync(
                    "Для всех ИНН указаны готовые ЛМ; установка пропущена.",
                    cancellation).ConfigureAwait(true);
                return;
            }
            string volume = ResolveInstallVolume(plan, inventory);
            if (!string.IsNullOrEmpty(volume))
            {
                plan = LocalModuleMsiPlanner.Build(
                    localLmKkts,
                    inventory.Assignments,
                    inventory.BaseInventory,
                    volume,
                    inventory.Listeners, ReservedExistingLocalApiPorts());
                RequireUsableLocalModulePlan(plan);
            }
            _lastAutomaticLocalModuleLines.Clear();
            bool ready = await EnsureMsiLocalModulesAsync(
                plan,
                inventory,
                localLmKkts,
                cancellation).ConfigureAwait(true);
            string summary = ready ? "Подготовка ЛМ завершена."
                : "Подготовка ЛМ не завершена. Результат по каждому ИНН указан в журнале.";
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
                cancellation, confirmManualScope: true).ConfigureAwait(true);
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
                ? reader.BuildPlan(
                    kkts,
                    CreateControllerTargetPorts(plan),
                    CreateControllerTargetAddresses())
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
            LocalModuleMsiOperatorInventorySnapshot inventory,
            CancellationToken cancellation)
        {
            // Сверка идёт по тем же контроллерам, что и привязка: по стоящим
            // на машине службам, а не по плану. Иначе ЕСМ, хранящий адрес
            // ещё не созданного контроллера, «подтверждал» контур.
            DirectControllerSetupOutcome expected =
                BuildInventoryControllerOutcome(kkts, plan);
            for (int index = 0; index < expected.Messages.Count; index++)
                Log(expected.Messages[index] + "\r\n");
            // Контроллер и привязка в ЕСМ переживают удаление ЛМ: ЕСМ хранит
            // адрес контроллера, а не факт установки модуля. Наличие
            // автоматически настроенного ЛМ проверяется по инвентарю; его инициализация и
            // бизнес-статус здесь не критерий.
            int modulesMissing = 0;
            IList<LmGatewayKkt> localLmKkts = SelectLocalLmKkts(kkts);
            for (int index = 0; index < localLmKkts.Count; index++)
            {
                LmGatewayKkt kkt = localLmKkts[index];
                if (kkt == null) continue;
                if (inventory.HasInstalledModule(plan.FindByInn(kkt.KktInn)))
                    continue;
                modulesMissing++;
                Log("ККТ " + kkt.KktSerial + ": ЛМ ЧЗ для ИНН " + kkt.KktInn +
                    " не установлен — сверять контур нечем; выполните " +
                    "«Шаг 1: ЛМ ЧЗ».\r\n");
            }
            FullAutomaticLocalSetupOutcome outcome =
                new FullAutomaticLocalSetupOutcome();
            bool confirmed = await ReadbackContourAsync(
                kkts,
                expected,
                outcome,
                cancellation).ConfigureAwait(true) &&
                expected.FailedCount == 0 && modulesMissing == 0;
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
                (modulesMissing > 0
                    ? "; ЛМ не установлен: " +
                        modulesMissing.ToString(CultureInfo.InvariantCulture)
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
            _session.ApplyReadback(readback);
            FillRows(null);
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

        private void RecordLocalModuleResult(LocalModuleMsiPlan plan, LocalModuleMsiProvisioningItemResult item)
        {
            _lastMsiItemResults[item.Inn] = item;
            string line = DescribeLocalModuleResult(plan, item);
            _lastAutomaticLocalModuleLines.Add(line);
            Log(line + Environment.NewLine);
        }

        private static string DescribeLocalModuleResult(
            LocalModuleMsiPlan plan,
            LocalModuleMsiProvisioningItemResult item)
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
            string message = SensitiveDataMasker.Mask(item.Message ?? string.Empty);
            string prefix = "ИНН " + inn + ": " + role + " · порт " +
                apiPort.ToString(CultureInfo.InvariantCulture) + ". ";
            switch (item.Status)
            {
                case LmServiceProvisioningStatus.Succeeded:
                    return prefix + message;
                case LmServiceProvisioningStatus.Cancelled:
                    return prefix + "Установка отложена." + (message.Length == 0 ? "" : " " + message);
                case LmServiceProvisioningStatus.Failed:
                    return prefix + "Ошибка установки. " + message;
                default:
                    return prefix + "Требуется проверка (" + item.Status + "). " + message;
            }
        }
    }
}
