using System;
using System.Collections.Generic;
using EsmTspiot.Shared.Models;

#if NETFRAMEWORK
using System.Web.Script.Serialization;
#else
using System.Text.Json;
#endif

namespace EsmTspiot.Shared.Services
{
    public static class DkktListParser
    {
        public static IList<DkktDeviceInfo> Parse(string json)
        {
            IList<DkktDeviceInfo> devices;
            return TryParse(json, out devices) ? devices : new List<DkktDeviceInfo>();
        }

        public static bool TryParse(string json, out IList<DkktDeviceInfo> devices)
        {
            devices = new List<DkktDeviceInfo>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
#if NETFRAMEWORK
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                object root = serializer.DeserializeObject(json);
                object[] records;
                if (!TryGetRecords(root, out records))
                {
                    return false;
                }

                for (int i = 0; i < records.Length; i++)
                {
                    Dictionary<string, object> record = records[i] as Dictionary<string, object>;
                    if (record == null)
                    {
                        continue;
                    }

                    devices.Add(CreateDevice(record));
                }
#else
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement records;
                    if (!TryGetRecords(document.RootElement, out records))
                    {
                        return false;
                    }

                    foreach (JsonElement record in records.EnumerateArray())
                    {
                        if (record.ValueKind == JsonValueKind.Object)
                        {
                            devices.Add(CreateDevice(record));
                        }
                    }
                }
#endif
            }
            catch
            {
                devices = new List<DkktDeviceInfo>();
                return false;
            }

            return true;
        }

        public static bool TryParse(
            ApiResponse response,
            out IList<DkktDeviceInfo> devices)
        {
            devices = new List<DkktDeviceInfo>();
            if (response == null || !response.IsSuccess)
            {
                return false;
            }
            if (response.StatusCode == 204)
            {
                return string.IsNullOrWhiteSpace(response.ResponseBody);
            }

            return TryParse(response.ResponseBody, out devices);
        }

#if NETFRAMEWORK
        private static bool TryGetRecords(object root, out object[] records)
        {
            records = root as object[];
            if (records != null)
            {
                return true;
            }

            Dictionary<string, object> dictionary = root as Dictionary<string, object>;
            object value;
            if (dictionary != null && TryGetValue(dictionary, "kkt", out value) && value is object[])
            {
                records = (object[])value;
                return true;
            }

            return false;
        }

        private static DkktDeviceInfo CreateDevice(Dictionary<string, object> record)
        {
            return new DkktDeviceInfo
            {
                KktSerial = ToText(GetValue(record, "kktSerial")).Trim(),
                FnSerial = ToText(GetValue(record, "fnSerial")).Trim(),
                KktInn = ToText(GetValue(record, "kktInn")).Trim(),
                KktRnm = ToText(GetValue(record, "kktRnm")).Trim(),
                ModelName = ToText(GetValue(record, "modelName")).Trim(),
                DkktVersion = ToText(GetValue(record, "dkktVersion")).Trim(),
                Developer = ToText(GetValue(record, "developer")).Trim(),
                Manufacturer = ToText(GetValue(record, "manufacturer")).Trim(),
                ShiftState = ToText(GetValue(record, "shiftState")).Trim()
            };
        }

        private static bool TryGetValue(Dictionary<string, object> dictionary, string key, out object value)
        {
            foreach (KeyValuePair<string, object> pair in dictionary)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static object GetValue(Dictionary<string, object> dictionary, string key)
        {
            object value;
            return TryGetValue(dictionary, key, out value) ? value : null;
        }

        private static string ToText(object value)
        {
            return value == null ? string.Empty : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }
#else
        private static bool TryGetRecords(JsonElement root, out JsonElement records)
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                records = root;
                return true;
            }

            if (root.ValueKind == JsonValueKind.Object &&
                TryGetProperty(root, "kkt", out records) &&
                records.ValueKind == JsonValueKind.Array)
            {
                return true;
            }

            records = default(JsonElement);
            return false;
        }

        private static DkktDeviceInfo CreateDevice(JsonElement record)
        {
            return new DkktDeviceInfo
            {
                KktSerial = ToText(GetProperty(record, "kktSerial")).Trim(),
                FnSerial = ToText(GetProperty(record, "fnSerial")).Trim(),
                KktInn = ToText(GetProperty(record, "kktInn")).Trim(),
                KktRnm = ToText(GetProperty(record, "kktRnm")).Trim(),
                ModelName = ToText(GetProperty(record, "modelName")).Trim(),
                DkktVersion = ToText(GetProperty(record, "dkktVersion")).Trim(),
                Developer = ToText(GetProperty(record, "developer")).Trim(),
                Manufacturer = ToText(GetProperty(record, "manufacturer")).Trim(),
                ShiftState = ToText(GetProperty(record, "shiftState")).Trim()
            };
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default(JsonElement);
            return false;
        }

        private static JsonElement GetProperty(JsonElement element, string name)
        {
            JsonElement value;
            return TryGetProperty(element, name, out value) ? value : default(JsonElement);
        }

        private static string ToText(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Undefined || element.ValueKind == JsonValueKind.Null)
            {
                return string.Empty;
            }

            return element.ValueKind == JsonValueKind.String
                ? (element.GetString() ?? string.Empty)
                : element.ToString();
        }
#endif
    }
}
