using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Pedia.Services;
using Pedia.ViewModels;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;
using System.Runtime.InteropServices;

namespace Pedia;

public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 1600;
    private const int DefaultHeight = 950;
    private const int MinimumWidth = 1180;
    private const int MinimumHeight = 710;

    private readonly ISettingsService _settings;
    private readonly IFilePickerService _filePicker;
    private readonly IDialogService _dialogs;
    private AppWindow _appWindow = null!;
    private bool _closeHandled;
    private bool _restored;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    public MainWindow(
        MainWindowViewModel viewModel,
        ISettingsService settings,
        IFilePickerService filePicker,
        IDialogService dialogs)
    {
        ViewModel = viewModel;
        _settings = settings;
        _filePicker = filePicker;
        _dialogs = dialogs;
        InitializeComponent();
        var settingsAccelerator = new KeyboardAccelerator
        {
            Key = (VirtualKey)188,
            Modifiers = VirtualKeyModifiers.Control
        };
        settingsAccelerator.Invoked += OnSettingsAccelerator;
        RootGrid.KeyboardAccelerators.Add(settingsAccelerator);
        RootGrid.DataContext = ViewModel;
        ViewModel.Detail.EditorFocusRequested = () => DispatcherQueue.TryEnqueue(ArticleDetail.FocusTitleEditor);
        ViewModel.Detail.PropertyChanged += OnDetailPropertyChanged;
        ViewModel.DensityChanged = ApplyDensity;
        ConfigureWindow();
        Activated += OnActivated;
        Closed += OnClosed;
        RootGrid.Loaded += OnRootLoaded;
        RootGrid.SizeChanged += OnRootSizeChanged;
    }

    public MainWindowViewModel ViewModel { get; }

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        var handle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(handle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Title = "Pedia";
        _appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Pedia.ico"));
        _appWindow.Closing += OnAppWindowClosing;
        _appWindow.Changed += OnAppWindowChanged;
        _appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        ApplyCaptionButtonColors();
        _filePicker.AttachWindow(this);
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        _dialogs.AttachXamlRoot(RootGrid.XamlRoot);
        await ViewModel.InitializeAsync();
        RestoreLayout();
        RestoreArticleScrollPosition();
    }

    private void RestoreLayout()
    {
        if (_restored)
        {
            return;
        }

        _restored = true;
        var state = _settings.Current.Window;
        var scale = GetWindowScale();
        var requestedWidth = ToPhysicalPixels(state.Width, scale);
        var requestedHeight = ToPhysicalPixels(state.Height, scale);
        var display = DisplayArea.GetFromRect(
            new RectInt32(state.X, state.Y, requestedWidth, requestedHeight),
            DisplayAreaFallback.Primary);
        var workArea = display.WorkArea;
        var width = Math.Clamp(requestedWidth, ToPhysicalPixels(MinimumWidth, scale), workArea.Width);
        var height = Math.Clamp(requestedHeight, ToPhysicalPixels(MinimumHeight, scale), workArea.Height);
        var x = state.X;
        var y = state.Y;
        if (x + 100 < workArea.X || y + 50 < workArea.Y || x >= workArea.X + workArea.Width || y >= workArea.Y + workArea.Height)
        {
            x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
            y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
        }

        _appWindow.MoveAndResize(new RectInt32(x, y, width, height));
        TopicPaneColumn.Width = state.IsTopicPaneCollapsed
            ? new GridLength(0)
            : new GridLength(Math.Clamp(state.TopicPaneWidth, 220, 430));
        ArticlePaneColumn.Width = new GridLength(Math.Clamp(state.ArticlePaneWidth, 420, 760));
        TopicSplitter.Visibility = state.IsTopicPaneCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ExpandTopicPaneButton.Visibility = state.IsTopicPaneCollapsed ? Visibility.Visible : Visibility.Collapsed;
        if (state.IsMaximized && _appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        ApplyCaptionButtonColors();
    }

    private void ApplyCaptionButtonColors()
    {
        var titleBar = _appWindow.TitleBar;
        titleBar.BackgroundColor = ColorHelper.FromArgb(255, 14, 14, 14);
        titleBar.ForegroundColor = ColorHelper.FromArgb(255, 244, 244, 244);
        titleBar.InactiveBackgroundColor = ColorHelper.FromArgb(255, 14, 14, 14);
        titleBar.InactiveForegroundColor = ColorHelper.FromArgb(255, 136, 136, 136);
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = ColorHelper.FromArgb(255, 244, 244, 244);
        titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(255, 34, 34, 34);
        titleBar.ButtonHoverForegroundColor = Colors.White;
        titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(255, 50, 50, 50);
        titleBar.ButtonPressedForegroundColor = Colors.White;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(255, 99, 99, 99);
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeHandled)
        {
            return;
        }

        args.Cancel = true;
        if (ViewModel.IsOperationBlockingClose)
        {
            return;
        }

        if (!await ViewModel.Detail.TryLeaveEditorAsync())
        {
            return;
        }

        await PersistWindowStateAsync();
        _closeHandled = true;
        Close();
    }

    private async Task PersistWindowStateAsync()
    {
        var state = _settings.Current.Window;
        var isMaximized = _appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        state.IsMaximized = isMaximized;
        if (!isMaximized)
        {
            var scale = GetWindowScale();
            state.X = _appWindow.Position.X;
            state.Y = _appWindow.Position.Y;
            state.Width = ToLogicalPixels(_appWindow.Size.Width, scale);
            state.Height = ToLogicalPixels(_appWindow.Size.Height, scale);
        }
        if (TopicPaneColumn.Width.Value > 0)
        {
            state.TopicPaneWidth = Math.Max(220, TopicPaneColumn.ActualWidth);
        }
        state.ArticlePaneWidth = Math.Max(420, ArticlePaneColumn.ActualWidth);
        state.IsTopicPaneCollapsed = TopicPaneColumn.Width.Value == 0;
        await ViewModel.SaveSessionAsync();
    }

    private void OnClosed(object sender, WindowEventArgs args) => App.Current.Exit();

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange && _appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            var scale = GetWindowScale();
            var width = Math.Max(_appWindow.Size.Width, ToPhysicalPixels(MinimumWidth, scale));
            var height = Math.Max(_appWindow.Size.Height, ToPhysicalPixels(MinimumHeight, scale));
            if (width != _appWindow.Size.Width || height != _appWindow.Size.Height)
            {
                _appWindow.Resize(new SizeInt32(width, height));
            }
        }
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width < 1260 && TopicPaneColumn.Width.Value > 0)
        {
            CollapseTopicPane();
        }
    }

    private void OnCollapseTopicPane(object? sender, EventArgs e) => CollapseTopicPane();

    private void CollapseTopicPane()
    {
        if (TopicPaneColumn.Width.Value <= 0)
        {
            return;
        }
        _settings.Current.Window.TopicPaneWidth = TopicPaneColumn.ActualWidth;
        TopicPaneColumn.Width = new GridLength(0);
        TopicSplitter.Visibility = Visibility.Collapsed;
        ExpandTopicPaneButton.Visibility = Visibility.Visible;
    }

    private void OnExpandTopicPane(object sender, RoutedEventArgs e)
    {
        TopicPaneColumn.Width = new GridLength(Math.Clamp(_settings.Current.Window.TopicPaneWidth, 220, 430));
        TopicSplitter.Visibility = Visibility.Visible;
        ExpandTopicPaneButton.Visibility = Visibility.Collapsed;
    }

    private void OnTopicPaneWidthChanged(object? sender, double width) => _settings.Current.Window.TopicPaneWidth = width;
    private void OnArticlePaneWidthChanged(object? sender, double width) => _settings.Current.Window.ArticlePaneWidth = width;

    private void OnArticleScrollPositionChanged(object? sender, (long ArticleId, double Offset) value)
    {
        if (!_settings.Current.RememberScrollPositions)
        {
            return;
        }
        _settings.Current.ArticleScrollPositions[value.ArticleId] = value.Offset;
        while (_settings.Current.ArticleScrollPositions.Count > 50)
        {
            var oldest = _settings.Current.ArticleScrollPositions.Keys.First();
            _settings.Current.ArticleScrollPositions.Remove(oldest);
        }
    }

    private void OnNewArticleAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!IsTextEditing(args.Element) && ViewModel.NewArticleCommand.CanExecute(null))
        {
            ViewModel.NewArticleCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnNewTopicAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.CanUseTitleBarCommands
            && !ViewModel.IsSettingsVisible
            && !IsTextEditing(args.Element)
            && ViewModel.Topics.CreateRootTopicCommand.CanExecute(null))
        {
            ViewModel.Topics.CreateRootTopicCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnSearchAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ArticleBrowser.FocusSearch();
        args.Handled = true;
    }

    private void OnSaveAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.Detail.IsEditing && ViewModel.Detail.SaveCommand.CanExecute(null))
        {
            ViewModel.Detail.SaveCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnRenameTopicAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.CanUseTitleBarCommands
            && !ViewModel.IsSettingsVisible
            && !IsTextEditing(args.Element)
            && ViewModel.Topics.RenameTopicCommand.CanExecute(null))
        {
            ViewModel.Topics.RenameTopicCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnSettingsAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!IsTextEditing(args.Element) && ViewModel.OpenSettingsCommand.CanExecute(null))
        {
            ViewModel.OpenSettingsCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnEscapeAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.Detail.IsEditing && ViewModel.Detail.CancelEditCommand.CanExecute(null))
        {
            ViewModel.Detail.CancelEditCommand.Execute(null);
            args.Handled = true;
        }
        else if (!string.IsNullOrEmpty(ViewModel.Browser.SearchText))
        {
            ViewModel.Browser.SearchText = string.Empty;
            args.Handled = true;
        }
    }

    private void OnBackAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.IsSettingsVisible && ViewModel.Settings.BackCommand.CanExecute(null))
        {
            ViewModel.Settings.BackCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void RestoreArticleScrollPosition()
    {
        if (!_settings.Current.RememberScrollPositions
            || ViewModel.Detail.Article is not { } article
            || !_settings.Current.ArticleScrollPositions.TryGetValue(article.Id, out var offset))
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() => ArticleDetail.SetScrollPosition(offset));
    }

    private void OnDetailPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ArticleDetailViewModel.Article))
        {
            RestoreArticleScrollPosition();
        }
    }

    private double GetWindowScale()
    {
        var dpi = GetDpiForWindow(WindowNative.GetWindowHandle(this));
        return dpi == 0 ? 1d : dpi / 96d;
    }

    private static int ToPhysicalPixels(int logicalPixels, double scale) =>
        (int)Math.Round(logicalPixels * scale, MidpointRounding.AwayFromZero);

    private static int ToLogicalPixels(int physicalPixels, double scale) =>
        (int)Math.Round(physicalPixels / Math.Max(scale, 0.01), MidpointRounding.AwayFromZero);

    private static void ApplyDensity(bool compact)
    {
        var resources = Application.Current.Resources;
        resources["PediaControlMinHeight"] = compact ? 30d : 36d;
        resources["PediaInputMinHeight"] = compact ? 32d : 38d;
        resources["PediaListItemMinHeight"] = compact ? 40d : 46d;
        resources["PediaTreeItemMinHeight"] = compact ? 34d : 40d;
        resources["PediaButtonPadding"] = compact
            ? new Thickness(9, 4, 9, 4)
            : new Thickness(12, 7, 12, 7);
    }

    private static bool IsTextEditing(DependencyObject? element) => element is Microsoft.UI.Xaml.Controls.TextBox
        or Microsoft.UI.Xaml.Controls.RichEditBox
        or Microsoft.UI.Xaml.Controls.PasswordBox;
}
