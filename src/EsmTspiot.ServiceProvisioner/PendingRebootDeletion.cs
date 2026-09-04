using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace EsmTspiot.ServiceProvisioner
{
    /// <summary>
    /// Планирует удаление остатков каталога снятого клона ЛМ на ближайшую
    /// перезагрузку. Вендорский пакет останавливает службы, но узел Erlang
    /// завершается не мгновенно и держит файлы своего каталога. Гасить чужие
    /// процессы помощнику запрещено, поэтому оставшееся отдаём тому же
    /// механизму, которым пользуются установщики Windows: список отложенных
    /// переименований, который система разбирает при старте.
    /// </summary>
    internal static class PendingRebootDeletion
    {
        private const uint MoveFileDelayUntilReboot = 0x00000004;

        /// <summary>
        /// Ставит дерево в очередь на удаление. Возвращает false, если
        /// хотя бы один элемент поставить не удалось.
        /// </summary>
        internal static bool ScheduleTree(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return true;
            bool scheduled = true;
            string[] files = Directory.GetFiles(
                root, "*", SearchOption.AllDirectories);
            for (int index = 0; index < files.Length; index++)
                scheduled &= Schedule(files[index]);

            // Каталоги удаляются снизу вверх: система разбирает очередь по
            // порядку, и к моменту удаления каталога он уже пуст.
            List<string> directories = new List<string>(
                Directory.GetDirectories(
                    root, "*", SearchOption.AllDirectories));
            directories.Sort(delegate(string left, string right)
            {
                return right.Length.CompareTo(left.Length);
            });
            for (int index = 0; index < directories.Count; index++)
                scheduled &= Schedule(directories[index]);
            scheduled &= Schedule(root);
            return scheduled;
        }

        private static bool Schedule(string path)
        {
            return MoveFileExW(path, null, MoveFileDelayUntilReboot);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileExW(
            string existingFileName,
            string newFileName,
            uint flags);
    }
}
