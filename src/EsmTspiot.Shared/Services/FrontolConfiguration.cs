using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public sealed class FrontolSettings
    {
        public string Database { get; set; }
        public string User { get; set; }
        public int Workplace { get; set; }
    }

    public sealed class FrontolDevice
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        public int Code { get; set; }
        public int ChangeStamp { get; set; }
        public string Name { get; set; }
        public string ComPort { get; set; }
        public string Host { get; set; }
        public int? Port { get; set; }
    }

    public sealed class FrontolChange
    {
        public FrontolDevice Device { get; set; }
        public string ComPort { get; set; }
        public string KktSerial { get; set; }
        public string OldHost { get; set; }
        public int? OldPort { get; set; }
        public string NewHost { get; set; }
        public int NewPort { get; set; }
    }

    public static class FrontolConfiguration
    {
        public static string DefaultIniPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"ATOL\Frontol6\Settings\Frontol.ini"); }
        }

        public static FrontolSettings LoadSettings(string path)
        {
            if (!File.Exists(path))
                throw new InvalidOperationException("Не найден Frontol.ini кассового приложения.");
            string directory = ReadIni(path, "DATABASE", "Path");
            string file = ReadIni(path, "DATABASE", "DB");
            string user = ReadIni(path, "DATABASE", "User");
            int workplace;
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(file) ||
                file.IndexOfAny(new[] { '\\', '/', ':' }) >= 0 ||
                !int.TryParse(ReadIni(path, "POS", "NPOS"), NumberStyles.None, CultureInfo.InvariantCulture, out workplace) ||
                workplace < 1)
                throw new InvalidOperationException("В Frontol.ini должны быть DATABASE.Path, DATABASE.DB и корректный POS.NPOS. Базы FrontolAdmin.ini не используются.");
            if (!string.Equals(user, "SYSDBA", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Автонастройка Frontol рассчитана на стандартное подключение SYSDBA.");
            return new FrontolSettings {
                Database = directory.TrimEnd('\\', '/') + "\\" + file,
                User = user,
                Workplace = workplace
            };
        }

        public static string ReadComPort(string json)
        {
            try
            {
                FrontolDriverSettings settings = JsonHelper.Deserialize<FrontolDriverSettings>(json);
                if (settings == null || settings.Port != 0)
                    throw new InvalidOperationException("Автонастройка Frontol поддерживает ККТ с COM-подключением АТОЛ.");
                return NormalizeComPort(settings.ComFile);
            }
            catch (System.Runtime.Serialization.SerializationException)
            {
                throw new InvalidOperationException("Не удалось прочитать COM-порт из свойств ККТ Frontol.");
            }
        }

        public static string NormalizeComPort(string value)
        {
            string number = Regex.Replace((value ?? string.Empty).Trim(), "^COM", "", RegexOptions.IgnoreCase);
            int port;
            if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535)
                throw new InvalidOperationException("В свойствах ККТ не указан корректный COM-порт.");
            return "COM" + port.ToString(CultureInfo.InvariantCulture);
        }

        public static List<FrontolChange> BuildPlan(
            IList<FrontolDevice> devices,
            IList<KktConnectionIdentity> identities,
            IList<LmGatewayKkt> instances,
            string baseUrl)
        {
            Uri endpoint;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out endpoint) ||
                (endpoint.Scheme != "http" && endpoint.Scheme != "https") ||
                endpoint.UserInfo.Length != 0 || endpoint.Host.Length > 256)
                throw new InvalidOperationException("Некорректный адрес ЕСМ для Frontol.");
            if (devices == null || devices.Count == 0)
                throw new InvalidOperationException("На текущем рабочем месте Frontol не найдены ККТ.");
            if (identities == null || instances == null)
                throw new InvalidOperationException("Нет проверенной карты COM-портов и зарегистрированных ККТ.");
            var result = new List<FrontolChange>();
            var ports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<int>();
            foreach (FrontolDevice device in devices)
            {
                string port = NormalizeComPort(device.ComPort);
                if (!ports.Add(port) || !ids.Add(device.Id))
                    throw new InvalidOperationException("В текущем РМ Frontol несколько ККТ соответствуют " + port + ". Настройки не изменены.");
                var matches = identities.Where(i => i != null &&
                    string.Equals(NormalizeComPort(i.PortName), port, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count != 1 || string.IsNullOrWhiteSpace(matches[0].KktSerial))
                    throw new InvalidOperationException("Для ККТ Frontol «" + device.Name + "» (" + port + ") нет однозначного физического соответствия.");
                string serial = matches[0].KktSerial.Trim();
                if (identities.Count(i => i != null && string.Equals((i.KktSerial ?? "").Trim(), serial, StringComparison.Ordinal)) != 1)
                    throw new InvalidOperationException("Серийный номер ККТ найден на нескольких COM-портах.");
                var registered = instances.Where(i => i != null &&
                    string.Equals((i.KktSerial ?? "").Trim(), serial, StringComparison.Ordinal) &&
                    string.Equals((i.InstanceId ?? "").Trim(), serial, StringComparison.Ordinal)).ToList();
                int softPort;
                if (registered.Count != 1 ||
                    !registered[0].RegistrationConfirmed ||
                    !int.TryParse(registered[0].SoftPort, NumberStyles.None, CultureInfo.InvariantCulture, out softPort) ||
                    softPort < 1 || softPort > 65535)
                    throw new InvalidOperationException("ЕСМ не подтвердил экземпляр и порт ПО для ККТ на " + port + ". Значение порта не подбирается автоматически.");
                result.Add(new FrontolChange {
                    Device = device, ComPort = port, KktSerial = serial,
                    OldHost = device.Host, OldPort = device.Port,
                    NewHost = endpoint.Host, NewPort = softPort
                });
            }
            if (result.GroupBy(c => c.NewPort).Any(g => g.Count() > 1))
                throw new InvalidOperationException("Нескольким ККТ назначен один порт ПО ЕСМ.");
            return result;
        }

        private static string ReadIni(string path, string section, string key)
        {
            var value = new StringBuilder(4096);
            uint length = GetPrivateProfileString(section, key, "", value, value.Capacity, Path.GetFullPath(path));
            if (length >= value.Capacity - 1)
                throw new InvalidOperationException("Слишком длинное значение в Frontol.ini.");
            return value.ToString().Trim();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetPrivateProfileString(string section, string key, string defaultValue, StringBuilder value, int size, string path);
    }

    public sealed class FrontolDriverSettings
    {
        public int? Port { get; set; }
        public string ComFile { get; set; }
    }

    public sealed class FrontolBackup
    {
        public string Database { get; set; }
        public int Workplace { get; set; }
        public List<FrontolChange> Changes { get; set; }
    }
}
