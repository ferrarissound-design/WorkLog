using System.Globalization;
using System.Text;
using WorkLog.Core;
using WorkLog.Storage;

namespace WorkLog.UI;

/// <summary>期間を指定して「日付,プロジェクト,分数」の CSV を書き出す。</summary>
internal sealed class CsvExportForm : Form
{
    private readonly Tracker _tracker;
    private readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Short, Width = 130 };
    private readonly DateTimePicker _to = new() { Format = DateTimePickerFormat.Short, Width = 130 };

    public CsvExportForm(Tracker tracker, Icon icon)
    {
        _tracker = tracker;
        Text = "WorkLog - CSV出力";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _to.Value = DateTime.Today;
        _from.Value = DateTime.Today.AddDays(-6);

        var flow = new FlowLayoutPanel { AutoSize = true, Padding = new Padding(12), FlowDirection = FlowDirection.TopDown };
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.AddRange(new Control[] { new Label { Text = "期間", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _from, new Label { Text = "〜", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _to });
        var note = new Label
        {
            Text = "列：日付, プロジェクト, 分数（小数1桁）\nExcel で開けるよう UTF-8（BOM付き）で保存します。",
            AutoSize = true, ForeColor = ChartStyle.SubTextColor, Padding = new Padding(0, 6, 0, 6),
        };
        var btns = new FlowLayoutPanel { AutoSize = true };
        var export = new Button { Text = "書き出す…", AutoSize = true };
        var close = new Button { Text = "閉じる", AutoSize = true, DialogResult = DialogResult.Cancel };
        export.Click += (_, _) => Export();
        btns.Controls.AddRange(new Control[] { export, close });
        flow.Controls.AddRange(new Control[] { row, note, btns });
        Controls.Add(flow);
        CancelButton = close;
    }

    private void Export()
    {
        var from = DateOnly.FromDateTime(_from.Value);
        var to = DateOnly.FromDateTime(_to.Value);
        if (from > to) (from, to) = (to, from);
        if (to.DayNumber - from.DayNumber > 3660)
        {
            MessageBox.Show(this, "期間は10年以内にしてください。", "WorkLog");
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Filter = "CSV ファイル (*.csv)|*.csv",
            FileName = $"WorkLog_{from:yyyyMMdd}-{to:yyyyMMdd}.csv",
            OverwritePrompt = true, // 既存ファイルへの上書きは Windows の確認ダイアログを通す
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("日付,プロジェクト,分数");
            int rows = 0;
            foreach (var (date, entries) in Stats.EntriesForRange(from, to, _tracker))
            {
                foreach (var (project, total) in Stats.ByProject(entries))
                {
                    sb.Append(date.ToString("yyyy-MM-dd")).Append(',')
                      .Append(Csv(project)).Append(',')
                      .AppendLine(total.TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture));
                    rows++;
                }
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            MessageBox.Show(this, $"{rows} 行を書き出しました。\n{dlg.FileName}", "WorkLog");
        }
        catch (Exception ex)
        {
            ErrorLog.Write("CSV 出力に失敗", ex);
            MessageBox.Show(this, "書き出しに失敗しました：" + ex.Message, "WorkLog", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string Csv(string s) =>
        s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
