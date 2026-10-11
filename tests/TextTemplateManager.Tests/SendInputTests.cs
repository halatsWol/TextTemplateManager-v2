using System.Runtime.InteropServices;
using TextTemplateManager.Helpers;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>SendInput sends nothing unless cbSize is exactly the native sizeof(INPUT), which the mouse
/// member of the union decides: 40 bytes in a 64-bit process (x64 and ARM64).</summary>
public class SendInputTests
{
    [Fact]
    public void The_input_struct_has_the_native_size() =>
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf<WindowHelper.INPUT>());

    [Fact]
    public void Ctrl_v_becomes_four_keyboard_inputs_in_order()
    {
        var inputs = WindowHelper.KeyInputs(new (ushort, bool)[]
        {
            (WindowHelper.VK_CONTROL, false), (WindowHelper.VK_V, false), (WindowHelper.VK_V, true), (WindowHelper.VK_CONTROL, true),
        });

        Assert.All(inputs, i => Assert.Equal(1u, i.type));
        Assert.Equal(new ushort[] { 0x11, 0x56, 0x56, 0x11 }, inputs.Select(i => i.u.ki.wVk));
        Assert.Equal(new uint[] { 0, 0, 2, 2 }, inputs.Select(i => i.u.ki.dwFlags));
    }
}
