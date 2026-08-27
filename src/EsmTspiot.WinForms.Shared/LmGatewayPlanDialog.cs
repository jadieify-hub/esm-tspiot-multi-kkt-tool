using System;
using System.Drawing;
using System.Windows.Forms;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class LmGatewayPlanDialog : Form
    {
        private readonly LmGatewayPlan _source;
        private readonly DataGridView _grid = new DataGridView();
        private readonly Button _startButton = new Button();

        internal LmGatewayPlanDialog(
            LmGatewayPlan plan,
            Func<string, bool> hasCredentials)
        {
            _source = plan ?? throw new ArgumentNullException("plan");
            Text = "План контроллеров ЛМ ЧЗ";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(900, 480);
            MinimumSize = new Size(700, 360);
            Font = new Font("Segoe UI", 8.25F);
            BuildLayout();
            Fill(plan, hasCredentials);
        }

        internal LmGatewayPlan SelectedPlan { get; private set; }

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Выполнить", Width = 65 });
            _grid.Columns.Add(Column("ККТ", 120));
            _grid.Columns.Add(Column("ИНН", 100));
            _grid.Columns.Add(Column("Действие", 145));
            _grid.Columns.Add(Column("Роль / итог", 120));
            _grid.Columns.Add(Column("Служба", 185));
            _grid.Columns.Add(Column("gRPC / REST", 95));
            _grid.Columns.Add(Column("Целевой ЛМ", 150));
            _grid.Columns.Add(Column("Credentials", 100));
            _grid.Columns.Add(Column("Проверка", 260));
            _grid.CellValueChanged += delegate { UpdateStartState(); };
            _grid.CurrentCellDirtyStateChanged += delegate
            {
                if (_grid.IsCurrentCellDirty)
                {
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            root.Controls.Add(_grid, 0, 0);

            Label warning = new Label
            {
                AutoSize = true,
                Text = "Windows запросит повышение прав только после нажатия «Начать». Пароли в план не входят.",
                Margin = new Padding(0, 7, 0, 7)
            };
            root.Controls.Add(warning, 0, 1);

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            Button cancel = new Button { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
            _startButton.Text = "Начать";
            _startButton.AutoSize = true;
            _startButton.Click += Accept;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(_startButton);
            root.Controls.Add(buttons, 0, 2);
            AcceptButton = _startButton;
            CancelButton = cancel;
            Controls.Add(root);
        }

        private void Fill(LmGatewayPlan plan, Func<string, bool> hasCredentials)
        {
            for (int index = 0; index < plan.Items.Count; index++)
            {
                LmGatewayPlanItem item = plan.Items[index];
                bool valid = item != null && item.IsValid;
                bool credentials = valid && hasCredentials != null &&
                    hasCredentials(item.Kkt.KktSerial);
                string validation = item == null
                    ? "Строка плана отсутствует."
                    : Join(item);
                int row = _grid.Rows.Add(
                    valid && credentials,
                    item == null || item.Kkt == null ? string.Empty : item.Kkt.KktSerial,
                    item == null || item.Kkt == null ? string.Empty : item.Kkt.KktInn,
                    item == null ? LmGatewayPlanAction.Blocked.ToString() : ActionText(item.Action),
                    item == null ? "Не определена" : RoleText(item),
                    item == null || item.Spec == null ? string.Empty : item.Spec.ServiceName,
                    item == null || item.Spec == null ? string.Empty :
                        item.Spec.Ports.GrpcPort + " / " + item.Spec.Ports.RestPort,
                    item == null || item.Spec == null ? string.Empty :
                        item.Spec.Target.Address + ":" + item.Spec.Target.Port,
                    credentials ? "заданы" : "не заданы",
                    valid && credentials ? "Готово" : validation.Length == 0
                        ? "Логин/пароль не заданы."
                        : validation);
                _grid.Rows[row].Tag = item;
                if (!valid || !credentials)
                {
                    _grid.Rows[row].Cells[0].ReadOnly = true;
                    _grid.Rows[row].DefaultCellStyle.BackColor = Color.MistyRose;
                }
            }
            UpdateStartState();
        }

        private void Accept(object sender, EventArgs e)
        {
            LmGatewayPlan selected = new LmGatewayPlan();
            for (int index = 0; index < _grid.Rows.Count; index++)
            {
                LmGatewayPlanItem item = _grid.Rows[index].Tag as LmGatewayPlanItem;
                bool execute = Convert.ToBoolean(_grid.Rows[index].Cells[0].Value);
                if (item != null && item.IsValid && execute)
                {
                    selected.Items.Add(item);
                }
            }
            SelectedPlan = selected;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void UpdateStartState()
        {
            bool enabled = false;
            for (int index = 0; index < _grid.Rows.Count; index++)
            {
                if (!_grid.Rows[index].Cells[0].ReadOnly &&
                    Convert.ToBoolean(_grid.Rows[index].Cells[0].Value))
                {
                    enabled = true;
                    break;
                }
            }
            _startButton.Enabled = enabled;
        }

        private static DataGridViewTextBoxColumn Column(string text, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = text,
                Width = width,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

        private static string Join(LmGatewayPlanItem item)
        {
            string service = item.ServiceValidation.JoinMessages().Replace("\r\n", "; ");
            string binding = item.BindingValidation.JoinMessages().Replace("\r\n", "; ");
            return service.Length == 0 ? binding : binding.Length == 0 ? service : service + "; " + binding;
        }

        private static string ActionText(LmGatewayPlanAction action)
        {
            switch (action)
            {
                case LmGatewayPlanAction.CreateManagedService: return "Создать";
                case LmGatewayPlanAction.UpdateManagedService: return "Обновить";
                case LmGatewayPlanAction.StartManagedService: return "Запустить";
                case LmGatewayPlanAction.BindReadyService: return "Привязать";
                case LmGatewayPlanAction.NoChange: return "Проверить и привязать";
                default: return "Заблокировано";
            }
        }

        private static string RoleText(LmGatewayPlanItem item)
        {
            if (item == null || item.Action == LmGatewayPlanAction.Blocked)
            {
                return "Не определена";
            }
            return item.Action == LmGatewayPlanAction.CreateManagedService
                ? "Не создан → управляемый"
                : "Управляемый";
        }
    }
}
