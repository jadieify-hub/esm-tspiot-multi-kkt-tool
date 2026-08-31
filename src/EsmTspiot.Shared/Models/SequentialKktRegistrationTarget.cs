namespace EsmTspiot.Shared.Models
{
    public sealed class SequentialKktRegistrationTarget
    {
        public KktConnectionPort Port { get; set; }
        public KktConnectionIdentity Identity { get; set; }
        public DkktDeviceInfo Device { get; set; }
    }
}
