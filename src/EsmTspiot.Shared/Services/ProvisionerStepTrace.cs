using System;
using System.IO;
using System.Text;

namespace EsmTspiot.Shared.Services
{
    /// <summary>
    /// Пошаговый след привилегированной стадии. Помощник работает отдельным
    /// процессом и отвечает один раз в самом конце, поэтому во время работы
    /// журнал программы молчит и любое ожидание выглядит как зависание.
    /// След пишется по одной строке на операцию и закрывается сразу, чтобы
    /// его можно было прочитать, пока помощник ещё работает, и чтобы он
    /// пережил принудительное завершение процесса.
    /// </summary>
    public static class ProvisionerStepTrace
    {
        private static readonly object Gate = new object();

        // След лежит рядом с журналом программы, а не в ProgramData: в
        // машинный каталог с защищёнными файлами окно без прав администратора
        // писать не может, и след молча не создавался. Помощник запускается
        // тем же пользователем через UAC, поэтому профиль у обоих один.
        public static string GetPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "EsmTspiotTool",
                "Logs",
                "provisioner-steps.log");
        }

        public static void Write(string step)
        {
            if (string.IsNullOrEmpty(step)) return;
            try
            {
                string path = GetPath();
                string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                string line = DateTime.Now.ToString("HH:mm:ss") + " " + step;
                lock (Gate)
                {
                    using (FileStream stream = new FileStream(
                        path,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite))
                    using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        writer.WriteLine(line);
                        writer.Flush();
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (NotSupportedException) { }
        }

        /// <summary>Последняя записанная строка или пустая строка.</summary>
        public static string ReadLastStep()
        {
            try
            {
                string path = GetPath();
                if (!File.Exists(path)) return string.Empty;
                string last = string.Empty;
                using (FileStream stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length > 0) last = line;
                    }
                }
                return last;
            }
            catch (IOException) { return string.Empty; }
            catch (UnauthorizedAccessException) { return string.Empty; }
        }

        /// <summary>Начать новый след для одной стадии.</summary>
        public static void Restart(string stageName)
        {
            try
            {
                string path = GetPath();
                string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(path, string.Empty, new UTF8Encoding(false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Write("=== " + stageName + " ===");
        }
    }
}
