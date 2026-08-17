<!-- generated-by: gsd-doc-writer -->
# Pedia

Pedia is a private, local knowledge library for Windows, built as a native WinUI 3 desktop application.

## Offline by design

Normal library operations use local files and a local SQLite database. Online article retrieval is not implemented: Pedia does not download pages, call Wikipedia or MediaWiki, scrape sites, synchronize with a remote service, or send telemetry. A source URL is metadata only and opens in the system browser only when the user explicitly chooses that action.

## Current feature set

- Three-pane, monochrome WinUI 3 workspace with a hierarchical topic tree, paginated article browser, article reader/editor, custom title bar, resizable panes, and status bar.
- Smart views for All articles, Favorites, Recently edited, Uncategorized, and Trash.
- Nested topics with create, rename/description edit, move-to-parent/root, sibling reordering, expand/collapse, path copying, validation, cycle prevention, and confirmed safe deletion.
- Articles with metadata, ordered sections and sources, favorites, many-to-many topic assignments, one primary topic, duplication, source URL open/copy actions, soft deletion, restore, individual permanent deletion, and confirmed Empty Trash.
- Local SQLite FTS5 search across titles and article text, with title/topic/library scopes, punctuation-safe queries, relevance ranking, combined metadata/range/date filters, SQL sorting, snippets, and 25/50/100-row pagination.
- Multiple-file local `.txt` and `.md` import with preview, duplicate handling, destination topic, language/status selection, per-file results, and import-run history.
- Full-article export for one or multiple selected articles as plain text, Markdown, or versioned Pedia JSON, preserving the format-appropriate metadata, sections, sources, and topic paths with collision-safe file names.
- Versioned `.pediabackup` creation, validation, safety backup, restore, rollback, and search-index rebuild.
- Persisted settings, window/pane state, selection and current result page, search query, active filters and sorting, Include subtopics choice, and up to 50 remembered article scroll positions.
- First-database sample library and a Settings command that deletes records still marked as sample.
- Local file logging and automated tests for Core integration behavior plus platform-independent ViewModel state and commands.

Topic deletion never deletes articles. After confirmation, direct child topics move to the deleted topic's parent, assignments to the deleted topic are removed, and affected articles keep another primary assignment when one remains. An article with no remaining assignments appears under Uncategorized. A name collision at the destination aborts the transaction.

## Prerequisites

- x64 Windows 10 version 1809 (`10.0.17763`) or later. The application targets `net8.0-windows10.0.19041.0`.
- The local command-line build uses .NET SDK `10.0.302` to target .NET 8.
- PowerShell, with the working directory set to the repository root.
- The unpackaged launch profile needs the compatible x64 Windows App Runtime installed. The packaged MSIX profile needs Windows application/MSIX tooling plus a signed, deployed, and registered development package.

All direct NuGet package versions are centrally pinned in `Directory.Packages.props`.

## Restore and build

Run these commands in PowerShell from the repository root:

```powershell
dotnet restore .\Pedia.sln -p:Platform=x64
dotnet build .\Pedia.sln -c Debug -p:Platform=x64 --no-restore
```

Release build:

```powershell
dotnet build .\Pedia.sln -c Release -p:Platform=x64 --no-restore
```

## Run

The repository defines `Pedia.App (Package)` and `Pedia.App (Unpackaged)` launch profiles. The local command-line path is the unpackaged profile:

```powershell
dotnet run --project .\src\Pedia.App\Pedia.App.csproj -c Debug -p:Platform=x64 --launch-profile "Pedia.App (Unpackaged)"
```

The project itself resolves to an MSIX build by default. Use `Pedia.App (Package)` from Visual Studio only after its package and Windows App Runtime dependencies can be deployed and registered. The unpackaged profile starts the project executable without package identity and resolves the installed Windows App Runtime at startup. Package identity and Windows file virtualization can change the physical location shown by Explorer; the Data section in Pedia Settings displays the database path used by the running process.

## Test

The single test project targets `net8.0-windows10.0.19041.0` for x64, creates isolated temporary SQLite databases, and never uses the user's Pedia database. It covers Core integration behavior and linked, platform-independent ViewModel behavior without starting the WinUI runtime.

