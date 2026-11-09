using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace EsmTspiot.Shared.Services
{
    // Optional display metadata, separate from all operational settings.
    public sealed class KktOrganizationStore
    {
        private readonly string _path;
        private readonly Dictionary<string, Record> _records = new Dictionary<string, Record>(StringComparer.Ordinal);

        public KktOrganizationStore(string path)
        {
            _path = Path.GetFullPath(path);
            try
            {
                if (!File.Exists(_path) || new FileInfo(_path).Length > 1024 * 1024) return;
                using (var input = File.OpenRead(_path))
                {
                    var records = (List<Record>)new DataContractJsonSerializer(typeof(List<Record>)).ReadObject(input);
                    if (records != null)
                        foreach (var record in records)
                            if (record != null) Remember(record.Serial, record.Inn, record.Name);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SerializationException) { }
        }

        public static KktOrganizationStore CreateDefault()
        {
            return new KktOrganizationStore(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "EsmTspiotTool", "kkt-organizations.json"));
        }

        public bool Remember(string serial, string inn, string name)
        {
            serial = (serial ?? string.Empty).Trim();
            inn = (inn ?? string.Empty).Trim();
            name = (name ?? string.Empty).Trim();
            if (serial.Length == 0 || inn.Length == 0 || name.Length == 0) return false;
            Record old;
            string key = serial + "|" + inn;
            if (_records.TryGetValue(key, out old) && old.Name == name) return false;
            _records[key] = new Record { Serial = serial, Inn = inn, Name = name };
            return true;
        }

        public string GetName(string serial, string inn)
        {
            Record record;
            return _records.TryGetValue((serial ?? string.Empty).Trim() + "|" + (inn ?? string.Empty).Trim(), out record)
                ? record.Name : string.Empty;
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = File.Create(temporary))
                    new DataContractJsonSerializer(typeof(List<Record>)).WriteObject(output, new List<Record>(_records.Values));
                if (File.Exists(_path)) File.Replace(temporary, _path, null);
                else File.Move(temporary, _path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public static string DisplayName(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0) return "Не получено";
            string[] forms = { "Индивидуальный предприниматель", "Общество с ограниченной ответственностью" };
            string[] shortForms = { "ИП", "ООО" };
            for (int index = 0; index < forms.Length; index++)
                if (name.StartsWith(forms[index], StringComparison.OrdinalIgnoreCase) &&
                    (name.Length == forms[index].Length || char.IsWhiteSpace(name[forms[index].Length])))
                    return shortForms[index] + name.Substring(forms[index].Length);
            return name;
        }

        [DataContract]
        private sealed class Record
        {
            [DataMember] public string Serial;
            [DataMember] public string Inn;
            [DataMember] public string Name;
        }
    }
}
