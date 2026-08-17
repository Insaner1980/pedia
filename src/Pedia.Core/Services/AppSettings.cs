using System.Globalization;
using Pedia.Core.Models;

namespace Pedia.Core.Services;

public enum AppTheme
{
    System,
    Light,
    Dark
}

public enum SearchSortMode
{
    Relevance,
    Title,
    ModifiedNewest
}

public enum UiDensity
{
    Compact,
    Comfortable,
    Spacious
}

public sealed record GeneralSettings(string Language, bool CheckForUpdates, bool ConfirmDeletion)
{
    public string DefaultArticleStatus { get; init; } = ArticleStatuses.Draft;
}

public sealed record ReadingSettings(double FontSize, double LineHeight, double ContentWidth);

public sealed record AppearanceSettings(AppTheme Theme, string? AccentColor)
{
    public UiDensity Density { get; init; } = UiDensity.Comfortable;
}

public sealed record WindowSettings(double? X, double? Y, double Width, double Height, bool IsMaximized);

public sealed record LayoutSettings(double NavigationPaneWidth, double DetailsPaneWidth, bool NavigationPaneVisible, bool DetailsPaneVisible);

public sealed record SearchSettings(bool SearchTitles, bool SearchContent, int MaximumResults, SearchSortMode SortMode)
{
    public int PageSize { get; init; } = 50;

    public bool IncludeSubtopics { get; init; } = true;
}

public sealed record FilterSettings(string? Query, string[] Tags, DateTimeOffset? ModifiedFromUtc, DateTimeOffset? ModifiedToUtc);

public sealed record SelectionSettings(string? TopicId, string? ArticleId)
{
    public bool RestoreLastSelection { get; init; } = true;
}

public sealed record ScrollSettings(string? ArticleId, double VerticalOffset)
{
    public bool RememberPosition { get; init; } = true;

    public IReadOnlyDictionary<string, double> ArticleOffsets { get; init; } =
        new Dictionary<string, double>(StringComparer.Ordinal);
}

