# Pedia — current product and implementation reference

## 1. Purpose and authority

This document is the detailed reference for the Pedia implementation currently present in this workspace. It is intended for code review, architecture questions, UI work, regression analysis, and change-impact assessment.

The source code, project files, manifests, resources, and executable tests are authoritative. This document describes implemented behavior; it is not a roadmap. The root `pedia-ui-reference.png` is a visual design reference, not runtime evidence. Where that image and the XAML differ, the XAML and ViewModels define the current application.

Evidence labels used here:

- **Implemented** means the behavior is directly represented by current source/configuration.
- **Tested** means a current automated test exercises the stated contract.
- **Runtime gate** means source/build/test inspection is not sufficient and a packaged or unpackaged Windows run is still required.
- **Not implemented** means no current production path provides the behavior.

## 2. Product definition

Pedia is a private, local knowledge library for Windows. It is a native C#/XAML WinUI 3 desktop application backed by SQLite. Its working model is a long-lived library of articles organized through a nested topic hierarchy, with local search, local import/export, backups, and persisted desktop state.

Implemented product scope:

- A dark, grayscale, three-pane workspace containing a topic tree, article browser, and article reader/editor.
- Topic and article creation and maintenance, article sources, many-to-many topic assignment, favorites, Trash, restore, and permanent deletion from Trash.
- Five presentation-only smart scopes: All articles, Favorites, Recently edited, Uncategorized, and Trash.
- Database-side counting, filtering, sorting, paging, recursive topic scope, and FTS5 search.
- Multiple-file local TXT/Markdown import with preview and duplicate policy.
- Plain-text, Markdown, and versioned Pedia JSON export.
- Versioned local database backup, validation, safety backup, restore, rollback, and search-index rebuild.
- Persisted settings, window/layout state, filter state, current page, selection, and remembered article scroll offsets.
- A first-run sample library and explicit sample-content deletion.
- Local information-and-higher file logging.
- Core integration tests and linked, platform-independent ViewModel tests.

## 3. Explicit non-goals and non-claims

The current product does **not** implement:

- Wikipedia, MediaWiki, Wikimedia dump, Kiwix, ZIM, HTTP download, scraping, crawling, URL extraction, article retrieval, or automatic source refresh.
- WebView or embedded-browser article rendering.
- Remote synchronization, cloud storage, accounts, sign-in, shared libraries, collaboration, or conflict resolution.
- Telemetry, analytics, advertising, subscriptions, payments, or billing.
- AI, language-model integration, generated summaries, recommendations, embeddings, vector search, chat, automatic classification, or automatic topic generation.
- Background network activity, a provider interface, a registered external-content adapter, or a declared Internet capability.
- Topic drag-and-drop.
- Article-body autosave.
- Automatic monitoring of imported files after import.
- Localization beyond the checked-in `en-US` resource set.

Stored source URLs are inert metadata. `Open source` validates an absolute URI and invokes the Windows default handler only after an explicit user action. `Copy source` places the stored URL text on the clipboard. Import never dereferences links or loads remote media.

The MSIX manifest declares only the restricted `runFullTrust` capability; it does not declare an Internet capability. This is configuration evidence, not a general claim that every future code change is offline-safe.

## 4. Technology and build contract

| Concern | Current implementation |
|---|---|
| UI | WinUI 3 / Windows App SDK, XAML, native Windows windowing APIs |
| Language/runtime | C# with nullable enabled, implicit usings, `LangVersion=latest`, deterministic builds |
| Application target | `net8.0-windows10.0.19041.0`, x64, `win-x64` |
| Minimum Windows version | `10.0.17763.0` (Windows 10 version 1809) |
| Core target | `net8.0`, no WinUI reference |
| MVVM | CommunityToolkit.Mvvm generated observable properties and commands |
| Composition | Microsoft.Extensions.DependencyInjection, singleton graph, scope validation |
| Persistence | Microsoft.Data.Sqlite, explicit parameterized SQL, SQLite FTS5 |
| Settings | Normalized indented JSON under LocalAppData |
| Tests | xUnit/VSTest, x64 Windows target, temporary SQLite databases |
| Packaging | MSIX tooling enabled; packaged and unpackaged launch profiles |
| Publish profile | self-contained `win-x64`, ReadyToRun, not single-file, not trimmed |

Direct package versions are centrally pinned in `Directory.Packages.props`:

| Package | Version | Production/test role |
|---|---:|---|
| `Microsoft.WindowsAppSDK` | 2.3.1 | WinUI 3 runtime and APIs |
| `Microsoft.Windows.SDK.BuildTools` | 10.0.26100.4654 | Windows build tooling |
| `CommunityToolkit.Mvvm` | 8.4.2 | observable state and commands |
| `Microsoft.Data.Sqlite` | 10.0.11 | SQLite, transactions, online backup, FTS5 access |
| `Microsoft.Extensions.DependencyInjection` | 10.0.11 | composition root |
| `Microsoft.Extensions.Logging.Abstractions` / `.Debug` | 10.0.11 | logging contracts and debug sink |
| `Microsoft.NET.Test.Sdk` | 18.8.1 | test host |
| `xunit` | 2.9.3 | test framework |
| `xunit.runner.visualstudio` | 3.1.5 | test adapter |
| `coverlet.collector` | 10.0.1 | optional coverage collection |

Canonical PowerShell commands from the repository root:

```powershell
dotnet restore .\Pedia.sln -p:Platform=x64
dotnet build .\Pedia.sln -c Debug -p:Platform=x64 --no-restore
dotnet build .\Pedia.sln -c Release -p:Platform=x64 --no-restore
dotnet test .\tests\Pedia.Tests\Pedia.Tests.csproj -c Debug --no-restore -p:Platform=x64
dotnet test .\tests\Pedia.Tests\Pedia.Tests.csproj -c Release --no-restore -p:Platform=x64
```

Unpackaged command-line launch:

```powershell
dotnet run --project .\src\Pedia.App\Pedia.App.csproj -c Debug -p:Platform=x64 --launch-profile "Pedia.App (Unpackaged)"
```

The launch profiles are `Pedia.App (Package)` (`MsixPackage`) and `Pedia.App (Unpackaged)` (`Project`). Packaging, signing, deployment, Windows App Runtime availability, title-bar behavior, native pickers, clipboard integration, URI launch, and multi-monitor/DPI behavior remain runtime gates.

## 5. Solution architecture and dependency direction

```text
WinUI views and MainWindow code-behind
                |
                v
CommunityToolkit.Mvvm ViewModels
                |
                v
Pedia.App presentation services and models
                |
                v
Pedia.Core repositories and services
                |
                v
SQLite + user-selected local files + settings/log files
```

Dependency rules:

