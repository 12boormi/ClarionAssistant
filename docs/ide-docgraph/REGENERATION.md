# Clarion IDE API DocGraph — Regeneration Runbook (item 7)

**Ticket:** 8f1dda1c · **Checklist item [7]** · run this **after a Clarion upgrade**.

The "Clarion IDE API" library is **bundled and regenerable** — it is machine-generated from the installed Clarion assemblies and overwritten on each upgrade. The **personal** DocGraph DB (`personal-docgraph.db`, user's own docs) is **never** touched by this process.

## One-command regeneration

From `tools/IdeApiExtractor/`:

```powershell
./regenerate.ps1 -BuildStamp 12.0.0.14100
```

`regenerate.ps1` does, in order:

1. **Guard** — aborts if a Clarion process is running (the bundled DB may be locked, and the extractor reflection-reads `bin\*.dll`).
2. **Re-stamp** — if `-BuildStamp` is given, rewrites `Targets.BuildStamp` in `Targets.cs` so the new `libraries.version` + per-chunk `Build:` line reflect the upgraded build.
3. **Resolve DB** — defaults to the **bundled** `%APPDATA%\ClarionAssistant\docgraph.db`; prints the personal DB path purely to confirm it is *not* the target.
4. **Backup** — copies the bundled DB to `docgraph.db.pre-ideapi-bak` (skip with `-NoBackup`).
5. **Build + run** — `dotnet build -c Release` then `dotnet run -- --bin <bin> --db <bundledDb> --verify`.

### Parameters

| Param | Default | Notes |
|---|---|---|
| `-Bin` | `C:\Clarion12\bin` | point at the upgraded install if the path changed |
| `-Db` | `%APPDATA%\ClarionAssistant\docgraph.db` | bundled DB (the one `DocGraphService()` reads) |
| `-BuildStamp` | *(unchanged)* | e.g. `12.0.0.14100`; updates `Targets.cs` before building |
| `-NoBackup` | off | skip the pre-run backup |

## Why this is safe to re-run

- `EnsureLibrary` upserts by `UNIQUE(vendor, name)` → the single `IDE-Internals / Clarion IDE API` row is updated in place, not duplicated.
- `DeleteLibraryChunks(libId)` removes only that library's chunks before re-insert → no stale chunks, no impact on other libraries.
- `RebuildFtsIndex` rebuilds `doc_fts` from **all** `doc_chunks` (every library stays searchable).
- Idempotent: re-running with the same install yields the same content.

## Manual equivalent (no script)

```powershell
# 1. Close Clarion.
# 2. (optional) edit Targets.cs → BuildStamp = "12.0.0.14100"
# 3.
cd tools/IdeApiExtractor
Copy-Item "$env:APPDATA\ClarionAssistant\docgraph.db" "$env:APPDATA\ClarionAssistant\docgraph.db.pre-ideapi-bak"
dotnet run -c Release -- --verify
```

## After regeneration

- `--verify` prints FTS sample lookups; expect `EntityBrowserDetails AddParent`, `IPadContent`, `DataDictionaryService` to hit.
- In the IDE: `docgraph_stats` / `list_doc_libraries` should show `Clarion IDE API` (~11.8K chunks) with the new version stamp, alongside untouched existing libraries.
- If the assembly set changed across the upgrade (new/removed IDE DLLs), update `Targets.Assemblies` in `Targets.cs` accordingly — see [TARGET-ASSEMBLIES.md](TARGET-ASSEMBLIES.md) for the curation rationale, and re-run the inventory (`docs/ide-docgraph/addins-inventory.cs`) to spot newly-relevant assemblies.

## Installer / distribution note

For a shipped bundled DB (per [STORAGE-MODEL.md](STORAGE-MODEL.md)), run the extractor on the build machine against a reference Clarion install, then ship the resulting `docgraph.db` as the bundled (overwritable) database. The user's `personal-docgraph.db` is merged at query time by `DocGraphService.QueryDocsMulti` and is never shipped/overwritten.
