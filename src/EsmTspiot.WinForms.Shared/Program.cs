using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace EsmTspiot.WinForms.Shared
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
#if NET8_0_OR_GREATER
            ApplicationConfiguration.Initialize();
#else
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
#endif
            try
            {
                RunMainForm();
            }
            catch (FileNotFoundException ex)
            {
                ReportMissingComponent(ex.FileName, ex);
            }
            catch (FileLoadException ex)
            {
                ReportMissingComponent(ex.FileName, ex);
            }
            catch (BadImageFormatException ex)
            {
                ReportMissingComponent(ex.FileName, ex);
            }
            catch (TypeLoadException ex)
            {
                ReportMissingComponent(null, ex);
            }
        }

        // Kept out of Main on purpose. When EsmTspiot.Shared.dll is missing
        // next to the executable, the runtime fails while compiling the method
        // that creates MainForm; isolating that method lets Main catch the
        // failure and explain it instead of leaving only a KERNELBASE crash
        // record in the Windows event log.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunMainForm()
        {
            Application.Run(new MainForm());
        }

        private static void ReportMissingComponent(string fileName, Exception error)
        {
            string component = DescribeComponent(fileName);
            MessageBox.Show(
                "Программа не может запуститься: не найден компонент " + component +
                ".\r\n\r\nРядом с MultiKKT-ESM-TSPioT.exe должны лежать все файлы " +
                "поставки, включая EsmTspiot.Shared.dll и папку Provisioner. " +
                "Распакуйте архив целиком в любую папку и запустите программу из неё." +
                "\r\n\r\nПодробности: " + error.GetType().Name + ": " + error.Message,
                "MultiKKT",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Environment.ExitCode = 3;
        }

        private static string DescribeComponent(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return "(имя не определено)";
            }
            // Assembly failures report a display name such as
            // "EsmTspiot.Shared, Version=11.3.2.0, Culture=neutral, ...".
            int comma = fileName.IndexOf(',');
            string simpleName = comma > 0 ? fileName.Substring(0, comma).Trim() : fileName;
            if (simpleName.IndexOfAny(new char[] { '\\', '/', ':' }) < 0 &&
                !simpleName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                !simpleName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return simpleName + ".dll";
            }
            return simpleName;
        }
    }
}
