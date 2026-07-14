using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using EqWikiOverlay.Core;

namespace EqWikiOverlay.Ui;

/// <summary>System-tray presence and menu for the app.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Settings _settings;
    private readonly App _app;
    private readonly NotifyIcon _icon;

    public TrayIcon(Settings settings, App app)
    {
        _settings = settings;
        _app = app;

        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => RebuildMenu(menu); // refresh labels (hotkey, checks) each open
        RebuildMenu(menu);

        _icon = new NotifyIcon
        {
            Icon = BuildIcon(),
            Text = "EQ Wiki Overlay",
            Visible = false,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => PromptTestLookup();
    }

    public void Show() => _icon.Visible = true;

    private void RebuildMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();

        menu.Items.Add(new ToolStripMenuItem($"Look up item under cursor  ({_app.CurrentHotkey})", null,
            (_, _) => _app.TriggerHotkeyLookup()));
        menu.Items.Add(new ToolStripMenuItem("Set hotkey…", null, (_, _) => PromptSetHotkey()));
        menu.Items.Add(new ToolStripMenuItem("Test lookup by name…", null, (_, _) => PromptTestLookup()));
        menu.Items.Add(new ToolStripSeparator());

        // Display mode radio group.
        menu.Items.Add(new ToolStripMenuItem("Display mode") { Enabled = false });
        foreach (DisplayMode mode in Enum.GetValues<DisplayMode>())
        {
            var item = new ToolStripMenuItem(mode.ToString())
            {
                Checked = _settings.DisplayMode == mode,
                CheckOnClick = false
            };
            item.Click += (_, _) => _app.ChangeDisplayMode(mode);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Show OCR debug window", null, (_, _) => _app.ShowDebugWindow()));
        menu.Items.Add(new ToolStripMenuItem("Clear wiki cache", null, (_, _) => _app.ClearCache()));
        menu.Items.Add(new ToolStripMenuItem("Open settings folder", null,
            (_, _) => OpenFolder(Settings.DefaultDirectory)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => _app.ExitApp()));
    }

    private void PromptSetHotkey()
    {
        var picked = HotkeyDialog.Show(_app.CurrentHotkey);
        if (!string.IsNullOrWhiteSpace(picked))
            _app.ChangeHotkey(picked);
    }

    private void PromptTestLookup()
    {
        var name = Prompt.Show("Enter an item name to look up on the wiki:", "EQ Wiki Overlay — Test lookup");
        if (!string.IsNullOrWhiteSpace(name))
            _app.TestLookup(name.Trim());
    }

    private static void OpenFolder(string path)
    {
        try
        {
            System.IO.Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    /// <summary>Draws a simple gold "EQ" glyph icon at runtime (no asset file needed).</summary>
    private static Icon BuildIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Color.FromArgb(27, 30, 39));
            g.FillEllipse(bg, 1, 1, 30, 30);
            using var pen = new Pen(Color.FromArgb(244, 197, 66), 2);
            g.DrawEllipse(pen, 1, 1, 30, 30);
            using var font = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            using var fg = new SolidBrush(Color.FromArgb(244, 197, 66));
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("EQ", font, fg, new RectangleF(0, 0, 32, 32), sf);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
