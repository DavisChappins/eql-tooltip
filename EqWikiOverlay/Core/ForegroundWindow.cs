using System.Diagnostics;

namespace EqWikiOverlay.Core;

/// <summary>Helpers for identifying the current foreground window's owning process.</summary>
public static class ForegroundWindow
{
    /// <summary>Process name of the foreground window (without ".exe"), or "" if unavailable.</summary>
    public static string ProcessName()
    {
        try
        {
            var hwnd = Native.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return "";
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
                return "";
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// True if the foreground window belongs to the given process. An empty <paramref name="wanted"/>
    /// disables the gate (matches anything), so clearing the setting restores fire-anywhere behavior.
    /// </summary>
    public static bool IsProcess(string wanted) => Matches(ProcessName(), wanted);

    /// <summary>Pure match used by <see cref="IsProcess"/> (foreground process vs the wanted name).</summary>
    internal static bool Matches(string foreground, string wanted) =>
        string.IsNullOrWhiteSpace(wanted) ||
        string.Equals(foreground, wanted, StringComparison.OrdinalIgnoreCase);
}
