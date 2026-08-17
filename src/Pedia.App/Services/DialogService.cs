using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Pedia.Models;
using Pedia.ViewModels;

namespace Pedia.Services;

public sealed class DialogService(IStringService strings) : IDialogService
{
    private XamlRoot? _xamlRoot;

    public void AttachXamlRoot(XamlRoot xamlRoot) => _xamlRoot = xamlRoot;

    public Task ShowErrorAsync(string message) => ShowMessageAsync(strings.Get("ErrorTitle"), message);

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = CreateDialog(title);
        dialog.Content = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520
        };
        dialog.CloseButtonText = strings.Get("CloseButtonText");
        await dialog.ShowAsync();
    }

    public async Task<bool> ConfirmAsync(string title, string message, string primaryButtonText)
    {
        var dialog = CreateDialog(title);
        dialog.Content = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520
        };
        dialog.PrimaryButtonText = primaryButtonText;
        dialog.CloseButtonText = strings.Get("CancelButtonText");
        dialog.DefaultButton = ContentDialogButton.Close;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task<UnsavedChangesChoice> ConfirmUnsavedChangesAsync()
    {
        var dialog = CreateDialog(strings.Get("UnsavedChangesTitle"));
        dialog.Content = new TextBlock
        {
            Text = strings.Get("UnsavedChangesMessage"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520
        };
        dialog.PrimaryButtonText = strings.Get("SaveButtonText");
        dialog.SecondaryButtonText = strings.Get("DiscardButtonText");
        dialog.CloseButtonText = strings.Get("CancelButtonText");
        dialog.DefaultButton = ContentDialogButton.Primary;
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => UnsavedChangesChoice.Save,
            ContentDialogResult.Secondary => UnsavedChangesChoice.Discard,
            _ => UnsavedChangesChoice.Cancel
        };
    }

    public async Task<TopicDialogResult?> ShowTopicEditorAsync(
        string title,
        string? name = null,
        string? description = null)
    {
        var nameBox = new TextBox
        {
            Header = strings.Get("NameFieldHeader"),
            Text = name ?? string.Empty,
            MaxLength = 200,
            SelectionStart = 0,
            SelectionLength = name?.Length ?? 0
        };
        var descriptionBox = new TextBox
        {
            Header = strings.Get("DescriptionFieldHeader"),
            Text = description ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 90,
            MaxLength = 2000
        };
        var validation = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PediaSecondaryTextBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        var panel = new StackPanel { Spacing = 12, Width = 440 };
        panel.Children.Add(nameBox);
        panel.Children.Add(descriptionBox);
        panel.Children.Add(validation);

        var dialog = CreateDialog(title);
        dialog.Content = panel;
        dialog.PrimaryButtonText = strings.Get("SaveButtonText");
        dialog.CloseButtonText = strings.Get("CancelButtonText");
        dialog.DefaultButton = ContentDialogButton.Primary;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                args.Cancel = true;
                validation.Text = strings.Get("TopicNameRequiredText");
                validation.Visibility = Visibility.Visible;
                nameBox.Focus(FocusState.Programmatic);
            }
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return new TopicDialogResult(nameBox.Text.Trim(), NullIfWhiteSpace(descriptionBox.Text));
    }

    public async Task<TopicNodeViewModel?> ChooseTopicAsync(
        string title,
        IReadOnlyList<TopicNodeViewModel> topics,
        long? selectedTopicId = null)
    {
        var searchBox = new TextBox { PlaceholderText = strings.Get("SearchTopicsPlaceholder") };
        var tree = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.Single,
            Height = 340
        };
        var panel = new StackPanel { Spacing = 10, Width = 480 };
        panel.Children.Add(searchBox);
        panel.Children.Add(tree);

        var dialog = CreateDialog(title);
        dialog.Content = panel;
        dialog.PrimaryButtonText = strings.Get("ContinueButtonText");
        dialog.CloseButtonText = strings.Get("CancelButtonText");
        dialog.DefaultButton = ContentDialogButton.Primary;
        var currentTopicId = selectedTopicId;
        var rebuilding = false;

        void RebuildTree()
        {
            rebuilding = true;
            var nodes = PopulateTopicTree(tree, topics, searchBox.Text, CreateTopicLabel);
            tree.SelectedNode = currentTopicId is { } id && nodes.TryGetValue(id, out var selectedNode)
                ? selectedNode
                : null;
            ExpandAncestors(tree.SelectedNode);
            dialog.IsPrimaryButtonEnabled = tree.SelectedNode is not null;
            rebuilding = false;
        }

        tree.SelectionChanged += (_, args) =>
        {
            if (!rebuilding)
            {
                currentTopicId = tree.SelectedNode?.Content is FrameworkElement element && element.Tag is long id
                    ? id
                    : null;
            }
            dialog.IsPrimaryButtonEnabled = tree.SelectedNode is not null;
        };
        searchBox.TextChanged += (_, _) => RebuildTree();
        RebuildTree();

        return await dialog.ShowAsync() == ContentDialogResult.Primary && currentTopicId is { } chosenId
            ? topics.FirstOrDefault(topic => topic.Id == chosenId)
            : null;
    }

    public async Task<IReadOnlyList<TopicNodeViewModel>?> ChooseTopicsAsync(
        IReadOnlyList<TopicNodeViewModel> topics,
        IReadOnlySet<long> selectedTopicIds)
    {
        var searchBox = new TextBox { PlaceholderText = strings.Get("SearchTopicsByPathPlaceholder") };
        var selectedIds = selectedTopicIds.ToHashSet();
        var tree = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.None,
            Height = 340
        };

        var panel = new StackPanel { Spacing = 10, Width = 500 };
        panel.Children.Add(searchBox);
        panel.Children.Add(tree);

        var dialog = CreateDialog(strings.Get("AssignTopicsTitle"));
        dialog.Content = panel;
        dialog.PrimaryButtonText = strings.Get("SaveButtonText");
        dialog.CloseButtonText = strings.Get("CancelButtonText");
        dialog.DefaultButton = ContentDialogButton.Primary;

        void RebuildTree()
        {
            PopulateTopicTree(tree, topics, searchBox.Text, topic =>
            {
                var checkBox = new CheckBox
                {
                    Content = topic.Name,
                    IsChecked = selectedIds.Contains(topic.Id),
                    Tag = topic.Id
                };
                ToolTipService.SetToolTip(checkBox, topic.FullPath);
                AutomationProperties.SetName(checkBox, topic.AccessibleName);
                checkBox.Checked += (_, _) => selectedIds.Add(topic.Id);
                checkBox.Unchecked += (_, _) => selectedIds.Remove(topic.Id);
                return checkBox;
            });
        }

        searchBox.TextChanged += (_, _) => RebuildTree();
        RebuildTree();

        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? topics.Where(topic => selectedIds.Contains(topic.Id)).ToArray()
            : null;
    }

    public async Task<ImportDialogResult?> ShowImportPreviewAsync(
        ImportPreviewResult preview,
        IReadOnlyList<TopicNodeViewModel> topics,
        long? defaultTopicId)
    {
        var summary = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["PediaSecondaryTextBrush"]
        };
        var topicOptions = topics
            .Select(topic => new ImportDestinationOption(topic.Id, topic.FullPath))
            .Prepend(new ImportDestinationOption(null, strings.Get("UncategorizedOption")))
            .ToArray();
        var topicBox = new ComboBox
        {
            Header = strings.Get("DestinationTopicHeader"),
            ItemsSource = topicOptions,
            DisplayMemberPath = nameof(ImportDestinationOption.Label),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        topicBox.SelectedItem = topicOptions.FirstOrDefault(option => option.TopicId == defaultTopicId)
            ?? topicOptions[0];
        var languageBox = new TextBox
        {
            Header = strings.Get("LanguageCodeHeader"),
            Text = "en",
            MaxLength = 35
        };
        var statusBox = new ComboBox
        {
            Header = strings.Get("InitialStatusHeader"),
            ItemsSource = new[]
            {
                new ValueLabelOption("Draft", strings.Get("DraftStatus")),
                new ValueLabelOption("Ready", strings.Get("ReadyStatus")),
                new ValueLabelOption("Needs review", strings.Get("NeedsReviewStatus")),
                new ValueLabelOption("Archived", strings.Get("ArchivedStatus"))
            },
            DisplayMemberPath = nameof(ValueLabelOption.Label),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var duplicateBox = new ComboBox
        {
            Header = strings.Get("DuplicateHandlingHeader"),
            ItemsSource = new[]
            {
                new KeyValuePair<ImportDuplicateHandling, string>(
                    ImportDuplicateHandling.Skip,
                    strings.Get("ImportDuplicateSkipOption")),
                new KeyValuePair<ImportDuplicateHandling, string>(
                    ImportDuplicateHandling.CreateCopy,
                    strings.Get("ImportDuplicateCreateCopyOption")),
                new KeyValuePair<ImportDuplicateHandling, string>(
                    ImportDuplicateHandling.Replace,
                    strings.Get("ImportDuplicateReplaceOption"))
            },
            DisplayMemberPath = nameof(KeyValuePair<ImportDuplicateHandling, string>.Value),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var previewList = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            Height = 280,
            Background = (Brush)Application.Current.Resources["PediaSecondarySurfaceBrush"],
            BorderBrush = (Brush)Application.Current.Resources["PediaPrimaryBorderBrush"],
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0)
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(previewList, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollMode(previewList, ScrollMode.Enabled);
        ScrollViewer.SetVerticalScrollBarVisibility(previewList, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollMode(previewList, ScrollMode.Enabled);

        var options = new Grid { ColumnSpacing = 10 };
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        Grid.SetColumn(languageBox, 1);
        Grid.SetColumn(statusBox, 2);
        Grid.SetColumn(duplicateBox, 3);
        options.Children.Add(topicBox);
        options.Children.Add(languageBox);
        options.Children.Add(statusBox);
        options.Children.Add(duplicateBox);

        var panel = new StackPanel { Spacing = 10, Width = 1000 };
        panel.Children.Add(summary);
        panel.Children.Add(previewList);
        panel.Children.Add(options);

        var dialog = CreateDialog(strings.Get("ImportPreviewTitle"));
        dialog.MaxWidth = 1080;
        dialog.Content = panel;
        dialog.PrimaryButtonText = strings.Get("ImportButtonText");
        dialog.CloseButtonText = strings.Get("CancelButtonText");
        dialog.DefaultButton = ContentDialogButton.Primary;

        var columnWidths = new[] { 150d, 90d, 180d, 160d, 110d, 80d, 110d, 220d };
        var primaryTextBrush = (Brush)Application.Current.Resources["PediaPrimaryTextBrush"];
        var secondaryTextBrush = (Brush)Application.Current.Resources["PediaSecondaryTextBrush"];
        var elevatedSurfaceBrush = (Brush)Application.Current.Resources["PediaElevatedSurfaceBrush"];
        var dividerBrush = (Brush)Application.Current.Resources["PediaSubtleDividerBrush"];

        ListViewItem CreateRow(IReadOnlyList<string> values, bool isHeader, string? filePath = null)
        {
            var row = new Grid
            {
                Width = columnWidths.Sum(),
                Background = isHeader ? elevatedSurfaceBrush : null
            };
            foreach (var width in columnWidths)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
            }

            for (var column = 0; column < values.Count; column++)
            {
                var text = new TextBlock
                {
                    Text = values[column],
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = isHeader ? secondaryTextBrush : primaryTextBrush,
                    FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
                };
                if (!isHeader)
                {
                    ToolTipService.SetToolTip(text, column == 0 ? filePath : values[column]);
                }

                var cell = new Border
                {
                    Padding = new Thickness(8, 0, 8, 0),
                    BorderBrush = dividerBrush,
                    BorderThickness = new Thickness(0, 0, column == values.Count - 1 ? 0 : 1, 1),
                    Child = text
                };
                Grid.SetColumn(cell, column);
                row.Children.Add(cell);
            }

            return new ListViewItem
            {
                Content = row,
                Padding = new Thickness(0),
                MinHeight = isHeader ? 34 : 40,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                IsTabStop = false
            };
        }

        ImportDuplicateHandling GetDuplicateHandling() =>
            duplicateBox.SelectedItem is KeyValuePair<ImportDuplicateHandling, string> option
                ? option.Key
                : ImportDuplicateHandling.Skip;

        ImportPreviewAction GetAction(ImportPreviewItem item, ImportDuplicateHandling duplicateHandling) => item.Error is not null
            ? ImportPreviewAction.Failed
            : !item.HasTitleConflict
                ? ImportPreviewAction.Import
                : duplicateHandling switch
                {
                    ImportDuplicateHandling.CreateCopy => ImportPreviewAction.CreateCopy,
                    ImportDuplicateHandling.Replace => ImportPreviewAction.Replace,
                    _ => ImportPreviewAction.Skip
                };

        string GetActionText(ImportPreviewAction action) => strings.Get(action switch
        {
            ImportPreviewAction.Import => "ImportActionImport",
            ImportPreviewAction.CreateCopy => "ImportActionCreateCopy",
            ImportPreviewAction.Replace => "ImportActionReplace",
            ImportPreviewAction.Skip => "ImportActionSkip",
            _ => "ImportActionFailed"
        });

        void RefreshPreview()
        {
            var target = (topicBox.SelectedItem as ImportDestinationOption)?.Label
                ?? strings.Get("UncategorizedOption");
            var status = (statusBox.SelectedItem as ValueLabelOption)?.Label ?? strings.Get("DraftStatus");
            var duplicateHandling = GetDuplicateHandling();
            var actions = preview.Items.Select(item => GetAction(item, duplicateHandling)).ToArray();

            summary.Text = strings.Format(
                "ImportPreviewSummaryFormat",
                preview.Items.Count,
                actions.Count(action => action == ImportPreviewAction.Import),
                actions.Count(action => action == ImportPreviewAction.CreateCopy),
                actions.Count(action => action == ImportPreviewAction.Replace),
                actions.Count(action => action == ImportPreviewAction.Skip),
                actions.Count(action => action == ImportPreviewAction.Failed));
            dialog.IsPrimaryButtonEnabled = actions.Any(action =>
                action is ImportPreviewAction.Import or ImportPreviewAction.CreateCopy or ImportPreviewAction.Replace);

            previewList.Items.Clear();
            previewList.Items.Add(CreateRow(
                [
                    strings.Get("FileColumnText"),
                    strings.Get("TypeColumnText"),
                    strings.Get("TitleColumnText"),
                    strings.Get("TargetColumnText"),
                    strings.Get("StatusColumnText"),
                    strings.Get("ConflictColumnText"),
                    strings.Get("ActionColumnText"),
                    strings.Get("ErrorColumnText")
                ],
                isHeader: true));
            for (var index = 0; index < preview.Items.Count; index++)
            {
                var item = preview.Items[index];
                previewList.Items.Add(CreateRow(
                    [
                        item.FileName,
                        item.Format,
                        item.ProposedTitle,
                        target,
                        status,
                        strings.Get(item.HasTitleConflict ? "YesText" : "NoText"),
                        GetActionText(actions[index]),
                        item.Error ?? strings.Get("NoneText")
                    ],
                    isHeader: false,
                    filePath: item.FilePath));
            }
        }

        topicBox.SelectionChanged += (_, _) => RefreshPreview();
        statusBox.SelectionChanged += (_, _) => RefreshPreview();
        duplicateBox.SelectionChanged += (_, _) => RefreshPreview();
        RefreshPreview();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        var result = new ImportDialogResult(
            (topicBox.SelectedItem as ImportDestinationOption)?.TopicId,
            string.IsNullOrWhiteSpace(languageBox.Text) ? "en" : languageBox.Text.Trim(),
            (statusBox.SelectedItem as ValueLabelOption)?.Value ?? "Draft",
            GetDuplicateHandling());

        if (result.DuplicateHandling == ImportDuplicateHandling.Replace &&
            !await ConfirmAsync(
                strings.Get("ConfirmReplacementTitle"),
                strings.Get("ConfirmReplacementMessage"),
                strings.Get("ReplaceAndImportButton")))
        {
            return null;
        }

        return result;
    }

    public async Task<ExportFormat?> ChooseExportFormatAsync()
    {
        var formatBox = new ComboBox
        {
            Header = strings.Get("ExportFormatHeader"),
            ItemsSource = new[]
            {
                strings.Get("PlainTextFormat"),
                strings.Get("MarkdownFormat"),
                strings.Get("PediaJsonFormat")
            },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Width = 360
        };
        var dialog = CreateDialog(strings.Get("ExportArticleTitle"));
        dialog.Content = formatBox;
        dialog.PrimaryButtonText = strings.Get("ContinueButtonText");
        dialog.CloseButtonText = strings.Get("CancelButtonText");
        dialog.DefaultButton = ContentDialogButton.Primary;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return formatBox.SelectedIndex switch
        {
            1 => ExportFormat.Markdown,
            2 => ExportFormat.PediaJson,
            _ => ExportFormat.PlainText
        };
    }

    public async Task<string?> ChooseArticleStatusAsync()
    {
        var statuses = new[]
        {
            new ValueLabelOption("Draft", strings.Get("DraftStatus")),
            new ValueLabelOption("Ready", strings.Get("ReadyStatus")),
            new ValueLabelOption("Needs review", strings.Get("NeedsReviewStatus")),
            new ValueLabelOption("Archived", strings.Get("ArchivedStatus"))
        };
        var statusBox = new ComboBox
        {
            Header = strings.Get("ArticleStatusFieldHeader"),
            ItemsSource = statuses,
            DisplayMemberPath = nameof(ValueLabelOption.Label),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Width = 360
        };
        var dialog = CreateDialog(strings.Get("ChangeArticleStatusTitle"));
        dialog.Content = statusBox;
        dialog.PrimaryButtonText = strings.Get("ApplyButtonText");
        dialog.CloseButtonText = strings.Get("CancelButtonText");
        dialog.DefaultButton = ContentDialogButton.Primary;

        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? (statusBox.SelectedItem as ValueLabelOption)?.Value
            : null;
    }

    private static IReadOnlyDictionary<long, TreeViewNode> PopulateTopicTree(
        TreeView tree,
        IReadOnlyList<TopicNodeViewModel> topics,
        string searchText,
        Func<TopicNodeViewModel, FrameworkElement> contentFactory)
    {
        var topicsById = topics.ToDictionary(topic => topic.Id);
        var query = searchText.Trim();
        var visibleIds = string.IsNullOrEmpty(query)
            ? topicsById.Keys.ToHashSet()
            : topics
                .Where(topic => topic.FullPath.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .Select(topic => topic.Id)
                .ToHashSet();

        if (!string.IsNullOrEmpty(query))
        {
            foreach (var topicId in visibleIds.ToArray())
            {
                var current = topicsById[topicId];
                while (current.ParentId is { } parentId && topicsById.TryGetValue(parentId, out current))
                {
                    visibleIds.Add(current.Id);
                }
            }
        }

        tree.RootNodes.Clear();
        var nodes = new Dictionary<long, TreeViewNode>();
        foreach (var topic in topics.Where(topic => visibleIds.Contains(topic.Id)))
        {
            nodes.Add(topic.Id, new TreeViewNode
            {
                Content = contentFactory(topic),
                IsExpanded = !string.IsNullOrEmpty(query) || topic.IsExpanded
            });
        }

        foreach (var topic in topics.Where(topic => visibleIds.Contains(topic.Id)))
        {
            var node = nodes[topic.Id];
            if (topic.ParentId is { } parentId && nodes.TryGetValue(parentId, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                tree.RootNodes.Add(node);
            }
        }

        return nodes;
    }

    private static TextBlock CreateTopicLabel(TopicNodeViewModel topic)
    {
        var label = new TextBlock
        {
            Text = topic.Name,
            Tag = topic.Id,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        ToolTipService.SetToolTip(label, topic.FullPath);
        AutomationProperties.SetName(label, topic.AccessibleName);
        return label;
    }

    private static void ExpandAncestors(TreeViewNode? node)
    {
        for (var parent = node?.Parent; parent is not null; parent = parent.Parent)
        {
            parent.IsExpanded = true;
        }
    }

    private ContentDialog CreateDialog(string title)
    {
        if (_xamlRoot is null)
        {
            throw new InvalidOperationException("The dialog service has not been attached to a XAML root.");
        }

        return new ContentDialog
        {
            XamlRoot = _xamlRoot,
            Title = title,
            RequestedTheme = ElementTheme.Dark
        };
    }

    private static string? NullIfWhiteSpace(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ImportDestinationOption(long? TopicId, string Label);

    private enum ImportPreviewAction
    {
        Import,
        CreateCopy,
        Replace,
        Skip,
        Failed
    }
}
