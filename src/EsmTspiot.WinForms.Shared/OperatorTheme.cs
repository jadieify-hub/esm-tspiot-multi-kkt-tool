using System;
using System.Drawing;
using System.Windows.Forms;

namespace EsmTspiot.WinForms.Shared
{
    internal static class OperatorTheme
    {
        internal static readonly Icon AppIcon;
        internal static readonly Color Ink = Color.FromArgb(31, 43, 51);
        internal static readonly Color Muted = Color.FromArgb(87, 103, 112);
        internal static readonly Color Blue = Color.FromArgb(36, 91, 115);
        internal static readonly Color Green = Color.FromArgb(32, 110, 73);
        internal static readonly Color Line = Color.FromArgb(217, 225, 229);
        internal static readonly Color Background = Color.FromArgb(245, 247, 248);
        internal static readonly Color Surface = Color.White;
        internal static readonly Color Soft = Background;
        internal static readonly Color Selection = Color.FromArgb(234, 242, 245);
        internal static readonly Color Warning = Color.FromArgb(139, 96, 27);
        internal static readonly Color Error = Color.FromArgb(177, 62, 69);

        static OperatorTheme()
        {
            using (var stream = typeof(OperatorTheme).Assembly.GetManifestResourceStream(
                "EsmTspiot.WinForms.Shared.Assets.app.ico"))
                AppIcon = new Icon(stream);
            AntdUI.Style.SetPrimary(Blue);
            AntdUI.Style.SetInfo(Blue);
            AntdUI.Style.SetSuccess(Green);
            AntdUI.Style.SetWarning(Warning);
            AntdUI.Style.SetError(Error);
        }

        internal static void Button(AntdUI.Button button, bool primary)
        {
            button.AutoSize = true;
            button.AutoSizeMode = AntdUI.TAutoSize.Width;
            button.Radius = 4;
            button.WaveSize = 1;
            button.Font = new Font("Segoe UI", 10F, primary ? FontStyle.Bold : FontStyle.Regular);
            button.Type = primary ? AntdUI.TTypeMini.Primary : AntdUI.TTypeMini.Default;
            button.BackColor = primary ? Blue : Color.White;
            button.ForeColor = primary ? Color.White : Ink;
            button.BackHover = primary ? Blue : Selection;
            button.ForeHover = primary ? Color.White : Blue;
            button.BackActive = primary ? Blue : Selection;
            button.DefaultBorderColor = Line;
            button.BorderWidth = primary ? 0 : 1;
            button.MinimumSize = Size.Empty;
            button.Height = 36;
            button.Margin = new Padding(0, 2, 8, 2);
            button.Padding = Padding.Empty;
        }

        internal static void Segments(AntdUI.Segmented control, params string[] labels)
        {
            foreach (string label in labels) control.Items.Add(new AntdUI.SegmentedItem { Text = label });
            control.AccessibleName = string.Join(", ", labels);
            control.AccessibleDescription = "Выбор стрелками влево и вправо.";
            control.Full = true;
            control.Radius = 4;
            control.Gap = 3;
            control.IconAlign = AntdUI.TAlignMini.Left;
            control.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            control.BackColor = Background;
            control.BackActive = Blue;
            control.ForeColor = Muted;
            control.ForeActive = Color.White;
            control.SelectIndex = 0;
        }

        internal static void Dialog(Form dialog)
        {
            dialog.BackColor = Surface;
            dialog.ForeColor = Ink;
            StyleNativeControls(dialog);
        }

        private static void StyleNativeControls(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                var button = control as System.Windows.Forms.Button;
                if (button != null)
                {
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = Line;
                    button.FlatAppearance.MouseOverBackColor = Selection;
                    button.BackColor = Surface;
                    button.ForeColor = Ink;
                    button.Padding = new Padding(8, 4, 8, 4);
                    button.MinimumSize = new Size(0, 34);
                }
                var grid = control as DataGridView;
                if (grid != null) Grid(grid);
                else StyleNativeControls(control);
            }
        }