- `Pedia.App` references `Pedia.Core`.
- `Pedia.Core` does not reference WinUI, `Pedia.App`, or the test project.
- `Pedia.Tests` references `Pedia.Core` and links selected production presentation models/ViewModels/settings files directly. It does not reference or start `Pedia.App` as a WinUI application.
- Native window, picker, XAML dialog, clipboard, folder-launch, and URI-launch behavior stays in `Pedia.App`.
- SQLite schema, repositories, search, import parsing, export serialization, backup, and normalized settings storage stay in `Pedia.Core`.
- `CorePediaDataService` is the adapter between presentation contracts and Core contracts.

The implementation deliberately does not use Entity Framework Core, an ORM, a generic repository framework, CQRS, MediatR, or a service locator.

## 6. Source ownership map

### Root and project configuration

| Path | Ownership |
|---|---|
| `Pedia.sln` | Three-project solution and x64 configuration mapping. `Pedia.App` maps even Any CPU/x86 solution selections to x64. |
| `Directory.Build.props` | Shared C# compiler settings: latest language, nullable, implicit usings, deterministic output. |
| `Directory.Packages.props` | Central NuGet version authority. |
| `README.md` | User/developer entry point; secondary to current code for implementation facts. |
| `docs/ARCHITECTURE.md` | Architecture overview; secondary to current code. |
| `PROJECT.md` | This code-review/UI implementation reference. |
| `pedia-ui-reference.png` | Non-executable visual reference. It includes concepts not identical to current XAML, such as different shell wording and reference-only article columns. |

### `Pedia.App`

| Path | Ownership |
|---|---|
| `App.xaml` | Fixed dark application theme and merged WinUI/Pedia resource dictionaries. |
| `App.xaml.cs` | Sole DI composition root, application launch, top-level WinUI exception logging/dialog fallback. |
| `MainWindow.xaml` | Shell rows, custom title bar, title commands, three-pane grid, splitters, Settings swap, status bar, initialization overlay, InfoBar, keyboard accelerators. |
| `MainWindow.xaml.cs` | AppWindow/title-bar integration, DPI-aware geometry, pane persistence/collapse, close guard, scroll persistence, density resource mutation, accelerator routing. |
| `Controls/PaneSplitter.xaml(.cs)` | Pointer-captured and keyboard-operable horizontal splitter with min/max/adjacent-pane constraints. |
| `Converters/PresentationConverters.cs` | Visibility, heading-size, date, and stored-value presentation conversions. |
| `Models/PresentationModels.cs` | UI enums, query/row/document records, import/export/backup contracts, editable observable graphs. |
| `Services/IPediaDataService.cs` | ViewModel-facing data-operation boundary. |
| `Services/CorePediaDataService.cs` | UI/Core mapping, off-UI-thread dispatch, per-import composition, export/backup orchestration, statistics mapping. |
| `Services/IDialogService.cs`, `DialogService.cs` | Confirmations and focused editor/selection/import/export dialogs; XamlRoot attachment. |
| `Services/IFilePickerService.cs`, `FilePickerService.cs` | Window-bound native import/export/backup pickers. |
| `Services/IStringService.cs` | ResourceLoader-backed string access and formatting. |
| `Services/PediaSettings.cs` | App-facing settings and window/filter state. |
| `Services/SettingsService.cs` | Mapping between app settings and normalized Core JSON model. |
| `Services/LocalFileLoggerProvider.cs` | Daily LocalAppData file sink, information level and above. |
| `ViewModels/MainWindowViewModel.cs` | Cross-pane coordinator, initialization, import, settings navigation, bulk actions, refresh/statistics, close-blocking state. |
| `ViewModels/TopicPaneViewModel.cs` | Smart nodes, topic tree/filter/selection, CRUD/reorder/expand/collapse/path-copy commands. |
| `ViewModels/TopicNodeViewModel.cs` | Per-node hierarchy, identity, count, glyph, path, accessible name, expansion state. |
| `ViewModels/ArticleBrowserViewModel.cs` | Search/filter/sort/page query state, selection, bulk-action routing, cancellation and stale-result rejection. |
| `ViewModels/ArticleRowViewModel.cs` | Localized article-list row display and accessible name. |
| `ViewModels/ArticleDetailViewModel.cs` | Reader/editor state machine, dirty tracking, save/leave guard, article/source/topic/lifecycle actions. |
| `ViewModels/SettingsViewModel.cs` | Editable settings, database information, backup/restore/index/sample operations and busy gate. |
| `Views/TopicPaneView.xaml(.cs)` | Topic header/filter/tree/context menu; event-to-command routing and rejected-selection restoration. |
| `Views/ArticleBrowserView.xaml(.cs)` | Search/filter/list/paging/bulk UI; extended selection and row context/keyboard routing. |
| `Views/ArticleDetailView.xaml(.cs)` | Reader tabs, metadata/source/topic views, full editor, source/topic event routing, scroll events. |
| `Views/SettingsView.xaml(.cs)` | Settings screen and focus behavior. |
| `Themes/PediaTheme.xaml` | Grayscale tokens, WinUI resource overrides, density dimensions, shared text/button/list/tree styles. |
| `Resources/en-US/Resources.resw` | The single checked-in UI resource language, currently 408 entries. |
| `Package.appxmanifest` | Identity/version, assets, Windows device families, `en-US`, `runFullTrust`. |
| `app.manifest` | Windows compatibility declaration and PerMonitorV2 DPI awareness. |
| `Properties/launchSettings.json` | Packaged and unpackaged development profiles. |
| `Properties/PublishProfiles/win-x64.pubxml` | Self-contained x64 filesystem publish settings. |

### `Pedia.Core`

