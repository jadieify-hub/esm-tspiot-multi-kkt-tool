using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace EsmTspiot.ServiceProvisioner
{
    internal interface IWindowsInstallerNative
    {
        int SetInternalUi(int uiLevel);
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
        private const int InstallerCallTimeoutMilliseconds = 900000;

        private readonly IWindowsInstallerNative _native;
        private readonly Action<int> _wait;
        private readonly Func<DateTime> _clock;

        internal WindowsInstallerApi()
            : this(new WindowsInstallerNative(), null, null)
        {
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
            return RunWithoutInstallerUi(
                "install",
                delegate
                {
                    return _native.InstallProduct(
                        path,
                        hiddenProperties ?? string.Empty);
                });
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
        private static extern int MsiSetInternalUI(
            int uiLevel,
            IntPtr windowHandle);

    }
}
