using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace TextTemplateManager.Helpers;

/// <summary>The file dialogs for .ttmdata files (backups, exports, sync files). Each returns the chosen
/// path, or null when the user cancels.</summary>
public static class DataFilePicker
{
    public static async Task<string?> PickToOpenAsync(IntPtr owner)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, owner);
        picker.FileTypeFilter.Add(".ttmdata");
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public static async Task<string?> PickToSaveAsync(IntPtr owner, string suggestedName)
    {
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, owner);
        picker.FileTypeChoices.Add("TextTemplateManager data", new List<string> { ".ttmdata" });
        picker.SuggestedFileName = suggestedName;
        return (await picker.PickSaveFileAsync())?.Path;
    }
}
