using System;
using System.Globalization;
using System.Net;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    /// <summary>
    /// Адрес ЛМ, на который смотрит контроллер ККТ. Пустая строка — ЛМ на
    /// этом же компьютере под управлением программы. Непустая строка —
    /// готовый ЛМ по заданному адресу, в том числе 127.0.0.1. Допускаются
    /// только буквы, цифры, точка и дефис:
    /// значение попадает в YAML контроллера без экранирования.
    /// </summary>
    public static class LocalModuleTargetAddress
    {
        public const string Local = "127.0.0.1";

        // null означает, что сравнить адреса по имеющимся данным нельзя.
        public static bool? MatchesSavedTarget(LmGatewayTarget saved, string desiredEndpoint, int automaticPort)
        {
            if (saved == null) return null;
            string host;
            int port;
            if (!TryParseEndpoint(desiredEndpoint, out host, out port))
            {
                if (!string.IsNullOrEmpty(desiredEndpoint) || automaticPort == 0) return null;
                host = Local;
                port = automaticPort;
            }
            return saved.Port == port && string.Equals(
                Describe(saved.Address), host, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsRemote(string host)
        {
            // Историческое имя: признак явного адреса готового ЛМ, а не его физического размещения.
            return !string.IsNullOrEmpty(host);
        }

        public static string Describe(string host)
        {
            if (string.IsNullOrEmpty(host) || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return Local;
            IPAddress address;
            return IPAddress.TryParse(host, out address) && IPAddress.IsLoopback(address) ? Local : host;
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
            IPAddress address;
            if (IPAddress.TryParse(host, out address))
                return address.AddressFamily ==
                    System.Net.Sockets.AddressFamily.InterNetwork &&
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
            if (GetRemotePortError(value.Substring(colon + 1), out parsed) != null ||
                !IsValidRemoteHost(candidate))
                return false;
            host = Describe(candidate);
            port = parsed;
            return true;
        }

        public static string GetRemotePortError(string text, out int port)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port) ||
                port < 1024 || port > 65535)
                return "Введите число от 1024 до 65535.";
            if (DirectControllerIdentity.IsControllerPort(port))
                return "Порт зарезервирован для контроллера.";
            return null;
        }
    }
}
