<!-- generated-by: gsd-doc-writer -->
# Pedia architecture

## System overview

Pedia is a layered native Windows desktop application. WinUI views bind to CommunityToolkit.Mvvm ViewModels; presentation services map UI-specific models to a WinUI-free core; core repositories/services execute parameterized SQLite and local-file operations. Native pickers, clipboard writes, opening the data folder, and launching a stored source URL are user-initiated presentation operations; there is no article-retrieval network flow.

```text
WinUI Views / MainWindow code-behind
              |
              v
       App ViewModels
              |
              v
IPediaDataService + dialogs/pickers/settings
              |
              v
 Pedia.Core repositories and services
              |
              v
 SQLite database + selected local files
```

Arrows mean the upper layer calls the lower layer. `Pedia.Core` has no reference to WinUI or `Pedia.App`; `Pedia.Tests` references only `Pedia.Core`.

## Solution structure and dependency direction

```text
Pedia.sln
├─ src/Pedia.App
│  ├─ App.xaml(.cs)              composition and application lifetime
│  ├─ MainWindow.xaml(.cs)       shell, title bar, state, accelerators
│  ├─ Views/                     four focused WinUI views
│  ├─ ViewModels/                shell/topic/browser/detail/settings state
│  ├─ Services/                  UI adapters, dialogs, pickers, settings, logs
│  ├─ Controls/                  keyboard/pointer pane splitter
│  ├─ Resources/en-US/           user-facing resource strings
│  └─ Themes/                    centralized grayscale resources/styles
├─ src/Pedia.Core
│  ├─ Data/                      connections, schema/migrations, seed, info
│  ├─ Models/                    article/topic/query contracts
│  ├─ Repositories/              transactional topic/article persistence
│  ├─ Search/                    FTS documents, query builder, paging query
│  ├─ Importing/                 TXT/Markdown parsing and import runs
│  ├─ Exporting/                 text/Markdown/versioned JSON serialization
│  ├─ Backup/                    archive creation, validation, restore
│  ├─ Services/                  normalized JSON settings model/storage
│  └─ Utilities/                 UTC clock and Windows file-name handling
└─ tests/Pedia.Tests             temporary-database core tests
```

`CorePediaDataService` is the presentation adapter. It maps `Pedia.Models` records to core records, composes per-import `PediaImportRepository`/`FileImportService` instances, and provides a single interface to ViewModels. Windows-only pickers, URL launching, clipboard access, XAML dialogs, window geometry, and title-bar behavior remain in `Pedia.App`.

## Composition root

`App.ConfigureServices` is the sole composition root. It creates `DatabaseOptions.CreateDefault`, registers connection/data/search/import/export/backup services, then presentation services and singleton ViewModels/MainWindow. The container is built with scope validation. There is no service locator or mutable static service registry.

Application launch resolves and activates `MainWindow`. The window's first loaded event attaches picker/dialog window state and calls `MainWindowViewModel.InitializeAsync`.

## Database lifecycle

1. `DatabaseOptions.CreateDefault` resolves `%LocalAppData%\Pedia\Data\pedia.db` (subject to packaged-process virtualization).
2. `SqliteConnectionFactory` creates the directory and opens a pooled shared-cache read/write connection.
3. Each opened connection enables foreign keys, a 5,000 ms busy timeout, and WAL mode.
4. `SqliteConnectionFactory` associates the canonical database path with a process-wide `DatabaseWriteGate`.
5. `DatabaseInitializer` runs ordered migrations.
6. Only when the database was newly created, `SampleDataSeeder` checks that topics/articles are empty and inserts the sample library in one transaction; presentation startup then selects the History of Shanghai sample topic/article.
7. Startup verifies that the SQLite native module exposes FTS5 before normal use.

Repositories open short-lived connections per operation. Routine write entry points acquire the per-database write gate before opening their transaction; factories for the same canonical path share one semaphore, while different database paths remain independent. Reads do not acquire the gate and may proceed during a queued write. Multi-row changes still use explicit transactions, with WAL and the busy timeout handling SQLite-level coordination.

## Schema and migrations

`MigrationRunner.CurrentSchemaVersion` is 1. The ordered in-code migration creates `SchemaInfo`, `Topics`, `Articles`, `ArticleSections`, `ArticleTopics`, `ArticleSources`, `SearchDocuments`, the FTS5 virtual table, `ImportRuns`, foreign keys, checks, and query indexes. It updates `SchemaInfo` inside the same migration transaction.

Before changing a non-new database whose version is behind, the runner creates `pedia.db.pre-migration-<timestamp>.bak` with SQLite's online backup API. A database newer than the supported schema is rejected. Failed migration SQL rolls back through the SQLite transaction.

To add a migration, append a deterministic `Migration(version, sql)` item, increment `CurrentSchemaVersion`, and cover both a clean database and an upgrade from the prior version. Migration SQL must preserve data and leave `SchemaInfo` updates to the runner.

