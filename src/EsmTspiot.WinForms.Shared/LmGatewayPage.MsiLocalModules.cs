using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class FullAutomaticLocalSetupOutcome
    {
        internal FullAutomaticLocalSetupOutcome()
        {
            LocalModuleLines = new List<string>();
            EsmLines = new List<string>();
            IncompleteReason = string.Empty;
        }

        internal DirectControllerSetupOutcome Controllers { get; set; }
        internal bool LocalModulesDeferred { get; set; }
        internal int LocalModulesReady { get; set; }
        internal int LocalModulesFailed { get; set; }
        internal bool EsmReadbackSucceeded { get; set; }
        internal int EsmVerifiedCount { get; set; }
        internal int EsmLocalModulePendingCount { get; set; }
        internal int EsmAttentionCount { get; set; }
        internal bool Complete { get; set; }
        internal string IncompleteReason { get; set; }
        internal IList<string> LocalModuleLines { get; private set; }
        internal IList<string> EsmLines { get; private set; }

        internal string FormatSummary()
        {
            string controllers = Controllers == null
                ? "контроллеры не запускались"
                : Controllers.FormatSummary();
            StringBuilder text = new StringBuilder();
            text.Append(controllers + " ЛМ готовы: " +
                LocalModulesReady.ToString(CultureInfo.InvariantCulture) +
                "; ЛМ отложены/с ошибкой: " +
                LocalModulesFailed.ToString(CultureInfo.InvariantCulture) +
                ". ЕСМ подтвердил привязку: " +
                EsmVerifiedCount.ToString(CultureInfo.InvariantCulture) +
                "; ЛМ ждёт инициализации: " +
                EsmLocalModulePendingCount.ToString(CultureInfo.InvariantCulture) +
                "; требуется проверка: " +
                EsmAttentionCount.ToString(CultureInfo.InvariantCulture) + ".");
            if (!Complete && !string.IsNullOrEmpty(IncompleteReason))
                text.Append(" Контур не завершён: " + IncompleteReason + ".");
            for (int index = 0; index < LocalModuleLines.Count; index++)
                text.Append("\r\n" + LocalModuleLines[index]);
            for (int index = 0; index < EsmLines.Count; index++)
                text.Append("\r\n" + EsmLines[index]);
            return text.ToString();
        }
    }

    public sealed partial class LmGatewayPage
    {
        private bool HasOwnedComponentsForRemoval()
        {
            return (_localModuleMsiInventory != null &&
                    _localModuleMsiInventory.Items.Count > 0) ||
                HasDirectControllersForRemoval();
        }

        private async Task ConfirmAndRemoveEverythingAsync()
        {
            RefreshServiceInventory();
            IList<DirectControllerProvisioningItemRequest> controllers =
                new DirectControllerOperatorInventoryReader()
                    .ReadRemovalItems();
            IList<LocalModuleMsiProvisioningItemRequest> localModules =
                CreateMsiRemovalItems(_localModuleMsiInventory);
            if (controllers.Count == 0 && localModules.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Созданные программой контроллеры и ЛМ ЧЗ не найдены.",
                    "Удалить всё созданное",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            string confirmation =
                LocalModuleRemovalMessagePolicy.BuildRemoveEverythingConfirmation(
                controllers.Count,
                CountMsiClones(_localModuleMsiInventory),
                HasPreExistingMsiBase(_localModuleMsiInventory));
            if (MessageBox.Show(
                    this,
                    confirmation,
                    "Удалить всё созданное",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            await RunOperationAsync(
                async delegate(CancellationToken operationCancellation)
                {
                    if (localModules.Count > 0)
                    {
                        LmServiceProvisioningBatchRequest request =
                            CreateMsiMutationRequest(
                                LmServiceOperation.RemoveAllMsiLocalModules,
                                localModules);
                        LmServiceProvisioningBatchResult msi =
                            await _localModuleMsiProvisioner.RunAsync(
                                request,
                                operationCancellation).ConfigureAwait(true);
                        for (int index = 0;
                            index < msi.LocalModuleMsiItems.Count;
                            index++)
                        {
                            LocalModuleMsiProvisioningItemResult item =
                                msi.LocalModuleMsiItems[index];
                            Log("ИНН " + (item.Inn ?? string.Empty) +
                                ": удаление ЛМ " + item.Status.ToString() +
                                "; " + SensitiveDataMasker.Mask(
                                    item.Message) + "\r\n");
                        }
                    }
                    if (controllers.Count > 0)
                    {
                        LmServiceProvisioningBatchResult direct =
                            await _serviceProvisioner
                                .RemoveAllDirectControllersAsync(
                                    controllers,
                                    Guid.NewGuid().ToString("N"),
                                    operationCancellation).ConfigureAwait(true);
                        for (int index = 0; index < direct.Items.Count; index++)
                            Log(direct.Items[index].FormatLogLine() + "\r\n");
                    }
                    await RefreshCoreAsync(CancellationToken.None);
                    _statusLabel.Text =
                        "Удаление завершено; поставщицкий базовый ЛМ сохранён, если он существовал до автомата.";
                },
                "Удаление созданных ЛМ и контроллеров; подтвердите UAC...");
        }

        private static int CountMsiClones(
            LocalModuleMsiOperatorInventorySnapshot inventory)
        {
            int result = 0;
            for (int index = 0;
                inventory != null && index < inventory.Items.Count;
                index++)
                if (inventory.Items[index].CloneOrdinal > 0) result++;
            return result;
        }

        private static bool HasPreExistingMsiBase(
            LocalModuleMsiOperatorInventorySnapshot inventory)
        {
            for (int index = 0;
                inventory != null && index < inventory.Items.Count;
                index++)
            {
                LocalModuleMsiInventoryItem item = inventory.Items[index];
                if (item.CloneOrdinal == 0 && item.PreExisting) return true;
            }
            return false;
        }

        private static IList<LocalModuleMsiProvisioningItemRequest>
            CreateMsiRemovalItems(
                LocalModuleMsiOperatorInventorySnapshot inventory)
        {
            List<LocalModuleMsiProvisioningItemRequest> result =
                new List<LocalModuleMsiProvisioningItemRequest>();
            if (inventory == null) return result;
            for (int index = 0; index < inventory.Items.Count; index++)
            {
                LocalModuleMsiInventoryItem item = inventory.Items[index];
                result.Add(new LocalModuleMsiProvisioningItemRequest
                {
                    Inn = item.Inn,
                    CloneOrdinal = item.CloneOrdinal,
                    ApiPort = item.ApiPort,
                    DatabasePort = item.DatabasePort,
                    InstallVolumeRoot =
                        LocalModuleInstallRootPolicy.GetVolumeRoot(
                            item.InstallRoot),
                    RemoteAddress = "LocalSubnet",
                    ExpectedManifestSha256 = item.ManifestSha256
                });
            }
            result.Sort(delegate(
                LocalModuleMsiProvisioningItemRequest left,
                LocalModuleMsiProvisioningItemRequest right)
            {
                return left.CloneOrdinal.CompareTo(right.CloneOrdinal);
            });
            return result;
        }

        private static LmServiceProvisioningBatchRequest
            CreateMsiMutationRequest(
                LmServiceOperation operation,
                IList<LocalModuleMsiProvisioningItemRequest> items)
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (identity == null || identity.User == null)
                throw new InvalidOperationException(
                    "Не удалось определить SID текущего пользователя.");
            LmServiceProvisioningBatchRequest request =
                new LmServiceProvisioningBatchRequest
                {
                    SchemaVersion = 3,
                    Operation = operation,
                    OperationId = Guid.NewGuid().ToString("N"),
                    InitiatingSid = identity.User.Value
                };
            for (int index = 0; index < items.Count; index++)
                request.LocalModuleMsiItems.Add(items[index]);
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            return request;
        }

        internal async Task<FullAutomaticLocalSetupOutcome>
            RunFullAutomaticLocalSetupFromHostAsync(
                IList<LmGatewayKkt> registeredKkts,
                CancellationToken cancellation)
        {
            if (registeredKkts == null || registeredKkts.Count == 0)
                throw new InvalidOperationException(
                    "В ЕСМ нет зарегистрированных ККТ для настройки.");

            // Выбор пакета живёт на вкладке «ЛМ ЧЗ», а полный прогон
            // запускают с другой вкладки: без этого вопроса ЛМ ЧЗ
            // молча пропускается, и это выясняется только в журнале.
            if (_localModuleInstallerSelection == null &&
                MessageBox.Show(
                    this,
                    "Официальный MSI ЛМ ЧЗ ещё не выбран, поэтому установка " +
                        "ЛМ ЧЗ будет пропущена." + Environment.NewLine +
                        "Выбрать файл сейчас?",
                    "Полная автоматическая настройка",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
            {
                SelectLocalModuleInstaller(this);
            }

            FullAutomaticLocalSetupOutcome outcome =
                new FullAutomaticLocalSetupOutcome();
            _lastAutomaticLocalModulesReady = 0;
            _lastAutomaticLocalModulesFailed = 0;
            _lastAutomaticLocalModuleLines.Clear();
            LocalModuleMsiOperatorInventorySnapshot inventory =
                new LocalModuleMsiOperatorInventoryReader().Read();
            LocalModuleMsiPlan plan = LocalModuleMsiPlanner.Build(
                registeredKkts,
                inventory.Assignments,
                inventory.BaseInventory,
                null,
                inventory.Listeners);
            if (!plan.IsValid)
                throw new InvalidOperationException(
                    "Не удалось построить план ЛМ ЧЗ: " +
                    string.Join("; ", plan.ValidationMessages));

            string requestedVolume = ResolveInstallVolume(plan, inventory);
            if (!string.IsNullOrEmpty(requestedVolume))
            {
                plan = LocalModuleMsiPlanner.Build(
                    registeredKkts,
                    inventory.Assignments,
                    inventory.BaseInventory,
                    requestedVolume,
                    inventory.Listeners);
                if (!plan.IsValid)
                    throw new InvalidOperationException(
                        "Системный диск не подходит для ЛМ ЧЗ: " +
                        string.Join("; ", plan.ValidationMessages));
            }

            DirectControllerSetupOutcome controllers = null;
            LmAutomaticSetupResult coordinated =
                await new LmAutomaticSetupCoordinator().ExecuteFullAsync(
                    delegate { return Task.FromResult(true); },
                    async delegate(CancellationToken operationCancellation)
                    {
                        controllers =
                            await EnsureDirectControllersFromHostAsync(
                                registeredKkts,
                                plan.CreateTargetApiPortMap(),
                                operationCancellation).ConfigureAwait(true);
                        outcome.Controllers = controllers;
                        return DirectControllerSetupPolicy.IsComplete(
                            registeredKkts.Count,
                            controllers.ReadyCount,
                            controllers.FailedCount,
                            controllers.Cancelled);
                    },
                    async delegate(CancellationToken operationCancellation)
                    {
                        if (_localModuleInstallerSelection == null)
                        {
                            outcome.LocalModulesDeferred = true;
                            outcome.LocalModulesFailed = Math.Max(
                                outcome.LocalModulesFailed,
                                plan.Assignments.Count);
                            Log("MSI ЛМ ЧЗ не выбран или установка отложена; " +
                                "регистрация и контроллеры не отменяются.\r\n");
                            return false;
                        }
                        return await EnsureMsiLocalModulesAsync(
                            plan,
                            inventory,
                            operationCancellation).ConfigureAwait(true);
                    },
                    async delegate(CancellationToken operationCancellation)
                    {
                        if (controllers == null) return false;
                        await BindReadyDirectControllersAsync(
                            registeredKkts,
                            controllers,
                            operationCancellation).ConfigureAwait(true);
                        FinalizeDirectControllerOutcome(controllers);
                        return controllers.Complete;
                    },
                    async delegate(CancellationToken operationCancellation)
                    {
                        return await ReadbackContourAsync(
                            registeredKkts,
                            controllers,
                            outcome,
                            operationCancellation).ConfigureAwait(true);
                    },
                    ReportFullAutomaticStage,
                    cancellation).ConfigureAwait(true);

            outcome.LocalModulesDeferred =
                outcome.LocalModulesDeferred || coordinated.LocalModuleDeferred;
            outcome.LocalModulesReady = _lastAutomaticLocalModulesReady;
            outcome.LocalModulesFailed = Math.Max(
                outcome.LocalModulesFailed,
                _lastAutomaticLocalModulesFailed);
            outcome.EsmReadbackSucceeded = coordinated.EsmReadbackSucceeded;
            outcome.Complete = coordinated.Complete;
            outcome.IncompleteReason = coordinated.IncompleteReason;
            for (int index = 0;
                index < _lastAutomaticLocalModuleLines.Count;
                index++)
                outcome.LocalModuleLines.Add(_lastAutomaticLocalModuleLines[index]);
            if (outcome.LocalModulesDeferred)
                outcome.LocalModuleLines.Add(
                    "ЛМ ЧЗ: установка отложена оператором или MSI не выбран; " +
                    "контур без ЛМ не считается завершённым.");
            Log("Инициализация ЛМ ЧЗ не блокирует автомат: она выполняется " +
                "ЛМ отдельно после запуска.\r\n");
            _statusLabel.Text = outcome.Complete
                ? "Контур настроен: контроллеры, ЛМ ЧЗ и привязка подтверждены " +
                    "ЕСМ; инициализация ЛМ выполняется отдельно."
                : "Настройка завершена частично: " + outcome.IncompleteReason +
                    ". Проверьте журнал.";
            Log("=== Итог полного автомата ===\r\n" +
                outcome.FormatSummary() + "\r\n");
            return outcome;
        }

        private string ResolveInstallVolume(
            LocalModuleMsiPlan plan,
            LocalModuleMsiOperatorInventorySnapshot inventory)
        {
            int newClones = 0;
            int newProducts = 0;
            for (int index = 0; index < plan.Assignments.Count; index++)
            {
                LocalModuleMsiAssignment assignment = plan.Assignments[index];
                if (FindInventoryItem(inventory, assignment.Inn) != null)
                    continue;
                newProducts++;
                if (assignment.CloneOrdinal > 0) newClones++;
            }
            if (newProducts == 0) return string.Empty;

            string systemVolume = LocalModuleInstallRootPolicy.GetSystemVolumeRoot();
            long free = new DriveInfo(systemVolume).AvailableFreeSpace;
            LocalModuleDiskSpaceProjection projection =
                LocalModuleDiskSpacePolicy.Evaluate(
                    newClones,
                    newProducts,
                    free,
                    free);
            long required = projection.InstallVolumeRequiredBytes +
                projection.SystemVolumeRequiredBytes;
            if (free < required)
            {
                throw new InvalidOperationException(
                    "На системном диске " + systemVolume +
                    " недостаточно места для ЛМ ЧЗ: нужно не менее " +
                    (required / 1048576L).ToString(CultureInfo.InvariantCulture) +
                    " МиБ, свободно " +
                    (free / 1048576L).ToString(CultureInfo.InvariantCulture) +
                    " МиБ.");
            }
            Log("Новые ЛМ ЧЗ будут установлены на системный диск " +
                systemVolume + ".\r\n");
            return systemVolume;
        }

        private async Task<bool> EnsureMsiLocalModulesAsync(
            LocalModuleMsiPlan plan,
            LocalModuleMsiOperatorInventorySnapshot inventory,
            CancellationToken cancellation)
        {
            if (MessageBox.Show(
                    this,
                    "Будет установлен один независимый ЛМ ЧЗ на каждый ИНН; " +
                        "новые ЛМ ставятся на системный диск, уже установленный " +
                        "базовый ЛМ остаётся в своём каталоге; " +
                        "службам ЛМ будет включён автозапуск (базовому ЛМ — тоже, " +
                        "с возвратом прежнего режима при удалении). " +
                        "Подтвердите право использовать пакет ЛМ и продолжить.",
                    "Установка ЛМ ЧЗ",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                Log("Установка ЛМ ЧЗ отложена оператором.\r\n");
                _lastAutomaticLocalModulesFailed = plan.Assignments.Count;
                return false;
            }

            LmServiceProvisioningBatchRequest request =
                CreateMsiEnsureRequest(plan, inventory);
            _statusLabel.Text =
                "Проверка MSI и установка независимых ЛМ; подтвердите UAC...";
            LmServiceProvisioningBatchResult result =
                await _localModuleMsiProvisioner.RunEnsureAsync(
                    request,
                    cancellation).ConfigureAwait(true);
            int succeeded = 0;
            int failed = 0;
            for (int index = 0;
                index < result.LocalModuleMsiItems.Count;
                index++)
            {
                LocalModuleMsiProvisioningItemResult item =
                    result.LocalModuleMsiItems[index];
                bool ready = item.Status ==
                    LmServiceProvisioningStatus.Succeeded;
                if (ready) succeeded++; else failed++;
                string line = DescribeLocalModuleResult(plan, item, ready);
                _lastAutomaticLocalModuleLines.Add(line);
                Log(line + "\r\n");
            }
            _lastAutomaticLocalModulesReady = succeeded;
            _lastAutomaticLocalModulesFailed = failed;
            return failed == 0 && succeeded == plan.Assignments.Count;
        }

        private LmServiceProvisioningBatchRequest CreateMsiEnsureRequest(
            LocalModuleMsiPlan plan,
            LocalModuleMsiOperatorInventorySnapshot inventory)
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (identity == null || identity.User == null)
                throw new InvalidOperationException(
                    "Не удалось определить SID текущего пользователя.");
            LmServiceProvisioningBatchRequest request =
                new LmServiceProvisioningBatchRequest
                {
                    SchemaVersion = 3,
                    Operation = LmServiceOperation.EnsureMsiLocalModules,
                    OperationId = Guid.NewGuid().ToString("N"),
                    InitiatingSid = identity.User.Value,
                    LocalModuleInstallerSelection =
                        CopyLocalModuleInstallerForCompleteSetup(
                            _localModuleInstallerSelection,
                            true)
                };
            for (int index = 0; index < plan.Assignments.Count; index++)
            {
                LocalModuleMsiAssignment assignment = plan.Assignments[index];
                LocalModuleMsiInventoryItem existing =
                    FindInventoryItem(inventory, assignment.Inn);
                request.LocalModuleMsiItems.Add(
                    new LocalModuleMsiProvisioningItemRequest
                    {
                        Inn = assignment.Inn,
                        CloneOrdinal = assignment.CloneOrdinal,
                        ApiPort = assignment.ApiPort,
                        DatabasePort = assignment.DatabasePort,
                        InstallVolumeRoot = assignment.InstallVolumeRoot,
                        RemoteAddress = "LocalSubnet",
                        ExpectedManifestSha256 = existing == null
                            ? string.Empty
                            : existing.ManifestSha256
                    });
            }
            request.PlanHash = CanonicalLmPlanHasher.Compute(request);
            return request;
        }

        private void ReportFullAutomaticStage(LmAutomaticSetupStage stage)
        {
            if (stage == LmAutomaticSetupStage.ControllerEnsure)
                _statusLabel.Text = "Создание независимых контроллеров...";
            else if (stage == LmAutomaticSetupStage.LocalModuleEnsure)
                _statusLabel.Text = "Установка независимых ЛМ ЧЗ...";
            else if (stage == LmAutomaticSetupStage.EsmBinding)
                _statusLabel.Text = "Привязка контроллеров к ЕСМ...";
            else if (stage == LmAutomaticSetupStage.EsmReadback)
                _statusLabel.Text = "Контрольное чтение настроек ЕСМ...";
            else if (stage == LmAutomaticSetupStage.InitializationDeferred)
                _statusLabel.Text = "Инициализация ЛМ выполняется отдельно.";
        }

        private static LocalModuleMsiInventoryItem FindInventoryItem(
            LocalModuleMsiOperatorInventorySnapshot inventory,
            string inn)
        {
            for (int index = 0; index < inventory.Items.Count; index++)
                if (string.Equals(inventory.Items[index].Inn,
                        inn, StringComparison.Ordinal))
                    return inventory.Items[index];
            return null;
        }

    }
}
