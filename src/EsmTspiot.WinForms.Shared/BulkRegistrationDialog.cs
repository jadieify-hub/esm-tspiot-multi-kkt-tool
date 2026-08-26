using System;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using EsmTspiot.Shared.Models;
using EsmTspiot.Shared.Services;

namespace EsmTspiot.WinForms.Shared
{
    public sealed class BulkRegistrationDialog : Form
    {
        private readonly BulkRegistrationDiscovery _discovery;
        private readonly BulkRegistrationWorkflow _workflow;
        private readonly Action<BulkRegistrationProgress> _externalProgress;
        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _statusLabel = new Label();
        private readonly ProgressBar _progressBar = new ProgressBar();
        private readonly Button _startButton = new Button();
        private readonly Button _cancelButton = new Button();
        private CancellationTokenSource _cancellation;
        private bool _running;

        public BulkRegistrationDialog(
            BulkRegistrationDiscovery discovery,
            BulkRegistrationWorkflow workflow,
            Action<BulkRegistrationProgress> externalProgress)
        {
            _discovery = discovery;
            _workflow = workflow;
            _externalProgress = externalProgress;

            Text = "Массовая регистрация ККТ";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(720, 460);
            MinimumSize = new Size(600, 360);
            Font = new Font("Segoe UI", 8.25F);
            MaximizeBox = false;

            BuildLayout();
            FillRows();
            Shown += delegate { ConstrainToWorkingArea(); };
            FormClosing += OnFormClosing;
        }

        public BulkRegistrationOutcome Outcome { get; private set; }

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(8);
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));

            ConfigureGrid();
            root.Controls.Add(_grid, 0, 0);

            _statusLabel.AutoSize = true;
            _statusLabel.Text = "Проверьте список. Некорректные строки будут пропущены.";
            _statusLabel.Margin = new Padding(0, 6, 0, 3);
            root.Controls.Add(_statusLabel, 0, 1);

            _progressBar.Dock = DockStyle.Fill;
            _progressBar.Minimum = 0;
            _progressBar.Maximum = Math.Max(1, _discovery.Items.Count);
            _progressBar.Value = 0;
            root.Controls.Add(_progressBar, 0, 2);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;

            _startButton.Text = "Начать регистрацию";
            _startButton.AutoSize = true;
            _startButton.Click += async delegate { await StartAsync(); };

            _cancelButton.Text = "Отмена";
            _cancelButton.AutoSize = true;
            _cancelButton.Click += CancelOrClose;

            buttons.Controls.Add(_cancelButton);
            buttons.Controls.Add(_startButton);
            root.Controls.Add(buttons, 0, 3);
            Controls.Add(root);
        }

        private void ConfigureGrid()
        {
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.RowHeadersVisible = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.ScrollBars = ScrollBars.Both;
            _grid.Columns.Add(CreateColumn("Действие", 170));
            _grid.Columns.Add(CreateColumn("ККТ", 120));
            _grid.Columns.Add(CreateColumn("ФН", 135));
            _grid.Columns.Add(CreateColumn("ИНН", 105));
            _grid.Columns.Add(CreateColumn("port", 65));
            _grid.Columns.Add(CreateColumn("softPort", 70));
            _grid.Columns.Add(CreateColumn("dkktPort", 70));
            _grid.Columns.Add(CreateColumn("Проверка", 260));
        }

        private DataGridViewTextBoxColumn CreateColumn(string header, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                Width = width,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

        private void FillRows()
        {
            for (int i = 0; i < _discovery.InitialResults.Count; i++)
            {
                BulkKktRegistrationResult result = _discovery.InitialResults[i];
                _grid.Rows.Add(
                    result.FormatLogLine(),
                    result.KktSerial,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    result.Details);
            }

            for (int i = 0; i < _discovery.Items.Count; i++)
            {
                BulkRegistrationWorkItem workItem = _discovery.Items[i];
                BulkKktRegistrationItem item = workItem.Item;
                string validation = item.Validation.IsValid
                    ? (item.Validation.Warnings.Count == 0 ? "Данные корректны" : item.Validation.JoinWarnings())
                    : item.Validation.JoinMessages();
                int rowIndex = _grid.Rows.Add(
                    workItem.RequiresAdd ? "Добавить и зарегистрировать" : "Завершить регистрацию",
                    item.Input.KktSerial,
                    item.Input.FnSerial,
                    item.Input.KktInn,
                    item.Input.Port,
                    item.Input.SoftPort,
                    item.Input.DkktPort,
                    validation);

                if (!item.Validation.IsValid)
                {
                    _grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.MistyRose;
                }
                else if (item.Validation.Warnings.Count > 0)
                {
                    _grid.Rows[rowIndex].DefaultCellStyle.BackColor = Color.LemonChiffon;
                }
            }
        }

        private async System.Threading.Tasks.Task StartAsync()
        {
            if (_running)
            {
                return;
            }

            _running = true;
            _cancellation = new CancellationTokenSource();
            _startButton.Enabled = false;
            _cancelButton.Text = "Остановить";
            _statusLabel.Text = "Выполняется массовая регистрация...";

            try
            {
                Outcome = await _workflow.ExecuteAsync(_discovery, UpdateProgress, _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                Outcome = Outcome ?? new BulkRegistrationOutcome();
                Outcome.Cancelled = true;
            }
            finally
            {
                _running = false;
                _cancelButton.Text = "Закрыть";
                _cancelButton.Enabled = true;
                _statusLabel.Text = Outcome != null && Outcome.Cancelled
                    ? "Операция остановлена. Уже выполненные действия не отменены."
                    : "Операция завершена. Подробности записаны в журнал.";
                _progressBar.Value = _progressBar.Maximum;
            }
        }

        private void UpdateProgress(BulkRegistrationProgress progress)
        {
            if (_externalProgress != null)
            {
                _externalProgress(progress);
            }

            int total = Math.Max(1, progress.Total);
            _progressBar.Maximum = total;
            _progressBar.Value = Math.Min(total, Math.Max(0, progress.Current));
            StringBuilder text = new StringBuilder();
            if (progress.Current > 0 && progress.Total > 0)
            {
                text.Append("ККТ ");
                text.Append(progress.Current);
                text.Append(" из ");
                text.Append(progress.Total);
                text.Append(": ");
            }
            if (!string.IsNullOrWhiteSpace(progress.KktSerial))
            {
                text.Append(progress.KktSerial);
                text.Append(" — ");
            }
            text.Append(progress.Stage);
            if (!string.IsNullOrWhiteSpace(progress.Message))
            {
                text.Append(" (");
                text.Append(progress.Message);
                text.Append(")");
            }
            _statusLabel.Text = text.ToString();
        }

        private void CancelOrClose(object sender, EventArgs e)
        {
            if (_running)
            {
                _cancellation.Cancel();
                _cancelButton.Enabled = false;
                _statusLabel.Text = "Остановка после текущего запроса...";
                return;
            }

            Close();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_running)
            {
                _cancellation.Cancel();
                e.Cancel = true;
                _statusLabel.Text = "Остановка после текущего запроса...";
            }
        }

        private void ConstrainToWorkingArea()
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int width = Math.Min(Width, Math.Max(320, area.Width - 16));
            int height = Math.Min(Height, Math.Max(280, area.Height - 16));
            MinimumSize = new Size(
                Math.Min(MinimumSize.Width, width),
                Math.Min(MinimumSize.Height, height));
            Size = new Size(width, height);
        }
    }
}