public sealed record AppSettings(
    GeneralSettings General,
    ReadingSettings Reading,
    AppearanceSettings Appearance,
    WindowSettings Window,
    LayoutSettings Layout,
    SearchSettings Search,
    FilterSettings Filter,
    SelectionSettings Selection,
    ScrollSettings Scroll)
{
    private static readonly IReadOnlyDictionary<string, double> EmptyArticleOffsets =
        new Dictionary<string, double>(StringComparer.Ordinal);

    public static AppSettings CreateDefault() => new(
        new GeneralSettings("en-US", false, true),
        new ReadingSettings(16, 1.5, 760),
        new AppearanceSettings(AppTheme.Dark, null),
        new WindowSettings(null, null, 1600, 950, false),
        new LayoutSettings(290, 560, true, true),
        new SearchSettings(true, true, 100, SearchSortMode.Relevance),
        new FilterSettings(null, Array.Empty<string>(), null, null),
        new SelectionSettings(null, null),
        new ScrollSettings(null, 0) { ArticleOffsets = EmptyArticleOffsets });

    public AppSettings Normalize()
    {
        var defaults = CreateDefault();
        var general = General ?? defaults.General;
        var reading = Reading ?? defaults.Reading;
        var appearance = Appearance ?? defaults.Appearance;
        var window = Window ?? defaults.Window;
        var layout = Layout ?? defaults.Layout;
        var search = Search ?? defaults.Search;
        var filter = Filter ?? defaults.Filter;
        var selection = Selection ?? defaults.Selection;
        var scroll = Scroll ?? defaults.Scroll;

        var language = IsCultureName(general.Language) ? general.Language.Trim() : defaults.General.Language;
        var fontSize = ClampFinite(reading.FontSize, 10, 40, defaults.Reading.FontSize);
        var lineHeight = ClampFinite(reading.LineHeight, 1, 3, defaults.Reading.LineHeight);
        var contentWidth = ClampFinite(reading.ContentWidth, 320, 1600, defaults.Reading.ContentWidth);
        var defaultArticleStatus = IsValidArticleStatus(general.DefaultArticleStatus)
            ? general.DefaultArticleStatus.Trim()
            : defaults.General.DefaultArticleStatus;
        var theme = Enum.IsDefined(appearance.Theme) ? appearance.Theme : defaults.Appearance.Theme;
        var density = Enum.IsDefined(appearance.Density) ? appearance.Density : defaults.Appearance.Density;
        var accent = NormalizeAccentColor(appearance.AccentColor);
        var windowX = FiniteOrNull(window.X);
        var windowY = FiniteOrNull(window.Y);
        var width = ClampFinite(window.Width, 640, 7680, defaults.Window.Width);
        var height = ClampFinite(window.Height, 480, 4320, defaults.Window.Height);
        var navigationWidth = ClampFinite(layout.NavigationPaneWidth, 160, 800, defaults.Layout.NavigationPaneWidth);
        var detailsWidth = ClampFinite(layout.DetailsPaneWidth, 200, 1200, defaults.Layout.DetailsPaneWidth);
        var maximumResults = search.MaximumResults is >= 1 and <= 1000
            ? search.MaximumResults
            : defaults.Search.MaximumResults;
        var pageSize = search.PageSize is 25 or 50 or 100 ? search.PageSize : defaults.Search.PageSize;
        var sortMode = Enum.IsDefined(search.SortMode) ? search.SortMode : defaults.Search.SortMode;
        var query = NullIfWhiteSpace(filter.Query);
        var tags = (filter.Tags ?? Array.Empty<string>())
            .Select(tag => tag?.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();
        var from = filter.ModifiedFromUtc?.ToUniversalTime();
        var to = filter.ModifiedToUtc?.ToUniversalTime();
        if (from > to)
        {
            (from, to) = (to, from);
        }

        var articleOffsets = (scroll.ArticleOffsets ?? new Dictionary<string, double>())
            .Select(pair => new KeyValuePair<string, double>(pair.Key?.Trim() ?? string.Empty, pair.Value))
            .Where(pair => pair.Key.Length > 0 && double.IsFinite(pair.Value) && pair.Value >= 0)
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);

        return this with
        {
            General = general with { Language = language, DefaultArticleStatus = defaultArticleStatus },
            Reading = new ReadingSettings(fontSize, lineHeight, contentWidth),
            Appearance = new AppearanceSettings(theme, accent) { Density = density },
            Window = new WindowSettings(windowX, windowY, width, height, window.IsMaximized),
            Layout = new LayoutSettings(navigationWidth, detailsWidth, layout.NavigationPaneVisible, layout.DetailsPaneVisible),
            Search = search with { MaximumResults = maximumResults, SortMode = sortMode, PageSize = pageSize },
            Filter = new FilterSettings(query, tags, from, to),
            Selection = new SelectionSettings(NullIfWhiteSpace(selection.TopicId), NullIfWhiteSpace(selection.ArticleId))
            {
                RestoreLastSelection = selection.RestoreLastSelection
            },
            Scroll = new ScrollSettings(NullIfWhiteSpace(scroll.ArticleId), Math.Max(0, FiniteOrDefault(scroll.VerticalOffset, 0)))
            {
                RememberPosition = scroll.RememberPosition,
                ArticleOffsets = articleOffsets
            }
        };
    }

    private static bool IsValidArticleStatus(string? value) =>
        value?.Trim() is ArticleStatuses.Draft or ArticleStatuses.Ready or ArticleStatuses.NeedsReview or ArticleStatuses.Archived;

    private static bool IsCultureName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            _ = CultureInfo.GetCultureInfo(value.Trim());
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static string? NormalizeAccentColor(string? value)
    {
        var trimmed = NullIfWhiteSpace(value);
        if (trimmed is null || trimmed.Length is not (7 or 9) || trimmed[0] != '#')
        {
            return null;
        }

        return trimmed[1..].All(Uri.IsHexDigit) ? trimmed.ToUpperInvariant() : null;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static double? FiniteOrNull(double? value) =>
        value is { } actual && double.IsFinite(actual) ? actual : null;

    private static double FiniteOrDefault(double value, double fallback) =>
        double.IsFinite(value) ? value : fallback;

    private static double ClampFinite(double value, double minimum, double maximum, double fallback) =>
        Math.Clamp(FiniteOrDefault(value, fallback), minimum, maximum);
}
