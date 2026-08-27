using System;
using System.IO;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class VerifiedControllerBinary
    {
        internal string FullPath { get; set; }
        internal string Version { get; set; }
        internal string Sha256 { get; set; }
        internal string SignerThumbprint { get; set; }
        internal PeMachine Machine { get; set; }
    }

    internal sealed class VerifiedControllerBinaryResult
    {
        internal bool IsSuccess { get; set; }
        internal VerifiedControllerBinary Binary { get; set; }
        internal string ErrorMessage { get; set; }
    }

    internal sealed class OfficialControllerLocator
    {
        private readonly ControllerCapabilityProfile _profile;
        private readonly IFileTrustVerifier _trustVerifier;
        private readonly IPathSafety _pathSafety;

        internal OfficialControllerLocator(
            ControllerCapabilityProfile profile,
            IFileTrustVerifier trustVerifier,
            IPathSafety pathSafety)
        {
            _profile = profile ?? throw new ArgumentNullException("profile");
            _trustVerifier = trustVerifier ?? throw new ArgumentNullException("trustVerifier");
            _pathSafety = pathSafety ?? throw new ArgumentNullException("pathSafety");
        }

        internal VerifiedControllerBinaryResult ResolveVerifiedBinary()
        {
            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(
                    _profile.InstallRoot,
                    _profile.ControllerRelativePath));
                if (!PathSafety.IsUnderRoot(candidate, _profile.InstallRoot))
                {
                    return Failure("Official controller path escapes the fixed installation root.");
                }
            }
            catch (Exception ex)
            {
                return Failure("Cannot derive official controller path: " + ex.GetType().Name + ".");
            }

            ValidationResult pathValidation = _pathSafety.ValidateProtected(
                candidate,
                _profile.InstallRoot,
                null);
            if (!pathValidation.IsValid)
            {
                return Failure(pathValidation.JoinMessages());
            }

            FileTrustResult trust = _trustVerifier.Verify(candidate, _profile.ControllerBinary);
            if (!trust.IsTrusted || trust.Observed == null)
            {
                return Failure(trust.ErrorMessage ?? "Official controller trust verification failed.");
            }

            return new VerifiedControllerBinaryResult
            {
                IsSuccess = true,
                Binary = new VerifiedControllerBinary
                {
                    FullPath = Path.GetFullPath(candidate),
                    Version = _profile.Version,
                    Sha256 = trust.Observed.Sha256,
                    SignerThumbprint = trust.Observed.SignerThumbprint,
                    Machine = trust.Observed.Machine
                }
            };
        }

        private static VerifiedControllerBinaryResult Failure(string message)
        {
            return new VerifiedControllerBinaryResult
            {
                IsSuccess = false,
                ErrorMessage = message
            };
        }
    }
}
