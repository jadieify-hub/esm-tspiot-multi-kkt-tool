using System.Collections.Generic;

namespace EsmTspiot.Shared.Models
{
    public sealed class ValidationResult
    {
        private readonly List<string> _messages = new List<string>();
        private readonly List<string> _warnings = new List<string>();

        public bool IsValid
        {
            get { return _messages.Count == 0; }
        }

        public IList<string> Messages
        {
            get { return _messages; }
        }

        public IList<string> Warnings
        {
            get { return _warnings; }
        }

        public void Add(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                _messages.Add(message);
            }
        }

        public void AddWarning(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                _warnings.Add(message);
            }
        }

        public string JoinMessages()
        {
            return string.Join("\r\n", _messages.ToArray());
        }

        public string JoinWarnings()
        {
            return string.Join("\r\n", _warnings.ToArray());
        }
    }
}
