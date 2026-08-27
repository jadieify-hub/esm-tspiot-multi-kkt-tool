using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public enum LmProvisioningControlKind
    {
        [EnumMember]
        CancelAfterCurrentItem = 1
    }
}
