# IdeApiExtractor

Regenerates the bundled **"Clarion IDE API"** DocGraph library by reflection-loading the Clarion IDE's own .NET assemblies and emitting one type-level doc chunk per type. Ticket 8f1dda1c.

Standalone net9 console tool — **the addin gains no dependencies**; it only ever *reads* the resulting `docgraph.db`. See design docs in [`../../docs/ide-docgraph/`](../../docs/ide-docgraph/).

## Run

```
dotnet run -c Release -- [options]
```

| Option | Default | Meaning |
|---|---|---|
| `--bin <dir>` | `C:\Clarion12\bin` | Clarion install bin folder |
| `--db <path>` | `%APPDATA%\ClarionAssistant\docgraph.db` | target DocGraph DB (bundled) |
| `--dry-run` | off | write to a temp DB (`%TEMP%\ide-api-dryrun.db`) instead of the real one |
| `--no-decompile` | off | skip ilspycmd bodies (faster) |
| `--verify` | off | after writing, run FTS MATCH sample lookups |
| `--limit N` | all | only the first N target assemblies (smoke test) |
| `--html <path>` | off | also emit a self-contained, offline, searchable HTML browser (grouped by assembly → type → members) |

### HTML browser

```
# generate the browser without touching the real bundled DB:
dotnet run -c Release -- --dry-run --no-decompile --html clarion-ide-api-browser.html
```

Produces a single self-contained file (~14 MB, no external deps) with a live search box (filters by type *or* member name), kind/visibility filters, and collapsible assembly → type → member groups. Types in the decompile-flag set show a "decompiled" badge (their full C# body lives in DocGraph; query it with `query_docs`). Lazy-rendered so the browser stays responsive across 11k+ types.

Typical regeneration (item 7), run with Clarion **closed**:

```
dotnet run -c Release -- --verify
```

## What it does

1. Builds a `PathAssemblyResolver` over `bin\*.dll` + all of `bin\Addins\**` + .NET Framework v4 (+WPF) dir, and a `MetadataLoadContext` (reflection-only — never executes IDE code).
2. Loads the **54 target assemblies** ([`Targets.cs`](Targets.cs); rationale in [TARGET-ASSEMBLIES.md](../../docs/ide-docgraph/TARGET-ASSEMBLIES.md)). Native Win32 DLLs are skipped (`BadImageFormatException`).
3. Renders one **type-level chunk** per type ([`ReflectionRenderer.cs`](ReflectionRenderer.cs); schema in [CHUNK-SCHEMA.md](../../docs/ide-docgraph/CHUNK-SCHEMA.md)) — FQ name, kind, visibility (incl. non-public), base+interfaces, and a rendered member list. Large types split into continuation chunks; types whose members reference an unresolved assembly degrade to a header-only stub (never dropped).
4. For a small **decompile-flag allowlist** ([`Targets.DecompileFlag`](Targets.cs)) runs `ilspycmd` and stores the C# body in `code_example`.
5. Writes via [`DocGraphWriter.cs`](DocGraphWriter.cs) into the **same schema** as `ClarionAssistant.Services.DocGraphService` (libraries / doc_chunks / standalone FTS5 doc_fts), touching only the `Clarion IDE API` library row (other libraries preserved), then rebuilds the FTS index.

## Validated output (Clarion 12.0.0.14000)

- 54 assemblies, 11,636 types → **11,787 chunks**, 0 render failures, 11 decompiled bodies.
- Topic breakdown: class 6810, enum 2374, struct 1483, interface 569, delegate 376, static class 168, (stub) type 7.
- FTS verified: `EntityBrowserDetails AddParent`, `IPadContent`, `DataDictionaryService` all resolve. (`FileSchemaTree`'s add-row API is `AddItem()` / `NodeInserted(AddRemoveItemEventArgs)`, not "AddColumn".)

## Requirements

- .NET SDK 9.x; `ilspycmd` 8.2 as a global tool (`dotnet tool install -g ilspycmd`) for decompiled bodies (optional — `--no-decompile` works without it).
- The FTS5 module: provided by the tool's bundled SQLite (Microsoft.Data.Sqlite) and by the addin's `lib\sqlite-fts5` build. Note the **system** `sqlite3.exe` may lack FTS5 — use `--verify` to test MATCH through the tool.
