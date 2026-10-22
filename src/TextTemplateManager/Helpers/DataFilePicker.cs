using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace TextTemplateManager.Helpers;

/// <summary>The file dialogs for .ttmdata files (exports, imports, sync files) and backup zips. Each returns the chosen
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

    public static async Task<string?> PickBackupToOpenAsync(IntPtr owner)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, owner);
        picker.FileTypeFilter.Add(".zip");
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public static async Task<string?> PickBackupToSaveAsync(IntPtr owner, string suggestedName)
    {
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, owner);
        picker.FileTypeChoices.Add("TextTemplateManager backup", new List<string> { ".zip" });
        picker.SuggestedFileName = suggestedName;
        return (await picker.PickSaveFileAsync())?.Path;
    }

    public static async Task<string?> PickLogsToSaveAsync(IntPtr owner, string suggestedName)
    {
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, owner);
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.FileTypeChoices.Add("Logs for a problem report", new List<string> { ".zip" });
        picker.SuggestedFileName = suggestedName;
        return (await picker.PickSaveFileAsync())?.Path;
    }
}
