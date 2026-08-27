using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EsmTspiot.Shared.Logging;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;
using EsmTspiot.Shared.Validation;

namespace EsmTspiot.WinForms.Shared
{
    public sealed partial class LmGatewayPage : UserControl
    {
        private readonly Func<string> _baseUrlProvider;
        private readonly Action<string> _logger;
        private readonly LmGatewayDiscoveryWorkflow _discoveryWorkflow;
        private readonly LmGatewayBindingWorkflow _bindingWorkflow;
        private readonly LmGatewayBindingSession _session = new LmGatewayBindingSession();
        private readonly Dictionary<string, LmGatewayCredentials> _credentials =
            new Dictionary<string, LmGatewayCredentials>(StringComparer.Ordinal);
        private readonly DataGridView _grid = new DataGridView();
        private readonly Button _refreshButton = new Button();
        private readonly Button _saveDraftButton = new Button();
        private readonly Button _bindButton = new Button();
        private readonly Button _cancelButton = new Button();
        private readonly TextBox _addressTextBox = new TextBox();
        private readonly TextBox _portTextBox = new TextBox();
        private readonly TextBox _loginTextBox = new TextBox();
        private readonly TextBox _passwordTextBox = new TextBox();
        private readonly GroupBox _editorGroup = new GroupBox();
        private readonly Label _statusLabel = new Label();
        private CancellationTokenSource _cancellation;
        private bool _running;
        private bool _hostBusy;
        private bool _loadingGrid;
        private bool _hasLoaded;

        public LmGatewayPage(
            Func<string> baseUrlProvider,
            ITspiotApiClient apiClient,
            Action<string> logger)
        {
            if (baseUrlProvider == null)
            {
                throw new ArgumentNullException("baseUrlProvider");
            }
            if (apiClient == null)
            {
                throw new ArgumentNullException("apiClient");
            }

            _baseUrlProvider = baseUrlProvider;
            _logger = logger;
            _discoveryWorkflow = new LmGatewayDiscoveryWorkflow(apiClient);
            _bindingWorkflow = new LmGatewayBindingWorkflow(apiClient);

            Dock = DockStyle.Fill;
            AutoScroll = false;
            BuildLayout();
            UpdateActionState();
        }

        public event Action<bool> OperationStateChanged;

        public void SetHostBusy(bool busy)
        {
            _hostBusy = busy;
            UpdateActionState();
        }

        public Task RefreshIfNeededAsync()
        {
            return _hasLoaded ? Task.FromResult(0) : RefreshAsync();
        }

        public Task RefreshAsync()
        {
            return RunOperationAsync(RefreshCoreAsync, "Получение зарегистрированных ККТ из ЕСМ...");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_cancellation != null)
                {
                    _cancellation.Cancel();
                    _cancellation.Dispose();
                    _cancellation = null;
                }
                ClearAllCredentials();
            }

