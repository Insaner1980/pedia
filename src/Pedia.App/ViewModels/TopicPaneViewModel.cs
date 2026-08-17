using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Pedia.Models;
using Pedia.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Pedia.ViewModels;

public sealed partial class TopicPaneViewModel : ObservableObject
{
    private readonly IPediaDataService _dataService;
    private readonly IDialogService _dialogs;
    private readonly IStringService _strings;
    private readonly ILogger<TopicPaneViewModel> _logger;
    private IReadOnlyList<TopicData> _topicData = [];
    private long? _preferredTopicId;
    private int _selectionRequestVersion;

    public TopicPaneViewModel(
        IPediaDataService dataService,
        IDialogService dialogs,
        IStringService strings,
        ILogger<TopicPaneViewModel> logger)
    {
        _dataService = dataService;
        _dialogs = dialogs;
        _strings = strings;
        _logger = logger;
    }

    public ObservableCollection<TopicNodeViewModel> RootNodes { get; } = [];

    public Func<TopicNodeViewModel, Task<bool>>? ScopeSelected { get; set; }
    public Func<Task<bool>>? TopicMutationStarting { get; set; }
    public Func<Task>? TopicsChanged { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateChildTopicCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameTopicCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveTopicCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveTopicUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveTopicDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyTopicPathCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExpandDescendantsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CollapseDescendantsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteTopicCommand))]
    public partial TopicNodeViewModel? SelectedNode { get; set; }

    [ObservableProperty]
    public partial string TopicFilter { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateChildTopicCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameTopicCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveTopicCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveTopicUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveTopicDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyTopicPathCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExpandDescendantsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CollapseDescendantsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteTopicCommand))]
    public partial bool IsSelectionPending { get; private set; }

    [SuppressMessage(
        "Performance",
        "S2365",
        Justification = "The property intentionally returns a point-in-time flattened snapshot of the mutable topic tree.")]
    public IReadOnlyList<TopicNodeViewModel> UserTopics => Flatten(RootNodes.Where(node => !node.IsSmart)).ToArray();

    public bool ContainsTopic(long topicId) => ContainsTopic(_topicData, topicId);

    public async Task LoadAsync(long? preferredTopicId = null, CancellationToken cancellationToken = default)
    {
        _preferredTopicId = preferredTopicId ?? SelectedNode?.Id;
        _topicData = await _dataService.GetTopicsAsync(cancellationToken);
        RebuildNodes();
    }

    public async Task<bool> SelectNodeAsync(TopicNodeViewModel node)
    {
        var requestVersion = Interlocked.Increment(ref _selectionRequestVersion);
        IsSelectionPending = true;
        try
        {
            if (ReferenceEquals(SelectedNode, node))
            {
                return true;
            }

            var accepted = ScopeSelected is null || await ScopeSelected(node);
            if (requestVersion != Volatile.Read(ref _selectionRequestVersion))
            {
                return false;
            }

            if (!accepted)
            {
                OnPropertyChanged(nameof(SelectedNode));
                return false;
            }

            SelectedNode = node;
            _preferredTopicId = node.Id;
            return true;
        }
        finally
        {
            if (requestVersion == Volatile.Read(ref _selectionRequestVersion))
            {
                IsSelectionPending = false;
            }
        }
    }

    partial void OnTopicFilterChanged(string value)
    {
        Interlocked.Increment(ref _selectionRequestVersion);
        IsSelectionPending = false;
        RebuildNodes();
    }

    [RelayCommand]
    private async Task CreateRootTopicAsync() => await CreateTopicAsync(null);

    [RelayCommand(CanExecute = nameof(CanCreateChildTopic))]
    private async Task CreateChildTopicAsync() => await CreateTopicAsync(SelectedNode?.Id);

    private bool CanCreateChildTopic() => !IsSelectionPending && SelectedNode is { IsSmart: false };

    [RelayCommand(CanExecute = nameof(CanManageSelectedTopic))]
    private async Task RenameTopicAsync()
    {
        if (!await CanStartTopicMutationAsync())
        {
            return;
        }

        var node = SelectedNode!;
        var result = await _dialogs.ShowTopicEditorAsync(_strings.Get("EditTopicTitle"), node.Name, node.Description);
        if (result is null)
        {
            return;
        }

        try
        {
            await _dataService.RenameTopicAsync(node.Id, result.Name, result.Description);
            await RefreshAfterMutationAsync(node.Id);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not rename topic {TopicId}", node.Id);
            await _dialogs.ShowErrorAsync(_strings.OperationFailed);
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageSelectedTopic))]
    private async Task MoveTopicAsync()
    {
        if (!await CanStartTopicMutationAsync())
        {
            return;
        }

        var node = SelectedNode!;
        var excluded = GetDescendantIds(node).Append(node.Id).ToHashSet();
        var rootLabel = _strings.Get("RootTopLevelOption");
        var rootDestination = new TopicNodeViewModel(0, null, rootLabel, null, -1, 0, LibraryScopeKind.Topic, false, "\uE8B7", rootLabel);
        var destinations = UserTopics.Where(topic => !excluded.Contains(topic.Id)).Prepend(rootDestination).ToArray();
        var destination = await _dialogs.ChooseTopicAsync(_strings.Get("MoveTopicTitle"), destinations);
        if (destination is null)
        {
            return;
        }

        try
        {
            await _dataService.MoveTopicAsync(node.Id, destination.Id == 0 ? null : destination.Id);
            await RefreshAfterMutationAsync(node.Id);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not move topic {TopicId}", node.Id);
            await _dialogs.ShowErrorAsync(_strings.OperationFailed);
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageSelectedTopic))]
    private async Task DeleteTopicAsync()
    {
        if (!await CanStartTopicMutationAsync())
        {
            return;
        }

        var node = SelectedNode!;
        if (!await _dialogs.ConfirmAsync(
                _strings.Get("DeleteTopicTitle"),
                _strings.Get("DeleteTopicMessage"),
                _strings.Get("DeleteButtonText")))
        {
            return;
        }

        try
        {
            await _dataService.DeleteTopicAsync(node.Id);
            await RefreshAfterMutationAsync(node.ParentId);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not delete topic {TopicId}", node.Id);
            await _dialogs.ShowErrorAsync(_strings.OperationFailed);
        }
    }

    private bool CanManageSelectedTopic() => !IsSelectionPending && SelectedNode is { IsSmart: false };

    [RelayCommand(CanExecute = nameof(CanMoveSelectedTopicUp))]
    private async Task MoveTopicUpAsync()
    {
        var node = SelectedNode!;
        var siblings = UserTopics.Where(topic => topic.ParentId == node.ParentId).OrderBy(topic => topic.SortOrder).ToArray();
        var index = Array.IndexOf(siblings, node);
        if (index <= 0)
        {
            return;
        }

        await ReorderTopicAsync(node, index - 1);
    }

    private bool CanMoveSelectedTopicUp() =>
        !IsSelectionPending && string.IsNullOrWhiteSpace(TopicFilter) && GetSelectedSiblingIndex() > 0;

    [RelayCommand(CanExecute = nameof(CanMoveSelectedTopicDown))]
    private async Task MoveTopicDownAsync()
    {
        var node = SelectedNode!;
        var siblings = UserTopics.Where(topic => topic.ParentId == node.ParentId).OrderBy(topic => topic.SortOrder).ToArray();
        var index = Array.IndexOf(siblings, node);
        if (index < 0 || index >= siblings.Length - 1)
        {
            return;
        }

        await ReorderTopicAsync(node, index + 1);
    }

    private bool CanMoveSelectedTopicDown()
    {
        var index = GetSelectedSiblingIndex();
        return !IsSelectionPending
            && string.IsNullOrWhiteSpace(TopicFilter)
            && index >= 0
            && index < GetSelectedSiblings().Length - 1;
    }

    [RelayCommand(CanExecute = nameof(CanManageSelectedTopic))]
    private void CopyTopicPath()
    {
        var package = new DataPackage();
        package.SetText(SelectedNode!.FullPath);
        Clipboard.SetContent(package);
    }

    [RelayCommand(CanExecute = nameof(CanExpandSelectedTopic))]
    private void ExpandDescendants()
    {
        SetExpanded(SelectedNode!, true);
    }

    [RelayCommand(CanExecute = nameof(CanCollapseSelectedTopic))]
    private void CollapseDescendants()
    {
        SetExpanded(SelectedNode!, false);
    }

    private bool CanExpandSelectedTopic() =>
        !IsSelectionPending && SelectedNode is { IsSmart: false, Children: { Count: > 0 } } node &&
        (!node.IsExpanded || Flatten(node.Children).Any(descendant => !descendant.IsExpanded));

    private bool CanCollapseSelectedTopic() =>
        !IsSelectionPending && SelectedNode is { IsSmart: false, Children: { Count: > 0 } } node &&
        (node.IsExpanded || Flatten(node.Children).Any(descendant => descendant.IsExpanded));

    private async Task ReorderTopicAsync(TopicNodeViewModel node, int newSortOrder)
    {
        if (!await CanStartTopicMutationAsync())
        {
            return;
        }

        try
        {
            await _dataService.ReorderTopicAsync(node.Id, newSortOrder);
            await RefreshAfterMutationAsync(node.Id);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not reorder topic {TopicId}", node.Id);
            await _dialogs.ShowErrorAsync(_strings.OperationFailed);
        }
    }

    private TopicNodeViewModel[] GetSelectedSiblings() => SelectedNode is { IsSmart: false } node
        ? UserTopics.Where(topic => topic.ParentId == node.ParentId).OrderBy(topic => topic.SortOrder).ToArray()
        : [];

    private int GetSelectedSiblingIndex() => SelectedNode is null ? -1 : Array.IndexOf(GetSelectedSiblings(), SelectedNode);

    private static void SetExpanded(TopicNodeViewModel node, bool isExpanded)
    {
        node.IsExpanded = isExpanded;
        foreach (var child in node.Children)
        {
            SetExpanded(child, isExpanded);
        }
    }

    private async Task CreateTopicAsync(long? parentId)
    {
        if (!await CanStartTopicMutationAsync())
        {
            return;
        }

        var result = await _dialogs.ShowTopicEditorAsync(
            _strings.Get(parentId is null ? "NewRootTopicTitle" : "NewChildTopicTitle"));
        if (result is null)
        {
            return;
        }

        try
        {
            var id = await _dataService.CreateTopicAsync(result.Name, result.Description, parentId);
            await RefreshAfterMutationAsync(id);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not create topic under {ParentTopicId}", parentId);
            await _dialogs.ShowErrorAsync(_strings.OperationFailed);
        }
    }

    private async Task RefreshAfterMutationAsync(long? preferredTopicId)
    {
        await LoadAsync(preferredTopicId);
        if (TopicsChanged is not null)
        {
            await TopicsChanged();
        }
    }

    private Task<bool> CanStartTopicMutationAsync() =>
        TopicMutationStarting?.Invoke() ?? Task.FromResult(true);

    private void RebuildNodes()
    {
        RootNodes.Clear();
        RootNodes.Add(CreateSmartNode(-1, _strings.Get("AllArticlesNode"), LibraryScopeKind.AllArticles, "\uE8A5"));
        RootNodes.Add(CreateSmartNode(-2, _strings.Get("FavoritesNode"), LibraryScopeKind.Favorites, "\uE734"));
        RootNodes.Add(CreateSmartNode(-3, _strings.Get("RecentlyEditedNode"), LibraryScopeKind.RecentlyEdited, "\uE823"));
        RootNodes.Add(CreateSmartNode(-4, _strings.Get("UncategorizedNode"), LibraryScopeKind.Uncategorized, "\uE838"));
        RootNodes.Add(CreateSmartNode(-5, _strings.Get("TrashNode"), LibraryScopeKind.Trash, "\uE74D"));

        foreach (var topic in _topicData)
        {
            var node = BuildTopicNode(topic, string.Empty);
            if (node is not null)
            {
                RootNodes.Add(node);
            }
        }

        var selected = _preferredTopicId is not null
            ? Flatten(RootNodes).FirstOrDefault(node => node.Id == _preferredTopicId)
            : null;
        if (selected is not null)
        {
            SelectedNode = selected;
        }
        else if (string.IsNullOrWhiteSpace(TopicFilter))
        {
            SelectedNode = RootNodes.FirstOrDefault();
        }
        else
        {
            SelectedNode = null;
        }
    }

    private TopicNodeViewModel? BuildTopicNode(TopicData topic, string parentPath)
    {
        var path = string.IsNullOrEmpty(parentPath) ? topic.Name : $"{parentPath} / {topic.Name}";
        var childNodes = topic.Children
            .Select(child => BuildTopicNode(child, path))
            .Where(child => child is not null)
            .Cast<TopicNodeViewModel>()
            .ToArray();
        var matchesFilter = string.IsNullOrWhiteSpace(TopicFilter)
            || topic.Name.Contains(TopicFilter.Trim(), StringComparison.CurrentCultureIgnoreCase)
            || childNodes.Length > 0;
        if (!matchesFilter)
        {
            return null;
        }

        var node = new TopicNodeViewModel(
            topic.Id,
            topic.ParentId,
            topic.Name,
            topic.Description,
            topic.SortOrder,
            topic.ArticleCount,
            LibraryScopeKind.Topic,
            false,
            childNodes.Length > 0 ? "\uE8B7" : "\uE8A5",
            path,
            topic.ArticleCount > 0
                ? _strings.Format("TopicAccessibleNameFormat", topic.Name, topic.ArticleCount)
                : topic.Name)
        {
            IsExpanded = childNodes.Length > 0 && !string.IsNullOrWhiteSpace(TopicFilter)
        };
        foreach (var child in childNodes)
        {
            node.Children.Add(child);
        }

        return node;
    }

    private static TopicNodeViewModel CreateSmartNode(long id, string name, LibraryScopeKind scope, string glyph) =>
        new(id, null, name, null, -1, 0, scope, true, glyph, name);

    private static bool ContainsTopic(IEnumerable<TopicData> topics, long topicId) =>
        topics.Any(topic => topic.Id == topicId || ContainsTopic(topic.Children, topicId));

    private static IEnumerable<TopicNodeViewModel> Flatten(IEnumerable<TopicNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<long> GetDescendantIds(TopicNodeViewModel node) =>
        Flatten(node.Children).Select(child => child.Id);
}
