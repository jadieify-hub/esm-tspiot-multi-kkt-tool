using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace EsmTspiot.Shared.Services
{
    [DataContract]
    public sealed class OperatorPackagePaths
    {
        [DataMember(Order = 1)]
        public string ControllerInstallerPath { get; set; }

        [DataMember(Order = 2)]
        public string LocalModuleInstallerPath { get; set; }
    }

    public sealed class OperatorPackagePathStore
    {
        private const long MaximumFileBytes = 32 * 1024;
        private const int MaximumPathLength = 4096;
        private readonly string _path;

        public OperatorPackagePathStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "Package path settings file is required.",
                    "path");
            }
            _path = Path.GetFullPath(path);
        }

        public static OperatorPackagePathStore CreateDefault()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "EsmTspiotTool");
            return new OperatorPackagePathStore(
                Path.Combine(directory, "operator-package-paths.json"));
        }

        public OperatorPackagePaths Load()
        {
            if (!File.Exists(_path))
            {
                return Empty();
            }
            FileInfo file = new FileInfo(_path);
            if (file.Length < 0 || file.Length > MaximumFileBytes)
            {
                throw new InvalidDataException(
                    "Файл сохранённых путей к пакетам имеет недопустимый размер.");
            }

            OperatorPackagePaths stored;
            using (FileStream stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                stored = new DataContractJsonSerializer(
                    typeof(OperatorPackagePaths)).ReadObject(stream)
                    as OperatorPackagePaths;
            }
            if (stored == null)
            {
                return Empty();
            }
            return new OperatorPackagePaths
            {
                ControllerInstallerPath = NormalizeOptional(
                    stored.ControllerInstallerPath),
                LocalModuleInstallerPath = NormalizeOptional(
                    stored.LocalModuleInstallerPath)
            };
        }

        public void Save(
            string controllerInstallerPath,
            string localModuleInstallerPath)
        {
            OperatorPackagePaths safe = new OperatorPackagePaths
            {
                ControllerInstallerPath = NormalizeOptional(
                    controllerInstallerPath),
                LocalModuleInstallerPath = NormalizeOptional(
                    localModuleInstallerPath)
            };
            string directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidDataException(
                    "Не задан каталог сохранения путей к пакетам.");
            }
            Directory.CreateDirectory(directory);

            string temporary = _path + "." +
                Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    new DataContractJsonSerializer(
                        typeof(OperatorPackagePaths)).WriteObject(stream, safe);
                    stream.Flush(true);
                }
                if (File.Exists(_path))
                {
                    File.Replace(temporary, _path, null);
                }
                else
                {
                    File.Move(temporary, _path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static OperatorPackagePaths Empty()
        {
            return new OperatorPackagePaths
            {
                ControllerInstallerPath = string.Empty,
                LocalModuleInstallerPath = string.Empty
            };
        }

        private static string NormalizeOptional(string value)
        {
            string path = (value ?? string.Empty).Trim();
            if (path.Length == 0)
            {
                return string.Empty;
            }
            if (path.Length > MaximumPathLength ||
                path.IndexOf('\r') >= 0 ||
                path.IndexOf('\n') >= 0 ||
                !Path.IsPathRooted(path))
            {
                throw new InvalidDataException(
                    "Сохранённый путь к пакету недопустим.");
            }
            return Path.GetFullPath(path);
        }
    }
}
