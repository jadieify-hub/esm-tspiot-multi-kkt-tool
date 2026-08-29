using System;
using System.Collections.Generic;

namespace EsmTspiot.ServiceProvisioner
{
    internal enum WindowsServiceState
    {
        Unknown = 0,
        Stopped = 1,
        StartPending = 2,
        StopPending = 3,
        Running = 4,
        ContinuePending = 5,
        PausePending = 6,
        Paused = 7
    }

    internal enum WindowsServiceStartMode
    {
        AutoStart = 2,
        DemandStart = 3,
        Disabled = 4
    }

    internal enum WindowsServiceErrorControl
    {
        Ignore = 0,
        Normal = 1,
        Severe = 2,
        Critical = 3
    }

    internal enum WindowsServiceSidType
    {
        None = 0,
        Unrestricted = 1,
        Restricted = 3
    }

    internal sealed class WindowsServiceRecoveryPolicy
    {
        internal WindowsServiceRecoveryPolicy(
            int resetPeriodSeconds,
            IEnumerable<int> restartDelaysMilliseconds)
        {
            if (resetPeriodSeconds < 0)
            {
                throw new ArgumentOutOfRangeException("resetPeriodSeconds");
            }
            ResetPeriodSeconds = resetPeriodSeconds;
            if (restartDelaysMilliseconds == null)
            {
                throw new ArgumentNullException("restartDelaysMilliseconds");
            }
            List<int> delays = new List<int>(restartDelaysMilliseconds);
            RestartDelaysMilliseconds = delays.AsReadOnly();
            for (int index = 0; index < RestartDelaysMilliseconds.Count; index++)
            {
                if (RestartDelaysMilliseconds[index] < 0)
                {
                    throw new ArgumentOutOfRangeException("restartDelaysMilliseconds");
                }
            }
        }

        internal int ResetPeriodSeconds { get; private set; }
        internal IList<int> RestartDelaysMilliseconds { get; private set; }
    }

    internal sealed class WindowsServiceDefinition
    {
        internal string ServiceName { get; set; }
        internal string DisplayName { get; set; }
        internal string ImagePath { get; set; }
        internal string Description { get; set; }
        internal string AccountName { get; set; }
        internal IList<string> Dependencies { get; set; }
        internal WindowsServiceStartMode StartMode { get; set; }
        internal WindowsServiceErrorControl ErrorControl { get; set; }
        internal WindowsServiceSidType ServiceSidType { get; set; }
        internal WindowsServiceRecoveryPolicy RecoveryPolicy { get; set; }
        internal ServiceSecurityDescriptor SecurityDescriptor { get; set; }

        internal void Validate()
        {
            string serial;
            bool controllerName =
                EsmTspiot.Shared.Services.LmServiceIdentity.TryParseName(
                    ServiceName,
                    out serial);
            if ((!controllerName && !LocalModuleServiceIdentity.IsManagedName(ServiceName)) ||
                string.IsNullOrWhiteSpace(DisplayName) ||
                string.IsNullOrWhiteSpace(ImagePath) ||
                string.IsNullOrWhiteSpace(Description) ||
                !string.Equals(AccountName, "LocalSystem", StringComparison.Ordinal) ||
                Dependencies == null ||
                ServiceSidType != WindowsServiceSidType.Restricted ||
                RecoveryPolicy == null ||
                SecurityDescriptor == null ||
                !SecurityDescriptor.IsRestrictive)
            {
                throw new InvalidOperationException("Managed Windows service definition is invalid.");
            }
            string localInstanceId;
            LocalModuleProcessRole localRole;
            if (LocalModuleServiceIdentity.TryParseName(
                    ServiceName,
                    out localInstanceId,
                    out localRole))
            {
                bool validLocalDependencies =
                    (localRole == LocalModuleProcessRole.Database &&
                     Dependencies.Count == 0) ||
                    (localRole == LocalModuleProcessRole.Api &&
                     Dependencies.Count == 1 &&
                     string.Equals(
                         Dependencies[0],
                         LocalModuleServiceIdentity.CreateName(
                             localInstanceId,
                             LocalModuleProcessRole.Database),
                         StringComparison.Ordinal));
                if (!validLocalDependencies)
                {
                    throw new InvalidOperationException(
                        "Managed Windows service dependency is invalid.");
                }
            }
            else if (Dependencies.Count != 0)
            {
                throw new InvalidOperationException(
                    "Controller service dependency is not part of the exact profile.");
            }
        }
    }

