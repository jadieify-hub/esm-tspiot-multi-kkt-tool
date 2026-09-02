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
        RemoveAllManaged = 5,
        [EnumMember]
        EnsureManagedLocalModules = 6,
        [EnumMember]
        EnsureDirectControllers = 7,
        [EnumMember]
        RestartDirectController = 8,
        [EnumMember]
        RemoveDirectController = 9,
        [EnumMember]
        RemoveAllDirectControllers = 10,
        [EnumMember]
        EnsureMsiLocalModules = 11,
        [EnumMember]
        RestartMsiLocalModule = 12,
        [EnumMember]
        RemoveMsiLocalModule = 13,
        [EnumMember]
        RemoveAllMsiLocalModules = 14
    }
}
