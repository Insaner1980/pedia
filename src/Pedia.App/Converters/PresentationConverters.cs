using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Pedia.Converters;

public sealed partial class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Visible;
}

public sealed partial class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is not Visibility.Visible;
}

public sealed partial class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class HeadingLevelToFontSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        1 => 25d,
        3 => 19d,
        _ => 22d
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class DateTimeOffsetToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        DateTimeOffset date => date.ToLocalTime().ToString("g"),
        _ => string.Empty
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class StoredValueToDisplayConverter : IValueConverter
{
    private readonly ResourceLoader _resources = new();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string storedValue)
        {
            return string.Empty;
        }

        var resourceKey = storedValue switch
        {
            "General" => "GeneralArticleType",
            "Person" => "PersonArticleType",
            "Place" => "PlaceArticleType",
            "Event" => "EventArticleType",
            "Concept" => "ConceptArticleType",
            "Organization" => "OrganizationArticleType",
            "Timeline" => "TimelineArticleType",
            "Draft" => "DraftStatus",
            "Ready" => "ReadyStatus",
            "Needs review" => "NeedsReviewStatus",
            "Archived" => "ArchivedStatus",
            "Manual" => "ManualSourceType",
            "Local text file" => "LocalTextFileSourceType",
            "Local Markdown file" => "LocalMarkdownFileSourceType",
            "Book" => "BookSourceType",
            "Website" => "WebsiteSourceType",
            "Encyclopedia" => "EncyclopediaSourceType",
            "Other" => "OtherSourceType",
            _ => null
        };
        return resourceKey is null ? storedValue : _resources.GetString(resourceKey);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
