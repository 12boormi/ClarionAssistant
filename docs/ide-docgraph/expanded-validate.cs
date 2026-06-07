using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

class Expanded
{
    static readonly string BinDir = @"C:\Clarion12\bin";
    static readonly string AddinsDir = @"C:\Clarion12\bin\Addins";
    static readonly string FxDir = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319";

    // The 11 additions (DD family + object model) — by simple name.
    static readonly string[] Additions = {
        "DataDictionaryEditor","DataDictionaryFile","DataDictionary","SoftVelocity.DataDictionary.Design","DictionaryDiagram",
        "Generator","CommonSources","CommonControl","ListControl","Browser","WindowPreviewer",
    };

    static void Main()
    {
        var all = Directory.GetFiles(BinDir, "*.dll")
            .Concat(Directory.GetFiles(AddinsDir, "*.dll", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(FxDir, "*.dll")).ToList();
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in all) { var k = Path.GetFileNameWithoutExtension(p); if (!byName.ContainsKey(k)) byName[k] = p; }
        var mlc = new MetadataLoadContext(new System.Reflection.PathAssemblyResolver(byName.Values), "mscorlib");

        long totTypes = 0;
        Console.WriteLine("=== 11 ADDITIONS — load validation + canonical paths ===");
        foreach (var name in Additions)
        {
            if (!byName.TryGetValue(name, out var path)) { Console.WriteLine($"  MISSING   {name}"); continue; }
            try
            {
                var asm = mlc.LoadFromAssemblyPath(path);
                int types; int depFail = 0;
                try { types = asm.GetTypes().Length; }
                catch (ReflectionTypeLoadException r) { types = r.Types.Count(t => t != null); depFail = r.LoaderExceptions.Length; }
                totTypes += types;
                string rel = path.Replace(@"C:\Clarion12\bin\Addins\", "Addins\\");
                Console.WriteLine($"  OK  {name,-36} ver={asm.GetName().Version,-14} types={types,-5}{(depFail>0?$" depFails={depFail}":"")}  {rel}");
            }
            catch (Exception ex) { Console.WriteLine($"  FAIL {name} :: {ex.GetType().Name}: {ex.Message}"); }
        }
        Console.WriteLine($"\nAdditions total types: {totTypes}");
        Console.WriteLine("(43 core+curated types were 9,505 → expanded grand total ≈ " + (9505 + totTypes) + " types)");
    }
}
