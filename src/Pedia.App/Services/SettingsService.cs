using Microsoft.Extensions.Logging;
using System.Globalization;
using CoreAppSettings = Pedia.Core.Services.AppSettings;
using CoreAppearanceSettings = Pedia.Core.Services.AppearanceSettings;
using CoreAppTheme = Pedia.Core.Services.AppTheme;
using CoreFilterSettings = Pedia.Core.Services.FilterSettings;
using CoreGeneralSettings = Pedia.Core.Services.GeneralSettings;
using CoreLayoutSettings = Pedia.Core.Services.LayoutSettings;
using CoreReadingSettings = Pedia.Core.Services.ReadingSettings;
using CoreScrollSettings = Pedia.Core.Services.ScrollSettings;
using CoreSearchSettings = Pedia.Core.Services.SearchSettings;
using CoreSearchSortMode = Pedia.Core.Services.SearchSortMode;
using CoreSelectionSettings = Pedia.Core.Services.SelectionSettings;
using CoreSettingsService = Pedia.Core.Services.SettingsService;
using CoreUiDensity = Pedia.Core.Services.UiDensity;
using CoreWindowSettings = Pedia.Core.Services.WindowSettings;

namespace Pedia.Services;

public interface ISettingsService
{
    string PediaDirectory { get; }
    string SettingsPath { get; }
    PediaSettings Current { get; }
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);
}

public sealed class SettingsService(ILogger<SettingsService> logger) : ISettingsService
{
    public string PediaDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pedia");

    public string SettingsPath => Path.Combine(PediaDirectory, "Settings", "settings.json");

