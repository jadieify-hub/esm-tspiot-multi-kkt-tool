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
        // recoverFromAbortedOperation ставится только тогда, когда предыдущая
        // наша операция по этой ККТ оборвалась (манифест в RequiresAttention).
        // В этом случае запись «конфигурация применена» не является
        // доказательством того, что мы её действительно писали.
        EsmInstanceConfigApplyState ApplyAndRestart(
            DirectControllerManifest manifest,
            bool recoverFromAbortedOperation);
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
            DirectControllerManifest manifest,
            bool recoverFromAbortedOperation)
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
            DirectControllerManifest manifest,
            bool recoverFromAbortedOperation)
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
            if (!string.IsNullOrEmpty(manifest.EsmConfigAppliedSha256) &&
                !string.Equals(
                    currentHash,
                    manifest.EsmConfigAppliedSha256,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    currentHash,
                    manifest.EsmConfigOriginalSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!recoverFromAbortedOperation)
                {
                    throw new InvalidDataException(
                        "Конфигурация ЕСМ изменена после нашей последней операции; " +
                        "автоматическая перезапись запрещена. " +
                        "Снимите комплект этой ККТ кнопкой «Удалить всё созданное» и повторите настройку.");
                }
                // Прошлая наша операция оборвалась, а файл с тех пор сменился:
                // ЕСМ пересоздал конфигурацию при перерегистрации ККТ. Записи
                // о владении относятся к исчезнувшему файлу — перепривязываемся
                // к текущему, как к первому применению.
                manifest.EsmConfigOriginalSha256 = null;
                manifest.EsmConfigAppliedSha256 = null;
                TryDeleteBackup(
                    _manifests.GetEsmConfigBackupPath(manifest.KktSerial));
            }
            if (string.Equals(currentHash, appliedHash, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(manifest.EsmConfigAppliedSha256) &&
                    !string.Equals(
                        currentHash,
                        manifest.EsmConfigAppliedSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Хэш применённой конфигурации ЕСМ не совпадает с манифестом.");
                }
                return EsmInstanceConfigApplyState.AlreadyApplied;
            }

            string backupPath = _manifests.GetEsmConfigBackupPath(manifest.KktSerial);
            if (File.Exists(backupPath))
            {
                byte[] existingBackup = ReadBounded(backupPath);
                if (!string.Equals(
                        Sha256(existingBackup),
                        manifest.EsmConfigOriginalSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Защищённая резервная копия конфигурации ЕСМ изменилась.");
                }
            }
            else
            {
                _writer.WriteBytes(backupPath, original);
                _manifests.ProtectOwnedFile(backupPath);
            }
            manifest.EsmConfigOriginalSha256 = currentHash;
            manifest.EsmConfigAppliedSha256 = appliedHash;
            manifest.UpdatedUtc = DateTime.UtcNow.ToString("o");
            _manifests.Write(manifest);

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
                manifest.EsmConfigOriginalSha256 = null;
                manifest.EsmConfigAppliedSha256 = null;
                manifest.UpdatedUtc = DateTime.UtcNow.ToString("o");
                _manifests.Write(manifest);
                TryDeleteBackup(backupPath);
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
            if (!string.Equals(
                    Sha256(current),
                    manifest.EsmConfigAppliedSha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    Sha256(backup),
                    manifest.EsmConfigOriginalSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                Disown(manifest);
                return false;
            }
            string serviceName =
                EsmInstanceServiceIdentity.CreateName(manifest.KktSerial);
            bool wasRunning = StopInstance(serviceName);
            try
            {
                _writer.WriteBytes(configPath, backup);
                StartInstance(serviceName, wasRunning);
            }
            catch
            {
                _writer.WriteBytes(configPath, current);
                TryStartInstance(serviceName, wasRunning);
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
