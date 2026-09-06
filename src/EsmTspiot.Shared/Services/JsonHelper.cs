using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace EsmTspiot.Shared.Services
{
    public static class JsonHelper
    {
        public static T Deserialize<T>(string json)
        {
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
        }

        public static string Serialize<T>(T value)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