| Path | Ownership |
|---|---|
| `Data/DatabaseOptions.cs` | Default LocalAppData database path and 5,000 ms busy timeout. |
| `Data/SqliteConnectionFactory.cs` | Canonical path, directory creation, pooled shared-cache connections, foreign keys, busy timeout, WAL. |
| `Data/DatabaseWriteGate.cs` | Process-wide per-canonical-database semaphore; Windows path comparison is case-insensitive. |
| `Data/MigrationRunner.cs` | Schema version, ordered migration SQL, newer-schema rejection, pre-migration online backup. |
| `Data/DatabaseInitializer.cs` | Migration then new-database-only sample seeding. |
| `Data/SampleDataSeeder.cs` | Transactional first-run sample topics/articles/sources. |
| `Data/DatabaseInformationService.cs` | Path/size/schema/import timestamp and search-index count consistency. |
| `Data/DatabaseValue.cs` | UTC ISO-8601 conversion and nullable text/date helpers. |
| `Models/ArticleModels.cs` | Stable type/status/source constants and persistence drafts/details/statistics. |
| `Models/ArticleQueryModels.cs` | Core smart/search/sort query and page contracts. |
| `Models/TopicModels.cs` | Topic summaries and delete result. |
| `Repositories/ArticleRepository.cs` | Transactional article graph, assignments, bulk operations, Trash, statistics, sample cleanup. |
| `Repositories/TopicRepository.cs` | Topic hierarchy, normalized uniqueness, recursive reads, moves/reorder, deliberate delete semantics. |
| `Search/FtsQueryBuilder.cs` | Safe Unicode term/phrase-to-FTS expression conversion and short-input fallback decision. |
| `Search/SearchDocumentStore.cs` | Aggregated relational search document plus synchronized FTS row. |
| `Search/ArticleQueryService.cs` | SQL predicates, recursive topic scope, count/page query, allowlisted ordering, rebuild/FTS check. |
| `Search/WordCounter.cs` | Unicode word counting across section bodies. |
| `Importing/ParsedDocument.cs` | Paragraph/list/section intermediate document. |
| `Importing/DocumentParsing.cs` | Plain-text and Markdown parsing/sanitization. |
| `Importing/ImportPreviewService.cs` | Bounded file read, encoding, format, SHA-256 and parsing. |
| `Importing/ImportModels.cs` | Import policies, outcomes, run metadata and repository contract. |
| `Importing/FileImportService.cs` | Sequential cancellable batch, duplicate decisions, per-file isolation, run completion. |
| `Importing/PediaImportRepository.cs` | Parsed-document-to-article mapping and `ImportRuns` persistence. |
| `Exporting/DocumentExportService.cs` | Plain text/Markdown/Pedia JSON serializers, deserializers, collision-safe folder export. |
| `Backup/BackupModels.cs` | Backup manifest and operation results. |
| `Backup/BackupService.cs` | Snapshot/archive creation, validation, restore, rollback and schema inspection. |
| `Services/AppSettings.cs` | Normalizable generic settings record graph. |
| `Services/SettingsService.cs` | Atomic JSON load/save. |
| `Utilities/Clock.cs` | UTC clock abstraction. |
| `Utilities/FileNameUtilities.cs` | Unicode-safe Windows file-name sanitization and collision paths. |

## 7. Composition, startup, and shutdown

`App.ConfigureServices` is the only composition root. It registers one singleton graph in this order:

1. Information-level logging with Debug and local file providers.
2. `DatabaseOptions`, connection/initializer/information services, topic/article repositories, query service, import preview, export, and backup.
3. string, settings, picker, dialog, and `IPediaDataService` presentation services.
4. topic/browser/detail/settings/main ViewModels and `MainWindow`.

The container is built with `validateScopes: true`. Launch resolves `MainWindow` and activates it.

On shell load:

1. Dialogs receive the active `XamlRoot`.
2. Settings load and normalize.
3. Compact/comfortable density resources are applied.
4. Database migration and optional new-database sample seeding run away from the UI thread.
5. FTS5 availability is verified.
6. Browser filter state and topic selection are restored.
7. The topic tree, initial query, preferred article/page, and statistics load.
8. A genuinely new database selects the `History of Shanghai` sample topic/article when available.
9. Window layout and remembered reader scroll position are restored.

Initialization failure is logged as critical and reported through the shell InfoBar; database/search status strings switch to unavailable states.

Window close is intercepted until the following succeed:

- no settings/import/initialization operation is blocking close;
- the article editor accepts leave (save, discard, or cancel);
- window/session settings are persisted.

After successful persistence, the close handler permits one final close and `App.Current.Exit()` runs. There is no background tray lifetime.

`App.UnhandledException` logs a critical exception, marks it handled, and attempts to show a localized error when a live XAML root exists. Code review of new exception paths must consider that handled exceptions keep the process alive unless state is explicitly recovered.

## 8. UI design system

### 8.1 Color contract

The application requests `Dark` and uses a fixed monochrome resource dictionary. App settings map the Core theme to `Dark` and ignore the generic Core accent field.

| Token | Value | Use |
|---|---:|---|
| `PediaApplicationBackgroundColor` | `#FF0A0A0A` | application background |
| `PediaTitleBarBackgroundColor` | `#FF0E0E0E` | title/status bars |
| `PediaPrimaryPaneColor` | `#FF121212` | topic/detail/settings surfaces |
| `PediaSecondarySurfaceColor` | `#FF161616` | browser/editor secondary surfaces |
| `PediaElevatedSurfaceColor` | `#FF1B1B1B` | headers/cards/InfoBars |
| `PediaPointerOverColor` | `#FF222222` | hover |
| `PediaSelectedColor` | `#FF2A2A2A` | selection |
| `PediaPressedColor` | `#FF323232` | pressed/selected-hover |
| `PediaPrimaryBorderColor` | `#FF323232` | control borders |
| `PediaSubtleDividerColor` | `#FF2A2A2A` | pane/row dividers |
| `PediaPrimaryTextColor` | `#FFF4F4F4` | main text |
| `PediaSecondaryTextColor` | `#FFBABABA` | metadata |
| `PediaMutedTextColor` | `#FF888888` | muted metadata |
| `PediaDisabledTextColor` | `#FF636363` | disabled text |
| `PediaFocusColor` | `#FFD8D8D8` | focus/splitter hover |

Standard WinUI accent, text, fill, stroke, card, layer, selection, focus, hyperlink, and InfoBar resources are overridden to grayscale. System accent should not leak into normal controls. Caption-button colors are set separately in `MainWindow.xaml.cs` and are reapplied on activation.

### 8.2 Typography and dimensions

- Default UI family: `Segoe UI Variable Text, Segoe UI`, 14 px.
- Article title: Cambria, 36 px, line height 44.
- Section headings: Cambria; shared style is 24/31, while reader heading levels use converter sizes 25, 22, and 19.
- Reader body: Segoe UI family; persisted font size and line height.
- Metadata/muted text: 13 px.
- Control corner radius: 4.
- Title-bar row: 48; status row: 34.
- Default panes: topic 290, article browser 560, reader remainder.
- Topic width: 220–430; article-browser width: 420–760; each splitter preserves at least 450 for the adjacent remainder.
- Splitters occupy 5 px columns; keyboard Left/Right changes the target pane by 16 px.
- Settings content width: 760; reader default maximum width: 860.

Runtime density switches resource values:

| Resource | Compact | Comfortable |
|---|---:|---:|
| control minimum height | 30 | 36 |
| input minimum height | 32 | 38 |
| list item minimum height | 40 | 46 |
| tree item minimum height | 34 | 40 |
| button padding | `9,4,9,4` | `12,7,12,7` |

### 8.3 Shell layout and responsive behavior

Normal workspace layout is exactly three panes separated by two custom splitters. Settings replaces the workspace in the same content row; it is not a modal page.

The default logical window size is 1600 × 950 with an enforced minimum of 1180 × 710. PerMonitorV2 DPI awareness is declared. Window size is converted between logical and physical pixels; position remains physical. Restore selects the display nearest the saved rectangle, clamps size to its work area, preserves a still-connected secondary monitor, and recenters only when the saved rectangle is effectively outside the work area.

