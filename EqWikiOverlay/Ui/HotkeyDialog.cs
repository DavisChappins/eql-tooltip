using System.Windows.Forms;

namespace EqWikiOverlay.Ui;

/// <summary>
/// A capture dialog for choosing a hotkey combo. The user holds their desired modifiers and
/// presses a key; the combo (e.g. "Ctrl+Shift+I") is recorded. Returns null on cancel.
/// </summary>
internal static class HotkeyDialog
{
    public static string? Show(string current)
    {
        using var form = new Form
        {
            Width = 440,
            Height = 210,
            Text = "Set lookup hotkey",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            KeyPreview = true
        };

        var info = new Label
        {
            Left = 14, Top = 12, Width = 400, Height = 40,
            Text = "Press the combo you want to HOLD to show the overlay.\n" +
                   "e.g. Shift+A, Ctrl+C, Ctrl+Shift+I. Modifiers optional."
        };
        var display = new Label
        {
            Left = 14, Top = 60, Width = 400, Height = 34,
            Text = current, Font = new System.Drawing.Font("Segoe UI", 14, System.Drawing.FontStyle.Bold),
            BorderStyle = BorderStyle.FixedSingle, TextAlign = System.Drawing.ContentAlignment.MiddleCenter
        };
        var ok = new Button { Text = "Save", Left = 250, Width = 75, Top = 128, DialogResult = DialogResult.OK, Enabled = false };
        var cancel = new Button { Text = "Cancel", Left = 335, Width = 75, Top = 128, DialogResult = DialogResult.Cancel };

        string captured = current;

        void OnKeyDown(object? sender, KeyEventArgs e)
        {
            // Ignore pure modifier presses; wait for a real key.
            if (e.KeyCode is Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LWin or Keys.RWin)
                return;

            var parts = new List<string>();
            if (e.Control) parts.Add("Ctrl");
            if (e.Shift) parts.Add("Shift");
            if (e.Alt) parts.Add("Alt");

            var keyName = NormalizeKey(e.KeyCode);
            if (keyName is null)
                return;

            parts.Add(keyName);
            captured = string.Join("+", parts);
            display.Text = captured;
            ok.Enabled = true;
            e.SuppressKeyPress = true;
            e.Handled = true;
        }

        form.KeyDown += OnKeyDown;
        form.Controls.Add(info);
        form.Controls.Add(display);
        form.Controls.Add(ok);
        form.Controls.Add(cancel);
        form.CancelButton = cancel;

        return form.ShowDialog() == DialogResult.OK ? captured : null;
    }

    /// <summary>Maps a WinForms Keys value to a WPF-parseable key name, or null if unsupported.</summary>
    private static string? NormalizeKey(Keys key)
    {
        // Letters A-Z
        if (key is >= Keys.A and <= Keys.Z)
            return key.ToString();
        // Top-row digits D0-D9 -> "D0".."D9" (WPF Key enum uses the same names)
        if (key is >= Keys.D0 and <= Keys.D9)
            return key.ToString();
        // Numpad
        if (key is >= Keys.NumPad0 and <= Keys.NumPad9)
            return key.ToString();
        // Function keys
        if (key is >= Keys.F1 and <= Keys.F24)
            return key.ToString();

        return key switch
        {
            Keys.Oemtilde => "OemTilde",
            Keys.OemMinus => "OemMinus",
            Keys.Oemplus => "OemPlus",
            Keys.OemOpenBrackets => "OemOpenBrackets",
            Keys.OemCloseBrackets => "OemCloseBrackets",
            Keys.OemQuestion => "OemQuestion",
            Keys.OemPeriod => "OemPeriod",
            Keys.Oemcomma => "OemComma",
            Keys.OemSemicolon => "OemSemicolon",
            Keys.Space => "Space",
            Keys.Tab => "Tab",
            Keys.OemBackslash or Keys.OemPipe => "OemBackslash",
            _ => null
        };
    }
}
