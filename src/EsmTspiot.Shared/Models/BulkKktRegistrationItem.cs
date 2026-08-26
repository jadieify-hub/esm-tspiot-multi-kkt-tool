namespace EsmTspiot.Shared.Models
{
    public sealed class BulkKktRegistrationItem
    {
        public DkktDeviceInfo Device { get; set; }
        public TspiotFormInput Input { get; set; }
        public ValidationResult Validation { get; set; }
    }
}
