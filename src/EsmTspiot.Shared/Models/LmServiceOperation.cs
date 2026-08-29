using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public enum LmServiceOperation
    {
        [EnumMember]
        Unknown = 0,
        [EnumMember]
        InstallControllerVersion = 1,
        [EnumMember]
        EnsureBatch = 2,
        [EnumMember]
        RemoveManaged = 3,
        [EnumMember]
        CleanupManaged = 4,
        [EnumMember]
        RemoveAllManaged = 5
    }
}
