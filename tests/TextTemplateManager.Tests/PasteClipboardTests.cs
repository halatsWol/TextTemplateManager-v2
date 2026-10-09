using System.Runtime.InteropServices;
using TextTemplateManager.Services.Pasting;
using Xunit;

namespace TextTemplateManager.Tests;

/// <summary>The temporary clipboard writes of a paste must stay out of Win+V history, the cloud
/// clipboard and clipboard managers.</summary>
public class PasteClipboardTests
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string lpszFormat);

    private static readonly string[] Markers =
    {
        "ExcludeClipboardContentFromMonitorProcessing",
        "CanIncludeInClipboardHistory",
        "CanUploadToCloudClipboard",
    };

    [Fact]
    public void Transient_payload_carries_all_exclusion_markers_as_zero()
    {
        var marked = PasteService.AsTransient(new Dictionary<uint, byte[]> { [13] = new byte[] { 65, 0, 0, 0 } });

        foreach (var name in Markers)
        {
            uint id = RegisterClipboardFormat(name);
            Assert.True(marked.ContainsKey(id), name);
            Assert.Equal(0, BitConverter.ToInt32(marked[id]));
        }
    }

    [Fact]
    public void Transient_payload_keeps_the_content_and_leaves_the_input_alone()
    {
        var formats = new Dictionary<uint, byte[]> { [13] = new byte[] { 65, 0, 0, 0 } };

        var marked = PasteService.AsTransient(formats);

        Assert.Equal(formats[13], marked[13]);
        Assert.Single(formats);
    }
}
