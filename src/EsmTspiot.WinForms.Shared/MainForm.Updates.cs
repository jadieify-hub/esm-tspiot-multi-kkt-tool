using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    public sealed partial class MainForm
    {
        private readonly ToolStripMenuItem _updateAvailableMenuItem = new ToolStripMenuItem {
            Name = "update-available", Alignment = ToolStripItemAlignment.Right,
            ForeColor = OperatorTheme.Blue, Visible = false };
        private CancellationTokenSource _updateCancellation = new CancellationTokenSource();
        private bool _updatesChecked;

        private void ConfigureUpdateNotice(MenuStrip menu)
        {
            _updateAvailableMenuItem.Click += delegate {
                try { OpenPath(ReleaseUpdateChecker.ReleasesUrl); }
                catch (Exception error)
                {
                    if (!(error is System.ComponentModel.Win32Exception) && !(error is InvalidOperationException)) throw;
                    MessageBox.Show(this, "Не удалось открыть страницу загрузки: " + error.Message,
                        "Обновление MultiKKT", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            menu.Items.Add(_updateAvailableMenuItem);
            Disposed += delegate {
                var cancellation = _updateCancellation;
                _updateCancellation = null;
                if (cancellation == null) return;
                cancellation.Cancel();
                cancellation.Dispose();
            };
        }

        internal async Task CheckForUpdatesAsync(HttpClient client)
        {
            if (_updatesChecked || IsDisposed || Disposing) return;
            _updatesChecked = true;
            try
            {
                var version = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    typeof(MainForm).Assembly, typeof(AssemblyInformationalVersionAttribute));
                string available = await new ReleaseUpdateChecker(client).GetAvailableVersionAsync(
                    version.InformationalVersion, _updateCancellation.Token);
                if (IsDisposed || Disposing || available == null) return;
                _updateAvailableMenuItem.Text = "Доступно обновление " + available;
                _updateAvailableMenuItem.ToolTipText = "Открыть официальный релиз MultiKKT в браузере";
                _updateAvailableMenuItem.Visible = true;
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed && !Disposing && !_updateCancellation.IsCancellationRequested)
                    AppendLog("Проверка обновлений: истекло время ожидания GitHub. Работа программы продолжается.\r\n");
            }
            catch (Exception error)
            {
                if (!(error is HttpRequestException) && !(error is IOException) && !(error is SerializationException)) throw;
                if (!IsDisposed && !Disposing)
                    AppendLog("Проверка обновлений не выполнена: " + error.GetType().Name + ". Работа программы продолжается.\r\n");
            }
        }
    }
}