        internal static Control Input(TextBox input)
        {
            var frame = new AntdUI.Panel { Radius = 4, BorderWidth = 1, BorderColor = Line,
                Back = Color.White, BackColor = Color.White, Height = 34, MinimumSize = new Size(0, 34),
                Dock = DockStyle.Fill, Padding = new Padding(8, 5, 8, 3), Margin = input.Margin,
                TabIndex = input.TabIndex };
            input.BorderStyle = BorderStyle.None;
            input.Font = new Font("Segoe UI", 10F);
            input.Dock = DockStyle.Fill;
            input.Margin = Padding.Empty;
            input.TabIndex = 0;
            input.ForeColor = Ink;
            Action refresh = delegate
            {
                Color back = input.Enabled && !input.ReadOnly ? Surface : Soft;
                frame.Back = frame.BackColor = input.BackColor = back;
                frame.BorderColor = input.Focused ? Blue : Line;
            };
            input.Enter += delegate { refresh(); };
            input.Leave += delegate { refresh(); };
            input.EnabledChanged += delegate { refresh(); };
            input.ReadOnlyChanged += delegate { refresh(); };
            frame.Controls.Add(input);
            refresh();
            return frame;
        }

        internal static AntdUI.Panel Card(Control content, bool selected)
        {
            Color back = selected ? Soft : Surface;
            var card = new OperatorCard { Dock = DockStyle.Fill, AutoSize = content.AutoSize,
                Radius = 0, BorderWidth = selected ? 1 : 0, BorderColor = Line,
                Back = back, BackColor = back, Padding = new Padding(8), Margin = new Padding(0, 4, 0, 6) };
            content.BackColor = back;
            card.Controls.Add(content);
            return card;
        }

        internal static void Grid(DataGridView grid)
        {
            grid.EnableHeadersVisualStyles = false;
            grid.BorderStyle = BorderStyle.None;
            grid.BackgroundColor = Color.White;
            grid.GridColor = Line;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = 34;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Background;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Background;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Ink;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 4, 0);
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 9.5F);
            grid.DefaultCellStyle.ForeColor = Ink;
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.SelectionBackColor = Selection;
            grid.DefaultCellStyle.SelectionForeColor = Ink;
            grid.DefaultCellStyle.Padding = new Padding(6, 5, 6, 5);
            grid.RowTemplate.Height = 36;
            grid.CellFormatting += delegate(object sender, DataGridViewCellFormattingEventArgs e)
            {
                if (e.ColumnIndex < 0 || e.Value == null) return;
                string column = grid.Columns[e.ColumnIndex].Name;
                if (column != "Состояние" && column != "ЛМ ЧЗ" && column != "LmState" && column != "EsmLinkState") return;
                string value = e.Value.ToString();
                Color fore = Ink;
                if (value == "Зарегистрирована" || value == "Параметры применены")
                    fore = Green;
                else if (value == "Не зарегистрирована" || value == "Настройка отложена" || value == "Не установлен" || value == "Требует применения")
                    fore = Warning;
                else return;
                e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = fore;
            };
        }

        internal static void SetOrganizationCell(DataGridViewRow row, string fullName)
        {
            row.Cells["Organization"].Value = EsmTspiot.Shared.Services.KktOrganizationStore.DisplayName(fullName);
            row.Cells["Organization"].ToolTipText = fullName ?? string.Empty;
        }
    }

    internal sealed class OperatorSegments : AntdUI.Segmented
    {
        internal OperatorSegments()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.PageTabList;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Left || key == Keys.Right || key == Keys.Home || key == Keys.End || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Enabled && Items.Count > 0)
            {
                int index = SelectIndex;
                if (e.KeyCode == Keys.Left) index--;
                else if (e.KeyCode == Keys.Right) index++;
                else if (e.KeyCode == Keys.Home) index = 0;
                else if (e.KeyCode == Keys.End) index = Items.Count - 1;
                else { base.OnKeyDown(e); return; }
                SelectIndex = Math.Max(0, Math.Min(Items.Count - 1, index));
                e.Handled = e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            base.OnMouseDown(e);
        }

        protected override void OnEnter(EventArgs e) { base.OnEnter(e); Invalidate(); }
        protected override void OnLeave(EventArgs e) { base.OnLeave(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3));
        }
    }

    // AntdUI.Panel does not measure child controls for WinForms AutoSize rows.
    internal sealed class OperatorCard : AntdUI.Panel
    {
        public override Size GetPreferredSize(Size proposedSize)
        {
            if (Controls.Count == 0) return base.GetPreferredSize(proposedSize);
            // Constraining percent columns makes WinForms add unused row height.
            Size content = Controls[0].GetPreferredSize(Size.Empty);
            return new Size(content.Width + Padding.Horizontal, content.Height + Padding.Vertical);
        }
    }

    internal sealed class OperatorTabControl : TabControl
    {
        internal OperatorTabControl()
        {
            Appearance = TabAppearance.FlatButtons;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(1, 1);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x1328 && !DesignMode) { message.Result = IntPtr.Zero; return; }
            base.WndProc(ref message);
        }
    }
}
