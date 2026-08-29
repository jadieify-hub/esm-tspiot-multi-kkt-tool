using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public sealed class LmGatewayDraftSettingsStore
    {
        private const long MaximumFileBytes = 1024 * 1024;
        private readonly string _path;

        public LmGatewayDraftSettingsStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Draft settings path is required.", "path");
            }
            _path = Path.GetFullPath(path);
        }

        public static LmGatewayDraftSettingsStore CreateDefault()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EsmTspiotTool");
            return new LmGatewayDraftSettingsStore(Path.Combine(directory, "lm-gateway-drafts.json"));
        }

        public IList<LmGatewayDraft> LoadFor(IList<LmGatewayKkt> currentKkt)
        {
            List<LmGatewayDraft> result = new List<LmGatewayDraft>();
            if (!File.Exists(_path))
            {
                return result;
            }
            FileInfo file = new FileInfo(_path);
            if (file.Length < 0 || file.Length > MaximumFileBytes)
            {
                throw new InvalidDataException("Файл сохранённых параметров контроллеров имеет недопустимый размер.");
            }

            List<LmGatewayDraft> stored;
            using (FileStream stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                object value = new DataContractJsonSerializer(typeof(List<LmGatewayDraft>)).ReadObject(stream);
                stored = value as List<LmGatewayDraft>;
            }
            if (stored == null)
            {
                return result;
            }

            Dictionary<string, string> identities = new Dictionary<string, string>(StringComparer.Ordinal);
            if (currentKkt != null)
            {
                for (int index = 0; index < currentKkt.Count; index++)
                {
                    LmGatewayKkt kkt = currentKkt[index];
                    string serial = Trim(kkt == null ? null : kkt.KktSerial);
                    if (serial.Length > 0 && !identities.ContainsKey(serial))
                    {
                        identities.Add(serial, Trim(kkt.KktInn));
                    }
                }
            }

            HashSet<string> added = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < stored.Count; index++)
            {
                LmGatewayDraft draft = stored[index];
                string serial = Trim(draft == null ? null : draft.KktSerial);
                string expectedInn;
                if (draft == null || !identities.TryGetValue(serial, out expectedInn) ||
                    !string.Equals(Trim(draft.KktInn), expectedInn, StringComparison.Ordinal) ||
                    !added.Add(serial))
                {
                    continue;
                }
                result.Add(Copy(draft));
            }
            return result;
        }

        public void Save(IList<LmGatewayDraft> drafts)
        {
            string directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidDataException("Не задан каталог сохранения параметров контроллеров.");
            }
            Directory.CreateDirectory(directory);

            List<LmGatewayDraft> safe = new List<LmGatewayDraft>();
            HashSet<string> added = new HashSet<string>(StringComparer.Ordinal);
            if (drafts != null)
            {
                for (int index = 0; index < drafts.Count; index++)
                {
                    LmGatewayDraft draft = drafts[index];
                    string serial = Trim(draft == null ? null : draft.KktSerial);
                    if (draft == null || serial.Length == 0 || !added.Add(serial))
                    {
                        continue;
                    }
                    safe.Add(Copy(draft));
                }
            }

            string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(List<LmGatewayDraft>)).WriteObject(stream, safe);
                    stream.Flush(true);
                }
                if (File.Exists(_path))
                {
                    File.Replace(temporary, _path, null);
                }
                else
                {
                    File.Move(temporary, _path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static LmGatewayDraft Copy(LmGatewayDraft source)
        {
            return new LmGatewayDraft
            {
                KktSerial = Trim(source.KktSerial),
                KktInn = Trim(source.KktInn),
                TargetAddress = Trim(source.TargetAddress),
                TargetPort = Trim(source.TargetPort),
                GrpcPort = Trim(source.GrpcPort),
                RestPort = Trim(source.RestPort)
            };
        }

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
