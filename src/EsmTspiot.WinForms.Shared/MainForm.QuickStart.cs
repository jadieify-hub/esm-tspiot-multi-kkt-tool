using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Button = AntdUI.Button;

namespace EsmTspiot.WinForms.Shared
{
    public sealed partial class MainForm
    {
        internal void ShowQuickStartIfNew(string markerPath)
        {
            if (File.Exists(markerPath)) return;
            ShowQuickStart();

            string problem;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(markerPath));
                using (File.Open(markerPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
                return;
            }
            catch (IOException error) { problem = error.Message; }
            catch (UnauthorizedAccessException error) { problem = error.Message; }
            AppendLog("Быстрый старт: не удалось сохранить отметку о просмотре. " + problem + "\r\n");
        }

        private void ShowQuickStart()
        {
            using (var dialog = new Form {
                Name = "quick-start", Text = "Быстрый старт", Icon = OperatorTheme.AppIcon,
                ClientSize = new Size(640, 400), StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                ShowInTaskbar = false, Font = Font, BackColor = OperatorTheme.Surface, ForeColor = OperatorTheme.Ink })
            using (var headingFont = new Font(Font, FontStyle.Bold))
            {
                var steps = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true,
                    Padding = new Padding(20, 14, 20, 0), ColumnCount = 1 };
                steps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                AddQuickStartStep(steps, headingFont, "1. Подготовьте кассы",
                    "Установите штатные ЕСМ/ТС ПИоТ и драйвер АТОЛ. Подключите кассы по USB/VCOM. " +
                    "Если Frontol или тест драйвера занимает кассу, закройте его.");
                AddQuickStartStep(steps, headingFont, "2. Зарегистрируйте ККТ",
                    "В разделе «Настройка ККТ» нажмите «Найти и зарегистрировать кассы» для всех подключённых касс. " +
                    "Чтобы выбрать конкретную кассу, откройте «По одной кассе».");
                AddQuickStartStep(steps, headingFont, "3. Выберите, где работает ЛМ ЧЗ",
                    "Для новой установки на этом компьютере нужен официальный MSI. Если ЛМ уже работает на сервере, " +
                    "укажите его адрес и порт. В режиме FMU-API контроллеры пропускаются.");
                AddQuickStartStep(steps, headingFont, "4. Проверьте результат",
                    "В «Обзоре касс» видны зарегистрированные кассы и состояние ЛМ, в «Журнале» — подробности. " +
                    "Бизнес-инициализацию ЛМ и проверку продажи выполните отдельно.");

                var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true,
                    FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(12, 6, 12, 12) };
                var done = new Button { Text = "Понятно", DialogResult = DialogResult.OK };
                var instruction = new Button { Text = "Открыть инструкцию", DialogResult = DialogResult.Yes };
                OperatorTheme.Button(done, true);
                OperatorTheme.Button(instruction, false);
                actions.Controls.Add(done);
                actions.Controls.Add(instruction);
                dialog.Controls.Add(steps);
                dialog.Controls.Add(actions);
                dialog.AcceptButton = done;
                dialog.ActiveControl = done;
                if (dialog.ShowDialog(this) == DialogResult.Yes) OpenInstruction();
            }
        }

        private static void AddQuickStartStep(TableLayoutPanel steps, Font headingFont, string title, string body)
        {
            int row = steps.RowCount;
            steps.RowCount += 2;
            steps.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            steps.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            steps.Controls.Add(new Label { Text = title, Font = headingFont, AutoSize = true,
                Dock = DockStyle.Top, ForeColor = OperatorTheme.Blue, Margin = new Padding(0, 4, 0, 2) }, 0, row);
            steps.Controls.Add(new Label { Text = body, AutoSize = true, Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 8) }, 0, row + 1);
        }
    }
}
