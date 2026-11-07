using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Ui = AntdUI;

namespace MultiKKT.AntdPreview
{
    internal sealed class PreviewForm : Ui.BaseForm
    {
        private enum DemoStage { Empty, Found, Lm, Done }
        private static readonly Color Ink = Color.FromArgb(22, 39, 66);
        private static readonly Color Muted = Color.FromArgb(69, 85, 108);
        private static readonly Color Blue = Color.FromArgb(23, 83, 183);
        private static readonly Color Green = Color.FromArgb(25, 116, 64);
        private static readonly Color Line = Color.FromArgb(187, 201, 220);
        private readonly Ui.Segmented _navigation = new Ui.Segmented();
        private readonly Ui.Segmented _mode = new Ui.Segmented();
        private readonly Ui.Panel[] _stepCards = new Ui.Panel[3];
        private readonly Ui.Button[] _stepIcons = new Ui.Button[3];
        private readonly Label[] _stepTitles = new Label[3];
        private readonly Ui.Table _table = new Ui.Table();
        private readonly Ui.Panel _card = new Ui.Panel();
        private readonly System.Windows.Forms.Panel _body = new System.Windows.Forms.Panel();
        private readonly TableLayoutPanel _stack = Column();
        private readonly Ui.Panel _details = new Ui.Panel();
        private readonly Ui.Panel _empty = new Ui.Panel();
        private readonly Label _heading = TextLabel("", 18, true);
        private readonly Label _description = TextLabel("", 10, false, Muted);
        private readonly Label _selectedLabel = TextLabel("", 10.5F, true);
        private readonly Label _status = TextLabel("Демонстрационные данные · без подключения к оборудованию", 9, false, Muted);
        private readonly Ui.Alert _notice = new Ui.Alert();
        private readonly Ui.Button _primary = Button("Начать настройку", true);
        private readonly Ui.Button _secondary = Button("Настроить Frontol");
        private readonly Ui.Button _operations = Button("Отдельные операции");
        private readonly Ui.Segmented _placement = new Ui.Segmented();
        private readonly Ui.Input _host = Input("192.168.1.10");
        private readonly Ui.Input _port = Input("5995");
        private readonly Ui.Input _login = Input("admin");
        private readonly Ui.Input _password = Input("admin");
        private readonly Ui.Input _serial = Input("");
        private readonly Ui.Input _fn = Input("7300000000000001");
        private readonly Ui.Input _esmPort = Input("50402");
        private readonly Ui.Input _softPort = Input("51402");
        private readonly Row[] _rows = { new Row(1, "COM7"), new Row(2, "COM8") };
        private readonly string[] _args;
        private DemoStage _stage;
        private int _selected = 1;
        private bool _busy;
        private bool _rendering;

        internal PreviewForm(string[] args)
        {
            _args = args;
            Text = "MultiKKT — предпросмотр 02";
            FormBorderStyle = FormBorderStyle.None;
            Font = new Font("Segoe UI", 10.5F);
            ForeColor = Ink;
            BackColor = Color.FromArgb(229, 236, 246);
            ClientSize = new Size(1000, 740);
            Size = new Size(1000, 760);
            MinimumSize = new Size(920, 680);
            StartPosition = FormStartPosition.CenterScreen;
            Ui.Config.IsLight = true;
            Ui.Config.Animation = false;
            BuildLayout();
            Render();
            Shown += async delegate
            {
                if (_args.Length == 2 && _args[0] == "--capture")
                {
                    try { await CaptureExamplesAsync(_args[1]); }
                    catch (Exception error) { File.WriteAllText(Path.Combine(_args[1], "failure.txt"), error.ToString()); Environment.ExitCode = 1; }
                    Close();
                }
            };
        }

        private bool Manual { get { return _mode.SelectIndex == 1; } }
        private Row Selected { get { return _rows[_selected]; } }

