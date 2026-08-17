using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pedia.Models;
using Pedia.ViewModels;

namespace Pedia.Views;

public sealed partial class ArticleDetailView : UserControl
{
    public ArticleDetailView()
    {
        InitializeComponent();
    }

    public ArticleDetailViewModel ViewModel
    {
        get => (ArticleDetailViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(ArticleDetailViewModel), typeof(ArticleDetailView), new PropertyMetadata(null));

    public event EventHandler<(long ArticleId, double Offset)>? ScrollPositionChanged;

    public void FocusTitleEditor()
    {
        var titleBox = FindName("TitleField") as TextBox;
        titleBox?.Focus(FocusState.Programmatic);
    }

    public void SetScrollPosition(double offset) => ArticleScrollViewer.ChangeView(null, offset, null, true);

    private void OnReaderViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!e.IsIntermediate && ViewModel.Article is { } article)
        {
            ScrollPositionChanged?.Invoke(this, (article.Id, ArticleScrollViewer.VerticalOffset));
        }
    }

    private void OnMoveSectionUpClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EditableSection section)
        {
            ViewModel.MoveSectionUpCommand.Execute(section);
        }
    }

    private void OnMoveSectionDownClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EditableSection section)
        {
            ViewModel.MoveSectionDownCommand.Execute(section);
        }
    }

    private void OnDeleteSectionClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EditableSection section)
        {
            ViewModel.DeleteSectionCommand.Execute(section);
        }
    }

    private void OnDeleteSourceClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EditableSource source)
        {
            ViewModel.DeleteSourceCommand.Execute(source);
        }
    }

    private void OnMoveSourceUpClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EditableSource source)
        {
            ViewModel.MoveSourceUpCommand.Execute(source);
        }
    }

    private void OnMoveSourceDownClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EditableSource source)
        {
            ViewModel.MoveSourceDownCommand.Execute(source);
        }
    }

    private void OnSetPrimaryTopicClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EditableTopicAssignment assignment)
        {
            ViewModel.SetPrimaryTopicCommand.Execute(assignment);
        }
    }

    private void OnRemoveTopicAssignmentClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EditableTopicAssignment assignment)
        {
            ViewModel.RemoveTopicAssignmentCommand.Execute(assignment);
        }
    }

    private async void OnSetReaderPrimaryTopicClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ArticleTopicData assignment)
        {
            await ViewModel.SetPrimaryTopicAsync(assignment);
        }
    }

    private async void OnRemoveReaderTopicAssignmentClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ArticleTopicData assignment)
        {
            await ViewModel.RemoveTopicAssignmentAsync(assignment);
        }
    }

    private void OnOpenSourceClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ArticleSourceData source)
        {
            ViewModel.OpenSourceCommand.Execute(source);
        }
    }

    private void OnCopySourceClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ArticleSourceData source)
        {
            ViewModel.CopySourceUrlCommand.Execute(source);
        }
    }
}
