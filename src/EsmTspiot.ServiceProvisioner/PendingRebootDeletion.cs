using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

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

        /// <summary>
        /// Стоит ли путь (или каталог над ним) в системной очереди удаления
        /// на перезагрузку. Ставить туда же новую установку нельзя: очередь
        /// хранит абсолютные пути, и при загрузке система снесёт уже чужие
        /// файлы. Очередь читается целиком, поэтому в неё попадают и записи
        /// вендорских установщиков, а не только наши.
        /// </summary>
        internal static bool IsScheduled(string path)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Session Manager"))
                {
                    if (key == null) return false;
                    string[] entries =
                        key.GetValue("PendingFileRenameOperations") as string[];
                    return CoversPath(entries, path);
                }
            }
            catch (Exception)
            {
                // Очередь недоступна — считаем, что она пуста: отказывать в
                // установке из-за нечитаемого реестра нельзя.
                return false;
            }
        }

        /// <summary>
        /// Записи очереди — пары «источник», «цель»; источник приходит с
        /// префиксом \??\, а цель у удаления пустая. Совпадением считается
        /// как сам путь, так и любой каталог над ним.
        /// </summary>
        internal static bool CoversPath(string[] entries, string path)
        {
            if (entries == null || entries.Length == 0) return false;
            if (string.IsNullOrEmpty(path)) return false;
            string target = Normalize(path);
            if (target.Length == 0) return false;
            for (int index = 0; index < entries.Length; index += 2)
            {
                string scheduled = Normalize(entries[index]);
                if (scheduled.Length == 0) continue;
                if (string.Equals(scheduled, target,
                        StringComparison.OrdinalIgnoreCase))
                    return true;
                if (target.StartsWith(
                        scheduled + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string Normalize(string value)
        {
            // Живая очередь этой машины держит не только "\??\C:\...":
            // обновления Windows пишут туда "!\??\..." и "*1\??\...".
            // Путь начинается сразу за префиксом устройства, поэтому всё
            // до него отбрасывается целиком.
            string result = (value ?? string.Empty).Trim();
            int marker = result.IndexOf(@"\??\", StringComparison.Ordinal);
            if (marker >= 0) result = result.Substring(marker + 4);
            return result.TrimEnd(Path.DirectorySeparatorChar);
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
