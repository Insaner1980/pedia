using Pedia.Core.Services;
using Pedia.Core.Models;

namespace Pedia.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void Defaults_cover_persisted_application_state()
    {
        var settings = AppSettings.CreateDefault();

        Assert.Equal("en-US", settings.General.Language);
        Assert.Equal(ArticleStatuses.Draft, settings.General.DefaultArticleStatus);
        Assert.False(settings.General.CheckForUpdates);
        Assert.InRange(settings.Reading.FontSize, 10, 40);
        Assert.Equal(AppTheme.Dark, settings.Appearance.Theme);
        Assert.Equal(UiDensity.Comfortable, settings.Appearance.Density);
        Assert.Equal(1600, settings.Window.Width);
        Assert.Equal(950, settings.Window.Height);
        Assert.True(settings.Layout.NavigationPaneVisible);
        Assert.True(settings.Search.MaximumResults > 0);
        Assert.Equal(50, settings.Search.PageSize);
        Assert.True(settings.Search.IncludeSubtopics);
        Assert.Empty(settings.Filter.Tags);
        Assert.Null(settings.Selection.ArticleId);
        Assert.True(settings.Selection.RestoreLastSelection);
        Assert.Equal(0, settings.Scroll.VerticalOffset);
        Assert.True(settings.Scroll.RememberPosition);
        Assert.Empty(settings.Scroll.ArticleOffsets);
    }

    [Fact]
    public void Normalize_repairs_invalid_ranges_enums_and_state()
    {
        var settings = AppSettings.CreateDefault() with
        {
            General = new GeneralSettings("  ", true, true),
            Reading = new ReadingSettings(500, -1, double.NaN),
            Appearance = new AppearanceSettings((AppTheme)999, "not-a-color"),
            Window = new WindowSettings(double.NaN, double.PositiveInfinity, 10, 20, false),
            Layout = new LayoutSettings(-1, 50_000, true, true),
            Search = new SearchSettings(false, false, 0, (SearchSortMode)999),
            Filter = new FilterSettings(null, [" birds ", "birds", ""], null, null),
            Selection = new SelectionSettings(" ", " article "),
            Scroll = new ScrollSettings(" article ", -5)
            {
                RememberPosition = true,
                ArticleOffsets = new Dictionary<string, double>
                {
                    ["  article  "] = 42,
                    ["bad-negative"] = -2,
                    ["bad-finite"] = double.PositiveInfinity,
                    [" "] = 100
                }
            }
        };

        settings = settings with
        {
            General = settings.General with { DefaultArticleStatus = "Unknown" },
            Appearance = settings.Appearance with { Density = (UiDensity)999 },
            Search = settings.Search with { PageSize = 17 },
            Selection = settings.Selection with { RestoreLastSelection = false }
        };

        var normalized = settings.Normalize();

        Assert.Equal("en-US", normalized.General.Language);
        Assert.Equal(40, normalized.Reading.FontSize);
        Assert.Equal(AppTheme.Dark, normalized.Appearance.Theme);
        Assert.Null(normalized.Appearance.AccentColor);
        Assert.Null(normalized.Window.X);
        Assert.Null(normalized.Window.Y);
        Assert.Equal(640, normalized.Window.Width);
        Assert.Equal(100, normalized.Search.MaximumResults);
        Assert.Equal(SearchSortMode.Relevance, normalized.Search.SortMode);
        Assert.Equal(50, normalized.Search.PageSize);
        Assert.Equal(ArticleStatuses.Draft, normalized.General.DefaultArticleStatus);
        Assert.Equal(UiDensity.Comfortable, normalized.Appearance.Density);
        Assert.Equal(["birds"], normalized.Filter.Tags);
        Assert.Null(normalized.Selection.TopicId);
        Assert.Equal("article", normalized.Selection.ArticleId);
        Assert.Equal(0, normalized.Scroll.VerticalOffset);
        Assert.False(normalized.Selection.RestoreLastSelection);
        Assert.Equal(42, normalized.Scroll.ArticleOffsets["article"]);
        Assert.DoesNotContain("bad-negative", normalized.Scroll.ArticleOffsets.Keys);
    }

    [Fact]
    public async Task Settings_are_saved_and_loaded_as_normalized_json()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var service = new SettingsService(path);
        var settings = AppSettings.CreateDefault() with
        {
            General = new GeneralSettings("fi-FI", false, true),
            Selection = new SelectionSettings("topic-1", "article-2"),
            Scroll = new ScrollSettings("article-2", 123.5)
            {
                ArticleOffsets = new Dictionary<string, double> { ["article-2"] = 123.5 }
            }
        };

        await service.SaveAsync(settings, TestContext.Current.CancellationToken);
        var restored = await service.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("fi-FI", restored.General.Language);
        Assert.Equal("article-2", restored.Selection.ArticleId);
        Assert.Equal(123.5, restored.Scroll.VerticalOffset);
        Assert.Equal(123.5, restored.Scroll.ArticleOffsets["article-2"]);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task Missing_settings_return_defaults_but_malformed_json_is_reported()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var service = new SettingsService(path);

        Assert.Equal(AppSettings.CreateDefault(), await service.LoadAsync(TestContext.Current.CancellationToken));

        await File.WriteAllTextAsync(path, "{ definitely not json", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Syntactically_valid_partial_settings_are_completed_with_defaults()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(path, """{"general":{"language":"fi-FI","checkForUpdates":false,"confirmDeletion":true}}""", TestContext.Current.CancellationToken);
        var service = new SettingsService(path);

        var restored = await service.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("fi-FI", restored.General.Language);
        Assert.Equal(AppSettings.CreateDefault().Reading, restored.Reading);
        Assert.Equal(AppSettings.CreateDefault().Window, restored.Window);
    }
}
