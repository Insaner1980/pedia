using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Pedia.Models;

namespace Pedia.ViewModels;

public sealed partial class TopicNodeViewModel : ObservableObject
{
    public TopicNodeViewModel(
        long id,
        long? parentId,
        string name,
        string? description,
        int sortOrder,
        int articleCount,
        LibraryScopeKind scope,
        bool isSmart,
        string glyph,
        string fullPath,
        string? accessibleName = null)
    {
        Id = id;
        ParentId = parentId;
        Name = name;
        Description = description;
        SortOrder = sortOrder;
        ArticleCount = articleCount;
        Scope = scope;
        IsSmart = isSmart;
        Glyph = glyph;
        FullPath = fullPath;
        AccessibleName = accessibleName ?? name;
    }

    public long Id { get; }
    public long? ParentId { get; }
    public string Name { get; }
    public string? Description { get; }
    public int SortOrder { get; }
    public int ArticleCount { get; }
    public LibraryScopeKind Scope { get; }
    public bool IsSmart { get; }
    public string Glyph { get; }
    public string FullPath { get; }
    public string CountText => ArticleCount > 0 ? ArticleCount.ToString("N0") : string.Empty;
    public string AccessibleName { get; }
    public ObservableCollection<TopicNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }
}