```powershell
dotnet test .\tests\Pedia.Tests\Pedia.Tests.csproj -c Debug --no-restore -p:Platform=x64
dotnet test .\tests\Pedia.Tests\Pedia.Tests.csproj -c Release --no-restore -p:Platform=x64
```

## Local data

Paths are constructed from `Environment.SpecialFolder.LocalApplicationData`. Under the unpackaged profile they resolve to:

| Data | Path |
|---|---|
| Database | `%LocalAppData%\Pedia\Data\pedia.db` |
| Settings | `%LocalAppData%\Pedia\Settings\settings.json` |
| Logs | `%LocalAppData%\Pedia\Logs\pedia-yyyy-MM-dd.log` |

SQLite connections enable foreign keys, WAL mode, shared pooling, and a 5-second busy timeout. Settings are written through a temporary file and atomically replaced. Logs remain local.

## Backups

Back up now writes a `.pediabackup` ZIP archive to a user-selected path. It contains exactly `manifest.json` and a consistent `database.sqlite` snapshot made with SQLite's online backup API. The manifest records format version 2, Pedia version, creation time, schema version, active article/topic counts, database length, and database/schema SHA-256 hashes. If the requested name exists, Pedia chooses a numbered collision name.

Validation checks the extension and archive layout, bounded entry sizes, hashes, schema identity, `PRAGMA quick_check`, and foreign-key integrity. Restore first creates a safety `.pediabackup` beside the live database, clears pooled connections, restores through the online backup API, and validates the result. If post-restore validation fails, Pedia restores the safety snapshot. A valid backup must match the current schema version and schema hash.

The migration runner also creates a `pedia.db.pre-migration-<timestamp>.bak` online snapshot before upgrading an existing older database.

## Sample library

A brand-new database is seeded once with 12 sample topics, 15 original sample articles, three sample source records, multiple article types/statuses, and several multi-topic assignments. On that first initialization, the workspace selects the History of Shanghai topic and article as its useful starting view. Initialization never reseeds an existing database, even after sample deletion.

Delete sample content permanently deletes articles and topics whose `IsSample` flag is still set. Saving a sample article through the editor writes it back as non-sample, so that article survives later sample deletion. Renaming or moving a seeded topic does not clear its sample flag, so that topic is still deleted. Surviving user articles are never deleted; assignments to deleted sample topics are dropped, and surviving child topics are reparented. A sibling-name conflict while reparenting aborts the transaction instead of merging topics.

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+N` | Create an article, except while editing text |
| `Ctrl+Shift+N` | Create a root topic, except while editing text |
| `Ctrl+K` or `Ctrl+F` | Focus the article search box |
| `Ctrl+S` | Save the current article edit |
| `Ctrl+,` | Open Settings, except while editing text |
| `F2` | Rename the selected user topic from its topic menu |
| `Enter` | Open the selected article row |
| `Delete` | Move the selected active article to Trash |
| `Escape` | Cancel article editing, or clear the current search when not editing |
| `Alt+Left` | Return from Settings |

Standard text-editing shortcuts are left to text controls.

## Pinned dependencies

| Package | Version | Role |
|---|---:|---|
| `Microsoft.WindowsAppSDK` | 2.3.1 | WinUI 3 desktop runtime and APIs |
| `Microsoft.Windows.SDK.BuildTools` | 10.0.26100.4654 | Windows build tooling |
| `CommunityToolkit.Mvvm` | 8.4.2 | Observable state and commands |
| `Microsoft.Data.Sqlite` | 10.0.11 | SQLite, transactions, backup API, and FTS5 access |
| `Microsoft.Extensions.DependencyInjection` | 10.0.11 | Composition root |
| `Microsoft.Extensions.Logging.Abstractions` / `.Debug` | 10.0.11 | Core/app logging |
| `xunit` | 2.9.3 | Tests |
| `xunit.runner.visualstudio` | 3.1.5 | Visual Studio/VSTest adapter |
| `Microsoft.NET.Test.Sdk` | 18.8.1 | Test host |
| `coverlet.collector` | 10.0.1 | Coverage collection support |

See [PROJECT.md](PROJECT.md) for the product contract and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for implementation flows.