When the root width drops below 1260 and the topic pane is open, the topic pane collapses to zero width. A vertical edge button can reopen it. The previous topic width is remembered. There is no equivalent automatic collapse for the article browser, no mobile layout, and no single-pane navigation model.

`PaneSplitter` supports mouse/pen pointer capture, SizeWestEast cursor, hover focus color, capture-loss cleanup, and keyboard Left/Right. It clamps against configured min/max and calculated adjacent-space availability.

## 9. Current UI surfaces and states

### 9.1 Custom title bar

The title bar contains the Pedia icon/name and commands:

- New flyout: new article, root topic, child topic.
- Import.
- Refresh.
- Settings.

Commands disable during initialization, import, or Settings data operations. The shell extends content into the title bar and reserves a 140 px caption-button region.

### 9.2 Topic pane

The topic pane contains a header, add button, action menu, filter box, `TreeView`, and collapse button.

The first five nodes are synthetic and always rebuilt before stored topics:

| ID | Scope | Persistence |
|---:|---|---|
| `-1` | All articles | presentation only |
| `-2` | Favorites | presentation only |
| `-3` | Recently edited | presentation only; implemented as active articles with Updated descending default on scope entry |
| `-4` | Uncategorized | presentation only; articles with no topic assignment |
| `-5` | Trash | presentation only; articles with non-null `DeletedAtUtc` |

Stored topics display their direct active-article count, not a descendant aggregate. The UI builds nested nodes from the flat repository result. Filtering retains matching topics and ancestors of matching descendants, expands returned branches, invalidates any pending selection, clears hidden selection, and disables sibling reordering because visible indices no longer represent the complete sibling order.

Topic actions:

- create root/child;
- rename name and description;
- move to a searchable allowed parent or root;
- move up/down among unfiltered siblings;
- recursively expand/collapse descendants;
- copy full `Parent / Child` path;
- confirmed delete.

Smart nodes cannot be mutated. Topic mutation first invokes the article editor leave guard. A rejected or superseded scope selection does not replace the accepted selection.

### 9.3 Article browser

The browser surface contains:

- scope heading;
- `AutoSuggestBox` search;
- advanced filter flyout;
- quick language/type/status filters and Clear;
- fixed list header with Title, Language, Words, Status, Updated sorting;
- virtualized `ListView` with extended selection;
- empty state;
- bulk actions, Empty Trash, range/page controls, and page-size selector.

Available filters:

- search scope;
- quick language plus independent English/Finnish family selection;
- article type;
- status;
- favorites only;
- has sources / no sources;
- include subtopics;
- minimum/maximum word count;
- created from/to;
- updated from/to;
- archived only / exclude archived;
- sample only / user only.

Date upper bounds are converted to the final tick of the selected local calendar day and then to UTC. Language family `en` or `fi` matches both the exact value and BCP-47-style variants such as `en-US`; an explicit code containing `-` matches exactly, case-insensitively.

Search-scope behavior:

| UI option | Effective query |
|---|---|
| All text | current selected smart/topic scope; all indexed text |
| Title only | current selected smart/topic scope; title field only |
| Current topic | direct selected topic only |
| Current topic and descendants | selected topic plus recursive descendants |
| Entire library | removes selected topic/smart scope and queries all active articles |

Current topic options are removed for smart scopes. Include subtopics is enabled only for a stored topic using All text or Title only. Remove-selected-from-topic is allowed only for an active direct-topic result set with descendants and incompatible search scopes disabled.

The list queries only one page. `ItemsStackPanel` provides native vertical virtualization. Page sizes are 25, 50, and 100. A result-changing mutation clamps an out-of-range page and reloads. Empty results explicitly clear the reader selection.

Sorting toggles direction on the current column. A new column starts ascending except Updated, which starts descending. A non-empty text search defaults to relevance; an explicit sort remains explicit. Without search, the adapter maps Relevance to Title sorting. Recently edited starts Updated descending when entered.

Extended selection supports:

- add to one or more topics;
- remove from the current direct topic;
- change status;
- export selected active articles;
- move selected active articles to Trash.

Trashed rows are excluded from active bulk operations. Context actions expose open/edit/duplicate/topics/favorite/export/Trash for active articles and restore/permanent delete for trashed articles as applicable.

### 9.4 Article reader

When no article is selected, the detail pane shows an empty state. When reading, the top toolbar shows Edit, Favorite, busy state, and an actions flyout.

Reader tabs:

- **Read**: selectable title/subtitle/summary/body text, language/word/update/topic/source metadata, ordered sections, persisted reading size/spacing/width.
- **Metadata**: title, subtitle, summary, language, type, status, favorite, word count, created/updated timestamps, internal ID, notes.
- **Source**: all source metadata, selectable values, and explicit Copy/Open URL actions when the URL is absolute.
- **Topics**: assignments, primary radio state, remove buttons, and Manage topics.

Reader topic changes save immediately through `ReplaceTopicAssignmentsAsync`; no full article edit is required. Removing the primary assignment promotes the first remaining assignment. An article may have zero topics.

### 9.5 Article editor

The editor provides:

- required title;
- optional subtitle, summary, and notes;
- language code text;
- one of eight UI article types;
- one of four statuses;
- favorite state;
- ordered sections with heading level 1–3 and body;
- ordered sources with full provenance fields;
- topic assignments and one primary assignment;
- validation message, Cancel, and Save.

Stable UI type values: `General`, `Person`, `Place`, `Event`, `Concept`, `Organization`, `Timeline`, `Other`.

Stable status values: `Draft`, `Ready`, `Needs review`, `Archived`.

Stable source-type choices: `Manual`, `Local text file`, `Local Markdown file`, `Book`, `Website`, `Encyclopedia`, `Other`.

New article defaults come from settings, starts with one empty level-2 section, and receives the selected stored topic as primary when creation begins from a topic scope. It remains an unsaved in-memory graph until Save.

Dirty tracking covers the root editor, child property changes, and collection changes. Leaving through topic/article selection, New, Import, Settings, or window close invokes Save/Discard/Cancel. Cancel edit invokes the same decision when dirty. There is no periodic or focus-loss autosave.

Save trims and requires title, maps blank optional fields to null, defaults a blank language to `en`, clamps heading levels, writes `IsSample=false`, computes word count from section bodies only, saves the complete article graph transactionally, reloads the saved article, and refreshes surrounding data. The repository replaces all section/source/topic child rows on a normal article update.

### 9.6 Settings

Settings is a full workspace replacement with Back, a busy indicator, scrollable content, and Save.

Sections:

- General: default language/status, restore last article, Trash confirmation, include-subtopics default, page size.
- Reading: font size 13–24, line spacing 19–38, maximum width 600–1100, remember scroll positions.
- Appearance: fixed dark-theme explanation and compact/comfortable density toggle.
- Data: database path/size/schema, article/topic/source counts, search-index state, open data folder, backup, restore, rebuild index, delete sample content.
- About: product/version/privacy/third-party resource text.