## Topic flows

Create/rename normalizes the trimmed Unicode name to a Form-KC uppercase `NameKey`; the active-sibling expression index enforces case-insensitive uniqueness for roots and children. Move reads the current parent, rejects self/descendant destinations via recursive CTE, updates the parent, and normalizes both sibling lists transactionally. Reorder normalizes one sibling list; the presentation layer exposes root moves, sibling up/down, recursive expand/collapse, and clipboard path copying without changing the persistence model.

Safe deletion is an application transaction, not a cascade:

1. Read the deleted topic's parent and directly assigned article IDs.
2. Reparent direct child topics to that parent. A name collision aborts the transaction.
3. Delete assignments to the topic; never delete an article.
4. Repair the primary flag from the earliest remaining assignment when required.
5. Soft-delete the topic and normalize its former siblings.

No remaining assignment means the article is selected by the Uncategorized smart query.

Sample cleanup is a separate permanent-delete transaction. It first deletes all sample articles (including trashed ones), then processes sample topics deepest-first: reparent children, remove assignments, repair primary flags, and delete the sample topic row. Non-sample articles and topics survive. Any constraint failure, including a destination sibling-name collision, rolls back the entire cleanup.

The normal full-tree query computes direct active-article counts once in a grouped CTE and joins that result to topics; the UI displays that direct count. It does not expand every topic into every descendant merely to render the tree. Recursive descendant queries remain available for explicit descendant operations. This path has a bounded test with 10,000 topics and assignments.

## Article save flow

```text
Editor state
  -> CorePediaDataService.MapDraft
  -> ArticleRepository.Validate
  -> INSERT/UPDATE Articles
  -> replace ordered sections/sources/topics
  -> aggregate SearchDocuments row
  -> replace SearchDocumentsFts row
  -> COMMIT
  -> reload article and refresh topics/list/statistics
```

Validation trims the required title/language/type/status, bounds heading levels to 1–3, de-duplicates topic IDs, assigns at most one primary topic, and computes word count from section bodies. Article header and all child/search changes share one SQLite transaction. Updating through the UI writes `IsSample = false`.

Trash is a soft delete. Moving an article to Trash stamps `DeletedAtUtc` and removes both search rows in the transaction; restore clears the stamp and rebuilds them. Permanent delete requires a trashed article and cascades owned relational rows. Empty Trash is an explicitly confirmed Trash-scope command that removes stale FTS rows and every trashed article in one write-gated transaction.

## Search index and query flow

`SearchDocumentStore.ReindexArticleAsync` first removes the old relational/FTS records, then aggregates the active article's title, subtitle, summary, ordered section heading/body text, ordered source title/attribution/notes, and article notes. It inserts matching `SearchDocuments` and `SearchDocumentsFts` rows in the caller's transaction.

`FtsQueryBuilder` converts Unicode words into quoted prefix clauses and quoted input into phrase clauses. It discards unsafe punctuation rather than concatenating raw FTS syntax. Very short/non-token input takes the parameterized title `LIKE` fallback.

`ArticleQueryService.QueryAsync`:

1. Builds only allowlisted structural SQL and parameter values.
2. Maps the five UI search-scope choices to title/all-text matching plus the selected topic/smart scope, optional recursive descendants, or the whole active library, then adds filter predicates.
3. Executes a database count query.
4. Executes the page query with `LIMIT`/`OFFSET`, SQL ordering, topic/source counts, optional FTS snippet, and weighted `bm25` rank.
5. Returns `ArticlePage`; the WinUI adapter maps it to list rows.

Rebuild index clears both search stores and reindexes every active article inside one transaction. Database information reports the index ready only when active article, relational search-document, and FTS row counts agree.

## Import flow

```text
native multi-file picker
  -> ImportPreviewService (bounded read, decode, hash, parse)
  -> preview/duplicate dialog
  -> FileImportService (sequential, cancellable per-file loop)
  -> PediaImportRepository
  -> ArticleRepository save/index transaction
  -> ImportRuns completion + UI refresh
```

TXT parsing treats a short standalone first content line as the title only when it meets the bounded title heuristic and is separated from body text; otherwise it uses the file name. It preserves paragraphs/simple lists. Markdown parsing recognizes H1/H2/H3, converts H2/H3 to structured sections, keeps readable paragraph/list text, and strips HTML/comments/link targets/images. It never renders or executes imported content.

Duplicate handling is case-insensitive by active title: skip, generate a numbered copy title, or replace the existing article. Failures are recorded per file without undoing successful files. Cancellation marks the import run cancelled, then propagates. The source record stores local file identity and hash; no watcher or online follow-up exists.

## Export flow

