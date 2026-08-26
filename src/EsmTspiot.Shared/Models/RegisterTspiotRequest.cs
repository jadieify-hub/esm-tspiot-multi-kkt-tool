using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public sealed class RegisterTspiotRequest
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "kktSerial")]
        public string KktSerial { get; set; }

        [DataMember(Name = "fnSerial")]
        public string FnSerial { get; set; }

        [DataMember(Name = "kktInn")]
        public string KktInn { get; set; }
    }
}