        private void BuildLayout()
        {
            var shell = Column();
            shell.Dock = DockStyle.Fill;
            shell.AutoSize = false;
            shell.RowCount = 5;
            foreach (float height in new[] { 42F, 54F, 42F }) shell.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            var header = new Ui.PageHeader { Text = "MultiKKT", SubText = "Предпросмотр 02 · ККТ и ЛМ ЧЗ",
                ShowButton = true, ShowIcon = true, IconSvg = "ShopOutlined", Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 14, FontStyle.Bold), SubFont = new Font("Segoe UI", 10),
                Padding = new Padding(20, 0, 8, 0), BackColor = Color.FromArgb(213, 226, 245),
                ForeColor = Ink, UseSystemStyleColor = true, UseForeColorDrawIcons = true };
            shell.Controls.Add(header, 0, 0);
            shell.Controls.Add(BuildConnection(), 0, 1);

            ConfigureSegments(_navigation, "Настройка ККТ", "Кассы и ЛМ ЧЗ", "Журнал");
            _navigation.Items[0].IconSvg = "SettingOutlined";
            _navigation.Items[1].IconSvg = "DesktopOutlined";
            _navigation.Items[2].IconSvg = "FileTextOutlined";
            _navigation.BackColor = BackColor;
            _navigation.BackActive = Blue;
            _navigation.ForeActive = Color.White;
            _navigation.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
            _navigation.Margin = new Padding(20, 2, 20, 0);
            _navigation.Dock = DockStyle.Left;
            _navigation.Width = 520;
            _navigation.SelectIndexChanged += delegate { Render(); };
            shell.Controls.Add(_navigation, 0, 2);

            _card.Back = Color.White;
            _card.BackColor = Color.White;
            _card.Radius = 12;
            _card.BorderWidth = 1;
            _card.BorderColor = Line;
            _card.Shadow = 3;
            _card.ShadowOpacity = 0.10F;
            _card.Dock = DockStyle.Fill;
            _card.Margin = new Padding(20, 10, 20, 0);
            _card.Padding = new Padding(20, 14, 20, 12);
            var content = Column();
            content.Dock = DockStyle.Fill;
            content.AutoSize = false;
            content.BackColor = Color.White;
            content.RowCount = 6;
            foreach (float height in new[] { 52F, 40F, 50F }) content.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            content.Controls.Add(BuildSteps(), 0, 0);

