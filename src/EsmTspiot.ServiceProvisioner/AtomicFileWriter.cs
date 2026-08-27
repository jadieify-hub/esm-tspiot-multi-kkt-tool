using System;
using System.IO;
using System.Text;

namespace EsmTspiot.ServiceProvisioner
{
    internal sealed class AtomicFileWriter
    {
        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false, true);

        internal void WriteUtf8(string path, string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }
            WriteBytes(path, Utf8WithoutBom.GetBytes(value));
        }

        internal void WriteBytes(string path, byte[] payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException("payload");
            }
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException("Atomic write directory does not exist.");
            }

            string temporary = Path.Combine(
                directory,
                Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    stream.Write(payload, 0, payload.Length);
                    stream.Flush(true);
                }
                if (File.Exists(fullPath))
                {
                    File.Replace(temporary, fullPath, null, true);
                }
                else
                {
                    File.Move(temporary, fullPath);
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
    }
}
