namespace EsmTspiot.Shared.Models
{
    public sealed class KktInstanceDetails
    {
        public string ClientPort { get; set; }
        public KktRegistrationData RegistrationData { get; set; }

        public bool HasCompleteRegistrationData
        {
            get
            {
                return RegistrationData != null &&
                    !string.IsNullOrWhiteSpace(RegistrationData.KktSerial) &&
                    !string.IsNullOrWhiteSpace(RegistrationData.FnSerial) &&
                    !string.IsNullOrWhiteSpace(RegistrationData.KktInn);
            }
        }
    }
}