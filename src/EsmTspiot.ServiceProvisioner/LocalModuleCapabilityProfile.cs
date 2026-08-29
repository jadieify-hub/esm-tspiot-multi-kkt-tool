using System;
using System.Collections.Generic;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class LocalModuleRequiredFile
    {
        private readonly string _relativePath;
        private readonly long _byteLength;
        private readonly string _sha256;

        internal LocalModuleRequiredFile(
            string relativePath,
            long byteLength,
            string sha256)
        {
            _relativePath = relativePath;
            _byteLength = byteLength;
            _sha256 = sha256;
        }

        internal string RelativePath { get { return _relativePath; } }
        internal long ByteLength { get { return _byteLength; } }
        internal string Sha256 { get { return _sha256; } }
    }

    internal sealed class LocalModuleCapabilityProfile
    {
        private LocalModuleCapabilityProfile()
        {
            RequiredFiles = new List<LocalModuleRequiredFile>();
            RequiredDirectories = new List<string>();
            ExcludedRuntimeRelativePaths = new List<string>();
        }

        internal string CapabilityId { get; private set; }
        internal string ProductVersion { get; private set; }
        internal string PackageRevision { get; private set; }
        internal string ErtsVersion { get; private set; }
        internal string ApiRelease { get; private set; }
        internal string DatabaseRelease { get; private set; }
        internal string ErlExecutableRelativePath { get; private set; }
        internal string EpmdExecutableRelativePath { get; private set; }
        internal string ApiBootRelativePath { get; private set; }
        internal string DatabaseBootRelativePath { get; private set; }
        internal string DatabaseDefaultIniRelativePath { get; private set; }
        internal string RuntimeContractSha256 { get; private set; }
        internal IList<LocalModuleRequiredFile> RequiredFiles { get; private set; }
        internal IList<string> RequiredDirectories { get; private set; }
        internal IList<string> ExcludedRuntimeRelativePaths { get; private set; }

        internal static LocalModuleCapabilityProfile Resolve(string productVersion)
        {
            if (!string.Equals(productVersion, "2.6.1", StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    "Версия ЛМ ЧЗ " + (productVersion ?? "<null>") +
                    " не имеет проверенного capability-профиля.");
            }

            LocalModuleCapabilityProfile profile = new LocalModuleCapabilityProfile
            {
                CapabilityId = "local-module-2.6.1-7",
                ProductVersion = "2.6.1",
                PackageRevision = "2.6.1-7",
                ErtsVersion = "13.0.4",
                ApiRelease = "2.6.1-7",
                DatabaseRelease = "2.2.5-2110",
                ErlExecutableRelativePath = @"erts-13.0.4\bin\erl.exe",
                EpmdExecutableRelativePath = @"erts-13.0.4\bin\epmd.exe",
                ApiBootRelativePath = @"regime\releases\2.6.1-7\regime",
                DatabaseBootRelativePath = @"yenisei\releases\2.2.5-2110\yenisei",
                DatabaseDefaultIniRelativePath = @"yenisei\etc\default.ini",
                RuntimeContractSha256 =
                    "a6d537344f70f4396614bba6095b1d21bd061f717bf609175f4584c24510f272"
            };
            AddRequiredFiles(profile);
            profile.RequiredDirectories.Add(@"yenisei\etc\default.d");
            profile.RequiredDirectories.Add(@"yenisei\share\server");
            profile.ExcludedRuntimeRelativePaths.Add(@"bin\nssm.exe");
            profile.ExcludedRuntimeRelativePaths.Add(@"bin\InstallAutoUpdateLM.exe");
            profile.ExcludedRuntimeRelativePaths.Add(@"bin\regime.cmd");
            profile.ExcludedRuntimeRelativePaths.Add(@"bin\yenisei.cmd");
            profile.ExcludedRuntimeRelativePaths.Add(@"erts-13.0.4\bin\erl.ini");
            profile.RequiredFiles =
                new List<LocalModuleRequiredFile>(profile.RequiredFiles).AsReadOnly();
            profile.RequiredDirectories =
                new List<string>(profile.RequiredDirectories).AsReadOnly();
            profile.ExcludedRuntimeRelativePaths =
                new List<string>(profile.ExcludedRuntimeRelativePaths).AsReadOnly();
            return profile;
        }

        private static void AddRequiredFiles(LocalModuleCapabilityProfile profile)
        {
            Add(profile, @"bin\couchjs.exe", 318976,
                "2d4293b0de8c376aee26f7ada479b8628c1c0892a93fbaf7ff75fb1e02d900ef");
            Add(profile, @"erts-13.0.4\bin\beam.smp.dll", 4439040,
                "eb22033d1e55cb808679a3bb68bf890bc37caa3598717a79ba8fce24e8942717");
            Add(profile, @"erts-13.0.4\bin\epmd.exe", 55296,
                "bb1822dcd37686ac02608d33785fabc2bbfba5d321f89f351111dbb803740713");
            Add(profile, @"erts-13.0.4\bin\erl.exe", 127488,
                "bf69e12773ed7dcda60597dbefa7e9f648d708e3c56afb6f24f1457bcaa2ccd1");
            Add(profile, @"erts-13.0.4\bin\erlexec.dll", 174592,
                "977a6bd47285cffa5b29c0ebcc2b6d0030a322203814e3f771fd54797a93f055");
            Add(profile, @"regime\etc\local.ini.dist", 437,
                "e170108a3fbd8e55a7c5fd26954b1eba2efd38bc2231b5dd1e9bd00fd8b3a8d2");
            Add(profile, @"regime\etc\vm.args.dist", 2137,
                "9d0cfee63da84bd2e124856a17d7b2a9d02a4b0577599cdab8c8c7104ab0692f");
            Add(profile, @"regime\releases\2.6.1-7\regime.boot", 33302,
                "65408ec9269f577457454af03fe2f168fe461a1000ddabde51c002d2d6a37133");
            Add(profile, @"regime\releases\2.6.1-7\sys.config", 757,
                "d455b72d7e973c595d3e89dd9f76aa167435866cdbafae72e8308a6ec305c988");
            Add(profile, @"yenisei\etc\default.ini", 427,
                "3d0a3323c55be9866d3a9931863b22033d2224b21fb2609aa2f8495dc079d826");
            Add(profile, @"yenisei\etc\local.ini.dist", 247,
                "748991cbdacf1809774193dd0da47badf1c588cb0bd2e39d82c476ea8e179447");
            Add(profile, @"yenisei\etc\vm.args.dist", 2138,
                "ec1ec4b1b0f8e33fd709bfac16647c63abe365c87891f181290e5a3c32a93550");
            Add(profile, @"yenisei\releases\2.2.5-2110\sys.config", 212,
                "2a2221bf00be43f80ee8333822afdb9b9f62f977ab7d8345bed83d4a24a822ba");
            Add(profile, @"yenisei\releases\2.2.5-2110\yenisei.boot", 77561,
                "db734854e55d339cbcd2d6c6c729b5481a58b926f8252c5b97e24e54e54d6740");
            Add(profile, @"yenisei\share\server\main-coffee.js", 600614,
                "5f4744ff782248645c1478c7535ee09d29af457ca0d7c97c135bbf8dd1b06cb5");
            Add(profile, @"yenisei\share\server\main.js", 440386,
                "f69a1f6e8f687a1fa47d1569c69bf515f8c4d8252b12a8698d824e044da86b47");
        }

        private static void Add(
            LocalModuleCapabilityProfile profile,
            string relativePath,
            long byteLength,
            string sha256)
        {
            profile.RequiredFiles.Add(new LocalModuleRequiredFile(
                relativePath,
                byteLength,
                sha256));
        }
    }
}
