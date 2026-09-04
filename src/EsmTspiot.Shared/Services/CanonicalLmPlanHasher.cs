using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public static class CanonicalLmPlanHasher
    {
        public const string RemovalWarning =
            "ESM binding can continue to reference the removed local controller.";
        public const string LocalModuleLicenseNotice =
            "The operator confirms use of the selected official local-module package for the listed INN groups.";

        public static string Compute(LmServiceProvisioningBatchRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            StringBuilder canonical = new StringBuilder();
            Append(canonical, "schema", request.SchemaVersion.ToString(CultureInfo.InvariantCulture));
            Append(canonical, "operation", ((int)request.Operation).ToString(CultureInfo.InvariantCulture));

            if (request.Operation == LmServiceOperation.RemoveManaged)
            {
                AppendRemoval(canonical, request.RemovalConfirmation);
                Append(canonical, "warning", RemovalWarning);
            }
            else if (request.Operation == LmServiceOperation.CleanupManaged)
            {
                AppendCleanup(canonical, request.CleanupConfirmation);
            }
            else if (request.Operation == LmServiceOperation.RemoveAllManaged)
            {
                int count = request.RemovalConfirmations == null
                    ? 0
                    : request.RemovalConfirmations.Count;
                Append(canonical, "removal-count", count.ToString(CultureInfo.InvariantCulture));
                for (int index = 0; index < count; index++)
                {
                    Append(canonical, "removal-index", index.ToString(CultureInfo.InvariantCulture));
                    AppendRemoval(canonical, request.RemovalConfirmations[index]);
                }
                Append(canonical, "warning", RemovalWarning);
            }
            else if (IsDirectControllerOperation(request.Operation))
            {
                int count = request.DirectControllers == null
                    ? 0
                    : request.DirectControllers.Count;
                Append(canonical, "direct-count", count.ToString(CultureInfo.InvariantCulture));
                for (int index = 0; index < count; index++)
                {
                    DirectControllerProvisioningItemRequest item =
                        request.DirectControllers[index];
                    Append(canonical, "direct-index", index.ToString(CultureInfo.InvariantCulture));
                    Append(canonical, "direct-kkt", item == null ? null : item.KktSerial);
                    Append(canonical, "direct-inn", item == null ? null : item.Inn);
                    Append(canonical, "direct-ordinal", item == null
                        ? null
                        : item.Ordinal.ToString(CultureInfo.InvariantCulture));
                    Append(canonical, "direct-target-lm-port", item == null
                        ? null
                        : item.TargetLocalModulePort.ToString(
                            CultureInfo.InvariantCulture));
                    Append(canonical, "direct-manifest", item == null
                        ? null
                        : item.ExpectedManifestSha256);
                }
            }
            else if (IsMsiLocalModuleOperation(request.Operation))
            {
                if (request.Operation ==
                    LmServiceOperation.EnsureMsiLocalModules)
                {
                    AppendLocalModuleInstaller(
                        canonical,
                        request.LocalModuleInstallerSelection);
                    Append(canonical, "license-notice",
                        LocalModuleLicenseNotice);
                }
                int count = request.LocalModuleMsiItems == null
                    ? 0
                    : request.LocalModuleMsiItems.Count;
                Append(canonical, "msi-lm-count",
                    count.ToString(CultureInfo.InvariantCulture));
                for (int index = 0; index < count; index++)
                {
                    LocalModuleMsiProvisioningItemRequest item =
                        request.LocalModuleMsiItems[index];
                    Append(canonical, "msi-lm-index",
                        index.ToString(CultureInfo.InvariantCulture));
                    Append(canonical, "msi-lm-inn",
                        item == null ? null : item.Inn);
                    Append(canonical, "msi-lm-ordinal", item == null
                        ? null
                        : item.CloneOrdinal.ToString(
                            CultureInfo.InvariantCulture));
                    Append(canonical, "msi-lm-api", item == null
                        ? null
                        : item.ApiPort.ToString(CultureInfo.InvariantCulture));
                    Append(canonical, "msi-lm-db", item == null
                        ? null
                        : item.DatabasePort.ToString(
                            CultureInfo.InvariantCulture));
                    Append(canonical, "msi-lm-volume",
                        item == null ? null : item.InstallVolumeRoot);
                    Append(canonical, "msi-lm-remote",
                        item == null ? null : item.RemoteAddress);
                    Append(canonical, "msi-lm-manifest",
                        item == null ? null : item.ExpectedManifestSha256);
                }
            }

            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
                StringBuilder result = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                {
                    result.Append(digest[index].ToString("x2", CultureInfo.InvariantCulture));
                }

                return result.ToString();
            }
        }

        public static bool FixedTimeEqualsHex(string left, string right)
        {
            if (left == null || right == null || left.Length != 64 || right.Length != 64)
            {
                return false;
            }

            int difference = 0;
            for (int index = 0; index < left.Length; index++)
            {
                int leftValue = NormalizeHex(left[index]);
                int rightValue = NormalizeHex(right[index]);
                difference |= leftValue ^ rightValue;
                difference |= leftValue >> 4;
                difference |= rightValue >> 4;
            }

            return difference == 0;
        }

        public static bool IsWellFormedSha256(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                if (NormalizeHex(value[index]) > 15)
                {
                    return false;
                }
            }
            return true;
        }

        private static void AppendLocalModuleInstaller(
            StringBuilder canonical,
            LocalModuleInstallerSelection selection)
        {
            Append(canonical, "lm-source", selection == null ? null : selection.SourcePath);
            Append(canonical, "lm-file", selection == null ? null : selection.FileName);
            Append(canonical, "lm-length", selection == null
                ? null
                : selection.ByteLength.ToString(CultureInfo.InvariantCulture));
            Append(canonical, "lm-sha256", selection == null ? null : selection.Sha256);
            Append(canonical, "lm-product-name", selection == null ? null : selection.ProductName);
            Append(canonical, "lm-product-version", selection == null ? null : selection.ProductVersion);
            Append(canonical, "lm-product-code", selection == null ? null : selection.ProductCode);
            Append(canonical, "lm-upgrade-code", selection == null ? null : selection.UpgradeCode);
            Append(canonical, "lm-signer-subject", selection == null ? null : selection.SignerSubject);
            Append(canonical, "lm-signer-thumbprint", selection == null ? null : selection.SignerThumbprint);
            Append(canonical, "lm-license-accepted",
                selection != null && selection.LicenseNoticeAccepted ? "1" : "0");
        }

        private static void AppendRemoval(StringBuilder canonical, LmRemovalConfirmation confirmation)
        {
            string serial = confirmation == null ? null : confirmation.KktSerial;
            Append(canonical, "kkt", serial);
            Append(canonical, "service", TryCreateServiceName(serial));
            Append(canonical, "grpc", confirmation == null ? null : confirmation.GrpcPort.ToString(CultureInfo.InvariantCulture));
            Append(canonical, "rest", confirmation == null ? null : confirmation.RestPort.ToString(CultureInfo.InvariantCulture));
            Append(canonical, "manifest", confirmation == null || confirmation.ManifestFingerprint == null
                ? null
                : confirmation.ManifestFingerprint.Sha256);
            Append(canonical, "managed-state", confirmation == null ||
                confirmation.ManagedStateFingerprint == null
                ? null
                : confirmation.ManagedStateFingerprint.Sha256);
            Append(canonical, "warning-accepted", confirmation != null && confirmation.RetainedEsmWarningAccepted ? "1" : "0");
        }

        private static void AppendCleanup(StringBuilder canonical, LmCleanupConfirmation confirmation)
        {
            string serial = confirmation == null ? null : confirmation.KktSerial;
            Append(canonical, "kkt", serial);
            Append(canonical, "service", TryCreateServiceName(serial));
            Append(canonical, "manifest", confirmation == null || confirmation.ManifestFingerprint == null
                ? null
                : confirmation.ManifestFingerprint.Sha256);
            Append(canonical, "managed-state", confirmation == null ||
                confirmation.ManagedStateFingerprint == null
                ? null
                : confirmation.ManagedStateFingerprint.Sha256);
            Append(canonical, "displayed-state", confirmation == null
                ? null
                : ((int)confirmation.DisplayedState).ToString(CultureInfo.InvariantCulture));
        }

        private static string TryCreateServiceName(string serial)
        {
            try
            {
                return LmServiceIdentity.CreateName(serial);
            }
            catch (ArgumentException)
            {
                return "<invalid>";
            }
        }

        private static bool IsDirectControllerOperation(LmServiceOperation operation)
        {
            return operation == LmServiceOperation.EnsureDirectControllers ||
                operation == LmServiceOperation.RestartDirectController ||
                operation == LmServiceOperation.RemoveDirectController ||
                operation == LmServiceOperation.RemoveAllDirectControllers;
        }

        private static bool IsMsiLocalModuleOperation(
            LmServiceOperation operation)
        {
            return operation == LmServiceOperation.EnsureMsiLocalModules ||
                operation == LmServiceOperation.RestartMsiLocalModule ||
                operation == LmServiceOperation.RemoveMsiLocalModule ||
                operation == LmServiceOperation.RemoveAllMsiLocalModules;
        }

        private static void Append(StringBuilder canonical, string key, string value)
        {
            string safeValue = value ?? string.Empty;
            canonical.Append(key);
            canonical.Append('=');
            canonical.Append(safeValue.Length.ToString(CultureInfo.InvariantCulture));
            canonical.Append(':');
            canonical.Append(safeValue);
            canonical.Append('\n');
        }

        private static int NormalizeHex(char value)
        {
            if (value >= '0' && value <= '9')
            {
                return value - '0';
            }
            if (value >= 'a' && value <= 'f')
            {
                return value - 'a' + 10;
            }
            if (value >= 'A' && value <= 'F')
            {
                return value - 'A' + 10;
            }

            return 256;
        }
    }
}
