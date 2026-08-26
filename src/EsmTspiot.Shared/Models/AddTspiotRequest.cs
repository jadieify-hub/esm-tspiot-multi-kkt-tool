using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public sealed class AddTspiotRequest
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "port")]
        public int Port { get; set; }

        [DataMember(Name = "softPort")]
        public int SoftPort { get; set; }

        [DataMember(Name = "dkktPort")]
        public int DkktPort { get; set; }
    }
}
