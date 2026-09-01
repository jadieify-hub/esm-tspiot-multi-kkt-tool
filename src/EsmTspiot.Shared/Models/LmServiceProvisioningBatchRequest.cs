using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EsmTspiot.Shared.Models
{
    [DataContract]
    public sealed class LmServiceProvisioningBatchRequest
    {
        public LmServiceProvisioningBatchRequest()
        {
            Items = new List<LmServiceProvisioningItemRequest>();
            RemovalConfirmations = new List<LmRemovalConfirmation>();
            ManagedLocalModules = new List<ManagedLocalModuleProvisioningItemRequest>();
            DirectControllers = new List<DirectControllerProvisioningItemRequest>();
        }

        [DataMember(Order = 1)]
        public int SchemaVersion { get; set; }

        [DataMember(Order = 2)]
        public LmServiceOperation Operation { get; set; }

        [DataMember(Order = 3)]
        public string OperationId { get; set; }

        [DataMember(Order = 4)]
        public string InitiatingSid { get; set; }

        [DataMember(Order = 5)]
        public string PlanHash { get; set; }

        [DataMember(Order = 6)]
        public IList<LmServiceProvisioningItemRequest> Items { get; private set; }

        [DataMember(Order = 7, EmitDefaultValue = false)]
        public LmRemovalConfirmation RemovalConfirmation { get; set; }

        [DataMember(Order = 8, EmitDefaultValue = false)]
        public LmCleanupConfirmation CleanupConfirmation { get; set; }

        [DataMember(Order = 9, EmitDefaultValue = false)]
        public LmControllerInstallerSelection InstallerSelection { get; set; }

        [DataMember(Order = 10, EmitDefaultValue = false)]
        public IList<LmRemovalConfirmation> RemovalConfirmations { get; private set; }

        [DataMember(Order = 11, EmitDefaultValue = false)]
        public LocalModuleInstallerSelection LocalModuleInstallerSelection { get; set; }

        [DataMember(Order = 12, EmitDefaultValue = false)]
        public IList<ManagedLocalModuleProvisioningItemRequest> ManagedLocalModules { get; private set; }

        [DataMember(Order = 13, EmitDefaultValue = false)]
        public IList<DirectControllerProvisioningItemRequest> DirectControllers { get; private set; }
    }
}