Only Save commits editable settings. Data operations run immediately and use one Settings busy gate. Settings busy state disables shell commands and blocks close.

### 9.7 Status and transient feedback

The bottom status bar displays active article count, active topic count, current result count, last completed import, import progress/cancel, search-index state, and database state.

Success messages use a closable InfoBar. Data/action errors generally use a localized dialog; list/initialization failures can use the InfoBar. All severities share the grayscale resource treatment.

## 10. Keyboard, focus, and accessibility contract

| Shortcut | Current action |
|---|---|
| `Ctrl+N` | New article unless focus is in a text editor |
| `Ctrl+Shift+N` | New root topic unless focus is in a text editor |
| `Ctrl+K` / `Ctrl+F` | Focus article search |
| `Ctrl+S` | Save active article editor |
| `Ctrl+,` | Open Settings unless focus is in a text editor |
| `F2` | Rename selected mutable topic |
| `Enter` in article list | Open selected row |
| `Delete` in article list | Request Trash action for selected active row |
| `Escape` | Cancel editor; otherwise clear search |
| `Alt+Left` | Back from Settings |
| Left/Right on splitter | Resize target pane by 16 px |

Primary/icon-only commands use `x:Uid` resources for labels, tooltips, and automation names. Article rows and topic nodes expose composed accessible names. List/tree items use system focus visuals. Reader text and metadata values that users may need to copy are selectable.

Accessibility remains a runtime gate: keyboard order, narrator output, high-contrast behavior, scaling, focus restoration after dialogs, and automation names produced from resource resolution must be verified in a running Windows application.

## 11. ViewModel coordination and stale-work protections

`MainWindowViewModel` wires the singleton feature ViewModels through explicit delegates/events; they do not resolve each other from DI.

Important flow wiring:

- topic selection → dirty-editor guard → browser scope query;
- browser selection → dirty-editor guard → detail load;
- article mutation → reload topics/browser/statistics while preserving viable scope/selection;
- settings save → live browser page/include-subtopics update, reader token notification, density mutation;
- topic mutation → dirty-editor guard before dialog/write;
- import → leave guard, picker, preview, dialog, cancellable batch, refresh;
- bulk action → leave guard, dialog/confirmation, one data-service operation, refresh.

Concurrency/state protections that reviewers must preserve:

- Browser search has a 250 ms debounce cancellation source.
- Each browser load increments `_loadGeneration`, cancels the previous load, and ignores an obsolete result.
- Article-selection notifications use a monotonically increasing version plus a semaphore so a queued obsolete selection cannot reopen an old article.
- Detail loads use `_articleLoadGeneration` so a slower old load cannot replace the current article/editor.
- Detail dialogs capture article/editor identity and recheck it after the await before mutating.
- Save captures editor identity and generation; editor-leave cannot start a concurrent second save while busy.
- Reader mutations use `IsBusy` as a single-flight gate and recheck article identity before applying reloaded state.
- Topic selection uses `_selectionRequestVersion`; topic filtering invalidates pending selection.
- Import owns one cancellation source and disables conflicting shell operations.
- Settings owns a single busy flag and disables all Settings data commands together.
- Core writers for the same canonical database path share a static semaphore; reads do not take that gate.
- JSON settings writes have a separate `SemaphoreSlim`.

`CorePediaDataService` wraps Core operation bodies in `Task.Run`. This is intentional because Microsoft.Data.Sqlite operations are task-shaped but execute database work synchronously. ViewModels resume on the WinUI synchronization context after awaited calls.

## 12. Local storage and database connection contract

Paths are based on `Environment.SpecialFolder.LocalApplicationData`:

| Data | Constructed path |
|---|---|
| Database | `%LocalAppData%\Pedia\Data\pedia.db` |
| Settings | `%LocalAppData%\Pedia\Settings\settings.json` |
| Logs | `%LocalAppData%\Pedia\Logs\pedia-yyyy-MM-dd.log` |

Package identity and Windows file virtualization may affect the physical Explorer path of a packaged process. Settings displays the path used by the running process.

Every opened SQLite connection uses:

- `ReadWriteCreate` mode;
- shared cache;
- pooling;
- `PRAGMA foreign_keys = ON`;
- `PRAGMA busy_timeout = 5000`;
- `PRAGMA journal_mode = WAL`.

Writes are serialized in-process by canonical database path. The gate is not cross-process locking and does not replace SQLite transactions. Reads do not acquire it and can proceed while a writer waits.

## 13. Database schema version 1

All IDs are SQLite integer primary keys. Timestamps are UTC ISO-8601 text. Current schema authority is `MigrationRunner.CurrentSchemaVersion = 1`.

| Table | Fields and role |
|---|---|
| `SchemaInfo` | one row (`Id=1`), schema version, created/updated UTC |
| `Topics` | parent, name, normalized `NameKey`, description, sibling order, sample flag, timestamps, soft-delete timestamp |
| `Articles` | title/subtitle/summary, language, type/status, notes, favorite, computed word count, sample flag, timestamps, soft-delete timestamp |
| `ArticleSections` | article FK, optional heading, heading level 1–3, body, order |
| `ArticleTopics` | composite article/topic key, primary flag, assignment timestamp |
| `ArticleSources` | article FK, type/title/URL, external page/revision IDs, license, attribution, retrieved/checked dates, notes, order |
| `SearchDocuments` | one inspectable aggregate per active article |
| `SearchDocumentsFts` | FTS5 virtual table over the aggregate |
| `ImportRuns` | kind/source description, timing/status, imported/skipped/error counts and bounded error summary |

Key constraints/indexes:

- topic parent FKs and article-topic topic FKs use `RESTRICT`;
- article-owned sections, sources, assignments, and relational search documents use `ON DELETE CASCADE`;
- checks constrain booleans and section heading levels;
- `UX_Topics_ActiveSiblingName` enforces one active normalized name per parent/root;
- `UX_ArticleTopics_OnePrimary` enforces at most one primary assignment per article;
- indexes cover topic ordering, article title/language/type/status/update/favorite/deletion, section/source ordering, and both assignment directions.

Migrations are ordered in code and update `SchemaInfo` inside the same transaction. A database with a newer schema is rejected. Before upgrading an existing older database, the runner creates `pedia.db.pre-migration-<timestamp>.bak` through SQLite online backup. Schema version 1 currently has no older upgrade step beyond clean creation, but the safety path is implemented for future increments.

## 14. Topic persistence invariants

Topic names are trimmed, required, normalized to Unicode Form KC, and uppercased invariantly for `NameKey`. Active sibling uniqueness is enforced at the database boundary, including root topics. The same display name may exist in different branches.

Creation appends to the active sibling order. Move:

1. rejects self-parenting;
2. validates the destination is active;
3. rejects descendant-parenting using a recursive CTE;
4. temporarily moves the topic to the end;
5. normalizes old and new sibling orders transactionally;
6. aborts on destination name collision.

