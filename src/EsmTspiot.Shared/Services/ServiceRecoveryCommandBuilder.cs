using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace EsmTspiot.Shared.Services
{
    public static class ServiceRecoveryCommandBuilder
    {
        public const string ServiceNamePrefix =
            EsmInstanceServiceIdentity.Prefix;

        private static readonly string[] ControlModuleCandidates =
        {
            @"C:\Program Files\ESP\ESM\bin\controlModule.exe",
            @"C:\Program Files (x86)\ESP\ESM\bin\controlModule.exe"
        };

        public static bool IsManualServiceRecoveryError(string responseBody)
        {
            return TspiotErrorDecoder.ContainsErrorCode(responseBody, 1012) ||
                TspiotErrorDecoder.ContainsErrorCode(responseBody, 1013);
        }

        public static string BuildServiceName(string kktSerial)
        {
            return ServiceNamePrefix + Trim(kktSerial);
        }

        public static string FindControlModulePath()
        {
            for (int i = 0; i < ControlModuleCandidates.Length; i++)
            {
                if (File.Exists(ControlModuleCandidates[i]))
                {
                    return ControlModuleCandidates[i];
                }
            }

            return string.Empty;
        }

        public static string BuildPowerShellScript(string kktSerial, string port, string softPort, string controlModulePath)
        {
            int parsedPort = ParsePort(port, "port");
            int parsedSoftPort = ParsePort(softPort, "softPort");
            string normalizedPath = string.IsNullOrWhiteSpace(controlModulePath)
                ? ControlModuleCandidates[0]
                : controlModulePath.Trim();

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("$ErrorActionPreference = \"Stop\"");
            builder.AppendLine("$kkt = \"" + EscapeDoubleQuoted(Trim(kktSerial)) + "\"");
            builder.AppendLine("$port = " + parsedPort.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("$softPort = " + parsedSoftPort.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("$exe = \"" + EscapeDoubleQuoted(normalizedPath) + "\"");
            builder.AppendLine("$serviceName = \"esm-cm-$kkt\"");
            builder.AppendLine("$stamp = Get-Date -Format \"yyyyMMdd_HHmmss\"");
            builder.AppendLine("$logPath = Join-Path $env:TEMP (\"esm_tspiot_service_recovery_{0}_{1}.log\" -f $kkt, $stamp)");
            builder.AppendLine("$transcriptStarted = $false");
            builder.AppendLine("try {");
            builder.AppendLine("    Start-Transcript -Path $logPath");
            builder.AppendLine("    $transcriptStarted = $true");
            builder.AppendLine("} catch {");
            builder.AppendLine("    Write-Host \"Не удалось включить transcript-лог: $($_.Exception.Message)\"");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine("try {");
            builder.AppendLine("Write-Host \"=== Диагностика перед восстановлением службы ===\"");
            builder.AppendLine("Write-Host \"Время: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')\"");
            builder.AppendLine("Write-Host \"Пользователь: $env:USERNAME\"");
            builder.AppendLine("Write-Host \"Служба: $serviceName\"");
            builder.AppendLine("Write-Host \"Ожидаемые параметры: --id $kkt --port $port --soft-port $softPort\"");
            builder.AppendLine("Write-Host \"controlModule.exe: $exe\"");
            builder.AppendLine("Write-Host \"Лог восстановления: $logPath\"");
            builder.AppendLine();
            builder.AppendLine("if (-not (Test-Path -LiteralPath $exe)) {");
            builder.AppendLine("    throw \"Не найден controlModule.exe: $exe\"");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine("Write-Host \"--- Службы Windows по серийнику и портам ---\"");
            builder.AppendLine("$servicePattern = [regex]::Escape($kkt)");
            builder.AppendLine("Get-WmiObject Win32_Service | Where-Object { $_.Name -match $servicePattern -or $_.DisplayName -match $servicePattern -or $_.PathName -match $servicePattern -or $_.PathName -match \"50401|51401|50402|51402|4041|4042|4043\" } | Select-Object Name, DisplayName, State, PathName | Format-List | Out-Host");
            builder.AppendLine();
            builder.AppendLine("Write-Host \"--- Порты Windows: ожидаемые и типовые конфликтующие ---\"");
            builder.AppendLine("netstat -ano | findstr \":$port :$softPort :4041 :4042 :4043 :50401 :51401\" | Out-Host");
            builder.AppendLine();
            builder.AppendLine("Write-Host \"--- Процессы controlModule / esm ---\"");
            builder.AppendLine("tasklist | findstr /I \"controlModule esm\" | Out-Host");
            builder.AppendLine();
            builder.AppendLine("$serviceArgs = \"--id $kkt --port $port --soft-port $softPort --pretty-logs=true\"");
            builder.AppendLine("$expectedId = \"--id $kkt\"");
            builder.AppendLine("$expectedPort = \"--port $port\"");
            builder.AppendLine("$expectedSoftPort = \"--soft-port $softPort\"");
            builder.AppendLine("$createService = $false");
            builder.AppendLine("$existing = Get-WmiObject Win32_Service -Filter \"Name='$serviceName'\" -ErrorAction SilentlyContinue");
            builder.AppendLine("if ($null -eq $existing) {");
            builder.AppendLine("    $createService = $true");
            builder.AppendLine("} else {");
            builder.AppendLine("    $currentPath = [string]$existing.PathName");
            builder.AppendLine("    Write-Host \"Найдена существующая служба $serviceName\"");
            builder.AppendLine("    Write-Host \"Текущая команда службы: $currentPath\"");
            builder.AppendLine("    $pathOk = $currentPath.Contains($expectedId) -and $currentPath.Contains($expectedPort) -and $currentPath.Contains($expectedSoftPort)");
            builder.AppendLine("    if (-not $pathOk) {");
            builder.AppendLine("        Write-Host \"Параметры существующей службы отличаются от ожидаемых. Служба будет пересоздана.\"");
            builder.AppendLine("        Write-Host \"Ожидалось: $expectedId $expectedPort $expectedSoftPort\"");
            builder.AppendLine("        Stop-Service $serviceName -Force -ErrorAction SilentlyContinue");
            builder.AppendLine("        Start-Sleep -Seconds 2");
            builder.AppendLine("        sc.exe delete $serviceName | Out-Host");
            builder.AppendLine("        Start-Sleep -Seconds 2");
            builder.AppendLine("        $createService = $true");
            builder.AppendLine("    } else {");
            builder.AppendLine("        Write-Host \"Параметры существующей службы совпадают с ожидаемыми.\"");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine("if ($createService) {");
            builder.AppendLine("    New-Service `");
            builder.AppendLine("        -Name $serviceName `");
            builder.AppendLine("        -BinaryPathName \"`\"$exe`\" $serviceArgs\" `");
            builder.AppendLine("        -DisplayName \"ESM: Control Module $kkt\" `");
            builder.AppendLine("        -StartupType Automatic");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine("$serviceAfterFix = Get-Service -Name $serviceName -ErrorAction Stop");
            builder.AppendLine("if ($serviceAfterFix.Status -ne \"Running\") {");
            builder.AppendLine("    Start-Service $serviceName");
            builder.AppendLine("} else {");
            builder.AppendLine("    Write-Host \"Служба $serviceName уже запущена.\"");
            builder.AppendLine("}");
            builder.AppendLine("try {");
            builder.AppendLine("    Restart-Service esm-orchestrator");
            builder.AppendLine("} catch {");
            builder.AppendLine("    Write-Host \"Не удалось перезапустить службу esm-orchestrator: $($_.Exception.Message)\"");
            builder.AppendLine("}");
            builder.AppendLine("Write-Host \"Служба $serviceName создана/запущена. Проверьте список ККТ в утилите.\"");
            builder.AppendLine("Write-Host \"--- Служба после восстановления ---\"");
            builder.AppendLine("Get-WmiObject Win32_Service -Filter \"Name='$serviceName'\" | Select-Object Name, DisplayName, State, PathName | Format-List | Out-Host");
            builder.AppendLine("Write-Host \"--- Порты после восстановления ---\"");
            builder.AppendLine("netstat -ano | findstr \":$port :$softPort :4041 :4042 :4043 :50401 :51401\" | Out-Host");
            builder.AppendLine("} finally {");
            builder.AppendLine("    Write-Host \"Лог сохранён: $logPath\"");
            builder.AppendLine("    if ($transcriptStarted) {");
            builder.AppendLine("        try { Stop-Transcript | Out-Null } catch { Write-Host \"Не удалось остановить transcript-лог: $($_.Exception.Message)\" }");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static string EscapeDoubleQuoted(string value)
        {
            return (value ?? string.Empty)
                .Replace("`", "``")
                .Replace("\"", "`\"")
                .Replace("$", "`$");
        }

        private static int ParsePort(string value, string fieldName)
        {
            int port;
            if (!int.TryParse(Trim(value), out port) || port < 1 || port > 65535)
            {
                throw new ArgumentException(
                    "Поле " + fieldName + " должно содержать целое число от 1 до 65535.",
                    fieldName);
            }

            return port;
        }

        private static string Trim(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }
}
