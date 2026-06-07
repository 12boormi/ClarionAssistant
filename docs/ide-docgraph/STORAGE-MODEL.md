# Clarion IDE API DocGraph — Storage & Linking Model (item 2)

**Ticket:** 8f1dda1c · **Checklist item [2]** · **Status:** recommendation (awaiting PM approval)

## Recommendation: one labeled library inside the **bundled** DocGraph DB

Store the extracted IDE API as a **single library row `Clarion IDE API`** in the existing **bundled** DocGraph database (`%APPDATA%\ClarionAssistant\docgraph.db`) — **not** a new third database and **not** the personal DB. No schema changes, no new federation plumbing.

## Why (verified against current code)

| Requirement (ticket) | How it's already satisfied |
|---|---|
| "separate bundled DB vs distinct library in main DocGraph" | The **bundled DB is the main DocGraph** (`DocGraphService()` default = `GetDefaultDbPath()`, `McpToolRegistry.cs:56`). Adding a labeled library there *is* the bundled-DB option — no extra file needed. |
| "confirm query_docs federates it" | ✅ `query_docs` → `QueryDocsMulti(personalPath, …)` (`McpToolRegistry.cs:2401`) queries bundled + personal independently and merges. A library in the bundled DB is returned automatically; `library="Clarion IDE API"` scopes to it. |
| "mark it bundled/regenerable" | `libraries` row carries `version` (build stamp) + `tags`. Set `tags='bundled,regenerable,ide-api'`, `version='12.0.0.14000'`. Re-ingest is idempotent via `EnsureLibrary` + `DeleteLibraryChunks` + `RebuildFtsIndex`. |
| "results don't leak into solution CodeGraph" (item 6) | Structural: DocGraph (`docgraph.db`) and CodeGraph (separate DB, `query_codegraph` tool) are different files/tools. The IDE API lives in DocGraph only. Zero leak path. |

## Why NOT the alternatives

- **A separate 3rd database** — `QueryDocsMulti` hard-codes exactly two paths (bundled + personal). A third DB would need new federation code in the query path, `list_doc_libraries`, `docgraph_stats`, and `rebuild_docgraph_fts`. Putting it in bundled = federation for free.
- **The personal DB** — personal is "never overwritten by updates" (memory `project_dual_docgraph`). The IDE API is machine-generated and **must** be overwritten on each Clarion upgrade. Wrong lifecycle. Also `list_doc_libraries`/`docgraph_stats` only read bundled by default.

## Schema mapping (no DDL changes)

`libraries` row:

| Column | Value |
|---|---|
| name | `Clarion IDE API` |
| vendor | `IDE-Internals` (distinct vendor so the "discard .NET" rule is not mis-applied — see note) |
| version | `12.0.0.14000` |
| source_path | `C:\Clarion12\bin` |
| source_format | `reflection` |
| tags | `bundled,regenerable,ide-api,dotnet` |

`doc_chunks` per type/member (UNIQUE key `library_id,class_name,method_name,topic,heading` already fits):

| Column | Use |
|---|---|
| class_name | fully-qualified type name (`ICSharpCode.SharpDevelop.Workbench.IPadContent`) |
| method_name | member name (null for the type-level chunk) |
| topic | `type` / `method` / `property` / `field` / `event` / `interface` |
| heading | member display name |
| signature | FQ signature incl. visibility & params |
| content | visibility (incl. non-public), base class + interfaces, declaring type, assembly, build stamp, optional summary |
| code_example | optional decompiled body for flagged types (item 4/5, via ilspycmd) |
| anchor | `<assembly>@<asmversion>` |

FTS5 `doc_fts` indexes class_name/method_name/heading/content/code_example/signature → searchable immediately after `RebuildFtsIndex()`.

## Inverted-gotcha note (important for item 5 labeling)

The project CLAUDE.md warns: *SoftVelocity docs mix Clarion & .NET — discard the .NET examples.* For **this** library that rule is **inverted**: it is deliberately .NET C# (the IDE's own API), and the C# **is** the answer. Mitigation: distinct `vendor='IDE-Internals'` + a `tags` marker, and item 8 memory note so the assistant doesn't reflexively discard these results as "wrong-language."

## Net implementation impact

- **No** schema migration, **no** new DB file, **no** query/federation changes.
- Item 5 extractor only needs to call the **existing** ingest primitives: `EnsureLibrary` (new source_format `reflection`) → `InsertChunks` → `RebuildFtsIndex`. May add a thin `IngestReflection(...)` method on `DocGraphService` that builds `DocChunk`s from `MetadataLoadContext`, reusing `EnsureLibrary`/`DeleteLibraryChunks`/`InsertChunks`.
- Regeneration (item 7) = re-run the extractor; idempotent replace by `(vendor,name)`.
