using System.Windows.Input;
using EqWikiOverlay.Core;

namespace EqWikiOverlay.Tests;

public class HoldHotkeyTests
{
    private static uint Vk(Key k) => (uint)KeyInterop.VirtualKeyFromKey(k);

    [Fact]
    public void Parse_ShiftA()
    {
        Assert.Equal($"{Vk(Key.A)}|Shift", HoldHotkey.DescribeParse("Shift+A"));
    }

    [Fact]
    public void Parse_CtrlC()
    {
        Assert.Equal($"{Vk(Key.C)}|Ctrl", HoldHotkey.DescribeParse("Ctrl+C"));
    }

    [Fact]
    public void Parse_CtrlShiftI()
    {
        // Flags render in enum order (Shift=1, Ctrl=2); "Ctrl+Shift+I" -> Shift, Ctrl.
        Assert.Equal($"{Vk(Key.I)}|Shift, Ctrl", HoldHotkey.DescribeParse("Ctrl+Shift+I"));
    }

    [Fact]
    public void Parse_AltF1()
    {
        Assert.Equal($"{Vk(Key.F1)}|Alt", HoldHotkey.DescribeParse("Alt+F1"));
    }

    [Fact]
    public void Parse_NoModifierBareKey()
    {
        Assert.Equal($"{Vk(Key.G)}|None", HoldHotkey.DescribeParse("G"));
    }
}
