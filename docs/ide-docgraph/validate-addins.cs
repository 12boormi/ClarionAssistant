using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

class Validate
{
    static readonly string BinDir = @"C:\Clarion12\bin";
    static readonly string FxDir  = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319";
    static readonly string AddinRoot = @"C:\Clarion12\bin\Addins\BackendBindings\ClarionBinding";

    static readonly string[] Curated =
    {
        @"C:\Clarion12\bin\Addins\BackendBindings\ClarionBinding\ClarionWin\CWBinding.dll",
        @"C:\Clarion12\bin\Addins\BackendBindings\ClarionBinding\Common\ClarionParser.dll",
        @"C:\Clarion12\bin\Addins\BackendBindings\ClarionBinding\Common\clarion.gen.dll",
        @"C:\Clarion12\bin\Addins\BackendBindings\ClarionBinding\Common\ClarionNetWindow.dll",
        @"C:\Clarion12\bin\Addins\BackendBindings\ClarionBinding\Common\ClarionNetReport.dll",
    };

    static MetadataLoadContext MakeContext(bool addinsWin)
    {
        var ordered = new List<string>();
        // priority order controls who wins on simple-name collision
        if (addinsWin)
        {
            ordered.AddRange(Directory.GetFiles(AddinRoot, "*.dll", SearchOption.AllDirectories));
            ordered.AddRange(Directory.GetFiles(BinDir, "*.dll"));
        }
        else
        {
            ordered.AddRange(Directory.GetFiles(BinDir, "*.dll"));
            ordered.AddRange(Directory.GetFiles(AddinRoot, "*.dll", SearchOption.AllDirectories));
        }
        ordered.AddRange(Directory.GetFiles(FxDir, "*.dll"));

        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in ordered)
        {
            var key = Path.GetFileNameWithoutExtension(p);
            if (!byName.ContainsKey(key)) byName[key] = p;   // first wins
        }
        var resolver = new System.Reflection.PathAssemblyResolver(byName.Values);
        return new MetadataLoadContext(resolver, coreAssemblyName: "mscorlib");
    }

    static void Run(bool addinsWin)
    {
        Console.WriteLine($"\n========== RESOLVER PRIORITY: {(addinsWin ? "Addins-first" : "bin-first")} ==========");
        MetadataLoadContext mlc;
        try { mlc = MakeContext(addinsWin); }
        catch (Exception ex) { Console.WriteLine("  resolver build FAILED: " + ex.Message); return; }

        foreach (var path in Curated)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (!File.Exists(path)) { Console.WriteLine($"  MISSING   {name}"); continue; }
            try
            {
                var asm = mlc.LoadFromAssemblyPath(path);
                int types; int depFail = 0; string detail = "";
                try { types = asm.GetTypes().Length; }
                catch (ReflectionTypeLoadException rtle)
                {
                    types = rtle.Types?.Count(t => t != null) ?? 0;
                    depFail = rtle.LoaderExceptions?.Length ?? 0;
                    detail = string.Join(" | ",
                        (rtle.LoaderExceptions ?? Array.Empty<Exception>())
                        .Select(e => e.Message).Distinct().Take(3));
                }
                string status = depFail == 0 ? "OK" : "OK-PARTIAL";
                string line = $"  {status,-11} {name,-20} ver={asm.GetName().Version} types={types}";
                if (depFail > 0) line += $" depFails={depFail} :: {detail}";
                Console.WriteLine(line);
            }
            catch (BadImageFormatException) { Console.WriteLine($"  NATIVE    {name} (no managed metadata)"); }
            catch (Exception ex) { Console.WriteLine($"  LOAD-FAIL {name} :: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    static void Main()
    {
        Console.WriteLine("=== Curated bin\\Addins validation (item 1 scope #3) ===");
        Console.WriteLine("Curated set: CWBinding, ClarionParser, clarion.gen, ClarionNetWindow, ClarionNetReport");
        Run(addinsWin: false);   // bin core wins shared simple-names
        Run(addinsWin: true);    // addin-local versions win
    }
}
