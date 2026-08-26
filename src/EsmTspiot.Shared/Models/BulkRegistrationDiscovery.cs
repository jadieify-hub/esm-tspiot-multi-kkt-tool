using System.Collections.Generic;

namespace EsmTspiot.Shared.Models
{
    public sealed class BulkRegistrationDiscovery
    {
        public BulkRegistrationDiscovery()
        {
            Items = new List<BulkRegistrationWorkItem>();
            InitialResults = new List<BulkKktRegistrationResult>();
        }

        public BulkKktRegistrationPlan Plan { get; set; }
        public IList<BulkRegistrationWorkItem> Items { get; private set; }
        public IList<BulkKktRegistrationResult> InitialResults { get; private set; }
        public string ErrorMessage { get; set; }

        public bool IsValid
        {
            get { return string.IsNullOrWhiteSpace(ErrorMessage); }
        }
    }
}