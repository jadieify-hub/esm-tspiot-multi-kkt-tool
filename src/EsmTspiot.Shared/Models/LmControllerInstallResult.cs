using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public sealed class LmControllerInstallResult
    {
        [DataMember(Order = 1)]
        public LmServiceProvisioningStatus Status { get; set; }

        [DataMember(Order = 2)]
        public string InstalledVersion { get; set; }

        [DataMember(Order = 3)]
        public string Message { get; set; }
    }
}
