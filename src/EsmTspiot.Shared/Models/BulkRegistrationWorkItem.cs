namespace EsmTspiot.Shared.Models
{
    public sealed class BulkRegistrationWorkItem
    {
        public BulkKktRegistrationItem Item { get; set; }
        public bool RequiresAdd { get; set; }
    }
}