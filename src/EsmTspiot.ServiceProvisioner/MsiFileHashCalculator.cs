using System;
using System.IO;
using WixToolset.Dtf.WindowsInstaller;

namespace EsmTspiot.ServiceProvisioner
{
    internal static class MsiFileHashCalculator
    {
        internal static int[] Compute(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("MSI hash path is required.", "path");
            string fullPath = Path.GetFullPath(path);
            int[] result = new int[4];
            Installer.GetFileHash(fullPath, result);
            return result;
        }
    }
}
