using System.Drawing;
using System.Windows.Forms;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class SetupStepStrip : UserControl
    {
        private readonly Panel[] _cards = new Panel[3];
        private readonly AntdUI.Button[] _icons = new AntdUI.Button[3];
        private readonly Label[] _titles = new Label[3];

        internal SetupStepStrip()
        {
            Dock = DockStyle.Fill;
            Height = 52;
            MinimumSize = new Size(0, 52);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            string[] titles = { "Найти кассы", "Зарегистрировать", "Настроить ЛМ ЧЗ" };
            for (int index = 0; index < 3; index++)
            {
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
                var card = new Panel { Dock = DockStyle.Fill,
                    Padding = new Padding(8, 8, 6, 8), Margin = Padding.Empty };
                var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
                row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                var icon = new AntdUI.Button { Width = 29, Height = 29, Radius = 15, BorderWidth = 0,
                    Anchor = AnchorStyles.Left,
                    Type = AntdUI.TTypeMini.Primary, WaveSize = 0, TabStop = false, Cursor = Cursors.Default,
                    Font = new Font("Segoe UI", 11, FontStyle.Bold), Margin = Padding.Empty };
                var title = new Label { Text = titles[index], Dock = DockStyle.Fill, AutoEllipsis = true,
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
                    BackColor = Color.Transparent, Margin = Padding.Empty };
                row.Controls.Add(icon, 0, 0);
                row.Controls.Add(title, 1, 0);
                card.Controls.Add(row);
                layout.Controls.Add(card, index, 0);
                _cards[index] = card; _icons[index] = icon; _titles[index] = title;
            }
            Controls.Add(layout);
            SetStage(1, false);
        }

        internal void SetStage(int stage, bool attention)
        {
            for (int index = 0; index < 3; index++)
            {
                bool complete = index + 1 < stage;
                bool active = index + 1 == stage;
                Color back = OperatorTheme.Surface;
                _cards[index].BackColor = back;
                _titles[index].ForeColor = complete ? OperatorTheme.Green : active ? OperatorTheme.Ink : OperatorTheme.Muted;
                _icons[index].BackColor = complete ? OperatorTheme.Green : active
                    ? attention ? OperatorTheme.Warning : OperatorTheme.Blue : OperatorTheme.Selection;
                _icons[index].ForeColor = complete || active ? Color.White : OperatorTheme.Muted;
                _icons[index].Text = complete ? string.Empty : (index + 1).ToString();
                _icons[index].IconSvg = complete ? "CheckOutlined" : null;
            }
        }
    }
}