            var modes = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            modes.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350));
            modes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ConfigureSegments(_mode, "Автоматически", "По одной кассе");
            _mode.Items[0].IconSvg = "ThunderboltOutlined";
            _mode.Items[1].IconSvg = "SelectOutlined";
            _mode.Margin = new Padding(0, 1, 10, 4);
            _mode.Dock = DockStyle.Fill;
            _mode.SelectIndexChanged += delegate { if (!_rendering) Render(); };
            modes.Controls.Add(_mode, 0, 0);
            var fmu = new Ui.Checkbox { Text = "Пропустить контроллеры (FMU-API)", Dock = DockStyle.Right, Width = 330,
                Font = new Font("Segoe UI", 9.5F), ForeColor = Muted };
            fmu.CheckedChanged += delegate { _notice.Text = fmu.Checked
                ? "В этом режиме пропускаются только контроллеры и привязка. FMU-API настраивается отдельно." : NoticeText(); };
            modes.Controls.Add(fmu, 1, 0);
            content.Controls.Add(modes, 0, 1);

            var title = Column();
            title.Dock = DockStyle.Fill;
            title.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            title.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            title.Controls.Add(_heading, 0, 0);
            title.Controls.Add(_description, 0, 1);
            content.Controls.Add(title, 0, 2);

            _body.Dock = DockStyle.Fill;
            _body.BackColor = Color.White;
            _stack.BackColor = Color.White;
            _body.AutoScroll = true;
            _stack.Dock = DockStyle.Top;
            _body.Controls.Add(_stack);
            content.Controls.Add(_body, 0, 3);
            BuildTable();
            BuildEmpty();
            _details.Back = Color.FromArgb(234, 242, 255);
            _details.BackColor = Color.FromArgb(234, 242, 255);
            _details.Radius = 10;
            _details.BorderWidth = 1.5F;
            _details.BorderColor = Color.FromArgb(100, 145, 215);
            _details.Dock = DockStyle.Top;
            _details.Padding = new Padding(12, 8, 12, 6);
            _details.Margin = new Padding(0, 6, 0, 4);
            ConfigureSegments(_placement, "На этом компьютере", "На другом компьютере");
            _placement.SelectIndexChanged += delegate { if (!_rendering) RenderDetails(); };
            _password.UseSystemPasswordChar = true;
            _host.TextChanged += delegate { ValidateRemote(); };
            _port.TextChanged += delegate { ValidateRemote(); };

            _notice.Dock = DockStyle.Fill;
            _notice.Radius = 8;
            _notice.Icon = Ui.TType.Info;
            _notice.Font = new Font("Segoe UI", 9.5F);
            _notice.Margin = new Padding(0, 2, 0, 2);
            content.Controls.Add(_notice, 0, 4);
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _operations.Dock = DockStyle.Left;
            _operations.Width = 188;
            _operations.Ghost = true;
            _operations.Type = Ui.TTypeMini.Primary;
            _operations.IconSvg = "RightOutlined";
            _operations.Click += delegate { Info("Служебные операции", "В основной программе здесь будут ручной ввод, восстановление и удаление компонентов."); };
            actions.Controls.Add(_operations, 0, 0);
            var right = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            _secondary.Width = 162;
            _primary.Width = 288;
            _secondary.Click += delegate
            {
                if (_stage == DemoStage.Lm) { Selected.Deferred = true; _stage = DemoStage.Found; Render(); }
                else if (_stage == DemoStage.Found || _stage == DemoStage.Done) ResetDemo();
                else Info("Настройка Frontol", "Это демонстрация интерфейса. База Frontol не открывается и не изменяется.");
            };
            _primary.Click += async delegate { await AdvanceAsync(); };
            right.Controls.Add(_secondary);
            right.Controls.Add(_primary);
            actions.Controls.Add(right, 1, 0);
            content.Controls.Add(actions, 0, 5);
            _card.Controls.Add(content);
            shell.Controls.Add(_card, 0, 3);
            shell.Controls.Add(BuildPreviewFooter(), 0, 4);
            Controls.Add(shell);
        }

        private Control BuildSteps()
        {
            var steps = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
            string[] titles = { "Найти кассы", "Зарегистрировать", "Настроить ЛМ ЧЗ" };
            for (int index = 0; index < 3; index++)
            {
                steps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
                var card = new Ui.Panel { Dock = DockStyle.Fill, Radius = 8, BorderWidth = 1,
                    Padding = new Padding(9, 4, 8, 4), Margin = new Padding(0, 0, index == 2 ? 0 : 8, 6) };
                var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                var icon = new Ui.Button { Width = 30, Height = 30, Radius = 15, BorderWidth = 0,
                    Type = Ui.TTypeMini.Primary,
                    Font = new Font("Segoe UI", 12, FontStyle.Bold), WaveSize = 0, TabStop = false,
                    Cursor = Cursors.Default, Margin = new Padding(0, 2, 0, 0) };
                var title = TextLabel(titles[index], 11, true);
                row.Controls.Add(icon, 0, 0);
                row.Controls.Add(title, 1, 0);
                card.Controls.Add(row);
                steps.Controls.Add(card, index, 0);
                _stepCards[index] = card;
                _stepIcons[index] = icon;
                _stepTitles[index] = title;
            }
            return steps;
        }

        private void UpdateSteps()
        {
            int current = _stage == DemoStage.Empty ? 0 : _stage == DemoStage.Found ? 1 : 2;
            for (int index = 0; index < 3; index++)
            {
                bool complete = index < current || _stage == DemoStage.Done;
                bool active = index == current && !complete;
                Color back = complete ? Color.FromArgb(230, 244, 235) : active
                    ? Color.FromArgb(215, 232, 255) : Color.FromArgb(236, 240, 246);
                _stepCards[index].Back = _stepCards[index].BackColor = back;
                _stepCards[index].BorderColor = complete ? Color.FromArgb(143, 190, 158) : active ? Blue : Line;
                _stepTitles[index].ForeColor = complete ? Green : active ? Ink : Muted;
                _stepIcons[index].BackColor = complete ? Green : active ? Blue : Color.FromArgb(202, 212, 227);
                _stepIcons[index].ForeColor = complete || active ? Color.White : Muted;
                _stepIcons[index].Text = complete ? string.Empty : (index + 1).ToString();
                _stepIcons[index].IconSvg = complete ? "CheckOutlined" : null;
            }
        }

        private Control BuildConnection()
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, ColumnCount = 5,
                Padding = new Padding(24, 7, 20, 7), Margin = Padding.Empty };
            foreach (float width in new[] { 48F, -1F, 124F, 100F, 120F })
                row.ColumnStyles.Add(width < 0 ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.Absolute, width));
            row.Controls.Add(TextLabel("ЕСМ", 10, true), 0, 0);
            var address = Input("http://127.0.0.1:51077");
            address.PrefixSvg = "LinkOutlined";
            row.Controls.Add(address, 1, 0);
            row.Controls.Add(new Ui.Badge { Text = "Демо: доступен", State = Ui.TState.Success,
                Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5F), Margin = new Padding(8, 0, 0, 0) }, 2, 0);
            var check = Button("Проверить");
            check.Click += delegate { Info("Пробный интерфейс", "Проверка соединения в этом прототипе не выполняется."); };
            check.Dock = DockStyle.Fill;
            row.Controls.Add(check, 3, 0);
            var settings = Button("Параметры");
            settings.IconSvg = "SettingOutlined";
            settings.Dock = DockStyle.Fill;
            settings.Click += delegate { Info("Параметры подключения", "В основной программе здесь находятся dkktPort и параметры ЕСМ. В этом прототипе подключения нет."); };
            row.Controls.Add(settings, 4, 0);
            return row;
        }

        private Control BuildPreviewFooter()
        {
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3,
                Padding = new Padding(22, 0, 20, 0), Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 224));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            footer.Controls.Add(_status, 0, 0);
            var examples = new Ui.Select { Name = "demo-examples", Dock = DockStyle.Fill, Radius = 6, Font = new Font("Segoe UI", 9),
                List = true, PlaceholderText = "Открыть пример…", Margin = new Padding(0, 3, 8, 0), PrefixSvg = "EyeOutlined" };
            examples.Items.AddRange(new object[] { "Пример: начало", "Пример: найдены кассы", "Пример: удалённый ЛМ", "Пример: готово" });
            examples.SelectedIndexChanged += delegate
            {
                if (examples.SelectedIndex < 0) return;
                ShowExample(examples.SelectedIndex);
                examples.SelectedIndex = -1;
            };
            footer.Controls.Add(examples, 1, 0);
            var help = Button("Справка");
            help.Font = new Font("Segoe UI", 9);
            help.Ghost = true;
            help.Dock = DockStyle.Fill;
            help.Margin = Padding.Empty;
            help.Click += delegate
            {
                var menu = new ContextMenuStrip();
                menu.Items.Add("О прототипе", null, delegate { Info("MultiKKT — предпросмотр 02", "AntdUI 2.4.18 · Windows 10/11\r\nРазработчик: Руслан Керусов\r\nИздатель и владелец: KRS"); });
                menu.Items.Add("Поддержать разработку", null, delegate { using (var dialog = new EsmTspiot.WinForms.Shared.SupportDevelopmentDialog()) dialog.ShowDialog(this); });
                menu.Closed += delegate { menu.Dispose(); };
                menu.Show(help, new Point(0, -60));
            };
            footer.Controls.Add(help, 2, 0);
            return footer;
        }

        private void BuildTable()
        {
            _table.Dock = DockStyle.Top;
            _table.Height = 112;
            _table.Margin = Padding.Empty;
            _table.Radius = 8;
            _table.Bordered = false;
            _table.AutoSizeColumnsMode = Ui.ColumnsMode.Fill;
            _table.RowHeight = 40;
            _table.RowHeightHeader = 36;
            _table.Font = new Font("Segoe UI", 10.5F);
            _table.ColumnFont = new Font("Segoe UI", 10.5F, FontStyle.Bold);
            _table.ForeColor = Ink;
            _table.RowSelectedBg = Color.FromArgb(201, 222, 254);
            _table.RowSelectedFore = Ink;
            _table.Gap = 12;
            _table.LostFocusClearSelection = false;
            _table.Columns = new Ui.ColumnCollection {
                new Ui.Column("Kkt", "ККТ · серийный номер").SetWidthFill().SetStyle(new Ui.Table.CellStyleInfo { FontBold = true }),
                new Ui.Column("Inn", "ИНН").SetWidth(136),
                new Ui.Column("Com", "COM", Ui.ColumnAlign.Center).SetWidth(72),
                new Ui.Column("Registration", "Регистрация").SetWidth(208),
                new Ui.Column("Lm", "ЛМ ЧЗ").SetWidth(150) };
            foreach (Ui.Column column in _table.Columns)
                column.SetColStyle(Color.FromArgb(219, 229, 243), Ink);
            _table.CellClick += delegate(object sender, Ui.TableClickEventArgs e)
            {
                var row = e.Record as Row;
                if (_busy || row == null) return;
                _selected = row.Number - 1;
                if (Manual && _stage != DemoStage.Done) _stage = row.Registered ? DemoStage.Lm : DemoStage.Found;
                Render();
            };
        }

        private void BuildEmpty()
        {
            _empty.Dock = DockStyle.Top;
            _empty.Height = 226;
            _empty.Back = Color.FromArgb(247, 250, 255);
            _empty.BackColor = Color.FromArgb(247, 250, 255);
            _empty.Radius = 12;
            _empty.BorderColor = Color.FromArgb(225, 235, 251);
            _empty.BorderWidth = 1;
            _empty.Padding = new Padding(28, 22, 28, 20);
            var stack = Column();
            stack.Dock = DockStyle.Fill;
            stack.BackColor = Color.FromArgb(247, 250, 255);
            var title = TextLabel("Подключите кассы — остальное по шагам", 16, true);
            title.Height = 34;
            var subtitle = TextLabel("Программа найдёт USB/VCOM-кассы и предложит дальнейшие действия.", 10.5F, false, Muted);
            subtitle.Height = 30;
            var prep = TextLabel("Перед началом", 10.5F, true);
            prep.Height = 32;
            var checklist = TextLabel("Подключите ККТ по USB/VCOM.\r\nЗакройте Frontol и тест драйвера АТОЛ.\r\nПодготовьте официальный MSI, если ЛМ будет на этом компьютере.", 10.5F, false, Muted);
            checklist.Height = 84;
            foreach (Control control in new Control[] { title, subtitle, prep, checklist }) stack.Controls.Add(control);
            _empty.Controls.Add(stack);
        }

        private void Render()
        {
            if (IsDisposed) return;
            _rendering = true;
            SuspendLayout();
            try
            {
                _stack.Controls.Clear();
                UpdateSteps();
                _mode.Enabled = !_busy;
                _primary.Enabled = !_busy;
                _secondary.Enabled = !_busy;
                _table.Enabled = !_busy;
                _primary.IconSvg = _stage == DemoStage.Empty ? "SearchOutlined" : _stage == DemoStage.Done ? "CheckOutlined" : "ArrowRightOutlined";
                _secondary.IconSvg = _stage == DemoStage.Lm ? null : _stage == DemoStage.Empty ? "ApiOutlined" : "ReloadOutlined";
                _secondary.Text = _stage == DemoStage.Lm ? "Отложить" : _stage == DemoStage.Empty ? "Настроить Frontol" : "Повторить поиск";
                _primary.Text = _stage == DemoStage.Empty ? Manual ? "Найти ККТ" : "Начать настройку"
                    : _stage == DemoStage.Found ? "Зарегистрировать ККТ " + Selected.Number
                    : _stage == DemoStage.Lm ? Manual ? "Настроить ЛМ для ККТ " + Selected.Number : "Настроить ЛМ для 2 ККТ"
                    : "Изменить настройки ЛМ";
                _heading.Text = _stage == DemoStage.Empty ? Manual ? "Настройка по одной кассе" : "Настройка всех подключённых касс"
                    : _stage == DemoStage.Found ? "Выберите кассу для настройки"
                    : _stage == DemoStage.Lm ? "Где будет работать ЛМ ЧЗ?" : "Параметры успешно применены";
                _description.Text = _stage == DemoStage.Empty ? Manual ? "Сначала найдём кассы. Регистрацию вы запустите для выбранной строки." : "Поиск и регистрация выполнятся автоматически. Затем выберем размещение ЛМ."
                    : _stage == DemoStage.Found ? "Найдено 2 ККТ. Нажмите на нужную строку — её параметры появятся ниже."
                    : _stage == DemoStage.Lm ? Manual ? "ККТ уже зарегистрирована. Настраивается только выбранная касса." : "Регистрация сохранена. Обе кассы одного ИНН используют общий ЛМ."
                    : "Регистрация и параметры сохранены. Frontol и диагностика доступны отдельно.";

                if (_navigation.SelectIndex == 2)
                {
                    _heading.Text = "Журнал выполнения";
                    _description.Text = "Пример отображения этапов. Все записи демонстрационные.";
                    var log = TextLabel("12:30:01   Поиск завершён: найдены ККТ 1 и ККТ 2\r\n\r\n12:30:05   ККТ 2 · регистрация подтверждена\r\n\r\n12:30:12   Параметры подключения ЛМ приняты\r\n\r\nГотовность продажи проверяется отдельно.", 11, false, Muted);
                    log.Height = 220;
                    _stack.Controls.Add(log);
                    _primary.Visible = _secondary.Visible = false;
                }
                else
                {
                    _primary.Visible = _secondary.Visible = true;
                    if (_stage == DemoStage.Empty) _stack.Controls.Add(_empty);
                    else
                    {
                        _table.Height = 118;
                        _table.DataSource = _rows;
                        _table.SelectedIndex = _selected + 1;
                        _stack.Controls.Add(_table);
                        _stack.Controls.Add(_details);
                        RenderDetails();
                    }
                    if (_navigation.SelectIndex == 1)
                    {
                        _heading.Text = "Кассы и ЛМ ЧЗ";
                        _description.Text = "Регистрация, размещение ЛМ и параметры выбранной кассы в одном списке.";
                    }
                }
                _notice.Icon = _stage == DemoStage.Done ? Ui.TType.Success : Ui.TType.Info;
                _notice.Text = NoticeText();
            }
            finally { _rendering = false; ResumeLayout(true); }
            ValidateRemote();
        }

        private void RenderDetails()
        {
            _details.Controls.Clear();
            var fields = Column();
            fields.Dock = DockStyle.Fill;
            _selectedLabel.Text = _stage == DemoStage.Lm
                ? "ИНН 1234567894  ·  " + (Manual ? "ККТ " + Selected.Number : "ККТ 1 и ККТ 2")
                : "Выбрана ККТ " + Selected.Number + "  ·  " + Selected.Com;
            _selectedLabel.Height = 26;
            fields.Controls.Add(_selectedLabel);
            if (_stage == DemoStage.Lm)
            {
                _details.Height = _placement.SelectIndex == 1 ? 152 : 138;
                _placement.Height = 38;
                _placement.Dock = DockStyle.Top;
                fields.Controls.Add(_placement);
                if (_placement.SelectIndex == 1) fields.Controls.Add(FieldRow("Адрес ЛМ", _host, "Порт", _port));
                else
                {
                    var local = TextLabel("ЛМ установит программа. Адрес и свободный порт назначаются автоматически.", 9.5F, false, Muted);
                    local.Height = 28;
                    fields.Controls.Add(local);
                }
                fields.Controls.Add(FieldRow("Логин", _login, "Пароль", _password));
            }
            else
            {
                _details.Height = 110;
                _serial.Text = Selected.Serial;
                _serial.ReadOnly = _fn.ReadOnly = true;
                _esmPort.ReadOnly = _softPort.ReadOnly = Selected.Registered;
                fields.Controls.Add(FieldRow("Серийный №", _serial, "ФН", _fn));
                fields.Controls.Add(FieldRow("Порт ЕСМ", _esmPort, "Порт ПО", _softPort));
            }
            _details.Controls.Add(fields);
            ValidateRemote();
        }

        private string NoticeText()
        {
            return _stage == DemoStage.Empty ? "Официальный MSI понадобится позже — только для установки локальных ЛМ."
                : _stage == DemoStage.Found ? "Будет зарегистрирована только ККТ " + Selected.Number + ". Затем откроется выбор размещения ЛМ."
                : _stage == DemoStage.Lm ? "Повторная регистрация не требуется. Изменения применятся после нажатия кнопки."
                : "Параметры применены. Инициализация ЛМ и проверка продажи выполняются отдельно.";
        }

        private void ValidateRemote()
        {
            if (_rendering || _stage != DemoStage.Lm) return;
            int port;
            bool valid = _placement.SelectIndex == 0 || (!string.IsNullOrWhiteSpace(_host.Text) &&
                int.TryParse(_port.Text, out port) && port >= 1024 && port <= 65535);
            _port.Status = valid ? Ui.TType.None : Ui.TType.Error;
            _primary.Enabled = !_busy && valid;
            _notice.Icon = valid ? Ui.TType.Info : Ui.TType.Error;
            _notice.Text = valid ? NoticeText() : "Укажите адрес ЛМ и допустимый порт от 1024 до 65535.";
        }

        private async Task AdvanceAsync()
        {
            if (_busy) return;
            if (_stage == DemoStage.Done) { _stage = DemoStage.Lm; Render(); return; }
            _busy = true;
            _primary.Loading = true;
            Render();
            await Task.Delay(450);
            if (IsDisposed) return;
            if (_stage == DemoStage.Empty)
            {
                if (Manual) _stage = DemoStage.Found;
                else { foreach (Row row in _rows) row.Registered = true; _stage = DemoStage.Lm; }
            }
            else if (_stage == DemoStage.Found) { Selected.Registered = true; _stage = DemoStage.Lm; }
            else if (_stage == DemoStage.Lm)
            {
                if (Manual) Selected.Applied = true;
                else foreach (Row row in _rows) row.Applied = true;
                _stage = DemoStage.Done;
            }
            _busy = false;
            _primary.Loading = false;
            Render();
        }

        private void ResetDemo()
        {
            foreach (Row row in _rows) { row.Registered = row.Applied = row.Deferred = false; }
            _stage = DemoStage.Empty;
            _navigation.SelectIndex = 0;
            Render();
        }

        private void ShowExample(int index)
        {
            if (_busy) return;
            _rendering = true;
            _navigation.SelectIndex = 0;
            _mode.SelectIndex = index == 0 ? 0 : 1;
            _placement.SelectIndex = index == 2 ? 1 : 0;
            foreach (Row row in _rows) { row.Registered = row.Applied = row.Deferred = false; }
            _stage = (DemoStage)Math.Max(0, index);
            if (index >= 2) Selected.Registered = true;
            if (index == 3) Selected.Applied = true;
            _rendering = false;
            Render();
        }

        private async Task CaptureExamplesAsync(string directory)
        {
            Directory.CreateDirectory(directory);
            for (int index = 0; index < 4; index++)
            {
                ShowExample(index);
                await Task.Delay(180);
                if (index == 2)
                {
                    var fieldBounds = _body.RectangleToClient(_password.Parent.RectangleToScreen(_password.Bounds));
                    if (!_body.ClientRectangle.Contains(fieldBounds)) throw new InvalidOperationException("Remote credentials are clipped in the default window.");
                }
                SaveScreenshot(Path.Combine(directory, "0" + (index + 1) + new[] { "-start.png", "-manual.png", "-remote-lm.png", "-done.png" }[index]));
            }
            ShowExample(2);
            _port.Text = "70000";
            if (_primary.Enabled) throw new InvalidOperationException("Invalid port must block application.");
            _port.Text = "5995";
            if (!_primary.Enabled) throw new InvalidOperationException("Valid port must enable application.");
            ShowExample(1);
            _selected = 0;
            await AdvanceAsync();
            if (!_rows[0].Registered || _rows[1].Registered || _stage != DemoStage.Lm)
                throw new InvalidOperationException("Selecting and registering a demo row must reach the LM editor.");
            _mode.SelectIndex = 0;
            ResetDemo();
            await AdvanceAsync();
            if (_stage != DemoStage.Lm || !_rows[0].Registered || !_rows[1].Registered)
                throw new InvalidOperationException("The automatic demo must reach the shared LM step.");
            Size = new Size(920, 680);
            await Task.Delay(100);
            var bounds = RectangleToClient(_primary.Parent.RectangleToScreen(_primary.Bounds));
            if (!ClientRectangle.Contains(bounds)) throw new InvalidOperationException("The primary action is clipped at minimum size.");
            SaveScreenshot(Path.Combine(directory, "05-small-window.png"));
            File.WriteAllText(Path.Combine(directory, "checks.txt"), "PASS: startup, four states, remote port validation, manual/automatic transitions, minimum size. No equipment APIs referenced.");
        }

        private void SaveScreenshot(string path)
        {
            using (var bitmap = new Bitmap(Width, Height))
            { DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(path); }
        }

        private void Info(string title, string text) { MessageBox.Show(this, text, title, MessageBoxButtons.OK, MessageBoxIcon.Information); }

        private static TableLayoutPanel Column()
        {
            var column = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
            column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return column;
        }
        private static Label TextLabel(string text, float size, bool bold = false, Color? color = null)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color ?? Ink, BackColor = Color.Transparent, Margin = Padding.Empty };
        }
        private static Ui.Button Button(string text, bool primary = false)
        {
            return new Ui.Button { Text = text, Height = 42, Radius = 7,
                Font = new Font("Segoe UI", primary ? 11 : 10.5F, primary ? FontStyle.Bold : FontStyle.Regular),
                BackColor = primary ? Blue : (Color?)null,
                Type = primary ? Ui.TTypeMini.Primary : Ui.TTypeMini.Default, WaveSize = 1,
                Margin = new Padding(4, 4, 0, 0), BorderWidth = primary ? 0 : 1 };
        }
        private static Ui.Input Input(string value)
        {
            return new Ui.Input { Text = value, Dock = DockStyle.Fill, Radius = 6, BorderWidth = 1,
                BorderColor = Line, ForeColor = Ink,
                Font = new Font("Segoe UI", 10.5F), Margin = new Padding(0, 2, 8, 2) };
        }
        private static void ConfigureSegments(Ui.Segmented control, params string[] values)
        {
            foreach (string value in values) control.Items.Add(new Ui.SegmentedItem { Text = value });
            control.SelectIndex = 0;
            control.Full = true;
            control.Radius = 7;
            control.Gap = 3;
            control.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
            control.IconAlign = Ui.TAlignMini.Left;
            control.BackColor = Color.FromArgb(226, 233, 244);
            control.BackActive = Blue;
            control.ForeColor = Muted;
            control.ForeActive = Color.White;
        }
        private static Control FieldRow(string left, Ui.Input leftInput, string right, Ui.Input rightInput)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Top, Height = 36, ColumnCount = 4, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            row.Controls.Add(TextLabel(left, 10, false, Muted), 0, 0);
            row.Controls.Add(leftInput, 1, 0);
            row.Controls.Add(TextLabel(right, 10, false, Muted), 2, 0);
            row.Controls.Add(rightInput, 3, 0);
            return row;
        }

        private sealed class Row
        {
            internal Row(int number, string com) { Number = number; Com = com; }
            public int Number { get; private set; }
            public string Serial { get { return "0010570000000" + Number; } }
            public string Kkt { get { return "ККТ " + Number + " · " + Serial; } }
            public string Inn { get { return "1234567894"; } }
            public string Com { get; private set; }
            internal bool Registered, Applied, Deferred;
            public Ui.CellTag Registration { get { return new Ui.CellTag(Registered ? "Зарегистрирована" : "Не зарегистрирована", Registered ? Ui.TTypeMini.Success : Ui.TTypeMini.Warn) {
                Back = Registered ? Color.FromArgb(219, 243, 228) : Color.FromArgb(255, 237, 191),
                Fore = Registered ? Green : Color.FromArgb(117, 70, 0), BorderWidth = 0 }; } }
            public Ui.CellTag Lm { get { return new Ui.CellTag(Applied ? "Применено" : Deferred ? "Отложено" : "Не настроен", Applied ? Ui.TTypeMini.Success : Deferred ? Ui.TTypeMini.Warn : Ui.TTypeMini.Default) {
                Back = Applied ? Color.FromArgb(219, 243, 228) : Deferred ? Color.FromArgb(255, 237, 191) : Color.FromArgb(224, 231, 241),
                Fore = Applied ? Green : Deferred ? Color.FromArgb(117, 70, 0) : Muted, BorderWidth = 0 }; } }
        }
    }
}
