# Clarion IDE API DocGraph — Doc-Chunk Schema (item 3)

**Ticket:** 8f1dda1c · **Checklist item [3]** · **Status:** defined (PM-approved decisions baked in)
**Granularity (PM):** **type-level** — one `doc_chunks` row per type, members rendered inline. ~11.6K chunks for the 54-assembly set (vs ~100K for per-member). No DDL changes — reuses the existing `libraries`/`doc_chunks`/`doc_fts` schema (see [STORAGE-MODEL.md](STORAGE-MODEL.md)).
**Scope:** 54 assemblies, ≈ 11,636 types (5,517+ public / 3,988+ non-public), public **and** non-public types included.

## One chunk per type — column mapping

| `doc_chunks` column | Value | Notes |
|---|---|---|
| `library_id` | FK to the `Clarion IDE API` library row | one library (STORAGE-MODEL.md) |
| `class_name` | **reflection `Type.FullName`** | canonical/unique; uses `+` for nested, `` `n `` for generic arity. e.g. `SoftVelocity.DataDictionary.FileSchemaEditor.FileSchemaTree` |
| `method_name` | `NULL` | type-level chunk — members live in `content` |
| `topic` | type kind: `class` \| `static class` \| `interface` \| `struct` \| `enum` \| `delegate` | drives `docgraph_stats` topic breakdown |
| `heading` | **simple type name** (readable, generics as `Name<T>`) | the primary FTS hit token, e.g. `FileSchemaTree` |
| `signature` | the **declaration line** | `public class FileSchemaTree : BaseFileSchemaTree, IPadContent` (visibility + modifiers + base + interfaces) |
| `content` | **structured type card + rendered member list** (see below) | the bulk; FTS-indexed so member-name searches hit |
| `code_example` | optional **decompiled body** for flagged key types | else `NULL` (item 4/5 via ilspycmd) |
| `anchor` | `<assemblySimpleName>@<assemblyVersion>` | e.g. `DataDictionaryEditor@12.0.0.14000` — also doubles as nav hint |

`UNIQUE(library_id, class_name, method_name, topic, heading)` → with `method_name=NULL`, exactly one row per type. Idempotent re-ingest via `DeleteLibraryChunks` + re-insert.

## `content` rendering (the type card)

Plain text (FTS-friendly), built from `MetadataLoadContext` reflection:

```
Type: SoftVelocity.DataDictionary.FileSchemaEditor.FileSchemaTree
Kind: class   Visibility: public
Assembly: DataDictionaryEditor @ 12.0.0.14000   Build: Clarion 12.0.0.14000
DeclaringType: <only if nested>
Base: SoftVelocity.DataDictionary.FileSchemaEditor.BaseFileSchemaTree
Implements: ICSharpCode.SharpDevelop.Gui.IPadContent, System.IDisposable

Constructors:
  [public] (DataDictionaryService service)

Properties:
  [public] DDFile SelectedFile { get; }
  [protected] Boolean IsDirty { get; set; }

Fields:
  [private] DataDictionaryService _service

Events:
  EventHandler SelectionChanged

Methods:
  [public] void AddColumn(DDField field)
  [public] virtual void RefreshTree()
  [internal] void RebindProvider()
```

**Rules:**
- Member enumeration uses `BindingFlags.Public | NonPublic | Instance | Static | DeclaredOnly` (inherited members are NOT re-listed — they live on the base type's own chunk; `Base:` links them).
- Every member prefixed with its **access modifier** incl. non-public (`public`/`protected`/`internal`/`protected internal`/`private protected`/`private`) — satisfies the ticket's "visibility (incl. non-public)" requirement. `static`/`virtual` modifiers included where present.
- Type & member type names rendered readably: generics `List<DDField>`, arrays `DDField[]`, `ref` params flagged. `class_name` keeps the raw reflection `FullName` for uniqueness.
- **Special-name methods excluded** from the Methods list (property get/set, event add/remove, operators) — they're represented by their property/event entry.
- Enums: list members as `Name = value` under a `Members:` section instead of methods.

### Large-type split rule

If a rendered `content` exceeds **~8 KB** (a few hundred members), split into continuation chunks:
- `heading` = `FileSchemaTree (members 2/3)`; `topic` unchanged; members partitioned across rows.
- Keeps each chunk FTS-scannable and avoids unbounded rows. Most types fit in one chunk.
> Note: the prose parsers cap `content` at 4 KB; the **reflection path does NOT truncate** — it splits instead, so no API is lost.

## Decompiled-body flag (`code_example`)

A small, configurable **allowlist of fully-qualified type names** gets a decompiled C# body in `code_example` (item 4/5, via `ilspycmd` 8.2). Default flag set targets the known assistant workflows:
- `SoftVelocity.DataDictionary.FileSchemaEditor.FileSchemaTree`, `FileSchemaPad`, `FileSchemaControl`
- `SoftVelocity.DataDictionary.Editor.EntityBrowserDetails`, `EntityBrowserDetailsMaker`
- `SoftVelocity.DataDictionary.DataDictionaryService`, `DDFile`, `DDField`
- key interfaces: `IPadContent`, `IDataDictionary`, `IFileSchemaPadController`

Everything else is **signature + member-list only** (no decompiled body) to keep the DB compact. The flag list lives in the extractor config so it can grow without schema changes.

## `libraries` row (recap from STORAGE-MODEL.md)

`name='Clarion IDE API'`, `vendor='IDE-Internals'`, `version='12.0.0.14000'`, `source_path='C:\Clarion12\bin (+Addins)'`, `source_format='reflection'`, `tags='bundled,regenerable,ide-api,dotnet'`.

## FTS behaviour

`doc_fts` indexes `class_name, method_name, heading, content, code_example, signature`. Because members are rendered into `content`, a query like `query_docs("FileSchemaTree AddColumn")` matches the type chunk; `query_docs(library="Clarion IDE API", query="AddParent")` scopes to this library. Per-member relevance ranking is the accepted trade-off of type-level granularity (PM decision).

## Open items handed to item 4/5

- Implement `DocGraphService.IngestReflection(targets, flagSet)` reusing `EnsureLibrary`/`DeleteLibraryChunks`/`InsertChunks`/`RebuildFtsIndex`.
- Wire `ilspycmd` for the flagged decompile set (lazy — only flagged types).
- Confirm the 8 KB split threshold against the largest real types (`ICSharpCode.SharpDevelop` has 1088 types but per-type member counts are the relevant axis).