Reorder normalizes the complete unfiltered sibling list. Paths and descendants use recursive CTEs.

Normal topic deletion is confirmed and transactional:

1. Read parent and directly assigned article IDs.
2. Reparent direct children to the deleted topic's parent.
3. Abort the whole transaction if a reparented child collides by normalized name.
4. Remove assignments to the deleted topic; never delete articles.
5. If an affected article has assignments but no primary, promote the earliest by assignment time then topic ID.
6. Soft-delete the topic.
7. Normalize former siblings.

An article left with no assignments appears in Uncategorized.

## 15. Article persistence and lifecycle invariants

Article save validates required trimmed title/language/type/status, heading levels 1–3, nonblank source type, unique topic IDs, and available active topics. If assignments exist, the first explicitly primary topic wins; otherwise the first assignment becomes primary. Empty assignment lists are valid.

Create/update transaction:

```text
validate and normalize draft
  -> write Articles header
  -> replace ordered sections
  -> replace ordered sources
  -> replace topic assignments
  -> rebuild relational search document
  -> rebuild FTS row
  -> commit
```

Word count is recalculated from section bodies only. Subtitle, summary, notes, source optional values, and blank headings normalize to null where appropriate. UI save always clears `IsSample`; repository/Core APIs can still intentionally save a sample draft.

Article actions:

- Favorite updates the flag and `UpdatedAtUtc`.
- Topic-only changes replace assignments and touch the article timestamp without replacing sections/sources.
- Duplicate rejects Trash, copies metadata/sections/sources/topics, sets status Draft, clears sample flag, and uses `<original> Copy` without guaranteeing title uniqueness.
- Bulk add-topic uses `INSERT OR IGNORE`, repairs missing primary assignment, and touches each article.
- Bulk remove-topic repairs primary assignment when needed.
- Bulk status accepts only the four stable statuses.
- Bulk operations validate all selected records before writes and use one transaction.
- Move to Trash sets `DeletedAtUtc`/`UpdatedAtUtc` and removes both search rows.
- Restore requires Trash, clears `DeletedAtUtc`, updates timestamp, and reindexes.
- Permanent delete requires Trash and deletes the article row; cascade removes owned rows.
- Empty Trash deletes every trashed article in one transaction after clearing any matching FTS rows.

## 16. Search, filtering, sorting, and paging

`SearchDocuments` and `SearchDocumentsFts` contain only active articles. Indexed fields are:

- title;
- subtitle;
- summary;
- ordered section headings and bodies;
- ordered source titles, attribution, and notes;
- article notes.

Source URLs, external IDs, license names, language/type/status, and topic paths are not part of FTS content.

FTS uses `unicode61 remove_diacritics 2`. `FtsQueryBuilder` extracts Unicode letter/number terms, preserves internal apostrophe/curly-apostrophe/hyphen punctuation, quotes terms safely, turns unquoted terms into prefixes, and keeps quoted phrases as phrases. Title-only scope prefixes every clause with the Title column.

Very short input (all extracted terms under two runes) or punctuation-only input bypasses FTS. Trash always bypasses FTS because deleted articles have no search rows. Fallback behavior is parameterized `LIKE`:

- Title-only searches title.
- All-text fallback searches title, subtitle, summary, notes, section heading/body, and source title/attribution/notes.

FTS rank uses `bm25` weights:

| FTS column | Weight |
|---|---:|
| ArticleId | 0.0 |
| Title | 12.0 |
| Subtitle | 5.0 |
| Summary | 3.0 |
| SectionText | 1.0 |
| SourceText | 0.8 |
| Notes | 0.5 |

Relevance orders ascending rank, then newest update and ID descending. Snippets contain 24 tokens with bracket markers; the adapter removes markers before display. Non-relevance columns come from an enum-to-SQL allowlist; user text never becomes an order expression.

Count and page queries share the same generated predicates. Paging uses `LIMIT`/`OFFSET`; Core accepts page sizes 1–250, while the UI exposes 25/50/100.

Rebuild clears both search stores and reindexes every active article in one transaction. Search-index readiness means active article count equals both relational document count and FTS row count; it is not a semantic content audit.

## 17. Local import

The native picker accepts multiple `.txt` and `.md` files. Core preview additionally recognizes `.markdown` when called directly.

Preview contract:

- canonical full path and existence check;
- maximum 16 MiB before and after read;
- strict UTF-8 with/without BOM, UTF-16 little-endian BOM, or UTF-16 big-endian BOM;
- SHA-256 of original bytes;
- file name, length, and last-write UTC metadata;
- parsed title/content preview;
- conflict detection against active titles and earlier titles in the same preview batch.

Plain-text title heuristic accepts the first nonblank line only when it is at most 120 characters and 16 words, lacks sentence-ending `. ! ? ;`, lacks a simple list marker, and is followed by blank/end. Otherwise the file name is the title. Paragraphs and simple ordered/unordered lists are preserved.

Markdown behavior:

- first H1 becomes title;
- later H1 text is not a new section;
- H2/H3 become sections;
- paragraphs and simple lists become plain structured content;
- Markdown image targets, link targets, HTTP autolinks, HTML tags/comments, and complete script/style/iframe/object/embed/svg/math blocks are removed;
- HTML entities are decoded;
- content is never executed or rendered as HTML.

Import processes files sequentially, checks cancellation, and isolates non-cancellation failures per file. Duplicate policy by case-insensitive active title:

- Skip: retain existing article.
- Create copy: find `Title (2)`, `(3)`, and so on.
- Replace: update the existing article through the normal transaction/index path.

Imported articles use type General and selected language/status/destination topic. Local source metadata maps as follows:

| Stored field | Imported value |
|---|---|
| SourceType | Local text file / Local Markdown file |
| Title | file name |
| ExternalPageId | full local path |
| ExternalRevisionId | lowercase SHA-256 |
| RetrievedAtUtc | import time |
| LastCheckedAtUtc | source file modified time |
| Notes | `<byte count> bytes` |

`ImportRuns` starts before the loop and completes as Completed or Cancelled. Replaced files count toward stored `ImportedCount`; failures contribute a maximum 8,000-character file/error summary. Imported files are not modified or watched.

## 18. Export

Formats:

- Plain text: article header, metadata, ordered sections, and source details.
- Markdown: metadata, H2/H3-compatible sections, and Sources.
- Pedia JSON: `format = "pedia-document"`, version 1, with either parsed-document or full-article payload depending on serializer overload.

Full article JSON preserves metadata, timestamps, sections, sources, and topic paths. Deserializers reject wrong format/version and malformed payloads.

Single-article UI export uses the exact path returned by the native save picker and writes UTF-8 without BOM. That explicit path can replace content if the picker/user approves an existing file. Multi-article export selects a folder and uses Core collision handling.

Folder export:

