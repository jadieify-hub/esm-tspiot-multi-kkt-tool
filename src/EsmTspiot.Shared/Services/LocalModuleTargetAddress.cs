using System;
using System.Globalization;
using System.Net;

namespace EsmTspiot.Shared.Services
{
    /// <summary>
    /// Адрес ЛМ, на который смотрит контроллер ККТ. Пустая строка — ЛМ на
    /// этом же компьютере (127.0.0.1); иначе — IPv4 или имя компьютера, где
    /// стоит «Сервер ЛМ ЧЗ». Допускаются только буквы, цифры, точка и дефис:
    /// значение попадает в YAML контроллера без экранирования.
    /// </summary>
    public static class LocalModuleTargetAddress
    {
        public const string Local = "127.0.0.1";

        public static bool IsRemote(string host)
        {
            return !string.IsNullOrEmpty(host);
        }

        public static string Describe(string host)
        {
            return IsRemote(host) ? host : Local;
        }

        public static string Key(string host, int port)
        {
            return Describe(host) + ":" +
                port.ToString(CultureInfo.InvariantCulture);
        }

        public static bool IsValidRemoteHost(string host)
        {
            if (string.IsNullOrEmpty(host) || host.Length > 253 ||
                host[0] == '-' || host[0] == '.' ||
                host[host.Length - 1] == '-' || host[host.Length - 1] == '.')
                return false;
            for (int index = 0; index < host.Length; index++)
            {
                char value = host[index];
                if (!(value >= 'a' && value <= 'z') &&
                    !(value >= 'A' && value <= 'Z') &&
                    !(value >= '0' && value <= '9') &&
                    value != '.' && value != '-')
                    return false;
            }
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                return false;
            IPAddress address;
            if (IPAddress.TryParse(host, out address))
                return address.AddressFamily ==
                    System.Net.Sockets.AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address) &&
                    !IPAddress.Any.Equals(address);
            return true;
        }

        /// <summary>
        /// Разбирает «адрес:порт», который оператор копирует из окна
        /// «Сервер ЛМ ЧЗ»; префикс http:// допускается и отбрасывается.
        /// </summary>
        public static bool TryParseEndpoint(
            string text,
            out string host,
            out int port)
        {
            host = string.Empty;
            port = 0;
            string value = (text ?? string.Empty).Trim();
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("http://".Length);
            value = value.TrimEnd('/');
            int colon = value.LastIndexOf(':');
            if (colon <= 0) return false;
            string candidate = value.Substring(0, colon);
            int parsed;
            if (!int.TryParse(
                    value.Substring(colon + 1),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out parsed) ||
                parsed < 1024 || parsed > 65535 ||
                DirectControllerIdentity.IsControllerPort(parsed) ||
                !IsValidRemoteHost(candidate))
                return false;
            host = candidate;
            port = parsed;
            return true;
        }
    }
}