            base.Dispose(disposing);
        }

        private async Task RunOperationAsync(
            Func<CancellationToken, Task> operation,
            string startMessage)
        {
            if (_running || _hostBusy || operation == null)
            {
                return;
            }

            _running = true;
            _cancellation = new CancellationTokenSource();
            _statusLabel.Text = startMessage;
            RaiseOperationStateChanged(true);
            UpdateActionState();
            try
            {
                await operation(_cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed && !Disposing)
                {
                    _statusLabel.Text = "Операция отменена.";
                    Log("Операция на вкладке контроллеров ЛМ отменена.\r\n\r\n");
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                {
                    string message = SensitiveDataMasker.Mask(ex.Message);
                    _statusLabel.Text = "Ошибка: " + message;
                    Log("Ошибка на вкладке контроллеров ЛМ: " + message + "\r\n\r\n");
                    MessageBox.Show(this, message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                if (_cancellation != null)
                {
                    _cancellation.Dispose();
                    _cancellation = null;
                }
                _running = false;
                RaiseOperationStateChanged(false);
                UpdateActionState();
            }
        }

        private async Task RefreshCoreAsync(CancellationToken cancellationToken)
        {
            string baseUrl = ValidateAndReadBaseUrl();
            LmGatewayDiscovery discovery = await _discoveryWorkflow.DiscoverAsync(
                baseUrl,
                ReportDiscoveryProgress,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (!discovery.IsSuccessful)
            {
                throw new InvalidOperationException(discovery.ErrorMessage);
            }

            _session.ReplaceDiscovery(discovery);
            ClearCredentialsForInvalidatedRows();
            RemoveCredentialsForMissingRows();
            FillRows(null);
            _hasLoaded = true;
            _statusLabel.Text = discovery.Items.Count == 0
                ? "Зарегистрированные ККТ для привязки не найдены."
                : "Загружено ККТ: " + discovery.Items.Count.ToString() +
                    ". Текущая привязка не проверена: документированный read-back отсутствует.";

            StringBuilder log = new StringBuilder();
            log.AppendLine("=== ККТ для ручной привязки к контроллерам ЛМ ===");
            log.AppendLine("Зарегистрированных ККТ: " + discovery.Items.Count.ToString() + ".");
            for (int index = 0; index < discovery.Issues.Count; index++)
            {
                LmGatewayDiscoveryIssue issue = discovery.Issues[index];
                log.AppendLine("Пропущен экземпляр " + (issue.InstanceId ?? string.Empty) + ": " +
                    (issue.Message ?? string.Empty));
            }
            log.AppendLine();
            Log(log.ToString());
        }

        private Task BindSelectedAsync()
        {
            if (_running || _hostBusy)
            {
                return Task.FromResult(0);
            }

            SaveEditorWithoutChangingSelection();
            LmGatewayBindingPlan plan = _session.BuildSelectedPlan();
            if (plan.Items.Count == 0)
            {
                MessageBox.Show(this, "Выберите хотя бы одну ККТ и сохраните её параметры.",
                    "Привязка к ЕСМ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return Task.FromResult(0);
            }

            StringBuilder confirmation = new StringBuilder();
            confirmation.AppendLine("Будет выполнен только документированный запрос привязки ЕСМ.");
            confirmation.AppendLine("Windows-службы и профили контроллеров не изменяются.");
            confirmation.AppendLine();
            int executableCount = 0;
            for (int index = 0; index < plan.Items.Count; index++)
            {
                LmGatewayBindingItem item = plan.Items[index];
                bool hasCredentials = HasCompleteCredentials(item.Kkt.KktSerial);
                bool executable = item.IsValid && hasCredentials;
                if (executable)
                {
                    executableCount++;
                }

                confirmation.Append(item.Kkt.KktSerial);
                confirmation.Append(" / ИНН ");
                confirmation.Append(item.Kkt.KktInn);
                confirmation.Append(" → ");
                confirmation.Append(item.Input.ControllerAddress);
                confirmation.Append(":");
                confirmation.Append(item.Input.ControllerGrpcPort);
                confirmation.Append(" — ");
                if (!item.IsValid)
                {
                    confirmation.Append(item.Validation.JoinMessages().Replace("\r\n", "; "));
                }
                else if (!hasCredentials)
                {
                    confirmation.Append("логин/пароль не заданы, строка будет пропущена");
                }
                else
                {
                    confirmation.Append("готово");
                }
                confirmation.AppendLine();
            }

            if (executableCount == 0)
            {
                MessageBox.Show(this, confirmation.ToString(), "Нет готовых строк",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return Task.FromResult(0);
            }

            confirmation.AppendLine();
            confirmation.AppendLine("Ответ 2xx означает только «запрос принят», а не проверенную фактическую настройку.");
            if (MessageBox.Show(this, confirmation.ToString(), "Подтвердите привязку к ЕСМ",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return Task.FromResult(0);
            }

            return RunOperationAsync(
                delegate(CancellationToken token) { return ExecuteBindingAsync(plan, token); },
                "Выполняется последовательная привязка ККТ к контроллерам ЛМ...");
        }

        private async Task ExecuteBindingAsync(
            LmGatewayBindingPlan plan,
            CancellationToken cancellationToken)
        {
            string baseUrl = ValidateAndReadBaseUrl();
            Log("=== Ручная привязка ККТ к готовым контроллерам ЛМ ===\r\n");
            try
            {
                LmGatewayBindingOutcome outcome = await _bindingWorkflow.ExecuteAsync(
                    baseUrl,
                    plan,
                    GetCredentials,
                    ReportBindingProgress,
                    cancellationToken);

                if (IsDisposed || Disposing)
                {
                    return;
                }

                _session.ApplyOutcome(outcome);
                FillRows(null);
                int accepted = 0;
                int attention = 0;
                int failed = 0;
                for (int index = 0; index < outcome.Results.Count; index++)
                {
                    LmGatewayBindingResult result = outcome.Results[index];
                    if (result.Status == LmGatewayBindingStatus.BindingAccepted)
                    {
                        accepted++;
                    }
                    else if (result.Status == LmGatewayBindingStatus.RequiresAttention)
                    {
                        attention++;
                    }
                    else
                    {
                        failed++;
                    }
                    Log(result.KktSerial + ": " + GetBindingStatusText(result.Status) +
                        ". " + (result.Details ?? string.Empty) + "\r\n");
                }

                _statusLabel.Text = "Запрос принят: " + accepted.ToString() +
                    "; требуется проверка: " + attention.ToString() +
                    "; не выполнено/ошибка: " + failed.ToString() + ".";
                Log(_statusLabel.Text + "\r\n\r\n");
            }
            finally
            {
                ClearCredentialsForPlan(plan);
                if (!IsDisposed && !Disposing)
                {
                    _loginTextBox.Clear();
                    _passwordTextBox.Clear();
                }
            }
        }

        private string ValidateAndReadBaseUrl()
        {
            string baseUrl = (_baseUrlProvider() ?? string.Empty).Trim();
            ValidationResult validation = TspiotInputValidator.ValidateForCheck(
                new TspiotFormInput { BaseUrl = baseUrl });
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(validation.JoinMessages());
            }
            if (validation.Warnings.Count > 0 && MessageBox.Show(this,
                validation.JoinWarnings() + "\r\n\r\nПродолжить?", "Проверьте адрес ЕСМ",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                throw new OperationCanceledException();
            }

            return baseUrl;
        }

        private void SaveSelectedDraft()
        {
            LmGatewayBindingSessionRow row = GetSelectedSessionRow();
            if (row == null || row.Kkt == null)
            {
                MessageBox.Show(this, "Сначала выберите строку ККТ.", "Параметры контроллера",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string serial = row.Kkt.KktSerial;
            _session.TryUpdateDraft(serial, _addressTextBox.Text, _portTextBox.Text, true);
            StoreCredentials(serial, _loginTextBox.Text, _passwordTextBox.Text);
            FillRows(serial);
            _statusLabel.Text = "Параметры ККТ " + serial +
                " сохранены только в памяти текущего сеанса. Фактическая привязка ещё не проверена.";
        }

        private void SaveEditorWithoutChangingSelection()
        {
            LmGatewayBindingSessionRow row = GetSelectedSessionRow();
            if (row == null || row.Kkt == null)
            {
                return;
            }

            _session.TryUpdateDraft(
                row.Kkt.KktSerial,
                _addressTextBox.Text,
                _portTextBox.Text,
                row.IsSelected);
            StoreCredentials(row.Kkt.KktSerial, _loginTextBox.Text, _passwordTextBox.Text);
            FillRows(row.Kkt.KktSerial);
        }

        private void StoreCredentials(string serial, string login, string password)
        {
            string key = (serial ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(key))
            {
                return;
            }
            if (string.IsNullOrEmpty(login) && string.IsNullOrEmpty(password))
            {
                _credentials.Remove(key);
                return;
            }

            _credentials[key] = new LmGatewayCredentials
            {
                Login = (login ?? string.Empty).Trim(),
                Password = password ?? string.Empty
            };
        }

        private LmGatewayCredentials GetCredentials(string serial)
        {
            LmGatewayCredentials value;
            if (!_credentials.TryGetValue((serial ?? string.Empty).Trim(), out value) || value == null)
            {
                return null;
            }

            return new LmGatewayCredentials
            {
                Login = value.Login,
                Password = value.Password
            };
        }

        private bool HasCompleteCredentials(string serial)
        {
            LmGatewayCredentials value;
            return _credentials.TryGetValue((serial ?? string.Empty).Trim(), out value) &&
                value != null &&
                !string.IsNullOrWhiteSpace(value.Login) &&
                !string.IsNullOrWhiteSpace(value.Password);
        }

        private void FillRows(string selectedSerial)
        {
            if (_loadingGrid || IsDisposed || Disposing)
            {
                return;
            }

            if (string.IsNullOrEmpty(selectedSerial))
            {
                LmGatewayBindingSessionRow selected = GetSelectedSessionRow();
                selectedSerial = selected == null || selected.Kkt == null
                    ? string.Empty
                    : selected.Kkt.KktSerial;
            }

            _loadingGrid = true;
            try
            {
                _grid.Rows.Clear();
                int rowToSelect = -1;
                for (int index = 0; index < _session.Rows.Count; index++)
                {
                    LmGatewayBindingSessionRow item = _session.Rows[index];
                    int rowIndex = _grid.Rows.Add(
                        item.IsSelected,
                        item.Kkt.KktSerial,
                        item.Kkt.KktInn,
                        string.IsNullOrWhiteSpace(item.Kkt.ServiceState) ? "Не указано" : item.Kkt.ServiceState,
                        item.ControllerAddress,
                        item.ControllerGrpcPort,
                        item.LastBindingStatus.HasValue
                            ? GetBindingStatusText(item.LastBindingStatus.Value)
                            : "Не проверено (read-back нет)",
                        item.LastMessage ?? string.Empty);
                    _grid.Rows[rowIndex].Tag = item;
                    if (item.LastBindingStatus == LmGatewayBindingStatus.BindingAccepted)
                    {
                        _grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.Honeydew;
                    }
                    else if (item.LastBindingStatus == LmGatewayBindingStatus.RequiresAttention)
                    {
                        _grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.LemonChiffon;
                    }
                    else if (item.LastBindingStatus.HasValue)
                    {
                        _grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.MistyRose;
                    }
                    if (string.Equals(item.Kkt.KktSerial, selectedSerial, StringComparison.Ordinal))
                    {
                        rowToSelect = rowIndex;
                    }
                }

                if (rowToSelect >= 0)
                {
                    _grid.Rows[rowToSelect].Selected = true;
                    _grid.CurrentCell = _grid.Rows[rowToSelect].Cells[1];
                }
                else if (_grid.Rows.Count > 0)
                {
                    _grid.Rows[0].Selected = true;
                    _grid.CurrentCell = _grid.Rows[0].Cells[1];
                }
            }
            finally
            {
                _loadingGrid = false;
            }

            LoadSelectedEditor();
            UpdateActionState();
        }

        private void LoadSelectedEditor()
        {
            if (_loadingGrid)
            {
                return;
            }

            LmGatewayBindingSessionRow row = GetSelectedSessionRow();
            bool hasRow = row != null && row.Kkt != null;
            _editorGroup.Text = hasRow
                ? "Параметры ККТ " + row.Kkt.KktSerial + " / ИНН " + row.Kkt.KktInn +
                    " — только до закрытия программы"
                : "Параметры: ККТ не выбрана — хранятся только до закрытия программы";
            _addressTextBox.Text = hasRow ? row.ControllerAddress : string.Empty;
            _portTextBox.Text = hasRow ? row.ControllerGrpcPort : string.Empty;

            LmGatewayCredentials credentials;
            if (hasRow && _credentials.TryGetValue(row.Kkt.KktSerial, out credentials) && credentials != null)
            {
                _loginTextBox.Text = credentials.Login ?? string.Empty;
                _passwordTextBox.Text = credentials.Password ?? string.Empty;
            }
            else
            {
                _loginTextBox.Clear();
                _passwordTextBox.Clear();
            }
            UpdateActionState();
        }

        private LmGatewayBindingSessionRow GetSelectedSessionRow()
        {
            if (_grid.SelectedRows.Count != 1)
            {
                return null;
            }

            return _grid.SelectedRows[0].Tag as LmGatewayBindingSessionRow;
        }

        private void OnGridCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_loadingGrid || e.RowIndex < 0 || e.ColumnIndex != 0 || e.RowIndex >= _grid.Rows.Count)
            {
                return;
            }

            LmGatewayBindingSessionRow row = _grid.Rows[e.RowIndex].Tag as LmGatewayBindingSessionRow;
            if (row == null || row.Kkt == null)
            {
                return;
            }

            bool selected = Convert.ToBoolean(_grid.Rows[e.RowIndex].Cells[0].Value);
            _session.TryUpdateDraft(
                row.Kkt.KktSerial,
                row.ControllerAddress,
                row.ControllerGrpcPort,
                selected);
            UpdateActionState();
        }

        private void ReportDiscoveryProgress(LmGatewayProgress progress)
        {
            if (progress == null)
            {
                return;
            }

            PostToUi(delegate
            {
                _statusLabel.Text = progress.Stage + " " + progress.Current.ToString() + "/" +
                    progress.Total.ToString() + ": " + progress.InstanceId;
            });
        }

        private void ReportBindingProgress(LmGatewayBindingProgress progress)
        {
            if (progress == null)
            {
                return;
            }

            PostToUi(delegate
            {
                _statusLabel.Text = progress.Stage + " " + progress.Current.ToString() + "/" +
                    progress.Total.ToString() + ": ККТ " + progress.KktSerial + ". " + progress.Message;
                if (progress.Response != null)
                {
                    Log(LogFormatter.Format(progress.Response));
                }
            });
        }

        private void CancelCurrentOperation()
        {
            if (_cancellation == null || _cancellation.IsCancellationRequested)
            {
                return;
            }

            _cancellation.Cancel();
            _statusLabel.Text = "Остановка запрошена. Потерянный ответ не будет автоматически повторён.";
        }

        private void UpdateActionState()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            bool idle = !_running && !_hostBusy;
            bool hasRow = GetSelectedSessionRow() != null;
            bool hasSelected = false;
            for (int index = 0; index < _session.Rows.Count; index++)
            {
                if (_session.Rows[index].IsSelected)
                {
                    hasSelected = true;
                    break;
                }
            }

            _refreshButton.Enabled = idle;
            _saveDraftButton.Enabled = idle && hasRow;
            _bindButton.Enabled = idle && hasSelected;
            _addressTextBox.Enabled = idle && hasRow;
            _portTextBox.Enabled = idle && hasRow;
            _loginTextBox.Enabled = idle && hasRow;
            _passwordTextBox.Enabled = idle && hasRow;
            _grid.Enabled = idle;
            _cancelButton.Visible = _running;
            _cancelButton.Enabled = _running && _cancellation != null && !_cancellation.IsCancellationRequested;
        }

        private void RemoveCredentialsForMissingRows()
        {
            HashSet<string> existing = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < _session.Rows.Count; index++)
            {
                existing.Add(_session.Rows[index].Kkt.KktSerial);
            }

            List<string> stale = new List<string>();
            foreach (string serial in _credentials.Keys)
            {
                if (!existing.Contains(serial))
                {
                    stale.Add(serial);
                }
            }
            for (int index = 0; index < stale.Count; index++)
            {
                ClearCredential(stale[index]);
            }
        }

        private void ClearCredentialsForInvalidatedRows()
        {
            for (int index = 0; index < _session.InvalidatedKktSerials.Count; index++)
            {
                ClearCredential(_session.InvalidatedKktSerials[index]);
            }
        }

        private void ClearCredentialsForPlan(LmGatewayBindingPlan plan)
        {
            if (plan == null)
            {
                return;
            }
            for (int index = 0; index < plan.Items.Count; index++)
            {
                LmGatewayBindingItem item = plan.Items[index];
                if (item != null && item.Kkt != null)
                {
                    ClearCredential(item.Kkt.KktSerial);
                }
            }
        }

        private void ClearAllCredentials()
        {
            List<string> keys = new List<string>(_credentials.Keys);
            for (int index = 0; index < keys.Count; index++)
            {
                ClearCredential(keys[index]);
            }
            _loginTextBox.Clear();
            _passwordTextBox.Clear();
        }

        private void ClearCredential(string serial)
        {
            LmGatewayCredentials value;
            if (_credentials.TryGetValue(serial, out value) && value != null)
            {
                value.Login = string.Empty;
                value.Password = string.Empty;
            }
            _credentials.Remove(serial);
        }

        private void RaiseOperationStateChanged(bool busy)
        {
            Action<bool> handler = OperationStateChanged;
            if (handler != null)
            {
                handler(busy);
            }
        }

        private void PostToUi(Action action)
        {
            if (action == null || IsDisposed || Disposing)
            {
                return;
            }

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void Log(string text)
        {
            if (_logger != null)
            {
                _logger(SensitiveDataMasker.Mask(text));
            }
        }

        private static string GetBindingStatusText(LmGatewayBindingStatus status)
        {
            if (status == LmGatewayBindingStatus.BindingAccepted)
            {
                return "Запрос принят";
            }
            if (status == LmGatewayBindingStatus.RequiresAttention)
            {
                return "Требуется проверка";
            }
            if (status == LmGatewayBindingStatus.Invalid)
            {
                return "Некорректные данные";
            }
            if (status == LmGatewayBindingStatus.Cancelled)
            {
                return "Не выполнено";
            }

            return "Ошибка привязки";
        }
    }
}
