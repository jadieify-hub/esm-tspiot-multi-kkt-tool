using System;
using System.Collections.Generic;
using Microsoft.Win32;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class AtolVcomRegistryRecord
    {
        public string HardwareId { get; set; }
        public string PortName { get; set; }
        public bool IsPresent { get; set; }
    }

    internal interface IAtolVcomRegistry
    {
        IList<AtolVcomRegistryRecord> ReadRecords();
    }

    internal sealed class AtolVcomEnumerator
    {
        private readonly IAtolVcomRegistry _registry;

        internal AtolVcomEnumerator()
            : this(new WindowsAtolVcomRegistry())
        {
        }

        internal AtolVcomEnumerator(IAtolVcomRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException("registry");
            _registry = registry;
        }

        internal IList<KktConnectionPort> EnumeratePorts()
        {
            IList<AtolVcomRegistryRecord> records = _registry.ReadRecords();
            Dictionary<string, KktConnectionPort> ports =
                new Dictionary<string, KktConnectionPort>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < records.Count; index++)
            {
                AtolVcomRegistryRecord record = records[index];
                if (record == null || !record.IsPresent ||
                    !IsAtolPosChannel(record.HardwareId)) continue;
                int portNumber;
                string portName;
                if (!TryNormalizePort(record.PortName, out portName, out portNumber)) continue;
                if (!ports.ContainsKey(portName))
                {
                    ports.Add(portName, new KktConnectionPort
                    {
                        PortName = portName,
                        HardwareId = record.HardwareId.Trim()
                    });
                }
            }

            List<KktConnectionPort> result = new List<KktConnectionPort>(ports.Values);
            result.Sort(delegate(KktConnectionPort left, KktConnectionPort right)
            {
                int leftNumber;
                int rightNumber;
                string ignored;
                TryNormalizePort(left.PortName, out ignored, out leftNumber);
                TryNormalizePort(right.PortName, out ignored, out rightNumber);
                return leftNumber.CompareTo(rightNumber);
            });
            return result;
        }

        private static bool IsAtolPosChannel(string hardwareId)
        {
            if (string.IsNullOrWhiteSpace(hardwareId)) return false;
            return hardwareId.IndexOf("VID_2912", StringComparison.OrdinalIgnoreCase) >= 0 &&
                hardwareId.IndexOf("MI_00", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryNormalizePort(
            string value,
            out string portName,
            out int portNumber)
        {
            portName = string.Empty;
            portNumber = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string trimmed = value.Trim();
            if (!trimmed.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || trimmed.Length < 4)
            {
                return false;
            }
            int parsed;
            if (!int.TryParse(trimmed.Substring(3), out parsed) || parsed < 1) return false;
            portNumber = parsed;
            portName = "COM" + parsed;
            return true;
        }
    }

    internal sealed class WindowsAtolVcomRegistry : IAtolVcomRegistry
    {
        private const string UsbEnumPath = @"SYSTEM\CurrentControlSet\Enum\USB";
        private const string ActiveSerialPortsPath =
            @"HARDWARE\DEVICEMAP\SERIALCOMM";

        public IList<AtolVcomRegistryRecord> ReadRecords()
        {
            List<AtolVcomRegistryRecord> result = new List<AtolVcomRegistryRecord>();
            ISet<string> activePorts = ReadActivePorts();
            try
            {
                using (RegistryKey usb = Registry.LocalMachine.OpenSubKey(UsbEnumPath, false))
                {
                    if (usb == null) return result;
                    string[] interfaces = usb.GetSubKeyNames();
                    for (int interfaceIndex = 0; interfaceIndex < interfaces.Length; interfaceIndex++)
                    {
                        string hardwareId = interfaces[interfaceIndex];
                        if (hardwareId.IndexOf("VID_2912", StringComparison.OrdinalIgnoreCase) < 0 ||
                            hardwareId.IndexOf("MI_00", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                        using (RegistryKey interfaceKey = usb.OpenSubKey(hardwareId, false))
                        {
                            if (interfaceKey == null) continue;
                            string[] instances = interfaceKey.GetSubKeyNames();
                            for (int instanceIndex = 0; instanceIndex < instances.Length; instanceIndex++)
                            {
                                using (RegistryKey parameters = interfaceKey.OpenSubKey(
                                    instances[instanceIndex] + "\\Device Parameters", false))
                                {
                                    if (parameters == null) continue;
                                    string portName = parameters.GetValue("PortName") as string;
                                    result.Add(new AtolVcomRegistryRecord
                                    {
                                        HardwareId = "USB\\" + hardwareId,
                                        PortName = portName,
                                        IsPresent = !string.IsNullOrWhiteSpace(
                                            portName) &&
                                            activePorts.Contains(portName.Trim())
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (System.Security.SecurityException)
            {
            }
            return result;
        }

        private static ISet<string> ReadActivePorts()
        {
            HashSet<string> result =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (RegistryKey serial = Registry.LocalMachine.OpenSubKey(
                    ActiveSerialPortsPath,
                    false))
                {
                    if (serial == null) return result;
                    string[] names = serial.GetValueNames();
                    for (int index = 0; index < names.Length; index++)
                    {
                        string port = serial.GetValue(names[index]) as string;
                        if (!string.IsNullOrWhiteSpace(port))
                        {
                            result.Add(port.Trim());
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (System.Security.SecurityException)
            {
            }
            return result;
        }
    }
}
