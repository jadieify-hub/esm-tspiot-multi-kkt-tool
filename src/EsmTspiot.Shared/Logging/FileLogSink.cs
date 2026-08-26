using System;
using System.IO;
using System.Text;

namespace EsmTspiot.Shared.Logging
{
    public sealed class FileLogSink
    {
        private readonly object _sync = new object();
        private readonly string _directory;

        public FileLogSink(string directory, string fileName)
        {
            _directory = directory ?? string.Empty;
            LogFilePath = Path.Combine(_directory, fileName ?? string.Empty);
        }

        public string LogFilePath { get; private set; }

        public static FileLogSink CreateDefault()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EsmTspiotTool",
                "Logs");
            string fileName = "esm_tspiot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log";
            FileLogSink sink = new FileLogSink(directory, fileName);
            sink.TryDeleteOldLogs(TimeSpan.FromDays(14));
            return sink;
        }

        public bool TryAppend(string text)
        {
            try
            {
                lock (_sync)
                {
                    Directory.CreateDirectory(_directory);
                    using (StreamWriter writer = new StreamWriter(LogFilePath, true, new UTF8Encoding(false)))
                    {
                        writer.Write(text ?? string.Empty);
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void TryDeleteOldLogs(TimeSpan maximumAge)
        {
            try
            {
                if (!Directory.Exists(_directory))
                {
                    return;
                }

                string[] files = Directory.GetFiles(_directory, "esm_tspiot_*.log");
                DateTime cutoff = DateTime.Now.Subtract(maximumAge);
                for (int i = 0; i < files.Length; i++)
                {
                    if (File.GetLastWriteTime(files[i]) < cutoff)
                    {
                        File.Delete(files[i]);
                    }
                }
            }
            catch
            {
            }
        }
    }
}