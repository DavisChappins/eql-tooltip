using System.Drawing;
using System.Windows.Forms;

namespace EqWikiOverlay.Ui;

/// <summary>Minimal single-line text input dialog (WinForms has none built in).</summary>
internal static class Prompt
{
    public static string? Show(string text, string caption)
    {
        using var form = new Form
        {
            Width = 420,
            Height = 170,
            Text = caption,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false
        };

        var label = new Label { Left = 12, Top = 12, Width = 380, Text = text, AutoSize = false, Height = 40 };
        var input = new TextBox { Left = 12, Top = 56, Width = 380 };
        var ok = new Button { Text = "Look up", Left = 236, Width = 75, Top = 90, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 317, Width = 75, Top = 90, DialogResult = DialogResult.Cancel };

        form.Controls.Add(label);
        form.Controls.Add(input);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        return form.ShowDialog() == DialogResult.OK ? input.Text : null;
    }
}
