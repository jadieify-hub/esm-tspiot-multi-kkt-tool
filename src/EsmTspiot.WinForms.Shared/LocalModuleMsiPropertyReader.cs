using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace EsmTspiot.WinForms.Shared
{
    /// <summary>
    /// Чтение таблицы Property установочного пакета через msi.dll.
    /// Операторская часть не ссылается на DTF, поэтому используется
    /// прямой вызов Windows Installer только на чтение.
    /// </summary>
    internal static class LocalModuleMsiPropertyReader
    {
        private const int ErrorSuccess = 0;
        private const int ErrorMoreData = 234;
        private const int ErrorNoMoreItems = 259;

        internal static string ReadRequiredProperty(string msiPath, string name)
        {
            string value = ReadProperty(msiPath, name);
            if (value.Length == 0)
            {
                throw new InvalidDataException(
                    "В MSI отсутствует обязательное свойство " + name + ".");
            }
            return value;
        }

        internal static string ReadProperty(string msiPath, string name)
        {
            RequirePlainName(name);
            string path = Path.GetFullPath(msiPath);
            uint database = 0;
            uint view = 0;
            uint record = 0;
            try
            {
                int opened = MsiOpenDatabaseW(path, IntPtr.Zero, out database);
                if (opened != ErrorSuccess)
                {
                    throw new InvalidDataException(
                        "Не удалось открыть MSI для чтения свойств (код " +
                        opened.ToString() + ").");
                }
                string query =
                    "SELECT `Value` FROM `Property` WHERE `Property` = '" +
                    name + "'";
                int prepared = MsiDatabaseOpenViewW(database, query, out view);
                if (prepared != ErrorSuccess)
                {
                    throw new InvalidDataException(
                        "Не удалось прочитать таблицу Property в MSI (код " +
                        prepared.ToString() + ").");
                }
                int executed = MsiViewExecute(view, 0);
                if (executed != ErrorSuccess)
                {
                    throw new InvalidDataException(
                        "Не удалось выполнить запрос к MSI (код " +
                        executed.ToString() + ").");
                }
                int fetched = MsiViewFetch(view, out record);
                if (fetched == ErrorNoMoreItems)
                {
                    return string.Empty;
                }
                if (fetched != ErrorSuccess)
                {
                    throw new InvalidDataException(
                        "Не удалось получить строку Property из MSI (код " +
                        fetched.ToString() + ").");
                }
                return ReadRecordString(record);
            }
            finally
            {
                if (record != 0) MsiCloseHandle(record);
                if (view != 0) MsiCloseHandle(view);
                if (database != 0) MsiCloseHandle(database);
            }
        }

        private static string ReadRecordString(uint record)
        {
            StringBuilder buffer = new StringBuilder(512);
            uint size = (uint)buffer.Capacity;
            int status = MsiRecordGetStringW(record, 1, buffer, ref size);
            if (status == ErrorMoreData)
            {
                buffer = new StringBuilder((int)size + 1);
                size = (uint)buffer.Capacity;
                status = MsiRecordGetStringW(record, 1, buffer, ref size);
            }
            if (status != ErrorSuccess)
            {
                throw new InvalidDataException(
                    "Не удалось прочитать значение свойства MSI (код " +
                    status.ToString() + ").");
            }
            return buffer.ToString().Trim();
        }

        private static void RequirePlainName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("MSI property name is required.", "name");
            }
            for (int index = 0; index < name.Length; index++)
            {
                char current = name[index];
                if (!((current >= 'a' && current <= 'z') ||
                      (current >= 'A' && current <= 'Z') ||
                      (current >= '0' && current <= '9')))
                {
                    throw new ArgumentException(
                        "MSI property name must be alphanumeric.",
                        "name");
                }
            }
        }

        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int MsiOpenDatabaseW(
            string databasePath,
            IntPtr persist,
            out uint database);

        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int MsiDatabaseOpenViewW(
            uint database,
            string query,
            out uint view);

        [DllImport("msi.dll", ExactSpelling = true)]
        private static extern int MsiViewExecute(uint view, uint record);

        [DllImport("msi.dll", ExactSpelling = true)]
        private static extern int MsiViewFetch(uint view, out uint record);

        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int MsiRecordGetStringW(
            uint record,
            uint field,
            StringBuilder value,
            ref uint valueSize);

        [DllImport("msi.dll", ExactSpelling = true)]
        private static extern int MsiCloseHandle(uint handle);
    }
}
