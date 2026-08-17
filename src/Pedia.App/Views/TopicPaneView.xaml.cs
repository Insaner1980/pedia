using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pedia.ViewModels;

namespace Pedia.Views;

public sealed partial class TopicPaneView : UserControl
{
    private TopicNodeViewModel? _contextNode;
    private bool _restoringSelection;
    private int _selectionRequestVersion;
    public TopicPaneView()
    {
        InitializeComponent();
    }

    public TopicPaneViewModel ViewModel
    {
        get => (TopicPaneViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(TopicPaneViewModel), typeof(TopicPaneView), new PropertyMetadata(null));

    public event EventHandler? CollapseRequested;

    private async void OnTopicSelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (_restoringSelection
            || sender.SelectedItem is not TopicNodeViewModel node)
        {
            return;
        }

        var requestVersion = Interlocked.Increment(ref _selectionRequestVersion);
        if (!await ViewModel.SelectNodeAsync(node)
            && requestVersion == Volatile.Read(ref _selectionRequestVersion))
        {
            _restoringSelection = true;
            sender.SelectedItem = ViewModel.SelectedNode;
            _restoringSelection = false;
        }
    }

    private void OnTopicContextMenuOpening(object sender, object args)
    {
        if (sender is not MenuFlyout flyout || flyout.Target?.DataContext is not TopicNodeViewModel node)
        {
            return;
        }

        _contextNode = node;
        foreach (var item in flyout.Items.OfType<MenuFlyoutItem>())
        {
            item.IsEnabled = item.Tag switch
            {
                "move-up" => CanMove(node, -1),
                "move-down" => CanMove(node, 1),
                "new-child" => !node.IsSmart,
                "expand" => CanExpand(node),
                "collapse" => CanCollapse(node),
                "rename" or "move" or "copy-path" or "delete" => !node.IsSmart,
                _ => true
            };
        }
    }

    private bool CanMove(TopicNodeViewModel node, int direction)
    {
        if (!string.IsNullOrWhiteSpace(ViewModel.TopicFilter))
        {
            return false;
        }

        IList<TopicNodeViewModel>? siblings = node.ParentId is { } parentId
            ? FindNode(ViewModel.RootNodes, parentId)?.Children
            : ViewModel.RootNodes.Where(candidate => !candidate.IsSmart).ToArray();
        if (siblings is null)
        {
            return false;
        }

        var index = siblings.IndexOf(node);
        return direction < 0 ? index > 0 : index >= 0 && index < siblings.Count - 1;
    }

    private static bool CanExpand(TopicNodeViewModel node) =>
        !node.IsSmart
        && node.Children.Count > 0
        && (!node.IsExpanded || Flatten(node.Children).Any(descendant => !descendant.IsExpanded));

    private static bool CanCollapse(TopicNodeViewModel node) =>
        !node.IsSmart
        && node.Children.Count > 0
        && (node.IsExpanded || Flatten(node.Children).Any(descendant => descendant.IsExpanded));

    private static IEnumerable<TopicNodeViewModel> Flatten(IEnumerable<TopicNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var descendant in Flatten(node.Children))
            {
                yield return descendant;
            }
        }
    }

    private static TopicNodeViewModel? FindNode(IEnumerable<TopicNodeViewModel> nodes, long id)
    {
        foreach (var node in nodes)
        {
            if (node.Id == id)
            {
                return node;
            }

            if (FindNode(node.Children, id) is { } child)
            {
                return child;
            }
        }

        return null;
    }

    private async void OnTopicContextAction(object sender, RoutedEventArgs e)
    {
        if (_contextNode is null || sender is not MenuFlyoutItem { Tag: string action })
        {
            return;
        }

        var requestVersion = Interlocked.Increment(ref _selectionRequestVersion);
        if (!await ViewModel.SelectNodeAsync(_contextNode))
        {
            if (requestVersion == Volatile.Read(ref _selectionRequestVersion))
            {
                TopicTree.SelectedItem = ViewModel.SelectedNode;
            }
            return;
        }

        switch (action)
        {
            case "new-child":
                if (ViewModel.CreateChildTopicCommand.CanExecute(null)) ViewModel.CreateChildTopicCommand.Execute(null);
                break;
            case "rename":
                if (ViewModel.RenameTopicCommand.CanExecute(null)) ViewModel.RenameTopicCommand.Execute(null);
                break;
            case "move":
                if (ViewModel.MoveTopicCommand.CanExecute(null)) ViewModel.MoveTopicCommand.Execute(null);
                break;
            case "move-up":
                if (ViewModel.MoveTopicUpCommand.CanExecute(null)) ViewModel.MoveTopicUpCommand.Execute(null);
                break;
            case "move-down":
                if (ViewModel.MoveTopicDownCommand.CanExecute(null)) ViewModel.MoveTopicDownCommand.Execute(null);
                break;
            case "expand":
                if (ViewModel.ExpandDescendantsCommand.CanExecute(null)) ViewModel.ExpandDescendantsCommand.Execute(null);
                break;
            case "collapse":
                if (ViewModel.CollapseDescendantsCommand.CanExecute(null)) ViewModel.CollapseDescendantsCommand.Execute(null);
                break;
            case "copy-path":
                if (ViewModel.CopyTopicPathCommand.CanExecute(null)) ViewModel.CopyTopicPathCommand.Execute(null);
                break;
            case "delete":
                if (ViewModel.DeleteTopicCommand.CanExecute(null)) ViewModel.DeleteTopicCommand.Execute(null);
                break;
        }
    }

    private void OnCollapsePaneClick(object sender, RoutedEventArgs e) => CollapseRequested?.Invoke(this, EventArgs.Empty);
}
