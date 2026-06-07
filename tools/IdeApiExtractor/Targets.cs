using System;
using System.Collections.Generic;

namespace ClarionAssistant.Tools.IdeApiExtractor
{
    /// <summary>
    /// The locked 54-assembly target set (see docs/ide-docgraph/TARGET-ASSEMBLIES.md) by simple name,
    /// plus the decompile-body flag allowlist (see docs/ide-docgraph/CHUNK-SCHEMA.md).
    /// </summary>
    internal static class Targets
    {
        // 38 core (bin\) + 5 curated Addins + 11 DD/object-model additions = 54.
        public static readonly HashSet<string> Assemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // --- SoftVelocity core (22, bin\) ---
            "Clarion.asl","Clarion.Core","Clarion.dct","Clarion.Options","Clarion.prj","Clarion.TplHelp",
            "SoftVelocity.Clarion.ClarionCodeProvider","SoftVelocity.Clarion.Classes","SoftVelocity.Clarion.CompilerMessages",
            "SoftVelocity.Clarion.Design","SoftVelocity.Clarion.FileIO","SoftVelocity.Clarion.Files",
            "SoftVelocity.Clarion.LINQToFileProvider","SoftVelocity.Clarion.LINQToFileProviderNetDrv",
            "SoftVelocity.Clarion.Procedures","SoftVelocity.Clarion.Runtime.Classes","SoftVelocity.Clarion.Runtime.Procedures",
            "SoftVelocity.CW.Build.Tasks","SoftVelocity.Ide.Core","Softvelocity.ReportConvert","SoftVelocity.ReportPreviewer","SoftVelocity.RWClasses",
            // --- managed Cla* (3, bin\) ---
            "ClaRunExt","Claprnet","ClarionDL",
            // --- SharpDevelop core (8, bin\) ---
            "ICSharpCode.SharpDevelop","ICSharpCode.TextEditor","ICSharpCode.NRefactory","ICSharpCode.SharpDevelop.Dom",
            "ICSharpCode.Core","ICSharpCode.SharpDevelop.Widgets","ICSharpCode.Build.Tasks","ICSharpCode.SharpDevelop.Sda",
            // --- SharpDevelop deps (5, bin\) ---
            "Mono.Cecil","SmartPropertyGrid","WeifenLuo.WinFormsUI.Docking","Aga.Controls","Skybound.VisualTips",
            // --- curated Addins (5) ---
            "CWBinding","ClarionParser","clarion.gen","ClarionNetWindow","ClarionNetReport",
            // --- Group F: DD family + object model (11) ---
            "DataDictionaryEditor","DataDictionaryFile","DataDictionary","SoftVelocity.DataDictionary.Design","DictionaryDiagram",
            "Generator","CommonSources","CommonControl","ListControl","Browser","WindowPreviewer",
        };

        /// <summary>
        /// Types that get a decompiled C# body in code_example (via ilspycmd). Matched on FullName.
        /// Keep small — the known assistant workflows (variable-add flow, dictionary manipulation).
        /// </summary>
        public static readonly HashSet<string> DecompileFlag = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SoftVelocity.DataDictionary.FileSchemaEditor.FileSchemaTree",
            "SoftVelocity.DataDictionary.FileSchemaEditor.FileSchemaPad",
            "SoftVelocity.DataDictionary.FileSchemaEditor.FileSchemaControl",
            "SoftVelocity.DataDictionary.Editor.EntityBrowserDetails",
            "SoftVelocity.DataDictionary.Editor.EntityBrowserDetailsMaker",
            "SoftVelocity.DataDictionary.DataDictionaryService",
            "SoftVelocity.DataDictionary.DDFile",
            "SoftVelocity.DataDictionary.DDField",
            "ICSharpCode.SharpDevelop.Gui.IPadContent",
            "SoftVelocity.DataDictionary.Design.IDataDictionary",
            "SoftVelocity.DataDictionary.Schema.IFileSchemaPadController",
        };

        // Build stamp recorded on the library row (see ticket).
        public const string BuildStamp = "12.0.0.14000";
        public const string LibraryName = "Clarion IDE API";
        public const string LibraryVendor = "IDE-Internals";
    }
}
