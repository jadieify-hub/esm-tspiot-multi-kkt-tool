using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EsmTspiot.Shared.Services
{
    /// <summary>
    /// ИНН, чей ЛМ стоит на другом компьютере («Сервер ЛМ ЧЗ»), и его
    /// «адрес:порт». Файл — строки «ИНН адрес:порт»; ИНН без строки
    /// обслуживает ЛМ этого компьютера.
    /// </summary>
    public sealed class RemoteLocalModuleStore
    {
        private const long MaximumFileBytes = 32 * 1024;
        private readonly string _path;

        public RemoteLocalModuleStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Settings file is required.", "path");
            _path = Path.GetFullPath(path);
        }

        public static RemoteLocalModuleStore CreateDefault()
        {
            return new RemoteLocalModuleStore(Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "EsmTspiotTool",
                "remote-local-modules.txt"));
        }

        public SortedDictionary<string, string> Load()
        {
            SortedDictionary<string, string> result =
                new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(_path)) return result;
            if (new FileInfo(_path).Length > MaximumFileBytes)
                throw new InvalidDataException(
                    "Файл адресов удалённых ЛМ имеет недопустимый размер.");
            string[] lines = File.ReadAllLines(_path, Encoding.UTF8);
            for (int index = 0; index < lines.Length; index++)
            {
                string[] parts = lines[index].Split(
                    new[] { ' ', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);
                string host;
                int port;
                if (parts.Length == 2 &&
                    LocalModuleMsiIdentity.IsInn(parts[0]) &&
                    LocalModuleTargetAddress.TryParseEndpoint(
                        parts[1], out host, out port))
                    result[parts[0]] = LocalModuleTargetAddress.Key(host, port);
            }
            return result;
        }

        public void Save(IDictionary<string, string> endpointsByInn)
        {
            StringBuilder text = new StringBuilder();
            SortedDictionary<string, string> sorted =
                new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (endpointsByInn != null)
                foreach (KeyValuePair<string, string> pair in endpointsByInn)
                    sorted[pair.Key] = pair.Value;
            foreach (KeyValuePair<string, string> pair in sorted)
            {
                string host;
                int port;
                if (!LocalModuleMsiIdentity.IsInn(pair.Key) ||
                    !LocalModuleTargetAddress.TryParseEndpoint(
                        pair.Value, out host, out port))
                    throw new ArgumentException(
                        "Адрес ЛМ для ИНН " + pair.Key + " недопустим.");
                text.Append(pair.Key).Append(' ')
                    .Append(LocalModuleTargetAddress.Key(host, port))
                    .Append("\r\n");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, text.ToString(), new UTF8Encoding(false));
        }
    }
}
