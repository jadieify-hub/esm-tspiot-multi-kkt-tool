using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace EsmTspiot.Shared.Services
{
    public static class JsonHelper
    {
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
