using System;
using System.Windows.Forms;

namespace MultiKKT.AntdPreview
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Control.CheckForIllegalCrossThreadCalls = true;
            Application.Run(new PreviewForm(args));
        }
    }
}