`DocumentExportService` has full-`ArticleDetails` serializers for all three formats. Plain text and Markdown include article metadata, ordered sections, and source details; versioned Pedia JSON preserves the full article graph. Extended list selection exposes one- and multi-article export through the same service and drives transactional bulk topic, status, and Trash operations. The lower-level `ParsedDocument` overloads remain available for parsed-document use cases. Folder export sanitizes Windows names and probes numbered collision paths with `CreateNew` so existing files are preserved. Output is UTF-8 without BOM.

## Backup and restore flow

Backup creation uses a private, unpooled read-only source connection and SQLite `BackupDatabase` to create a consistent temporary snapshot. It inspects the snapshot, verifies the configured schema version, hashes the database and normalized schema, and writes a two-entry `.pediabackup` ZIP durably. A caller-selected collision is resolved with a numbered name.

Validation extracts into a unique temporary workspace with strict size/layout limits, verifies the manifest and database SHA-256, runs `quick_check` and `foreign_key_check`, and compares the live schema identity.

Restore proceeds as follows:

1. The presentation adapter leaves the UI thread and acquires the same per-database write gate used by routine writers.
2. Prepare and validate the selected archive; reject a different schema.
3. Create and prepare a safety `.pediabackup` of the current database.
4. Clear pooled connections.
5. Use SQLite Online Backup from the extracted snapshot into the live database. This avoids file-replacement conflicts with active/pooled connections.
6. Reinspect the live schema/hash and clear pools again.
7. If post-restore validation fails after data was written, restore the prepared safety snapshot through the same online-backup path; report an aggregate error if rollback also fails.
8. The adapter releases the restore write lease before reinitializing without sample seeding; migration initialization can therefore acquire the same non-reentrant gate normally. The Settings flow then refreshes application data.

## Threading and cancellation

Core database and file APIs are `Task`-based and accept `CancellationToken` where operations can be meaningful. `CorePediaDataService` dispatches its SQLite and file operation bodies with `Task.Run`, so awaited UI commands do not execute those bodies on the WinUI thread. Repository code uses async SQLite calls and avoids capturing a UI synchronization context. Routine database writers are serialized by the path-scoped write gate, but reads are not. Search input owns a cancellation source, waits 250 ms, and ignores cancelled obsolete work. Import checks cancellation between files and inside preview/file reads. Commands expose busy/loading state to prevent repeated UI operations.

Microsoft.Data.Sqlite performs its database work synchronously even through its task-shaped APIs. `CorePediaDataService` therefore runs database, search, import/export, backup, and restore operations on the thread pool before ViewModels resume on the WinUI context. SQLite writes also share the database-path `DatabaseWriteGate`; reads remain independent. The settings writer has its own `SemaphoreSlim` and performs atomic write-through replacement.

## Errors and logging

Core boundaries validate inputs and throw specific exceptions; repository transactions roll back on failure. ViewModels catch file/database/action failures, log operation/IDs or paths, and show a dialog or InfoBar. `App.UnhandledException` records a critical local log entry and presents an error when a XAML root is available.

`LocalFileLoggerProvider` writes information-and-higher events to `%LocalAppData%\Pedia\Logs\pedia-yyyy-MM-dd.log`. The implementation logs operation context and exceptions; it does not intentionally log whole article bodies or transmit logs. Operation-result InfoBars use the same grayscale visual resources as the rest of the shell.

## State persistence

The app adapter maps its runtime settings to the core `AppSettings` JSON model. Load normalizes language/status/enums, numeric bounds, dates, tags, and scroll offsets. Save serializes to a unique temporary file, flushes it to disk, and atomically replaces the old file. Window close captures physical screen position, DPI-independent window size, maximized state, pane widths/collapse, selected IDs and result page, query, search scope, all active filter controls, sorting, Include subtopics, and recent scroll positions. Restore converts the saved logical size using the current window DPI, validates placement against the saved rectangle's display work area, and restores a remembered article offset after content loads.

## Major decisions

- Native WinUI 3 and XAML, not a web wrapper.
- One UI project, one reusable core project, and one test project.
- Explicit parameterized SQL instead of an ORM, making transactions, recursive CTEs, FTS5, and backup behavior visible.
- A relational aggregate plus FTS table per active article, updated in the article transaction.
- SQL count/filter/sort/page operations and native list virtualization for large-library viability.
- One process-wide writer per canonical database path, while reads and independent databases remain concurrent.
- Soft-deleted articles with search removal; deliberate topic deletion that preserves content.
- Local JSON for small settings only; SQLite remains the source of truth for library data.
- Generic source metadata but no online-provider abstraction, app network client, or declared network capability.

## Future external-provider boundary

No external provider is implemented. If the product later authorizes one, add it as an explicit opt-in adapter above the current import/save boundary. The adapter should return validated local `ParsedDocument`/article/source drafts, while `ArticleRepository` remains responsible for atomic persistence and indexing. Generic external page/revision fields can store provenance without making the repository network-aware.

The provider would own HTTP, licensing/attribution policy, retry/rate-limit behavior, cancellation, and user consent. It must not be registered, scheduled, or given background activity in the current offline build.
