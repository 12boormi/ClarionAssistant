# Clarion IDE API DocGraph — Locked Target Assembly List

**Ticket:** 8f1dda1c · **Checklist item [1]** (finalize target list) · **Status:** locked
**Build stamp:** Clarion **12.0.0.14000** (record on every chunk; some .NET window/report assemblies version as 4.0.0.14000 — see notes)
**Scope decision (PM):** option #3 — the 38 spike-validated `bin\` managed assemblies **+ a curated subset** of the `bin\Addins\` designer/parser layer. Per-control designer DLLs and true third-party are **out**. "Can always add more later."

## Total: 54 assemblies (38 core + 5 curated Addins + 11 DD/object-model additions)

> **Amended (item 3 finding, PM-approved):** the original locked set was 43. While grounding the chunk schema we found the **FileSchema / DataDictionary API** (item 6's own verify targets `FileSchemaTree`, `EntityBrowserDetails`, plus `DDFile`/`DDField`) lives in `bin\Addins` assemblies that were not in the 43. PM approved expanding scope to "DD family + object model" → **+11 assemblies (Group F)**, total **54**, ≈ **11,636 types**.

All 54 confirmed loadable via `MetadataLoadContext` with **0 dependency-resolution failures**. See [`../spikes/ide-docgraph/`](../spikes/ide-docgraph/) for the core-38 spike and [`validate-addins-output.txt`](validate-addins-output.txt) for the curated-5 validation; the 11 additions validated in the item-3 expanded run (all `12.0.0.14000` except CommonControl/ListControl `4.0.0.14000`).

### Group A — SoftVelocity core (22) · from `C:\Clarion12\bin`

| Assembly | Types |
|---|---|
| Clarion.asl | 584 |
| Clarion.Core | 36 |
| Clarion.dct | 471 |
| Clarion.Options | 19 |
| Clarion.prj | 472 |
| Clarion.TplHelp | 343 |
| SoftVelocity.Clarion.ClarionCodeProvider | 549 |
| SoftVelocity.Clarion.Classes | 182 |
| SoftVelocity.Clarion.CompilerMessages | 12 |
| SoftVelocity.Clarion.Design | 46 |
| SoftVelocity.Clarion.FileIO | 73 |
| SoftVelocity.Clarion.Files | 424 |
| SoftVelocity.Clarion.LINQToFileProvider | 157 |
| SoftVelocity.Clarion.LINQToFileProviderNetDrv | 157 |
| SoftVelocity.Clarion.Procedures | 376 |
| SoftVelocity.Clarion.Runtime.Classes | 182 |
| SoftVelocity.Clarion.Runtime.Procedures | 376 |
| SoftVelocity.CW.Build.Tasks | 9 |
| SoftVelocity.Ide.Core | 6 |
| Softvelocity.ReportConvert | 44 |
| SoftVelocity.ReportPreviewer | 3 |
| SoftVelocity.RWClasses | 27 |

### Group B — Managed Cla\* (3) · from `C:\Clarion12\bin`

| Assembly | Types |
|---|---|
| ClaRunExt | 290 |
| Claprnet | 35 |
| ClarionDL | 25 |

### Group C — SharpDevelop core `ICSharpCode.*` (8) · from `C:\Clarion12\bin`

| Assembly | Types |
|---|---|
| ICSharpCode.SharpDevelop | 1088 |
| ICSharpCode.TextEditor | 248 |
| ICSharpCode.NRefactory | 211 |
| ICSharpCode.SharpDevelop.Dom | 177 |
| ICSharpCode.Core | 129 |
| ICSharpCode.SharpDevelop.Widgets | 53 |
| ICSharpCode.Build.Tasks | 22 |
| ICSharpCode.SharpDevelop.Sda | 11 |

### Group D — SharpDevelop deps (5) · from `C:\Clarion12\bin`

| Assembly | Types | Note |
|---|---|---|
| Mono.Cecil | 410 | IL inspection lib (generic) |
| SmartPropertyGrid | 290 | property grid control |
| WeifenLuo.WinFormsUI.Docking | 231 | docking framework |
| Aga.Controls | 106 | tree/list controls |
| Skybound.VisualTips | 73 | tooltips |

> Group D are generic (non-Clarion-specific) but are part of the IDE's shipped surface and referenced by the SharpDevelop core. Kept for now; trivially droppable later if noise.

### Group E — Curated `bin\Addins` designer/parser layer (5)

Canonical paths under `C:\Clarion12\bin\Addins\BackendBindings\ClarionBinding\`:

| Assembly | Path | Ver | Types |
|---|---|---|---|
| clarion.gen | `Common\clarion.gen.dll` | 12.0.0.14000 | 591 |
| ClarionParser | `Common\ClarionParser.dll` | 12.0.0.14000 | 590 |
| ClarionNetReport | `Common\ClarionNetReport.dll` | 4.0.0.14000 | 227 |
| CWBinding | `ClarionWin\CWBinding.dll` | 12.0.0.14000 | 101 |
| ClarionNetWindow | `Common\ClarionNetWindow.dll` | 4.0.0.14000 | 49 |

> `ClarionParser.dll` also exists at `Common\Controls\ClarionParser.dll` (duplicate) — use the `Common\` copy as canonical.

### Group F — DataDictionary family + object model (11) · `bin\Addins\BackendBindings\ClarionBinding\Common\`

Added per the item-3 scope expansion. All load with 0 dep failures.

| Assembly | Types | Ver | Why |
|---|---|---|---|
| CommonSources | 399 | 12.0.0.14000 | source/file object model (`SoftVelocity.Common`) |
| CommonControl | 322 | 4.0.0.14000 | ClarionNet control object model |
| ListControl | 301 | 4.0.0.14000 | list/browse control object model |
| DataDictionaryEditor | 296 | 12.0.0.14000 | **FileSchemaTree, FileSchemaPad, FileSchemaControl, EntityBrowserDetails** (item 6 targets) |
| DataDictionaryFile | 205 | 12.0.0.14000 | GlobalDataDictionary, DD tables |
| Generator | 192 | 12.0.0.14000 | code generator object model |
| DataDictionary | 175 | 12.0.0.14000 | **DDFile, DDField, DataDictionaryService, DDDataDictionary** |
| SoftVelocity.DataDictionary.Design | 143 | 12.0.0.14000 | `IDataDictionary`, `IFileSchemaPadController` interfaces |
| Browser | 45 | 12.0.0.14000 | data browser |
| DictionaryDiagram | 30 | 12.0.0.14000 | DD diagram |
| WindowPreviewer | 23 | 12.0.0.14000 | window preview |

> Optional later adds (trivial, designer-relevant): `FormulaEditor` (17), `TabOrderAssistant` (22), and the per-control DLLs (`ButtonControl`, `EntryControl`, … ~14 small assemblies). Explicitly third-party and excluded: `SharpSvn`, MindFusion (`Ddc`/`Ddp`), `ICSharpCode.SharpZipLib`, `ComponentInspector`, etc. (see item-3 inventory run).

## Resolver recipe (for the item-5 extractor)

`PathAssemblyResolver` built from, in priority order (first-wins on simple-name collision):
1. `C:\Clarion12\bin\*.dll`
2. `C:\Clarion12\bin\Addins\BackendBindings\ClarionBinding\**\*.dll` (recursive — supplies the curated set's private deps)
3. `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\*.dll` (mscorlib / System.* — assemblies are .NET Framework 4.x)

`MetadataLoadContext(resolver, coreAssemblyName: "mscorlib")`. Native DLLs filtered by catching `BadImageFormatException`. **bin-first vs Addins-first produced identical results** for the curated 5, so bin-first is the canonical rule.

## Explicitly EXCLUDED

- **Native Win32 (68):** all `Cla*` runtime/driver DLLs in `bin\` (`ClaIde`, `ClaRUN`, `Cladct`, `ClaDB*`, drivers…) + `cntbuilder`. No managed metadata.
- **Per-control designer DLLs** under `Addins\...\Common\Controls\` (`BoxControl`, `ButtonControl`, `EntryControl`, …): deferred (option #3 excludes them).
- **True third-party (31 managed in bin\):** `DevExpress.*`, `awssdk.*`, `log4net`, `Microsoft.Web.WebView2.*`, `System.*`, `SeriousBit.Licensing`, `AutoUpdater.NET`, `StringFunctionsCaller`.

## Follow-up hooks (later expansion)

- Add the `Common\Controls\*Control.dll` designer object model when the window-designer API becomes a priority.
- `clarion.gen` (591 types) is the generator object model — likely the highest-value target for "how does codegen work" questions.
