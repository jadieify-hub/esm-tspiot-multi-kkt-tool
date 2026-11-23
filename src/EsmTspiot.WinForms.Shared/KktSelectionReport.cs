using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace EsmTspiot.WinForms.Shared
{
    public sealed class KktSelectionReport
    {
        private readonly Label _summary;
        private readonly TextBox _report;
        private readonly Dictionary<string, string> _lastChecks = new Dictionary<string, string>(StringComparer.Ordinal);
        private string _serial;
        private string _endpoints;

        public KktSelectionReport(Label summary, TextBox report)
        {
            _summary = summary;
            _report = report;
        }

        public void Select(string serial, string lm, string controller)
        {
            if (_serial != serial)
            {
                _report.Clear();
                _report.Visible = false;
            }
            _serial = serial;
            _endpoints = "Выбрана ККТ " + serial + Environment.NewLine +
                "Выбранный ЛМ: " + lm + Environment.NewLine + "Контроллер для ЕСМ (по данным MultiKKT): " + controller;
            UpdateSummary();
        }

        public void Record(string serial, string operation, string result, string report)
        {
            _lastChecks[serial] = DateTime.Now.ToString("HH:mm:ss") + " · " + operation + " · " + result;
            if (_serial != serial) return;
            _report.Text = report;
            _report.Visible = true;
            UpdateSummary();
        }

        public void Clear()
        {
            _lastChecks.Clear();
            Select(null, null, null);
        }

        private void UpdateSummary()
        {
            _summary.Visible = !string.IsNullOrEmpty(_serial);
            if (string.IsNullOrEmpty(_serial)) { _summary.Text = string.Empty; return; }
            string last;
            _summary.Text = _endpoints + Environment.NewLine + "Последний ответ ЕСМ: " +
                (_lastChecks.TryGetValue(_serial, out last) ? last : "проверка ещё не выполнялась");
        }
    }
}
