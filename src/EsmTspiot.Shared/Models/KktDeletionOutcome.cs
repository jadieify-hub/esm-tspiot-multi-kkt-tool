namespace EsmTspiot.Shared.Models
{
    public sealed class KktDeletionOutcome
    {
        public bool IsSuccess { get; set; }
        public bool IsBlocked { get; set; }
        public bool DeleteRequestAccepted { get; set; }
        public int VerificationAttempts { get; set; }
        public string Message { get; set; }
        public ApiResponse DeleteResponse { get; set; }
    }
}
