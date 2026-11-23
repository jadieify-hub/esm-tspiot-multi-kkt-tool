using System.Collections.Generic;

namespace EsmTspiot.Shared.Models
{
    public sealed class BulkRegistrationDiscovery
    {
        public BulkRegistrationDiscovery()
        {
            Items = new List<BulkRegistrationWorkItem>();
            InitialResults = new List<BulkKktRegistrationResult>();
            ObservedDevices = new List<DkktDeviceInfo>();
        }

        public BulkKktRegistrationPlan Plan { get; set; }
        public IList<BulkRegistrationWorkItem> Items { get; private set; }
        public IList<BulkKktRegistrationResult> InitialResults { get; private set; }
        public IList<DkktDeviceInfo> ObservedDevices { get; private set; }
        public string ErrorMessage { get; set; }

        public BulkKktRegistrationResult FindInspectionFailure(string serial)
        {
            foreach (BulkKktRegistrationResult result in InitialResults)
                if (result.Status == BulkKktRegistrationStatus.InspectionFailed && result.KktSerial == serial) return result;
            return null;
        }

        public void BlockRegistration(BulkKktRegistrationResult failure)
        {
            for (int i = Items.Count - 1; i >= 0; i--)
                if (Items[i].Item.Input.KktSerial == failure.KktSerial) Items.RemoveAt(i);
            for (int i = InitialResults.Count - 1; i >= 0; i--)
                if (InitialResults[i].KktSerial == failure.KktSerial) InitialResults.RemoveAt(i);
            InitialResults.Add(failure);
        }

        public bool IsValid
        {
            get { return string.IsNullOrWhiteSpace(ErrorMessage); }
        }
    }
}