    public PediaSettings Current { get; private set; } = new();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = await new CoreSettingsService(SettingsPath).LoadAsync(cancellationToken);
            Current = FromCore(settings);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Could not access Pedia settings at {SettingsPath}", SettingsPath);
            Current = new PediaSettings();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await new CoreSettingsService(SettingsPath).SaveAsync(ToCore(Current), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Could not save Pedia settings to {SettingsPath}", SettingsPath);
            throw;
        }
    }

    private static PediaSettings FromCore(CoreAppSettings settings)
    {
        var current = new PediaSettings
        {
            DefaultLanguageCode = settings.General.Language,
            DefaultArticleStatus = settings.General.DefaultArticleStatus,
            RestoreLastArticle = settings.Selection.RestoreLastSelection,
            ConfirmBeforeTrash = settings.General.ConfirmDeletion,
            PageSize = settings.Search.PageSize,
            IncludeSubtopicsByDefault = settings.Search.IncludeSubtopics,
            ArticleBodyFontSize = settings.Reading.FontSize,
            ArticleLineSpacing = settings.Reading.LineHeight * settings.Reading.FontSize,
            MaximumReadingWidth = settings.Reading.ContentWidth,
            RememberScrollPositions = settings.Scroll.RememberPosition,
            CompactDensity = settings.Appearance.Density == CoreUiDensity.Compact,
            Window = new WindowLayoutState
            {
                X = (int)(settings.Window.X ?? 0),
                Y = (int)(settings.Window.Y ?? 0),
                Width = (int)settings.Window.Width,
                Height = (int)settings.Window.Height,
                IsMaximized = settings.Window.IsMaximized,
                TopicPaneWidth = settings.Layout.NavigationPaneWidth,
                ArticlePaneWidth = settings.Layout.DetailsPaneWidth,
                IsTopicPaneCollapsed = !settings.Layout.NavigationPaneVisible,
                SelectedTopicId = long.TryParse(settings.Selection.TopicId, out var topicId) ? topicId : null,
                SelectedArticleId = long.TryParse(settings.Selection.ArticleId, out var articleId) ? articleId : null,
                SearchQuery = settings.Filter.Query ?? string.Empty,
                SearchScope = ReadEnumTag(settings.Filter.Tags, "search-scope", Models.ArticleSearchScopeKind.AllText),
                SelectedLanguageCode = ReadTag(settings.Filter.Tags, "language"),
                IncludeEnglish = ReadBoolTag(settings.Filter.Tags, "include-en") ?? false,
                IncludeFinnish = ReadBoolTag(settings.Filter.Tags, "include-fi") ?? false,
                ArticleType = ReadTag(settings.Filter.Tags, "article-type"),
                ArticleStatus = ReadTag(settings.Filter.Tags, "article-status"),
                FavoritesOnly = ReadBoolTag(settings.Filter.Tags, "favorites") ?? false,
                HasSources = ReadBoolTag(settings.Filter.Tags, "has-sources"),
                MinimumWordCount = ReadIntTag(settings.Filter.Tags, "min-words"),
                MaximumWordCount = ReadIntTag(settings.Filter.Tags, "max-words"),
                CreatedFrom = ReadDateTag(settings.Filter.Tags, "created-from"),
                CreatedTo = ReadDateTag(settings.Filter.Tags, "created-to"),
                UpdatedFrom = settings.Filter.ModifiedFromUtc,
                UpdatedTo = settings.Filter.ModifiedToUtc,
                IsArchived = ReadBoolTag(settings.Filter.Tags, "archived"),
                IsSample = ReadBoolTag(settings.Filter.Tags, "sample"),
                SortField = ReadEnumTag(settings.Filter.Tags, "sort-field", Models.ArticleSortField.Relevance),
                SortDirection = ReadEnumTag(settings.Filter.Tags, "sort-direction", Models.SortDirection.Ascending),
                PageNumber = Math.Max(1, ReadIntTag(settings.Filter.Tags, "page") ?? 1),
                IncludeSubtopics = settings.Filter.Tags.Contains("pedia:include-subtopics:true", StringComparer.Ordinal)
                    || settings.Filter.Tags.Contains("pedia:include-subtopics", StringComparer.Ordinal)
                    || (!settings.Filter.Tags.Contains("pedia:include-subtopics:false", StringComparer.Ordinal)
                        && settings.Search.IncludeSubtopics)
            },
            ArticleScrollPositions = settings.Scroll.ArticleOffsets
                .Select(pair => (Key: long.TryParse(pair.Key, out var id) ? id : 0, pair.Value))
                .Where(pair => pair.Key > 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value)
        };
        return current;
    }

    private static CoreAppSettings ToCore(PediaSettings settings) => new(
        new CoreGeneralSettings(settings.DefaultLanguageCode, CheckForUpdates: false, settings.ConfirmBeforeTrash)
        {
            DefaultArticleStatus = settings.DefaultArticleStatus
        },
        new CoreReadingSettings(
            settings.ArticleBodyFontSize,
            settings.ArticleLineSpacing / Math.Max(settings.ArticleBodyFontSize, 1),
            settings.MaximumReadingWidth),
        new CoreAppearanceSettings(CoreAppTheme.Dark, null)
        {
            Density = settings.CompactDensity ? CoreUiDensity.Compact : CoreUiDensity.Comfortable
        },
        new CoreWindowSettings(
            settings.Window.X,
            settings.Window.Y,
            settings.Window.Width,
            settings.Window.Height,
            settings.Window.IsMaximized),
        new CoreLayoutSettings(
            settings.Window.TopicPaneWidth,
            settings.Window.ArticlePaneWidth,
            !settings.Window.IsTopicPaneCollapsed,
            DetailsPaneVisible: true),
        new CoreSearchSettings(
            SearchTitles: true,
            SearchContent: true,
            MaximumResults: settings.PageSize,
            CoreSearchSortMode.Relevance)
        {
            PageSize = settings.PageSize,
            IncludeSubtopics = settings.IncludeSubtopicsByDefault
        },
        new CoreFilterSettings(
            settings.Window.SearchQuery,
            CreateFilterTags(settings.Window),
            settings.Window.UpdatedFrom,
            settings.Window.UpdatedTo),
        new CoreSelectionSettings(
            settings.Window.SelectedTopicId?.ToString(),
            settings.Window.SelectedArticleId?.ToString())
        {
            RestoreLastSelection = settings.RestoreLastArticle
        },
        new CoreScrollSettings(
            settings.Window.SelectedArticleId?.ToString(),
            settings.Window.SelectedArticleId is { } articleId && settings.ArticleScrollPositions.TryGetValue(articleId, out var offset)
                ? offset
                : 0)
        {
            RememberPosition = settings.RememberScrollPositions,
            ArticleOffsets = settings.ArticleScrollPositions.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value)
        });

    private static string[] CreateFilterTags(WindowLayoutState state)
    {
        var tags = new List<string>
        {
            state.IncludeSubtopics ? "pedia:include-subtopics:true" : "pedia:include-subtopics:false",
            CreateTag("search-scope", state.SearchScope),
            CreateTag("include-en", state.IncludeEnglish),
            CreateTag("include-fi", state.IncludeFinnish),
            CreateTag("favorites", state.FavoritesOnly),
            CreateTag("sort-field", state.SortField),
            CreateTag("sort-direction", state.SortDirection)
        };
        tags.Add(CreateTag("page", Math.Max(1, state.PageNumber)));
        AddTag(tags, "language", state.SelectedLanguageCode);
        AddTag(tags, "article-type", state.ArticleType);
        AddTag(tags, "article-status", state.ArticleStatus);
        AddTag(tags, "has-sources", state.HasSources);
        AddTag(tags, "min-words", state.MinimumWordCount);
        AddTag(tags, "max-words", state.MaximumWordCount);
        AddTag(tags, "created-from", state.CreatedFrom?.ToUniversalTime().ToString("O"));
        AddTag(tags, "created-to", state.CreatedTo?.ToUniversalTime().ToString("O"));
        AddTag(tags, "archived", state.IsArchived);
        AddTag(tags, "sample", state.IsSample);
        return tags.ToArray();
    }

    private static void AddTag<T>(ICollection<string> tags, string key, T? value)
    {
        if (value is not null)
        {
            tags.Add(CreateTag(key, value));
        }
    }

    private static string CreateTag<T>(string key, T value)
    {
        var text = value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value?.ToString() ?? string.Empty;
        return $"pedia:{key}:{Uri.EscapeDataString(text)}";
    }

    private static string? ReadTag(IEnumerable<string> tags, string key)
    {
        var prefix = $"pedia:{key}:";
        var value = tags.FirstOrDefault(tag => tag.StartsWith(prefix, StringComparison.Ordinal));
        return value is null ? null : Uri.UnescapeDataString(value[prefix.Length..]);
    }

    private static bool? ReadBoolTag(IEnumerable<string> tags, string key) =>
        bool.TryParse(ReadTag(tags, key), out var value) ? value : null;

    private static int? ReadIntTag(IEnumerable<string> tags, string key) =>
        int.TryParse(ReadTag(tags, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static DateTimeOffset? ReadDateTag(IEnumerable<string> tags, string key) =>
        DateTimeOffset.TryParse(ReadTag(tags, key), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? value
            : null;

    private static T ReadEnumTag<T>(IEnumerable<string> tags, string key, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(ReadTag(tags, key), ignoreCase: true, out var value) ? value : fallback;
}
