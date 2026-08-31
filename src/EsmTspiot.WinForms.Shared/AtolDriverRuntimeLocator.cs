using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class AtolDriverRuntime
    {
        public string InstallationRoot { get; set; }
        public string WrapperPath { get; set; }
        public string NativeLibraryPath { get; set; }
    }

    internal sealed class AtolDriverRootCandidate
    {
        public string InstallationRoot { get; set; }
        public string Source { get; set; }
    }

    internal interface IAtolDriverEnvironment
    {
        IList<AtolDriverRootCandidate> GetCandidates();
        bool FileExists(string path);
        bool IsNativeLibraryCompatible(string path, bool process64Bit);
    }

    internal sealed class AtolDriverRuntimeLocator
    {
        private const string WrapperRelativePath = "langs\\csharp\\Atol.Drivers10.Fptr.dll";
        private const string NativeRelativePath = "bin\\fptr10.dll";
        private readonly IAtolDriverEnvironment _environment;
        private readonly bool _process64Bit;

        internal AtolDriverRuntimeLocator()
            : this(new WindowsAtolDriverEnvironment(), IntPtr.Size == 8)
        {
        }

        internal AtolDriverRuntimeLocator(
            IAtolDriverEnvironment environment,
            bool process64Bit)
        {
            if (environment == null) throw new ArgumentNullException("environment");
            _environment = environment;
            _process64Bit = process64Bit;
        }

        internal AtolDriverRuntime Resolve()
        {
            IList<AtolDriverRootCandidate> candidates = _environment.GetCandidates();
            for (int index = 0; index < candidates.Count; index++)
            {
                AtolDriverRootCandidate candidate = candidates[index];
                string root = NormalizeRoot(candidate == null ? null : candidate.InstallationRoot);
                if (string.IsNullOrEmpty(root)) continue;

                string wrapper = Path.GetFullPath(Path.Combine(root, WrapperRelativePath));
                string native = Path.GetFullPath(Path.Combine(root, NativeRelativePath));
                if (!IsWithinRoot(root, wrapper) || !IsWithinRoot(root, native)) continue;
                if (!_environment.FileExists(wrapper) || !_environment.FileExists(native)) continue;
                if (!_environment.IsNativeLibraryCompatible(native, _process64Bit)) continue;

                return new AtolDriverRuntime
                {
                    InstallationRoot = root,
                    WrapperPath = wrapper,
                    NativeLibraryPath = native
                };
            }

            string bitness = _process64Bit ? "64" : "32";
            throw new InvalidOperationException(
                "Не найден установленный Драйвер ККТ v.10 с " + bitness +
                "-разрядным API. Установите подходящую версию ДТО АТОЛ или запустите " +
                "соответствующий операторский EXE.");
        }

        private static string NormalizeRoot(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string trimmed = value.Trim().Trim('"');
            try
            {
                return Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsWithinRoot(string root, string path)
        {
            if (string.Equals(root, path, StringComparison.OrdinalIgnoreCase)) return true;
            string prefix = root + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class WindowsAtolDriverEnvironment : IAtolDriverEnvironment
    {
        private const string UninstallPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public IList<AtolDriverRootCandidate> GetCandidates()
        {
            List<AtolDriverRootCandidate> result = new List<AtolDriverRootCandidate>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddUninstallCandidates(result, seen, RegistryView.Registry32);
            AddUninstallCandidates(result, seen, RegistryView.Registry64);
            AddFallback(result, seen, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            AddFallback(result, seen, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            return result;
        }

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public bool IsNativeLibraryCompatible(string path, bool process64Bit)
        {
            try
            {
                return AtolPortableExecutable.IsCompatible(File.ReadAllBytes(path), process64Bit);
            }
            catch
            {
                return false;
            }
        }

        private static void AddUninstallCandidates(
            IList<AtolDriverRootCandidate> result,
            ISet<string> seen,
            RegistryView view)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (RegistryKey uninstall = baseKey.OpenSubKey(UninstallPath, false))
                {
                    if (uninstall == null) return;
                    string[] names = uninstall.GetSubKeyNames();
                    for (int index = 0; index < names.Length; index++)
                    {
                        using (RegistryKey product = uninstall.OpenSubKey(names[index], false))
                        {
                            if (product == null) continue;
                            string displayName = product.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(displayName) ||
                                displayName.IndexOf("Драйвер ККТ v.10", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                continue;
                            }
                            AddCandidate(
                                result,
                                seen,
                                product.GetValue("InstallLocation") as string,
                                "uninstall");
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
        }

        private static void AddFallback(
            IList<AtolDriverRootCandidate> result,
            ISet<string> seen,
            string programFiles)
        {
            if (string.IsNullOrWhiteSpace(programFiles)) return;
            AddCandidate(result, seen, Path.Combine(programFiles, @"ATOL\Drivers10\KKT"), "fallback");
        }

        private static void AddCandidate(
            IList<AtolDriverRootCandidate> result,
            ISet<string> seen,
            string root,
            string source)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            string normalized;
            try
            {
                normalized = Path.GetFullPath(root.Trim().Trim('"'))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return;
            }
            if (!seen.Add(normalized)) return;
            result.Add(new AtolDriverRootCandidate
            {
                InstallationRoot = normalized,
                Source = source
            });
        }
    }

    internal static class AtolPortableExecutable
    {
        private const ushort MachineX86 = 0x014c;
        private const ushort MachineX64 = 0x8664;

        internal static bool IsCompatible(byte[] image, bool process64Bit)
        {
            if (image == null || image.Length < 0x40) return false;
            if (image[0] != (byte)'M' || image[1] != (byte)'Z') return false;
            int peOffset = image[0x3c] |
                (image[0x3d] << 8) |
                (image[0x3e] << 16) |
                (image[0x3f] << 24);
            if (peOffset < 0 || peOffset > image.Length - 6) return false;
            if (image[peOffset] != (byte)'P' || image[peOffset + 1] != (byte)'E' ||
                image[peOffset + 2] != 0 || image[peOffset + 3] != 0)
            {
                return false;
            }
            ushort machine = (ushort)(image[peOffset + 4] | (image[peOffset + 5] << 8));
            return process64Bit ? machine == MachineX64 : machine == MachineX86;
        }
    }
}