    internal sealed class WindowsServiceRecord
    {
        internal string ServiceName { get; set; }
        internal string DisplayName { get; set; }
        internal string ImagePath { get; set; }
        internal string Description { get; set; }
        internal string AccountName { get; set; }
        internal IList<string> Dependencies { get; set; }
        internal WindowsServiceStartMode StartMode { get; set; }
        internal WindowsServiceErrorControl ErrorControl { get; set; }
        internal WindowsServiceSidType ServiceSidType { get; set; }
        internal WindowsServiceRecoveryPolicy RecoveryPolicy { get; set; }
        internal ServiceSecurityDescriptor SecurityDescriptor { get; set; }
        internal WindowsServiceState State { get; set; }
        internal int ProcessId { get; set; }

        internal static WindowsServiceRecord FromDefinition(WindowsServiceDefinition definition)
        {
            definition.Validate();
            return new WindowsServiceRecord
            {
                ServiceName = definition.ServiceName,
                DisplayName = definition.DisplayName,
                ImagePath = definition.ImagePath,
                Description = definition.Description,
                AccountName = definition.AccountName,
                Dependencies = new List<string>(definition.Dependencies),
                StartMode = definition.StartMode,
                ErrorControl = definition.ErrorControl,
                ServiceSidType = definition.ServiceSidType,
                RecoveryPolicy = definition.RecoveryPolicy,
                SecurityDescriptor = definition.SecurityDescriptor,
                State = WindowsServiceState.Stopped,
                ProcessId = 0
            };
        }
    }

    internal static class WindowsServiceDefinitionMatcher
    {
        internal static bool Matches(
            WindowsServiceDefinition expected,
            WindowsServiceRecord actual)
        {
            if (expected == null || actual == null ||
                !string.Equals(expected.ServiceName, actual.ServiceName, StringComparison.Ordinal) ||
                !string.Equals(expected.DisplayName, actual.DisplayName, StringComparison.Ordinal) ||
                !string.Equals(expected.ImagePath, actual.ImagePath, StringComparison.Ordinal) ||
                !string.Equals(expected.Description, actual.Description, StringComparison.Ordinal) ||
                !string.Equals(expected.AccountName, actual.AccountName, StringComparison.Ordinal) ||
                expected.StartMode != actual.StartMode ||
                expected.ErrorControl != actual.ErrorControl ||
                expected.ServiceSidType != actual.ServiceSidType ||
                actual.SecurityDescriptor == null || !actual.SecurityDescriptor.IsRestrictive ||
                expected.SecurityDescriptor == null ||
                !string.Equals(
                    expected.SecurityDescriptor.OperatorSid,
                    actual.SecurityDescriptor.OperatorSid,
                    StringComparison.Ordinal))
            {
                return false;
            }
            IList<string> dependencies = actual.Dependencies ?? new List<string>();
            if (dependencies.Count != expected.Dependencies.Count)
            {
                return false;
            }
            for (int index = 0; index < dependencies.Count; index++)
            {
                if (!string.Equals(
                        dependencies[index],
                        expected.Dependencies[index],
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }
            if (actual.RecoveryPolicy == null || expected.RecoveryPolicy == null ||
                actual.RecoveryPolicy.ResetPeriodSeconds !=
                    expected.RecoveryPolicy.ResetPeriodSeconds ||
                actual.RecoveryPolicy.RestartDelaysMilliseconds.Count !=
                    expected.RecoveryPolicy.RestartDelaysMilliseconds.Count)
            {
                return false;
            }
            for (int index = 0;
                index < expected.RecoveryPolicy.RestartDelaysMilliseconds.Count;
                index++)
            {
                if (actual.RecoveryPolicy.RestartDelaysMilliseconds[index] !=
                    expected.RecoveryPolicy.RestartDelaysMilliseconds[index])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
