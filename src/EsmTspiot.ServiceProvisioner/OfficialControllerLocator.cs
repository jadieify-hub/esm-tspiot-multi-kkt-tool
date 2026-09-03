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
        private readonly IInstalledControllerProductVerifier _installedProductVerifier;

        internal OfficialControllerLocator(
            ControllerCapabilityProfile profile,
            IFileTrustVerifier trustVerifier,
            IPathSafety pathSafety)
            : this(
                profile,
                trustVerifier,
                pathSafety,
                new WindowsInstalledControllerProductVerifier())
        {
        }

        internal OfficialControllerLocator(
            ControllerCapabilityProfile profile,
            IFileTrustVerifier trustVerifier,
            IPathSafety pathSafety,
            IInstalledControllerProductVerifier installedProductVerifier)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            if (trustVerifier == null) throw new ArgumentNullException("trustVerifier");
            if (pathSafety == null) throw new ArgumentNullException("pathSafety");
            if (installedProductVerifier == null)
                throw new ArgumentNullException("installedProductVerifier");
            _profile = profile;
            _trustVerifier = trustVerifier;
            _pathSafety = pathSafety;
            _installedProductVerifier = installedProductVerifier;
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

            InstalledControllerProductResult installedProduct =
                _installedProductVerifier.Verify(_profile);
            if (!installedProduct.Validation.IsValid)
            {
                return Failure(installedProduct.Validation.JoinMessages());
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
                    Version = installedProduct.DisplayVersion,
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
