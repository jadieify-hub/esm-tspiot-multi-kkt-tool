using System;
using System.Runtime.Serialization;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public sealed class ControllerResetServiceRequest
    {
        [DataMember(Order = 1)] public string ServiceName { get; set; }
        [DataMember(Order = 2)] public string ExpectedImagePath { get; set; }

        public static bool IsAdditionalController(string name)
        {
            int ordinal;
            string serial;
            return (DirectControllerIdentity.TryParseServiceName(name, out ordinal) && ordinal > 1) ||
                LmServiceIdentity.TryParseName(name, out serial);
        }
    }
}