- removes Windows-invalid/control characters;
- collapses whitespace;
- trims trailing spaces/dots;
- limits the base name to 120 UTF-16 code units without splitting a surrogate pair;
- prefixes reserved DOS device names;
- falls back to `Untitled`;
- writes with `CreateNew` and probes `Name (2)`, `Name (3)`, etc., never overwriting an existing folder-export file.

## 19. Backup, validation, and restore

Backup format is `pedia-backup`, version 2, extension `.pediabackup`. Default database limit is 8 GiB; manifest limit is 64 KiB.

Archive contains exactly:

- `manifest.json`;
- `database.sqlite`.

Manifest records Pedia assembly version, UTC creation time, schema version, active article/topic counts, database length, database SHA-256, and normalized schema SHA-256.

Creation uses SQLite online backup to a temporary snapshot, inspects the snapshot, validates required schema, hashes it, writes a ZIP, flushes durably, and moves it to the first collision-free user destination. It does not overwrite an existing backup path.

Validation checks:

- `.pediabackup` extension;
- strict two-entry layout;
- manifest/database size bounds;
- supported format/version and valid nonnegative metadata;
- exact database length and SHA-256 syntax/value;
- SQLite `PRAGMA quick_check` result;
- empty `PRAGMA foreign_key_check` result;
- `SchemaInfo` presence/version;
- normalized schema hash;
- configured required schema version.

Restore flow:

1. Settings validates selected input before confirmation.
2. The data adapter acquires the normal database write gate.
3. BackupService prepares/validates the selected archive again.
4. It rejects a schema identity different from the live database.
5. It creates and prepares a safety `.pediabackup` beside the live database.
6. SQLite pools are cleared.
7. The selected snapshot is copied into the live database through SQLite online backup, not file replacement.
8. The live result is inspected for schema/hash validity.
9. On post-write failure, the prepared safety snapshot is restored through the same API; rollback failure is reported together with the original failure and safety path.
10. Pools are cleared and the app reinitializes without sample seeding, then refreshes UI state.

The safety backup remains available and its path is returned by Core, although the current presentation adapter does not display that path.

## 20. Settings and persisted session state

Core settings JSON is camel-cased, indented, normalized on load/save, and written through a unique write-through temporary file. Existing settings are replaced atomically with `File.Replace`; first save uses `File.Move`. Malformed JSON becomes `InvalidDataException`; the app adapter logs it and falls back to defaults.

Persisted user settings:

- default language/status;
- restore last selection;
- Trash confirmation;
- page size;
- include-subtopics default;
- reader font size/line spacing/maximum width;
- remember-scroll choice;
- compact/comfortable density.

Persisted window/session state:

- physical X/Y and logical width/height;
- maximized state;
- topic/article pane widths and topic collapse;
- selected topic/article IDs;
- search query/scope;
- quick and advanced filters;
- sort field/direction;
- include subtopics;
- current page;
- article scroll offsets.

At most 50 scroll offsets are retained by the shell. Eviction removes the first dictionary key; this is insertion-order behavior, not an explicit last-accessed LRU. Expanded topic state and selected reader tab are not persisted.

The generic Core settings model contains System/Light/Dark, Spacious density, accent, check-for-updates, details-pane visibility, and generic search flags. The app adapter currently writes fixed Dark, no accent, no update checks, details visible, search-title/content true, and does not expose those generic alternatives in UI.

## 21. Sample content semantics

Only a database identified as new by migration is eligible for seeding, and only when initialization requests samples. The seeder also requires empty article/topic tables and uses one transaction.

Current seed inventory:

- 12 topics marked `IsSample=1`;
- 15 articles marked `IsSample=1`;
- 3 sample source records;
- nested hierarchy, several types/statuses, and some multi-topic assignments.

First startup selects History of Shanghai when present. Existing databases are never reseeded automatically, including after explicit sample deletion or backup restore.

Delete sample content is a separate permanent transaction:

1. Delete every article still marked sample, including sample articles in Trash.
2. Process sample topics deepest-first.
3. Reparent surviving child topics.
4. Remove assignments from surviving articles and repair primary assignment.
5. Delete sample topic rows.
6. Roll back all work on collision/constraint failure.

Saving an article through the UI clears its sample flag, so it survives later cleanup. Renaming or moving a sample topic does not clear its sample flag, so it remains eligible for deletion. Non-sample articles are never deleted by this operation.

## 22. Logging, privacy, and local security boundaries

`LocalFileLoggerProvider` writes UTC timestamp, level, category, formatted message, and exception text to a daily append-only local file. It uses one in-process lock and allows other readers. It does not rotate by size or delete old logs.

Production log calls generally include operation context, IDs, or local paths. Exceptions can contain environment/path details. Article bodies are not intentionally logged, but reviewers must treat any new exception/log content as potentially local-sensitive.

Local-only boundaries:

- database, settings, logs, backups, imports, and exports are local files;
- no telemetry/network service is registered;
- no Internet manifest capability is declared;
- URLs launch only after explicit action;
- clipboard/folder/browser operations cross the process boundary only after explicit action;
- backups and exported documents can contain the entire library and are not encrypted by Pedia;
- settings/log files and the SQLite database are not application-level encrypted;
- `runFullTrust` is declared, so Windows file/process access follows desktop trust and the current user account.

These boundaries are design facts, not a claim of protection against a compromised Windows account or malicious future code.

## 23. Packaging and distribution state

MSIX identity is `Pedia.LocalKnowledgeLibrary`, publisher `CN=Pedia`, version `1.0.0.0`. Display name/publisher name are Pedia; default tile, splash, store, icon, and wide assets are checked in. Resources declare `en-US` only. Target device families include Universal and Desktop with minimum `10.0.17763.0` and max-tested `10.0.26100.0`.

The publish profile is self-contained x64, ReadyToRun, multi-file, and untrimmed. The project enables MSIX tooling and sets ReadyToRun false for Debug and true otherwise.

Repository configuration does not prove:

- a trusted production signing certificate;
- Store identity/reservation;
- successful Store ingestion;
- packaged install/upgrade/uninstall behavior;
- unpackaged runtime availability on a clean machine;
- real release screenshots or accessibility certification.

## 24. Automated test architecture and coverage map

`Pedia.Tests` targets x64 `net8.0-windows10.0.19041.0`. Core integration tests create isolated temporary directories/databases; they do not use the user's LocalAppData Pedia database. Selected ViewModel and presentation contract files are linked into the test assembly to exercise platform-independent behavior without launching WinUI.

The source currently contains 97 `[Fact]`/`[Theory]` methods plus theory data rows. Test-run pass counts should be taken from the current `dotnet test` output, not hard-coded as a permanent invariant.

