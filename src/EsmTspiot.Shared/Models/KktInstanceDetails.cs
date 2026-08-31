namespace EsmTspiot.Shared.Models
{
    public sealed class KktInstanceDetails
    {
        public string State { get; set; }
        public string ClientPort { get; set; }
        public KktRegistrationData RegistrationData { get; set; }

        public bool IsRegistered
        {
            get
            {
                string state = (State ?? string.Empty).Trim();
                return string.Equals(
                        state,
                        "Зарегистрирован",
                        System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        state,
                        "Registered",
                        System.StringComparison.OrdinalIgnoreCase);
            }
        }

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
