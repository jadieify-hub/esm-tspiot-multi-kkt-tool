using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public static class CanonicalLmPlanHasher
    {
        public const string InstallerWarning =
            "All managed instances will be stopped and moved to VersionVerificationPending.";
        public const string RemovalWarning =
            "ESM binding can continue to reference the removed local controller.";

        public static string Compute(LmServiceProvisioningBatchRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            StringBuilder canonical = new StringBuilder();
            Append(canonical, "schema", request.SchemaVersion.ToString(CultureInfo.InvariantCulture));
            Append(canonical, "operation", ((int)request.Operation).ToString(CultureInfo.InvariantCulture));

            if (request.Operation == LmServiceOperation.EnsureBatch)
            {
                int count = request.Items == null ? 0 : request.Items.Count;
                Append(canonical, "item-count", count.ToString(CultureInfo.InvariantCulture));
                for (int index = 0; index < count; index++)
                {
                    LmServiceProvisioningItemRequest item = request.Items[index];
                    Append(canonical, "item-index", index.ToString(CultureInfo.InvariantCulture));
                    AppendEnsureItem(canonical, item);
                }
            }
            else if (request.Operation == LmServiceOperation.InstallControllerVersion)
            {
                AppendInstaller(canonical, request.InstallerSelection);
                Append(canonical, "warning", InstallerWarning);
            }
            else if (request.Operation == LmServiceOperation.RemoveManaged)
            {
                AppendRemoval(canonical, request.RemovalConfirmation);
                Append(canonical, "warning", RemovalWarning);
            }
            else if (request.Operation == LmServiceOperation.CleanupManaged)
            {
                AppendCleanup(canonical, request.CleanupConfirmation);
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

        private static void AppendEnsureItem(StringBuilder canonical, LmServiceProvisioningItemRequest item)
        {
            Append(canonical, "kkt", item == null ? null : item.KktSerial);
            Append(canonical, "grpc", item == null ? null : item.GrpcPort.ToString(CultureInfo.InvariantCulture));
            Append(canonical, "rest", item == null ? null : item.RestPort.ToString(CultureInfo.InvariantCulture));
            Append(canonical, "target-address", item == null ? null : item.TargetAddress);
            Append(canonical, "target-port", item == null ? null : item.TargetPort.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendInstaller(StringBuilder canonical, LmControllerInstallerSelection selection)
        {
            Append(canonical, "source", selection == null ? null : selection.SourcePath);
            Append(canonical, "file", selection == null ? null : selection.FileName);
            Append(canonical, "length", selection == null ? null : selection.ByteLength.ToString(CultureInfo.InvariantCulture));
            Append(canonical, "sha256", selection == null ? null : selection.Sha256);
            Append(canonical, "file-version", selection == null ? null : selection.FileVersion);
            Append(canonical, "product-version", selection == null ? null : selection.ProductVersion);
            Append(canonical, "signer-subject", selection == null ? null : selection.SignerSubject);
            Append(canonical, "signer-thumbprint", selection == null ? null : selection.SignerThumbprint);
            Append(canonical, "warning-accepted", selection != null && selection.StopManagedInstancesWarningAccepted ? "1" : "0");
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