| Test file | Main contracts |
|---|---|
| `DatabaseTests.cs` | PRAGMAs, schema/FTS creation, idempotent initialization, database information |
| `DatabaseWriteGateTests.cs` | same-database writer serialization, reads not blocked by queued gate, independent database paths |
| `TopicRepositoryTests.cs` | hierarchy/path/counts, normalized sibling uniqueness, move/reorder/cycle prevention, safe delete |
| `TopicRepositoryPerformanceTests.cs` | 10,000-topic tree with assignments within a time budget |
| `ArticleRepositoryTests.cs` | full graph save/update, assignments, rollback, duplicate, lifecycle, bulk writes, sample deletion |
| `SearchTests.cs` | fields/phrases/Unicode/punctuation/prefix/fallback/rank, filters/sort/page, language families, smart/Trash, rebuild |
| `ImportParserTests.cs` | Markdown structure/sanitization and plain-text title/list/paragraph rules |
| `ImportRepositoryTests.cs` | parsed mapping, selected metadata/topic, replacement lookup, import-run persistence |
| `ImportServiceTests.cs` | preview, three duplicate modes, per-file isolation, cancellation/run state |
| `ExportServiceTests.cs` | JSON versions/round trips, text/Markdown content, collision behavior |
| `BackupServiceTests.cs` | snapshot/manifest, restore/safety, open-connection restore, invalid/different schema rejection, cancellation |
| `SettingsServiceTests.cs` | defaults, normalization, atomic round trip, missing/malformed/partial JSON |
| `UtilityTests.cs` | Windows/Unicode file names and UTC helpers |
| `ViewModelTests.cs` | browser cancellation/filter/scope/page/selection/bulk flows, detail identity/dirty/single-flight/topic flows, topic stale-selection guards |
| `ViewModelTestContracts.cs` | test fakes/interfaces needed by linked ViewModels |

Not covered by this test assembly:

- rendered XAML layout and styling;
- native ContentDialog/picker interaction;
- actual AppWindow/title-bar/caption behavior;
- DPI/multi-monitor runtime behavior;
- real clipboard, URL, and folder launching;
- packaged MSIX deployment/lifecycle;
- assistive technology and high contrast;
- full end-to-end UI automation.

## 25. Code-review change-impact matrix

| If changing… | Review together… |
|---|---|
| schema/table/index | migration version/SQL, models, repository SQL, backup schema identity, clean/upgrade tests |
| article fields | Core/presentation models, adapter mapping, editor/read/metadata XAML, search aggregate, import/export/JSON, tests |
| topic semantics | repository recursive SQL, UI filtering/reorder, smart scope, primary repair, sample cleanup, topic tests |
| status/type/source values | Core constants, ViewModel option lists, resources/converters, filters, import defaults, validation/tests |
| search fields/weights | search document aggregation, FTS schema, builder, fallback SQL, snippets, ranking tests, index rebuild |
| filter/sort/page state | presentation model, browser state, adapter mapping, query SQL, settings tags, XAML, ViewModel/search tests |
| Trash lifecycle | list command visibility, confirmations, repository/search rows, smart query, bulk behavior, backup/export expectations |
| article editor | dirty tracking, identity/generation checks, leave guard call sites, adapter mapping, transactional save, XAML bindings |
| topic/article selection | stale-result generations, semaphores/versions, rejected-selection restoration, editor leave guard tests |
| UI color/spacing | Pedia theme resources, runtime density mutation, title-bar hard-coded colors, XAML local overrides |
| pane/window layout | MainWindow XAML/code-behind, splitter constraints, DPI conversion, settings mapping, runtime multi-monitor QA |
| localization text | `en-US` resource keys, every `x:Uid`, `IStringService` calls, formatted placeholders, automation names |
| import formats | picker filters, preview format/encoding/size, parser, source mapping, duplicate dialog, import tests |
| export format | enum/picker extension, serializers/deserializers, single vs folder behavior, collision/round-trip tests |
| backup format/schema | manifest version, strict layout/limits/hash, restore/rollback, UI confirmation/errors, backup tests |
| settings | app/Core records, adapter tags/mapping, normalization bounds, Settings UI/ViewModel, state round-trip tests |
| logging/errors | local sensitivity, exception handling, user feedback route, unhandled-exception recovery |
| package capability/dependency | manifests, csproj, publish profile, privacy/non-goal claims, clean-machine runtime tests |

## 26. Review invariants

The following invariants are especially important during review:

1. No article content is deleted by normal topic deletion.
2. At most one primary topic exists when assignments exist; zero assignments is valid.
3. Article header, children, and search rows change atomically on full save.
4. Trashed articles have no active search-document/FTS rows.
5. Bulk writes validate the complete selection before applying changes.
6. Raw search/filter/sort text never becomes executable SQL structure.
7. Obsolete async selection/load/dialog results cannot replace newer UI state.
8. Dirty editor state is guarded before every navigation/mutation route that would discard it.
9. The same database path has only one in-process writer lease at a time.
10. Restore validates before modification and retains a safety recovery path.
11. Folder export and backup creation do not overwrite an existing file implicitly.
12. Settings writes are normalized and atomic.
13. UI save converts edited sample articles into user content intentionally.
14. Current offline/non-AI behavior must not be weakened by latent registrations, capabilities, scheduled work, or hidden fetches.

## 27. Known boundaries and honest limitations

- Current localization is English only even though article language codes are user-editable.
- The root visual reference is not a pixel contract and includes elements that are not current runtime behavior.
- Direct topic counts, not descendant totals, are shown in the tree.
- `Recently edited` is an ordering preset over active articles, not a date-window predicate.
- Duplicate article titles are allowed; only import duplicate handling attempts copy-name disambiguation.
- Single-file export follows the native save-pick path and does not apply the folder exporter’s collision loop.
- Backups/exports are not encrypted.
- Logs are not size-rotated or automatically purged.
- Scroll-offset eviction is insertion-order, not a formal LRU.
- The write gate is in-process only.
- UI/runtime/platform claims require a real Windows run; unit/integration tests do not substitute for it.
- Release readiness, signing, clean-machine installation, Store submission, accessibility certification, and real-user performance are not established by this document.

## 28. Future extension boundary

There is intentionally no online provider interface or placeholder implementation. If an external retrieval feature is separately approved, it should remain outside current repositories: an explicit user-initiated adapter would fetch and validate external data, map it into the existing local `ParsedDocument`/article/source draft shapes, and hand it to the existing transactional save/index path.

Such an adapter would own HTTP, licensing/attribution, retry/rate-limit policy, size/content validation, cancellation, consent, and duplicate decisions. It must not silently register background work or weaken current local-only boundaries. This is an architectural extension point, not implemented behavior.

## 29. Verification snapshot

Verification run from this workspace on 2026-08-14:

- `dotnet test .\tests\Pedia.Tests\Pedia.Tests.csproj -c Release --no-restore -p:Platform=x64` passed 104/104 tests with 0 failed and 0 skipped.
- `dotnet build .\Pedia.sln -c Release -p:Platform=x64 --no-restore` succeeded with 0 warnings and 0 errors.
- These results verify the current Release compilation and automated contracts. They do not close the Windows runtime gates listed above.
