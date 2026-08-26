using System.Collections.Generic;

namespace EsmTspiot.Shared.Models
{
    public sealed class BulkKktRegistrationPlan
    {
        public BulkKktRegistrationPlan()
        {
            Items = new List<BulkKktRegistrationItem>();
            ExistingDevices = new List<DkktDeviceInfo>();
            DuplicateDevices = new List<DkktDeviceInfo>();
        }

        public IList<BulkKktRegistrationItem> Items { get; private set; }
        public IList<DkktDeviceInfo> ExistingDevices { get; private set; }
        public IList<DkktDeviceInfo> DuplicateDevices { get; private set; }
    }
}
