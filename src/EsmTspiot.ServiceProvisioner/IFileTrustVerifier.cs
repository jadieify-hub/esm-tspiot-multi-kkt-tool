namespace EsmTspiot.ServiceProvisioner
{
    internal interface IFileTrustVerifier
    {
        FileTrustResult Verify(string path, TrustedFileExpectation expectation);
    }

    internal sealed class FileTrustResult
    {
        private FileTrustResult()
        {
        }

        internal bool IsTrusted { get; private set; }
        internal string FullPath { get; private set; }
        internal TrustedFileExpectation Observed { get; private set; }
        internal string ErrorMessage { get; private set; }

        internal static FileTrustResult Trusted(string path, TrustedFileExpectation observed)
        {
            return new FileTrustResult
            {
                IsTrusted = true,
                FullPath = path,
                Observed = observed == null ? null : observed.Clone()
            };
        }

        internal static FileTrustResult Rejected(string errorMessage)
        {
            return new FileTrustResult
            {
                IsTrusted = false,
                ErrorMessage = errorMessage
            };
        }
    }
}
