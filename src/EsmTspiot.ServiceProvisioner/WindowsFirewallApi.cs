using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class WindowsFirewallApi : IWindowsFirewallApi
    {
        private const int TcpProtocol = 6;
        private const int InboundDirection = 1;
        private const int AllowAction = 1;
        private const int DomainProfile = 1;
        private const int PrivateProfile = 2;
        private const int PublicProfile = 4;
        private const int FileNotFoundHresult = unchecked((int)0x80070002);
        private const int ElementNotFoundHresult = unchecked((int)0x80070490);

        public WindowsFirewallRuleRecord FindByName(string ruleName)
        {
            if (string.IsNullOrWhiteSpace(ruleName))
                throw new ArgumentException("Firewall rule name is required.",
                    "ruleName");
            object policyObject = null;
            object rulesObject = null;
            object ruleObject = null;
            try
            {
                policyObject = CreateCom("HNetCfg.FwPolicy2");
                rulesObject = GetProperty(policyObject, "Rules");
                try
                {
                    ruleObject = Invoke(rulesObject, "Item", ruleName);
                }
                catch (TargetInvocationException exception)
                {
                    if (exception.InnerException is System.IO.FileNotFoundException)
                        return null;
                    COMException com = exception.InnerException as COMException;
                    if (com != null &&
                        (com.ErrorCode == FileNotFoundHresult ||
                         com.ErrorCode == ElementNotFoundHresult)) return null;
                    throw;
                }
                int profiles = Convert.ToInt32(
                    GetProperty(ruleObject, "Profiles"),
                    CultureInfo.InvariantCulture);
                int protocol = Convert.ToInt32(
                    GetProperty(ruleObject, "Protocol"),
                    CultureInfo.InvariantCulture);
                int direction = Convert.ToInt32(
                    GetProperty(ruleObject, "Direction"),
                    CultureInfo.InvariantCulture);
                int action = Convert.ToInt32(
                    GetProperty(ruleObject, "Action"),
                    CultureInfo.InvariantCulture);
                int localPort;
                int.TryParse(
                    Convert.ToString(
                        GetProperty(ruleObject, "LocalPorts"),
                        CultureInfo.InvariantCulture),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out localPort);
                return new WindowsFirewallRuleRecord
                {
                    RuleName = Convert.ToString(
                        GetProperty(ruleObject, "Name")),
                    Grouping = Convert.ToString(
                        GetProperty(ruleObject, "Grouping")),
                    ProgramPath = Convert.ToString(
                        GetProperty(ruleObject, "ApplicationName")),
                    LocalPort = localPort,
                    RemoteAddress = Convert.ToString(
                        GetProperty(ruleObject, "RemoteAddresses")),
                    Enabled = Convert.ToBoolean(
                        GetProperty(ruleObject, "Enabled")),
                    Inbound = direction == InboundDirection,
                    Allow = action == AllowAction,
                    Tcp = protocol == TcpProtocol,
                    Domain = (profiles & DomainProfile) != 0,
                    Private = (profiles & PrivateProfile) != 0,
                    Public = (profiles & PublicProfile) != 0
                };
            }
            finally
            {
                Release(ruleObject);
                Release(rulesObject);
                Release(policyObject);
            }
        }

        public void Add(LocalModuleFirewallRule expected)
        {
            if (expected == null) throw new ArgumentNullException("expected");
            object policyObject = null;
            object rulesObject = null;
            object ruleObject = null;
            try
            {
                policyObject = CreateCom("HNetCfg.FwPolicy2");
                rulesObject = GetProperty(policyObject, "Rules");
                ruleObject = CreateCom("HNetCfg.FWRule");
                SetProperty(ruleObject, "Name", expected.RuleName);
                SetProperty(ruleObject, "Grouping", expected.RuleName);
                SetProperty(ruleObject, "Description",
                    "Owned local-module API rule.");
                SetProperty(ruleObject, "ApplicationName",
                    expected.ProgramPath);
                SetProperty(ruleObject, "Protocol", TcpProtocol);
                SetProperty(ruleObject, "LocalPorts",
                    expected.LocalPort.ToString(CultureInfo.InvariantCulture));
                SetProperty(ruleObject, "RemoteAddresses",
                    expected.RemoteAddress);
                SetProperty(ruleObject, "Direction", InboundDirection);
                SetProperty(ruleObject, "Action", AllowAction);
                SetProperty(ruleObject, "Profiles",
                    DomainProfile | PrivateProfile);
                SetProperty(ruleObject, "Enabled", true);
                SetProperty(ruleObject, "EdgeTraversal", false);
                Invoke(rulesObject, "Add", ruleObject);
            }
            finally
            {
                Release(ruleObject);
                Release(rulesObject);
                Release(policyObject);
            }
        }

        public void Remove(string ruleName)
        {
            if (string.IsNullOrWhiteSpace(ruleName))
                throw new ArgumentException("Firewall rule name is required.",
                    "ruleName");
            object policyObject = null;
            object rulesObject = null;
            try
            {
                policyObject = CreateCom("HNetCfg.FwPolicy2");
                rulesObject = GetProperty(policyObject, "Rules");
                Invoke(rulesObject, "Remove", ruleName);
            }
            finally
            {
                Release(rulesObject);
                Release(policyObject);
            }
        }

        private static object CreateCom(string progId)
        {
            Type type = Type.GetTypeFromProgID(progId, true);
            return Activator.CreateInstance(type);
        }

        private static object GetProperty(object target, string name)
        {
            return target.GetType().InvokeMember(
                name,
                BindingFlags.GetProperty,
                null,
                target,
                null,
                CultureInfo.InvariantCulture);
        }

        private static void SetProperty(
            object target,
            string name,
            object value)
        {
            target.GetType().InvokeMember(
                name,
                BindingFlags.SetProperty,
                null,
                target,
                new[] { value },
                CultureInfo.InvariantCulture);
        }

        private static object Invoke(
            object target,
            string name,
            object argument)
        {
            return target.GetType().InvokeMember(
                name,
                BindingFlags.InvokeMethod,
                null,
                target,
                new[] { argument },
                CultureInfo.InvariantCulture);
        }

        private static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }
    }
}
