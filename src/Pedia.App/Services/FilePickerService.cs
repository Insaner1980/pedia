using Microsoft.UI.Xaml;
using Pedia.Models;
using Windows.Storage.Pickers;

namespace Pedia.Services;

public sealed class FilePickerService(IStringService strings) : IFilePickerService
{
    private nint _windowHandle;

    public void AttachWindow(Window window)
    {
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
    }

    public async Task<IReadOnlyList<string>> PickImportFilesAsync()
    {
        var picker = new FileOpenPicker();
        Initialize(picker);
        picker.FileTypeFilter.Add(".txt");
        picker.FileTypeFilter.Add(".md");
        var files = await picker.PickMultipleFilesAsync();
        return files.Select(file => file.Path).ToArray();
    }

    public async Task<string?> PickExportDestinationAsync(ExportFormat format, bool multipleArticles)
    {
        if (multipleArticles)
        {
            var folderPicker = new FolderPicker();
            Initialize(folderPicker);
            folderPicker.FileTypeFilter.Add("*");
            return (await folderPicker.PickSingleFolderAsync())?.Path;
        }

        var picker = new FileSavePicker();
        Initialize(picker);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = strings.Get("ExportSuggestedFileName");
        var extension = format switch
        {
            ExportFormat.Markdown => ".md",
            ExportFormat.PediaJson => ".pedia.json",
            _ => ".txt"
        };
        picker.FileTypeChoices.Add(strings.Get(format switch
        {
            ExportFormat.Markdown => "MarkdownFileTypeDescription",
            ExportFormat.PediaJson => "PediaJsonFileTypeDescription",
            _ => "PlainTextFileTypeDescription"
        }), [extension]);
        return (await picker.PickSaveFileAsync())?.Path;
    }

    public async Task<string?> PickBackupDestinationAsync()
    {
        var picker = new FileSavePicker();
        Initialize(picker);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = $"Pedia-{DateTime.Now:yyyy-MM-dd-HHmm}";
        picker.FileTypeChoices.Add(strings.Get("PediaBackupFileTypeDescription"), [".pediabackup"]);
        return (await picker.PickSaveFileAsync())?.Path;
    }

    public async Task<string?> PickBackupSourceAsync()
    {
        var picker = new FileOpenPicker();
        Initialize(picker);
        picker.FileTypeFilter.Add(".pediabackup");
        return (await picker.PickSingleFileAsync())?.Path;
    }

    private void Initialize(object picker)
    {
        if (_windowHandle == 0)
        {
            throw new InvalidOperationException(strings.Get("FilePickerWindowUnavailableText"));
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandle);
    }
}
