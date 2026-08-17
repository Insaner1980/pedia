using Pedia.Models;

namespace Pedia.Services;

public interface IFilePickerService
{
    void AttachWindow(Microsoft.UI.Xaml.Window window);
    Task<IReadOnlyList<string>> PickImportFilesAsync();
    Task<string?> PickExportDestinationAsync(ExportFormat format, bool multipleArticles);
    Task<string?> PickBackupDestinationAsync();
    Task<string?> PickBackupSourceAsync();
}
