using System;
using System.IO;
using System.Text;
using System.Threading;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.ServiceProvisioner
{
    internal enum EsmInstanceConfigApplyState
    {
        Deferred = 1,
        AlreadyApplied = 2,
        Applied = 3
    }

    internal interface IEsmInstanceConfigManager
    {
        // Патч накладывается на текущее содержимое конфигурации ЕСМ, каким бы
        // оно ни было. ЕСМ переписывает свой файл сам при каждом перезапуске
        // экземпляра, поэтому «файл отличается от того, что мы записали» —
        // штатное состояние, а не признак чужого вмешательства.
        EsmInstanceConfigApplyState ApplyAndRestart(
            DirectControllerManifest manifest);
        bool RestoreAndRestart(DirectControllerManifest manifest);
    }

    internal sealed class DeferredEsmInstanceConfigManager :
        IEsmInstanceConfigManager
    {
        internal static readonly DeferredEsmInstanceConfigManager Instance =
            new DeferredEsmInstanceConfigManager();

        private DeferredEsmInstanceConfigManager()
        {
        }

        public EsmInstanceConfigApplyState ApplyAndRestart(
            DirectControllerManifest manifest)
        {
            return EsmInstanceConfigApplyState.Deferred;
        }

        public bool RestoreAndRestart(DirectControllerManifest manifest)
        {
            return false;
        }
    }

    internal sealed class EsmInstanceConfigManager : IEsmInstanceConfigManager
    {
        private const int FilePollAttempts = 20;
        private const int ServicePollAttempts = 120;
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly string _esmRoot;
        private readonly DirectControllerManifestStore _manifests;
        private readonly IWindowsServiceApi _services;
        private readonly AtomicFileWriter _writer;
        private readonly Action _delay;

        internal EsmInstanceConfigManager(
            string esmRoot,
            DirectControllerManifestStore manifests,
            IWindowsServiceApi services,
            AtomicFileWriter writer,
            Action delay)
        {
            if (manifests == null) throw new ArgumentNullException("manifests");
            if (services == null) throw new ArgumentNullException("services");
            if (writer == null) throw new ArgumentNullException("writer");
            if (delay == null) throw new ArgumentNullException("delay");
            _esmRoot = Path.GetFullPath(esmRoot);
            _manifests = manifests;
            _services = services;
            _writer = writer;
            _delay = delay;
        }

        public EsmInstanceConfigApplyState ApplyAndRestart(
            DirectControllerManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException("manifest");
            string configPath = GetConfigPath(manifest.KktSerial);
            if (!WaitForFile(configPath))
            {
                return EsmInstanceConfigApplyState.Deferred;
            }
            byte[] original = ReadBounded(configPath);
            string originalText = StrictUtf8.GetString(original);
            string patchedText = EsmInstanceControllerConfigPatcher.Patch(
                originalText,
                manifest.GrpcPort,
                manifest.RestPort);
            byte[] patched = StrictUtf8.GetBytes(patchedText);
            string currentHash = Sha256(original);
            string appliedHash = Sha256(patched);

            // Резервная копия снимается один раз — с того состояния, которое мы
            // застали первыми. Именно её возвращает «Удалить всё созданное»,
            // поэтому при повторных применениях она не переписывается.
            string backupPath = _manifests.GetEsmConfigBackupPath(manifest.KktSerial);
            bool createdBackup = false;
            if (File.Exists(backupPath))
            {
                string backupHash = Sha256(ReadBounded(backupPath));
                if (!string.IsNullOrEmpty(manifest.EsmConfigOriginalSha256) &&
                    !string.Equals(
                        backupHash,
                        manifest.EsmConfigOriginalSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Защищённая резервная копия конфигурации ЕСМ изменилась.");
                }
                manifest.EsmConfigOriginalSha256 = backupHash;
            }
            else
            {
                _writer.WriteBytes(backupPath, original);
                _manifests.ProtectOwnedFile(backupPath);
                createdBackup = true;
                manifest.EsmConfigOriginalSha256 = currentHash;
            }
            manifest.EsmConfigAppliedSha256 = appliedHash;
            manifest.UpdatedUtc = DateTime.UtcNow.ToString("o");
            _manifests.Write(manifest);
            if (string.Equals(currentHash, appliedHash, StringComparison.OrdinalIgnoreCase))
            {
                // Конфигурация уже наша, но прошлый заход мог оборваться между
                // остановкой службы и её запуском: при убитом процессе catch
                // ниже не отрабатывает, и экземпляр ЕСМ остаётся лежать — для
                // магазина это неработающая касса. Поэтому «уже применено»
                // всегда доводит службу до запущенного состояния.
                EnsureInstanceRunning(
                    EsmInstanceServiceIdentity.CreateName(manifest.KktSerial));
                return EsmInstanceConfigApplyState.AlreadyApplied;
            }

            string serviceName =
                EsmInstanceServiceIdentity.CreateName(manifest.KktSerial);
            bool wasRunning = false;
            bool applied = false;
            try
            {
                wasRunning = StopInstance(serviceName);
                _writer.WriteBytes(configPath, patched);
                applied = true;
                StartInstance(serviceName, wasRunning);
                return EsmInstanceConfigApplyState.Applied;
            }
            catch
            {
                if (applied)
                {
                    _writer.WriteBytes(configPath, original);
                }
                TryStartInstance(serviceName, wasRunning);
                if (createdBackup)
                {
                    // Резервную копию создали в этом же вызове — забираем её
                    // обратно вместе с признаком владения.
                    manifest.EsmConfigOriginalSha256 = null;
                    manifest.EsmConfigAppliedSha256 = null;
                    TryDeleteBackup(backupPath);
                }
                else
                {
                    // Копия снята прошлой операцией и остаётся точкой возврата;
                    // на диске сейчас лежит содержимое до патча.
                    manifest.EsmConfigAppliedSha256 = currentHash;
                }
                manifest.UpdatedUtc = DateTime.UtcNow.ToString("o");
                _manifests.Write(manifest);
                throw;
            }
        }

        public bool RestoreAndRestart(DirectControllerManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException("manifest");
            if (string.IsNullOrEmpty(manifest.EsmConfigAppliedSha256)) return false;
            string configPath = GetConfigPath(manifest.KktSerial);
            string backupPath = _manifests.GetEsmConfigBackupPath(manifest.KktSerial);
            if (!File.Exists(configPath) || !File.Exists(backupPath))
            {
                throw new InvalidDataException(
                    "Нельзя восстановить конфигурацию ЕСМ: файл или резервная копия отсутствует.");
            }
            byte[] current = ReadBounded(configPath);
            byte[] backup = ReadBounded(backupPath);
            string serviceName =
                EsmInstanceServiceIdentity.CreateName(manifest.KktSerial);
            if (!string.Equals(
                    Sha256(current),
                    manifest.EsmConfigAppliedSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    Sha256(backup),
                    manifest.EsmConfigOriginalSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                // Сюда же приходит прерванный откат: файл уже вернули, а
                // службу поднять не успели. Отказ от владения не повод
                // оставлять кассу с остановленным ЕСМ.
                Disown(manifest);
                EnsureInstanceRunning(serviceName);
                return false;
            }
            StopInstance(serviceName);
            try
            {
                _writer.WriteBytes(configPath, backup);
                // Служба поднимается всегда, а не «если работала до нас»: в
                // эту ветку заходят только с нашей записью в манифесте, то
                // есть остановлена она нами — либо сейчас, либо прошлым
                // заходом, который оборвался.
                StartInstance(serviceName, true);
            }
            catch
            {
                _writer.WriteBytes(configPath, current);
                TryStartInstance(serviceName, true);
                throw;
            }
            manifest.EsmConfigOriginalSha256 = null;
            manifest.EsmConfigAppliedSha256 = null;
            manifest.UpdatedUtc = DateTime.UtcNow.ToString("o");
            _manifests.Write(manifest);
            TryDeleteBackup(backupPath);
            return true;
        }

        // Снять с манифеста признак владения конфигурацией ЕСМ, не трогая
        // сам файл. Резервная копия остаётся на диске: она может понадобиться
        // оператору, а мы её больше не сторожим.
        private void Disown(DirectControllerManifest manifest)
        {
            manifest.EsmConfigOriginalSha256 = null;
            manifest.EsmConfigAppliedSha256 = null;
            manifest.UpdatedUtc = DateTime.UtcNow.ToString("o");
            _manifests.Write(manifest);
        }

        private string GetConfigPath(string serial)
        {
            if (serial == null || serial.Length != 14)
            {
                throw new InvalidDataException("Некорректный серийный номер ККТ.");
            }
            for (int index = 0; index < serial.Length; index++)
            {
                if (serial[index] < '0' || serial[index] > '9')
                {
                    throw new InvalidDataException("Некорректный серийный номер ККТ.");
                }
            }
            string path = Path.GetFullPath(
                Path.Combine(_esmRoot, "config_" + serial + ".yml"));
            if (!PathSafety.IsUnderRoot(path, _esmRoot) ||
                string.Equals(
                    Path.GetFileName(path),
                    "config-orchestrator.yml",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Некорректный путь конфигурации ЕСМ.");
            }
            return path;
        }

        private bool WaitForFile(string path)
        {
            for (int attempt = 0; attempt < FilePollAttempts; attempt++)
            {
                if (File.Exists(path)) return true;
                _delay();
            }
            return false;
        }

        private bool StopInstance(string serviceName)
        {
            ProvisionerStepTrace.Write("остановка экземпляра ЕСМ " + serviceName + " (до 30 с)");
            WindowsServiceRecord service = _services.Query(serviceName);
            if (service == null)
            {
                throw new InvalidDataException(
                    "Служба экземпляра ЕСМ " + serviceName + " отсутствует.");
            }
            bool wasRunning = service.State != WindowsServiceState.Stopped ||
                service.ProcessId != 0;
            if (!wasRunning) return false;
            _services.RequestStop(serviceName);
            for (int attempt = 0; attempt < ServicePollAttempts; attempt++)
            {
                service = _services.Query(serviceName);
                if (service != null && service.State == WindowsServiceState.Stopped &&
                    service.ProcessId == 0)
                {
                    return true;
                }
                _delay();
            }
            throw new TimeoutException(
                "Служба экземпляра ЕСМ не остановилась штатно.");
        }

        private void StartInstance(string serviceName, bool shouldRun)
        {
            if (!shouldRun) return;
            ProvisionerStepTrace.Write("запуск экземпляра ЕСМ " + serviceName + " (до 30 с)");
            _services.Start(serviceName);
            for (int attempt = 0; attempt < ServicePollAttempts; attempt++)
            {
                WindowsServiceRecord service = _services.Query(serviceName);
                if (service != null && service.State == WindowsServiceState.Running &&
                    service.ProcessId > 0)
                {
                    return;
                }
                _delay();
            }
            throw new TimeoutException(
                "Служба экземпляра ЕСМ не запустилась после изменения конфигурации.");
        }

        /// <summary>
        /// Поднимает экземпляр ЕСМ, если он не работает. Ошибку запуска не
        /// глотает: молчаливое «уже применено» поверх лежащей службы — ровно
        /// тот случай, когда программа объявляет успех при отказе.
        /// </summary>
        private void EnsureInstanceRunning(string serviceName)
        {
            WindowsServiceRecord service = _services.Query(serviceName);
            if (service == null) return;
            if (service.State == WindowsServiceState.Running &&
                service.ProcessId > 0)
            {
                return;
            }
            ProvisionerStepTrace.Write(
                "экземпляр ЕСМ " + serviceName +
                " остановлен при уже применённой конфигурации — запускаем");
            StartInstance(serviceName, true);
        }

        private void TryStartInstance(string serviceName, bool shouldRun)
        {
            try { StartInstance(serviceName, shouldRun); }
            catch { }
        }

        private static byte[] ReadBounded(string path)
        {
            FileInfo file = new FileInfo(path);
            if (file.Length <= 0 || file.Length > 1048576)
            {
                throw new InvalidDataException(
                    "Размер конфигурации ЕСМ находится вне разрешённого диапазона.");
            }
            return File.ReadAllBytes(path);
        }

        private static string Sha256(byte[] value)
        {
            using (System.Security.Cryptography.SHA256 hash =
                System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(value);
                StringBuilder result = new StringBuilder(bytes.Length * 2);
                for (int index = 0; index < bytes.Length; index++)
                {
                    result.Append(bytes[index].ToString("x2"));
                }
                return result.ToString();
            }
        }

        private static void TryDeleteBackup(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
            catch { }
        }
    }
}
