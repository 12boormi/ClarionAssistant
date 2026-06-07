# SPIKE: Reflection-load Clarion IDE assemblies via MetadataLoadContext

**Ticket:** 8f1dda1c — Build "Clarion IDE API" DocGraph library from decompiled IDE assemblies
**Checklist item:** [0] SPIKE — confirm `MetadataLoadContext` can reflection-load the target assemblies from `C:\Clarion12\bin` with a folder resolver.
**Result:** ✅ **PASS** — 38 managed target assemblies load with **0 dependency-resolution failures**.

## How to reproduce

```
cd docs/spikes/ide-docgraph
dotnet run -c Release        # requires .NET SDK 9.x; reads C:\Clarion12\bin directly
```

Output is captured in [`spike-output.txt`](spike-output.txt). Loader source: [`Program.cs`](Program.cs).

## Approach

- `PathAssemblyResolver` built from **every `*.dll` in `C:\Clarion12\bin`** plus the **.NET Framework v4 dir** (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319`) to supply `mscorlib`/`System.*` — the IDE assemblies target .NET Framework 4.x, so the desktop CLR reference set is required for dependency resolution.
- De-dup resolver entries by simple name (`PathAssemblyResolver` rejects duplicate simple names).
- `MetadataLoadContext(resolver, coreAssemblyName: "mscorlib")`.
- For each `bin\*.dll`: `LoadFromAssemblyPath` then force-enumerate `GetTypes()` to surface dependency failures (`ReflectionTypeLoadException.LoaderExceptions`).

## Findings

| Status | Count | Meaning |
|---|---|---|
| OK (managed, all deps resolved) | **38** | extraction targets |
| NATIVE (`BadImageFormatException`) | 68 | Win32 PE Cla* runtime/driver DLLs — **exclude** |
| LOAD-FAIL | 0 | — |
| Dependency-resolution failures | **0** | resolver is sufficient unchanged |

**Key correction to the ticket estimate:** the ticket assumed "~47 SoftVelocity + 12 SharpDevelop ≈ 59". In reality the bulk of the `Cla*.dll` files are **native** (no managed metadata) and must be excluded. The real managed target set is **38 assemblies**.

### The 38 managed target assemblies

**SoftVelocity group (22):**
`Clarion.asl` (584 types), `Clarion.Core` (36), `Clarion.dct` (471), `Clarion.Options` (19), `Clarion.prj` (472), `Clarion.TplHelp` (343), `SoftVelocity.Clarion.ClarionCodeProvider` (549), `SoftVelocity.Clarion.Classes` (182), `SoftVelocity.Clarion.CompilerMessages` (12), `SoftVelocity.Clarion.Design` (46), `SoftVelocity.Clarion.FileIO` (73), `SoftVelocity.Clarion.Files` (424), `SoftVelocity.Clarion.LINQToFileProvider` (157), `SoftVelocity.Clarion.LINQToFileProviderNetDrv` (157), `SoftVelocity.Clarion.Procedures` (376), `SoftVelocity.Clarion.Runtime.Classes` (182), `SoftVelocity.Clarion.Runtime.Procedures` (376), `SoftVelocity.CW.Build.Tasks` (9), `SoftVelocity.Ide.Core` (6), `Softvelocity.ReportConvert` (44), `SoftVelocity.ReportPreviewer` (3), `SoftVelocity.RWClasses` (27).

**Managed Cla\* (3):** `Claprnet` (35), `ClarionDL` (25), `ClaRunExt` (290).

**SharpDevelop `ICSharpCode.*` (8):** `ICSharpCode.Build.Tasks` (22), `ICSharpCode.Core` (129), `ICSharpCode.NRefactory` (211), `ICSharpCode.SharpDevelop` (1088), `ICSharpCode.SharpDevelop.Dom` (177), `ICSharpCode.SharpDevelop.Sda` (11), `ICSharpCode.SharpDevelop.Widgets` (53), `ICSharpCode.TextEditor` (248).

**SharpDevelop deps (5):** `Aga.Controls` (106), `Mono.Cecil` (410), `Skybound.VisualTips` (73), `SmartPropertyGrid` (290), `WeifenLuo.WinFormsUI.Docking` (231).

### Excluded (reference)

- **Native (68):** all `Cla*` runtime/driver DLLs (`ClaIde`, `ClaRUN`, `Cladct`, `ClaDB*`, drivers, etc.) + `cntbuilder`.
- **True third-party (31 managed):** `DevExpress.*`, `awssdk.*`, `log4net`, `Microsoft.Web.WebView2.*`, `System.*`, `SeriousBit.Licensing`, `AutoUpdater.NET`, `StringFunctionsCaller`. These are not IDE-internal API and are out of scope per the ticket.

## Implications for downstream items

- **Item 1 (finalize list):** target = the 38 above. Decision still open: include `Mono.Cecil`/`Aga.Controls`/`WeifenLuo` (generic SharpDevelop deps, not Clarion-specific) or trim to the ~33 Clarion+SharpDevelop-core.
- **Item 4/5 (extractor):** the resolver recipe in `Program.cs` is the load foundation — reuse the bin + Framework-v4 PathAssemblyResolver verbatim. Native DLLs must be filtered by catching `BadImageFormatException` (done here).
- **Item 4 (schema):** `GetTypes()` works including non-public types via `asm.GetTypes()` already returns nested+internal; for full non-public member visibility use `BindingFlags.NonPublic | Public | Instance | Static | DeclaredOnly` during member enumeration.
- Build stamp to record: Clarion 12.0.0.14000 (per ticket).
