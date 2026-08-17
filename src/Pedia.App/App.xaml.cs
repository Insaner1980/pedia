using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Pedia.Core.Backup;
using Pedia.Core.Data;
using Pedia.Core.Exporting;
using Pedia.Core.Importing;
using Pedia.Core.Repositories;
using Pedia.Core.Search;
using Pedia.Services;
using Pedia.ViewModels;

namespace Pedia;

public partial class App : Application
{
    private readonly ServiceProvider _services;
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        _services = ConfigureServices();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = _services.GetRequiredService<MainWindow>();
        _window.Activate();
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddDebug();
            builder.AddProvider(new LocalFileLoggerProvider());
        });

        var databaseOptions = DatabaseOptions.CreateDefault();
        services.AddSingleton(databaseOptions);
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<DatabaseInformationService>();
        services.AddSingleton<TopicRepository>();
        services.AddSingleton<ArticleRepository>();
        services.AddSingleton<IArticleQueryService, ArticleQueryService>();
        services.AddSingleton<ImportPreviewService>();
        services.AddSingleton<DocumentExportService>();
        services.AddSingleton(provider => new BackupService(
            provider.GetRequiredService<DatabaseOptions>().DatabasePath,
            requiredSchemaVersion: MigrationRunner.CurrentSchemaVersion));

        services.AddSingleton<IStringService, StringService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IPediaDataService, CorePediaDataService>();

        services.AddSingleton<TopicPaneViewModel>();
        services.AddSingleton<ArticleBrowserViewModel>();
        services.AddSingleton<ArticleDetailViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var logger = _services.GetService<ILogger<App>>();
        logger?.LogCritical(e.Exception, "An unhandled Pedia UI exception occurred");
        e.Handled = true;

        if (_window?.Content is FrameworkElement { XamlRoot: not null })
        {
            var dialogs = _services.GetRequiredService<IDialogService>();
            var strings = _services.GetRequiredService<IStringService>();
            _ = dialogs.ShowErrorAsync(strings.OperationFailed);
        }
    }
}
