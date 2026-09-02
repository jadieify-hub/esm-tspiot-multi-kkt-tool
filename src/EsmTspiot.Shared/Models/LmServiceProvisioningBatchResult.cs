using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public sealed class LmServiceProvisioningBatchResult
    {
        public LmServiceProvisioningBatchResult()
        {
            Items = new List<LmServiceProvisioningItemResult>();
            LocalModuleMsiItems = new List<LocalModuleMsiProvisioningItemResult>();
        }

        [DataMember(Order = 1)]
        public int SchemaVersion { get; set; }

        [DataMember(Order = 2)]
        public string OperationId { get; set; }

        [DataMember(Order = 3)]
        public string PlanHash { get; set; }

        [DataMember(Order = 4)]
        public LmServiceProvisioningStatus Status { get; set; }

        [DataMember(Order = 5)]
        public IList<LmServiceProvisioningItemResult> Items { get; private set; }

        [DataMember(Order = 6, EmitDefaultValue = false)]
        public IList<LocalModuleMsiProvisioningItemResult> LocalModuleMsiItems
        { get; private set; }
    }
}
