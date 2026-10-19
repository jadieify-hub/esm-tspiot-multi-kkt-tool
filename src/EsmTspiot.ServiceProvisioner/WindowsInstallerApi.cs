using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Services;
using Microsoft.Win32;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface IWindowsInstallerNative
    {
        int SetInternalUi(int uiLevel);
        // null выключает журнал.
        void EnableLog(string logPath);
        uint InstallProduct(string packagePath, string commandLine);
        uint ConfigureProduct(
            string productCode,
            int installLevel,
            int installState,
            string commandLine);
    }

    internal sealed class WindowsInstallerApi : IWindowsInstallerApi
    {
        private const int InstallLevelDefault = 0;
        private const int InstallStateAbsent = 2;
        private const int InstallStateDefault = 5;
        private const int InstallUiLevelNone = 2;

        // Windows Installer выполняет по одной операции на всю машину и держит
        // мьютекс ещё некоторое время после завершения предыдущей: пользовательские
        // действия пакета (регистрация служб, первый запуск) идут уже после того,
        // как msiexec вернул управление. Установка второго ЛМ подряд попадает
        // ровно в это окно и получает 1618. Ждём и повторяем.
        private const uint ErrorInstallAlreadyRunning = 1618;

        // Установщик Windows отвечает успехом двумя кодами помимо нуля:
        // 3010 — операция выполнена, часть файлов заменится при
        // перезагрузке; 1641 — выполнена, перезагрузка уже запущена. Оба
        // кода означают, что продукт установлен или снят: считать их
        // отказом нельзя, иначе успешная установка уходит в откат и
        // утилита сносит то, что только что поставила.
        private const uint ErrorSuccessRebootRequired = 3010;
        private const uint ErrorSuccessRebootInitiated = 1641;
        // Предел задан временем, а не числом попыток: сама попытка может длиться
        // десятки секунд, и «60 попыток» превращались в полчаса молчания.
        private const int InstallerBusyTimeoutMilliseconds = 180000;
        private const int InstallerBusyDelayMilliseconds = 5000;

        // MsiInstallProduct и MsiConfigureProductEx работают внутри нашего
        // процесса и не умеют ни отменяться, ни истекать. Если пакет вендора
        // встал на своём действии, помощник стоит вместе с ним неограниченно
        // долго и с нулевой загрузкой — снаружи это неотличимо от зависания
        // программы. Поэтому каждый вызов ограничен по времени: помощник
        // одноразовый, он завершится и отпустит оператора, а незавершённая
        // операция подхватится штатным повтором.
        // Час, а не 15 минут: на кассе с загруженным процессором базовый
        // ЛМ ставился дольше 15 минут и обрывался посреди установки.
        private const int InstallerCallTimeoutMilliseconds = 3600000;

        private readonly IWindowsInstallerNative _native;
        private readonly Action<int> _wait;
        private readonly Func<DateTime> _clock;
        private string _logDirectory;

        internal WindowsInstallerApi()
            : this(new WindowsInstallerNative(), null, null)
        {
            _logDirectory = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
                "KRS",
                "MultiKKT",
                "InstallerLogs");
        }

        // Каталог журнала установщика; null — журнал не ведётся.
        internal string LogDirectory
        {
            get { return _logDirectory; }
            set { _logDirectory = value; }
        }

        internal WindowsInstallerApi(IWindowsInstallerNative native)
            : this(native, null, null)
        {
        }

        internal WindowsInstallerApi(
            IWindowsInstallerNative native,
            Action<int> wait)
            : this(native, wait, null)
        {
        }

        internal WindowsInstallerApi(
            IWindowsInstallerNative native,
            Action<int> wait,
            Func<DateTime> clock)
        {
            if (native == null) throw new ArgumentNullException("native");
            _native = native;
            _wait = wait ?? DefaultWait;
            _clock = clock ?? DefaultClock;
        }

        private static DateTime DefaultClock()
        {
            return DateTime.UtcNow;
        }

        private static void DefaultWait(int milliseconds)
        {
            Thread.Sleep(milliseconds);
        }

        public uint Install(string packagePath, string hiddenProperties)
        {
            string path = Path.GetFullPath(packagePath);
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "Local-module MSI package was not found.",
                    path);
            // Код 1603 сам по себе ничего не объясняет, а доступа к кассе
            // клиента нет. Журнал установщика без подробного режима и без
            // дампа свойств: ADMINUSER/ADMINPASSWORD пакет и так скрывает
            // через MsiHiddenProperties.
            string logPath = CreateInstallLogPath();
            if (logPath != null) _native.EnableLog(logPath);
            try
            {
                return RunWithoutInstallerUi(
                    "install",
                    delegate
                    {
                        return _native.InstallProduct(
                            path,
                            hiddenProperties ?? string.Empty);
                    });
            }
            catch (WindowsInstallerOperationException error)
            {
                if (logPath == null ||
                    error.MsiErrorCode == ErrorInstallAlreadyRunning)
                    throw;
                string summary = SummarizeInstallLog(logPath);
                string hint = error.MsiErrorCode == 1603
                    ? DotNetHint(ReadDotNetRelease())
                    : string.Empty;
                throw new WindowsInstallerOperationException(
                    "install",
                    error.MsiErrorCode,
                    hint +
                    (summary.Length == 0 ? string.Empty : summary + " ") +
                    "Журнал установщика: " + logPath);
            }
            finally
            {
                if (logPath != null) _native.EnableLog(null);
            }
        }

        // Пакет ЛМ проверяет .NET Framework 4.8 (NDP\v4\Full, Release) и
        // при тихой установке без него отдаёт 1603.
        internal static string DotNetHint(int release)
        {
            return release >= 528040
                ? string.Empty
                : "Не установлен .NET Framework 4.8 — он нужен для ЛМ ЧЗ. " +
                    "Установите его, перезагрузите компьютер и повторите. ";
        }

        private static int ReadDotNetRelease()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    object value = key == null ? null : key.GetValue("Release");
                    return value is int ? (int)value : 0;
                }
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private string CreateInstallLogPath()
        {
            if (_logDirectory == null) return null;
            try
            {
                Directory.CreateDirectory(_logDirectory);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            return Path.Combine(
                _logDirectory,
                "install-" + _clock().ToString(
                    "yyyyMMdd-HHmmss-fff",
                    System.Globalization.CultureInfo.InvariantCulture) + ".log");
        }

        // Из журнала берутся только строки с номером ошибки установщика и
        // действием, вернувшим отказ: этого достаточно, чтобы понять
        // причину по журналу программы.
        internal static string SummarizeInstallLog(string logPath)
        {
            string[] lines;
            try
            {
                byte[] bytes = File.ReadAllBytes(logPath);
                Encoding encoding = bytes.Length >= 2 &&
                    bytes[0] == 0xFF && bytes[1] == 0xFE
                    ? Encoding.Unicode
                    : Encoding.Default;
                lines = encoding.GetString(bytes).Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
            }
            catch (IOException)
            {
                return string.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                return string.Empty;
            }
            List<string> found = new List<string>();
            // Сообщение сеанса helper ограничено 4096 символами: выдержка
            // не длиннее 4 строк по 250.
            for (int index = 0; index < lines.Length && found.Count < 4; index++)
            {
                string line = lines[index].Trim().TrimStart('\uFEFF');
                if (!IsInstallFailureLine(line)) continue;
                if (line.Length > 250) line = line.Substring(0, 250) + "...";
                if (!found.Contains(line)) found.Add(line);
            }
            return found.Count == 0
                ? string.Empty
                : SensitiveDataMasker.Mask(
                    "Установщик сообщил: " +
                    string.Join(" | ", found.ToArray()) + ".");
        }

        private static bool IsInstallFailureLine(string line)
        {
            if (line.IndexOf("Return value 3", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("значение 3", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            string[] markers = { "Error ", "Ошибка " };
            for (int index = 0; index < markers.Length; index++)
            {
                int at = line.IndexOf(markers[index], StringComparison.Ordinal);
                if (at < 0) continue;
                int digits = at + markers[index].Length;
                if (digits + 4 <= line.Length &&
                    char.IsDigit(line[digits]) && char.IsDigit(line[digits + 1]) &&
                    char.IsDigit(line[digits + 2]) && char.IsDigit(line[digits + 3]))
                    return true;
            }
            return false;
        }

        public uint Repair(string productCode)
        {
            string product = NormalizeProductCode(productCode);
            return RunWithoutInstallerUi(
                "repair",
                delegate
                {
                    return _native.ConfigureProduct(
                        product,
                        InstallLevelDefault,
                        InstallStateDefault,
                        "REINSTALL=ALL REINSTALLMODE=vomus");
                });
        }

        public uint Uninstall(string productCode)
        {
            string product = NormalizeProductCode(productCode);
            return RunWithoutInstallerUi(
                "uninstall",
                delegate
                {
                    return _native.ConfigureProduct(
                        product,
                        InstallLevelDefault,
                        InstallStateAbsent,
                        "REBOOT=ReallySuppress");
                });
        }

        private uint RunWithoutInstallerUi(
            string operation,
            Func<uint> nativeOperation)
        {
            int previous = _native.SetInternalUi(InstallUiLevelNone);
            try
            {
                DateTime deadline = _clock().AddMilliseconds(
                    InstallerBusyTimeoutMilliseconds);
                uint code = RunBounded(operation, nativeOperation);
                while (code == ErrorInstallAlreadyRunning &&
                    _clock() < deadline)
                {
                    _wait(InstallerBusyDelayMilliseconds);
                    code = RunBounded(operation, nativeOperation);
                }
                if (code == ErrorInstallAlreadyRunning)
                {
                    throw new WindowsInstallerOperationException(
                        operation,
                        code,
                        "Установщик Windows занят другой установкой дольше " +
                        (InstallerBusyTimeoutMilliseconds / 1000).ToString() +
                        " с. Дождитесь её окончания и повторите настройку.");
                }
                return Complete(operation, code);
            }
            finally
            {
                _native.SetInternalUi(previous);
            }
        }

        internal static string RedactInstallProperties(string properties)
        {
            if (string.IsNullOrWhiteSpace(properties)) return string.Empty;
            string[] values = properties.Split(
                new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            StringBuilder result = new StringBuilder(properties.Length);
            for (int index = 0; index < values.Length; index++)
            {
                if (index != 0) result.Append(' ');
                int equals = values[index].IndexOf('=');
                if (equals <= 0)
                    result.Append("<redacted>");
                else
                    result.Append(values[index].Substring(0, equals + 1))
                        .Append("<redacted>");
            }
            return result.ToString();
        }

        private uint RunBounded(string operation, Func<uint> nativeOperation)
        {
            uint code = 0;
            Exception failure = null;
            Thread worker = new Thread(delegate()
            {
                try { code = nativeOperation(); }
                catch (Exception error) { failure = error; }
            });
            worker.IsBackground = true;
            worker.Start();
            if (!worker.Join(InstallerCallTimeoutMilliseconds))
            {
                throw new TimeoutException(
                    "Установщик Windows не завершил операцию «" + operation +
                    "» за " +
                    (InstallerCallTimeoutMilliseconds / 60000).ToString() +
                    " мин. Операция осталась за установщиком; " +
                    "дождитесь её окончания и повторите шаг.");
            }
            if (failure != null) throw failure;
            return code;
        }

        private static uint Complete(string operation, uint code)
        {
            if (code == ErrorSuccessRebootRequired ||
                code == ErrorSuccessRebootInitiated)
            {
                ProvisionerStepTrace.Write(
                    "установщик Windows завершил операцию «" + operation +
                    "» успешно, но требует перезагрузки (код " +
                    code.ToString() + ")");
                return code;
            }
            if (code != 0)
                throw new WindowsInstallerOperationException(operation, code);
            return code;
        }

        private static string NormalizeProductCode(string productCode)
        {
            Guid value;
            if (!Guid.TryParseExact(productCode, "B", out value))
                throw new ArgumentException(
                    "MSI product code must be a braced GUID.",
                    "productCode");
            return value.ToString("B").ToUpperInvariant();
        }
    }

    internal sealed class WindowsInstallerNative : IWindowsInstallerNative
    {
        public int SetInternalUi(int uiLevel)
        {
            return MsiSetInternalUI(uiLevel, IntPtr.Zero);
        }

        // Ошибки, предупреждения, сведения, нехватка места и начало действий;
        // без подробного режима (0x1000) и дампа свойств (0x400).
        private const uint InstallLogMode = 0x1 | 0x2 | 0x4 | 0x10 | 0x80 | 0x100;

        public void EnableLog(string logPath)
        {
            MsiEnableLogW(logPath == null ? 0u : InstallLogMode, logPath, 0u);
        }

        public uint InstallProduct(string packagePath, string commandLine)
        {
            return MsiInstallProductW(packagePath, commandLine);
        }

        public uint ConfigureProduct(
            string productCode,
            int installLevel,
            int installState,
            string commandLine)
        {
            return MsiConfigureProductExW(
                productCode,
                installLevel,
                installState,
                commandLine);
        }

        [DllImport(
            "msi.dll",
            CharSet = CharSet.Unicode,
            ExactSpelling = true)]
        private static extern uint MsiInstallProductW(
            string packagePath,
            string commandLine);

        [DllImport(
            "msi.dll",
            CharSet = CharSet.Unicode,
            ExactSpelling = true)]
        private static extern uint MsiConfigureProductExW(
            string productCode,
            int installLevel,
            int installState,
            string commandLine);

        [DllImport(
            "msi.dll",
            CharSet = CharSet.Unicode,
            ExactSpelling = true)]
        private static extern uint MsiEnableLogW(
            uint logMode,
            string logFile,
            uint logAttributes);

        [DllImport(
            "msi.dll",
            CharSet = CharSet.Unicode,
            ExactSpelling = true)]
        private static extern int MsiSetInternalUI(
            int uiLevel,
            IntPtr windowHandle);

    }
}
