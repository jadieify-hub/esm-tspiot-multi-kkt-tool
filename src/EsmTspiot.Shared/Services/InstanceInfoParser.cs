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
    public static class InstanceInfoParser
    {
        public static IList<KktInstanceInfo> Parse(string json)
        {
            IList<KktInstanceInfo> instances;
            return TryParse(json, out instances) ? instances : new List<KktInstanceInfo>();
        }

        public static bool TryParse(string json, out IList<KktInstanceInfo> instances)
        {
            instances = new List<KktInstanceInfo>();
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
                        instances = new List<KktInstanceInfo>();
                        return false;
                    }

                    object idValue;
                    if (!TryGetValue(record, "id", out idValue) || string.IsNullOrWhiteSpace(ToText(idValue)))
                    {
                        instances = new List<KktInstanceInfo>();
                        return false;
                    }

                    instances.Add(new KktInstanceInfo
                    {
                        Id = ToText(idValue),
                        Port = ToText(GetValue(record, "port")),
                        SoftPort = ToText(GetValue(record, "softPort")),
                        DkktPort = ToText(GetValue(record, "dkktPort")),
                        ServiceState = ToText(GetValue(record, "serviceState"))
                    });
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
                        if (record.ValueKind != JsonValueKind.Object)
                        {
                            instances = new List<KktInstanceInfo>();
                            return false;
                        }

                        JsonElement idValue;
                        if (!TryGetProperty(record, "id", out idValue) || string.IsNullOrWhiteSpace(ToText(idValue)))
                        {
                            instances = new List<KktInstanceInfo>();
                            return false;
                        }

                        instances.Add(new KktInstanceInfo
                        {
                            Id = ToText(idValue),
                            Port = ToText(GetProperty(record, "port")),
                            SoftPort = ToText(GetProperty(record, "softPort")),
                            DkktPort = ToText(GetProperty(record, "dkktPort")),
                            ServiceState = ToText(GetProperty(record, "serviceState"))
                        });
                    }
                }
#endif
            }
            catch
            {
                instances = new List<KktInstanceInfo>();
                return false;
            }

            return true;
        }

        public static bool TryParse(ApiResponse response, out IList<KktInstanceInfo> instances)
        {
            instances = new List<KktInstanceInfo>();
            if (response == null || !response.IsSuccess)
            {
                return false;
            }

            if (response.StatusCode == 204)
            {
                return string.IsNullOrWhiteSpace(response.ResponseBody);
            }

            return TryParse(response.ResponseBody, out instances);
        }

        public static bool ContainsId(string json, string id)
        {
            IList<KktInstanceInfo> instances;
            if (!TryParse(json, out instances))
            {
                return false;
            }

            for (int i = 0; i < instances.Count; i++)
            {
                if (string.Equals(instances[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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
            if (dictionary == null)
            {
                return false;
            }

            object value;
            if ((TryGetValue(dictionary, "instances", out value) || TryGetValue(dictionary, "items", out value)) &&
                value is object[])
            {
                records = (object[])value;
                return true;
            }

            return false;
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
                (TryGetProperty(root, "instances", out records) || TryGetProperty(root, "items", out records)) &&
                records.ValueKind == JsonValueKind.Array)
            {
                return true;
            }

            records = default(JsonElement);
            return false;
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
