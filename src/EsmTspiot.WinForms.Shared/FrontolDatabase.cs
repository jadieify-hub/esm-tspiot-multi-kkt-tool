using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using EsmTspiot.Shared.Services;
using Microsoft.Win32;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class FrontolDatabase
    {
        private readonly FrontolSettings _settings;
        private const string EquipmentJoin =
            " FROM ECRDEV E JOIN DEVICES D ON D.ID=E.DEVICEID" +
            " JOIN RMKDEV RD ON RD.DEVICEID=D.ID JOIN RMK R ON R.ID=RD.RMKID";

        internal static string BackupDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EsmTspiotTool", "frontol-backups"); }
        }

        public FrontolDatabase(FrontolSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            _settings = settings;
        }

        public List<FrontolDevice> ReadDevices(CancellationToken token)
        {
            string where = " WHERE R.CODE=" + Number(_settings.Workplace) +
                " AND D.TYPEDEV=1 AND D.ISFOLDER=0";
            // ponytail: properties of the supported ATOL driver fit in 6000 characters;
            // larger vendor formats fail before writing, instead of truncating a BLOB.
            string output = RunSql(
                "ROLLBACK;\nSET TRANSACTION READ ONLY;\n" +
                "SELECT E.ID AS ECR_ID,D.ID AS DEVICE_ID,D.CODE AS DEVICE_CODE," +
                "D.CHNG AS CHANGE_STAMP,D.NAME AS DEVICE_NAME," +
                "CASE WHEN E.ESMHOST IS NULL THEN 1 ELSE 0 END AS HOST_IS_NULL," +
                "E.ESMHOST,E.ESMPORT,CAST(D.CONNECTIONSTRING AS VARCHAR(6000)) AS DRIVER_JSON" +
                EquipmentJoin + where + " ORDER BY D.CODE;\n" +
                "SELECT COUNT(*) AS KRS_TOTAL" + EquipmentJoin + where + ";\nROLLBACK;\n", token);
            var devices = new List<FrontolDevice>();
            string[] rows = Regex.Split(output, @"(?m)^\s*ECR_ID[ \t]+");
            for (int index = 1; index < rows.Length; index++)
            {
                string row = "ECR_ID " + rows[index];
                Match json = Regex.Match(row, @"(?s)DRIVER_JSON[ \t]+(\{.*\})");
                if (!json.Success)
                    throw new InvalidOperationException("Frontol: свойства ККТ не содержат JSON драйвера АТОЛ.");
                string port = ReadField(row, "ESMPORT");
                devices.Add(new FrontolDevice {
                    Id = ReadInteger(row, "ECR_ID"),
                    DeviceId = ReadInteger(row, "DEVICE_ID"),
                    Code = ReadInteger(row, "DEVICE_CODE"),
                    ChangeStamp = ReadInteger(row, "CHANGE_STAMP"),
                    Name = ReadField(row, "DEVICE_NAME"),
                    Host = ReadInteger(row, "HOST_IS_NULL") == 1 ? null : ReadField(row, "ESMHOST"),
                    Port = port == "<null>" ? (int?)null : ReadInteger(row, "ESMPORT"),
                    ComPort = FrontolConfiguration.ReadComPort(json.Groups[1].Value)
                });
            }
            if (devices.Count != ReadInteger(output, "KRS_TOTAL"))
                throw new InvalidOperationException("Frontol: не удалось прочитать все настройки ККТ. Запись запрещена.");
            return devices;
        }

        public string Apply(IList<FrontolChange> plan, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            List<FrontolChange> changes = plan.Where(c =>
                !string.Equals(c.OldHost, c.NewHost, StringComparison.Ordinal) || c.OldPort != c.NewPort).ToList();
            if (changes.Count == 0)
            {
                VerifySettings(plan, false, token);
                return string.Empty;
            }
            string block = BuildWriteBlock(changes, false);
            string directory = BackupDirectory;
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "frontol-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
                "-" + Guid.NewGuid().ToString("N") + ".json");
            var backup = new FrontolBackup { Database = _settings.Database, Workplace = _settings.Workplace, Changes = changes };
            byte[] content = Encoding.UTF8.GetBytes(JsonHelper.Serialize(backup));
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(content, 0, content.Length);
                file.Flush(true);
            }
            try
            {
                ApplyBlock(block, changes, token);
                VerifySettings(plan, false, CancellationToken.None);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new InvalidOperationException(ex.Message + " Снимок прежних настроек: " + path, ex);
            }
            return path;
        }

        public void Restore(string path, CancellationToken token)
        {
            if (new FileInfo(path).Length > 1024 * 1024)
                throw new InvalidOperationException("Слишком большой файл отката Frontol.");
            FrontolBackup backup = JsonHelper.Deserialize<FrontolBackup>(File.ReadAllText(path, Encoding.UTF8));
            if (backup == null || !string.Equals(backup.Database, _settings.Database, StringComparison.OrdinalIgnoreCase) ||
                backup.Workplace != _settings.Workplace)
                throw new InvalidOperationException("Этот снимок относится к другой базе или РМ Frontol. Откат отменён.");
            ApplyBlock(BuildWriteBlock(backup.Changes, true), backup.Changes, token);
            VerifySettings(backup.Changes, true, CancellationToken.None);
        }

        private void ApplyBlock(string block, IList<FrontolChange> changes, CancellationToken token)
        {
            // Cancellation is accepted before this short atomic phase, not between
            // COMMIT and its readback: the caller must receive the actual outcome.
            token.ThrowIfCancellationRequested();
            string output = RunSql("ROLLBACK;\nSET TRANSACTION SNAPSHOT NO WAIT;\n" + block + "\nCOMMIT;\n", CancellationToken.None);
            if (ReadInteger(output, "KRS_APPLIED") != changes.Count)
                throw new InvalidOperationException("Настройки Frontol изменились после чтения. Вся запись отменена; перечитайте базу.");
        }

        private void VerifySettings(IList<FrontolChange> changes, bool restore, CancellationToken token)
        {
            List<FrontolDevice> actual = ReadDevices(token);
            foreach (FrontolChange change in changes)
            {
                FrontolDevice device = actual.SingleOrDefault(d => d.Id == change.Device.Id && d.DeviceId == change.Device.DeviceId);
                string host = restore ? change.OldHost : change.NewHost;
                int? port = restore ? change.OldPort : change.NewPort;
                if (device == null || device.Host != host || device.Port != port)
                    throw new InvalidOperationException("Контрольное чтение Frontol не подтвердило ожидаемые адреса и порты. Перечитайте базу; прежние значения можно восстановить из снимка.");
            }
        }

        internal string BuildWriteBlock(IList<FrontolChange> changes, bool restore)
        {
            if (changes == null || changes.Count == 0 ||
                changes.Any(c => c == null || c.Device == null || c.Device.Id < 1 || c.Device.DeviceId < 1 ||
                    string.IsNullOrEmpty(c.NewHost) || c.NewHost.Length > 256 || c.NewHost.IndexOf('\0') >= 0 ||
                    (c.OldHost != null && (c.OldHost.Length > 256 || c.OldHost.IndexOf('\0') >= 0)) ||
                    c.NewPort < 1 || c.NewPort > 65535) ||
                changes.Select(c => c.Device.Id).Distinct().Count() != changes.Count)
                throw new InvalidOperationException("Некорректный план изменения настроек Frontol.");
            var guards = new List<string>();
            var updates = new StringBuilder();
            foreach (FrontolChange change in changes)
            {
                string expectedHost = restore ? change.NewHost : change.OldHost;
                int? expectedPort = restore ? change.NewPort : change.OldPort;
                string targetHost = restore ? change.OldHost : change.NewHost;
                int? targetPort = restore ? change.OldPort : change.NewPort;
                guards.Add("SINGULAR(SELECT E.ID" + EquipmentJoin +
                    " WHERE R.CODE=" + Number(_settings.Workplace) +
                    " AND E.ID=" + Number(change.Device.Id) +
                    " AND D.ID=" + Number(change.Device.DeviceId) +
                    " AND D.CHNG=" + Number(change.Device.ChangeStamp) +
                    " AND D.TYPEDEV=1 AND D.ISFOLDER=0" +
                    " AND E.ESMHOST IS NOT DISTINCT FROM " + Quote(expectedHost) +
                    " AND E.ESMPORT IS NOT DISTINCT FROM " + Number(expectedPort) + ")");
                updates.Append("UPDATE ECRDEV SET ESMHOST=").Append(Quote(targetHost))
                    .Append(",ESMPORT=").Append(Number(targetPort))
                    .Append(" WHERE ID=").Append(Number(change.Device.Id)).AppendLine(";");
            }
            // One guarded statement in SNAPSHOT: a stale row prevents the whole batch.
            // Any SQL/lock failure rolls back this entire block.
            return "SET TERM ^;\nEXECUTE BLOCK RETURNS (KRS_APPLIED INTEGER) AS\nBEGIN\nKRS_APPLIED=0;\nIF (" +
                string.Join(" AND ", guards) + ") THEN BEGIN\n" + updates +
                "KRS_APPLIED=" + Number(changes.Count) + ";\nEND\nSUSPEND;\nEND^\nSET TERM ;^\n";
        }

        internal string RunSql(string sql, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string inputPath = Path.Combine(Path.GetTempPath(), "krs-frontol-" + Guid.NewGuid().ToString("N") + ".sql");
            var start = new ProcessStartInfo {
                FileName = FindIsql(),
                Arguments = "-b -q -m -nod -n -ch UTF8 -i \"" + inputPath + "\"",
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            // Credentials exist only in this child's environment, not argv or files.
            start.EnvironmentVariables["ISC_USER"] = _settings.User;
            start.EnvironmentVariables["ISC_PASSWORD"] = "masterkey";
            try
            {
                // -i avoids the BOM .NET Framework may prepend to redirected stdin.
                // Keep the secret-free script read-shared, not writable, until isql exits.
                using (var file = new FileStream(inputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var process = new Process { StartInfo = start })
                {
                    string script = "CONNECT " + Quote(_settings.Database) + ";\nSET LIST ON;\n" + sql + "\nQUIT;\n";
                    byte[] input = Encoding.UTF8.GetBytes(script);
                    file.Write(input, 0, input.Length);
                    file.Flush();
                    process.Start();
                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();
                    try
                    {
                        var watch = Stopwatch.StartNew();
                        while (!process.WaitForExit(200))
                        {
                            token.ThrowIfCancellationRequested();
                            if (watch.Elapsed > TimeSpan.FromSeconds(30))
                                throw new TimeoutException("Firebird не ответил за 30 секунд. Результат записи нужно проверить повторным чтением.");
                        }
                        string text = output.GetAwaiter().GetResult();
                        string errors = error.GetAwaiter().GetResult();
                        if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(errors))
                            throw new InvalidOperationException("Firebird не выполнил запрос (код " + process.ExitCode +
                                "). Проверьте доступ к базе, стандартный пароль SYSDBA и отсутствие блокировок.");
                        return text;
                    }
                    finally
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                            process.WaitForExit(2000);
                        }
                    }
                }
            }
            finally
            {
                try { File.Delete(inputPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static string FindIsql()
        {
            foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (RegistryKey instances = machine.OpenSubKey(@"SOFTWARE\Firebird Project\Firebird Server\Instances"))
                {
                    string directory = instances == null ? null : instances.GetValue("DefaultInstance") as string;
                    if (string.IsNullOrWhiteSpace(directory)) continue;
                    foreach (string relative in new[] { @"bin\isql.exe", "isql.exe" })
                    {
                        string path = Path.Combine(directory, relative);
                        if (File.Exists(path)) return Path.GetFullPath(path);
                    }
                }
            }
            throw new InvalidOperationException("Не найдена isql.exe штатного Firebird. Проверьте установку Firebird, поставляемого с Frontol.");
        }

        internal static int ReadInteger(string output, string field)
        {
            int value;
            if (!int.TryParse(ReadField(output, field), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new InvalidOperationException("Firebird вернул некорректное поле " + field + ".");
            return value;
        }

        private static string ReadField(string output, string field)
        {
            Match match = Regex.Match(output, @"(?m)^[ \t]*" + Regex.Escape(field) + @"[ \t]+([^\r\n]*)");
            if (!match.Success)
                throw new InvalidOperationException("Firebird не вернул поле " + field + ". Запись не подтверждена.");
            return match.Groups[1].Value.Trim();
        }

        private static string Quote(string value)
        {
            return value == null ? "NULL" : "'" + value.Replace("'", "''") + "'";
        }

        private static string Number(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "NULL";
        }
    }
}
