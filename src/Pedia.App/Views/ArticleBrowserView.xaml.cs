using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Pedia.ViewModels;

namespace Pedia.Views;

public sealed partial class ArticleBrowserView : UserControl
{
    private ArticleRowViewModel? _contextArticle;

    public ArticleBrowserView()
    {
        InitializeComponent();
    }

    public ArticleBrowserViewModel ViewModel
    {
        get => (ArticleBrowserViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(ArticleBrowserViewModel), typeof(ArticleBrowserView), new PropertyMetadata(null));

    public void FocusSearch() => SearchBox.Focus(FocusState.Programmatic);

    private void OnArticleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView list)
        {
            ViewModel.SetSelectedArticles(list.SelectedItems.OfType<ArticleRowViewModel>());
        }
    }

    private void OnFocusSearchAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        FocusSearch();
        args.Handled = true;
    }

    private async void OnArticleDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.SelectedArticle is { } article)
        {
            await ViewModel.RequestArticleActionAsync(article, "edit");
        }
    }

    private void OnArticleContextMenuOpening(object sender, object args)
    {
        if (sender is not MenuFlyout flyout || flyout.Target?.DataContext is not ArticleRowViewModel article)
        {
            return;
        }

        _contextArticle = article;
        foreach (var item in flyout.Items.OfType<MenuFlyoutItem>())
        {
            item.IsEnabled = item.Tag switch
            {
                "restore" or "delete" => article.IsDeleted,
                "trash" or "edit" or "duplicate" or "favorite" or "add-topics" or "export" => !article.IsDeleted,
                "remove-topic" => !article.IsDeleted
                    && ViewModel.Scope is { IsSmart: false } scope
                    && !ViewModel.IncludeSubtopics
                    && ViewModel.SelectedSearchScope.Kind != Models.ArticleSearchScopeKind.CurrentTopicAndDescendants
                    && ViewModel.SelectedSearchScope.Kind != Models.ArticleSearchScopeKind.EntireLibrary
                    && scope.Id > 0,
                _ => true
            };
        }
    }

    private async void OnArticleContextAction(object sender, RoutedEventArgs e)
    {
        if (_contextArticle is null || sender is not MenuFlyoutItem { Tag: string action })
        {
            return;
        }

        if (action == "open")
        {
            ViewModel.SelectedArticle = _contextArticle;
            return;
        }

        await ViewModel.RequestArticleActionAsync(_contextArticle, action);
    }

    private async void OnOpenArticleAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.SelectedArticle is not null)
        {
            await ViewModel.OpenSelectedArticleAsync();
            args.Handled = true;
        }
    }

    private async void OnTrashArticleAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.SelectedArticle is { IsDeleted: false } article)
        {
            await ViewModel.RequestArticleActionAsync(article, "trash");
            args.Handled = true;
        }
    }
}
