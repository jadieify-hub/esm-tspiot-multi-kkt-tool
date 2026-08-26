namespace EsmTspiot.Shared.Models
{
    public sealed class BulkRegistrationProgress
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string KktSerial { get; set; }
        public string Stage { get; set; }
        public string Message { get; set; }
        public ApiResponse Response { get; set; }
    }
}