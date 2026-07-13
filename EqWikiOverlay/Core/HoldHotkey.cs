using System.Windows.Input;
using System.Windows.Threading;

namespace EqWikiOverlay.Core;

/// <summary>
/// A hold-to-show hotkey using a low-level keyboard hook. Raises <see cref="Held"/> when the combo
/// (e.g. "Shift+A", "Ctrl+C", "Ctrl+Shift+I") is down and <see cref="Released"/> when it is no
/// longer held (main key up, or any required modifier released).
///
/// The hook does NOT swallow the keystroke — it always calls the next hook — so the key still
/// reaches EQ. Events are marshaled onto the UI dispatcher.
/// </summary>
public sealed class HoldHotkey : IDisposable
{
    [Flags]
    private enum Mods { None = 0, Shift = 1, Ctrl = 2, Alt = 4 }

    private readonly Dispatcher _dispatcher;
    private readonly uint _mainKey;
    private readonly Mods _requiredMods;
    private Native.LowLevelKeyboardProc? _proc;
    private IntPtr _hook = IntPtr.Zero;
    private bool _isHeld;

    public event Action? Held;
    public event Action? Released;

    public HoldHotkey(string hotkey, Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        (_mainKey, _requiredMods) = Parse(hotkey);
    }

    public void Start()
    {
        _proc = HookCallback;
        _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            var info = System.Runtime.InteropServices.Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);

            bool isDown = msg is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
            bool isUp = msg is Native.WM_KEYUP or Native.WM_SYSKEYUP;

            if ((isDown || isUp) && (info.vkCode == _mainKey || IsModifierVk(info.vkCode)))
            {
                // Held iff the main key is currently down AND every required modifier is down.
                bool mainDown = info.vkCode == _mainKey ? isDown : IsKeyDown((int)_mainKey);
                bool modsOk = ModsSatisfied();
                Evaluate(mainDown && modsOk);
            }
        }

        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool ModsSatisfied()
    {
        if (_requiredMods.HasFlag(Mods.Shift) && !IsKeyDown(Native.VK_SHIFT)) return false;
        if (_requiredMods.HasFlag(Mods.Ctrl) && !IsKeyDown(Native.VK_CONTROL)) return false;
        if (_requiredMods.HasFlag(Mods.Alt) && !IsKeyDown(Native.VK_MENU)) return false;
        return true;
    }

    private void Evaluate(bool shouldBeHeld)
    {
        if (shouldBeHeld && !_isHeld)
        {
            _isHeld = true;
            _dispatcher.BeginInvoke(() => Held?.Invoke());
        }
        else if (!shouldBeHeld && _isHeld)
        {
            _isHeld = false;
            _dispatcher.BeginInvoke(() => Released?.Invoke());
        }
    }

    private static bool IsKeyDown(int vk) => (Native.GetKeyState(vk) & 0x8000) != 0;

    private static bool IsModifierVk(uint vk) =>
        vk is Native.VK_SHIFT or Native.VK_LSHIFT or Native.VK_RSHIFT
            or Native.VK_CONTROL or Native.VK_MENU
            or 0xA2 or 0xA3   // L/R Ctrl
            or 0xA4 or 0xA5;  // L/R Alt

    /// <summary>Test hook: returns "vk|mods" for a hotkey string.</summary>
    internal static string DescribeParse(string hotkey)
    {
        var (vk, mods) = Parse(hotkey);
        return $"{vk}|{mods}";
    }

    /// <summary>Parses "Ctrl+Shift+I" into the main virtual key + required modifier flags.</summary>
    private static (uint mainKey, Mods mods) Parse(string hotkey)
    {
        var mods = Mods.None;
        uint main = 0;
        foreach (var raw in (hotkey ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "shift": mods |= Mods.Shift; break;
                case "ctrl" or "control": mods |= Mods.Ctrl; break;
                case "alt": mods |= Mods.Alt; break;
                default:
                    if (Enum.TryParse<Key>(raw, ignoreCase: true, out var key))
                        main = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        if (main == 0) main = (uint)'A'; // safe fallback
        return (main, mods);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _proc = null;
    }
}
