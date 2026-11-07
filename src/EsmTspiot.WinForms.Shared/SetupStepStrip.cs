using System.Drawing;
using System.Windows.Forms;

namespace EsmTspiot.WinForms.Shared
{
    internal sealed class SetupStepStrip : UserControl
    {
        private readonly AntdUI.Panel[] _cards = new AntdUI.Panel[3];
        private readonly AntdUI.Button[] _icons = new AntdUI.Button[3];
        private readonly Label[] _titles = new Label[3];

        internal SetupStepStrip()
        {
            Dock = DockStyle.Fill;
            Height = 42;
            MinimumSize = new Size(0, 42);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
            string[] titles = { "Найти кассы", "Зарегистрировать", "Настроить ЛМ ЧЗ" };
            for (int index = 0; index < 3; index++)
            {
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
                var card = new AntdUI.Panel { Dock = DockStyle.Fill, Radius = 8, BorderWidth = 1,
                    Padding = new Padding(8, 4, 6, 4), Margin = new Padding(0, 0, index == 2 ? 0 : 8, 4) };
                var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                var icon = new AntdUI.Button { Width = 29, Height = 29, Radius = 15, BorderWidth = 0,
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
                Color back = complete ? Color.FromArgb(230, 244, 235) : active
                    ? attention ? Color.FromArgb(255, 237, 191) : Color.FromArgb(215, 232, 255)
                    : Color.FromArgb(236, 240, 246);
                _cards[index].Back = _cards[index].BackColor = back;
                _cards[index].BorderColor = complete ? Color.FromArgb(143, 190, 158) : active ? OperatorTheme.Blue : OperatorTheme.Line;
                _titles[index].ForeColor = complete ? OperatorTheme.Green : active ? OperatorTheme.Ink : OperatorTheme.Muted;
                _icons[index].BackColor = complete ? OperatorTheme.Green : active ? OperatorTheme.Blue : Color.FromArgb(202, 212, 227);
                _icons[index].ForeColor = complete || active ? Color.White : OperatorTheme.Muted;
                _icons[index].Text = complete ? string.Empty : (index + 1).ToString();
                _icons[index].IconSvg = complete ? "CheckOutlined" : null;
            }
        }
    }
}
