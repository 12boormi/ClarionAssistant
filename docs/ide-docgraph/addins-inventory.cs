using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

class Inv
{
    static readonly string BinDir = @"C:\Clarion12\bin";
    static readonly string AddinsDir = @"C:\Clarion12\bin\Addins";
    static readonly string FxDir = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319";

    static void Main()
    {
        // Names already in the locked bin\ + curated set — to flag "already in scope".
        var binNames = new HashSet<string>(
            Directory.GetFiles(BinDir, "*.dll").Select(p => Path.GetFileNameWithoutExtension(p)),
            StringComparer.OrdinalIgnoreCase);

        var all = Directory.GetFiles(BinDir, "*.dll")
            .Concat(Directory.GetFiles(AddinsDir, "*.dll", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(FxDir, "*.dll")).ToList();
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in all) { var k = Path.GetFileNameWithoutExtension(p); if (!byName.ContainsKey(k)) byName[k] = p; }
        var mlc = new MetadataLoadContext(new System.Reflection.PathAssemblyResolver(byName.Values), "mscorlib");

        // Unique managed assemblies under Addins NOT already in bin\.
        var rows = new List<(string name, int types, string topNs, bool clarionish)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.GetFiles(AddinsDir, "*.dll", SearchOption.AllDirectories)
                 .GroupBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase)
                 .Select(g => g.First()))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (binNames.Contains(name)) continue;       // already covered by bin\ scope
            if (!seen.Add(name)) continue;

            Assembly asm;
            try { asm = mlc.LoadFromAssemblyPath(path); }
            catch (BadImageFormatException) { continue; } // native
            catch { continue; }

            Type[] ts;
            try { ts = asm.GetTypes(); }
            catch (ReflectionTypeLoadException r) { ts = r.Types.Where(x => x != null).ToArray(); }
            catch { continue; }

            string topNs = ts.Where(t => !t.IsNested && t.Namespace != null)
                .GroupBy(t => t.Namespace.Split('.').Take(2).Aggregate((a, b) => a + "." + b))
                .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "(none)";

            bool clarionish = topNs.StartsWith("SoftVelocity", StringComparison.OrdinalIgnoreCase)
                           || topNs.StartsWith("Clarion", StringComparison.OrdinalIgnoreCase)
                           || name.StartsWith("Clarion", StringComparison.OrdinalIgnoreCase)
                           || name.StartsWith("CW", StringComparison.OrdinalIgnoreCase)
                           || name.IndexOf("DataDictionary", StringComparison.OrdinalIgnoreCase) >= 0;

            rows.Add((name, ts.Length, topNs, clarionish));
        }

        Console.WriteLine($"Managed assemblies under bin\\Addins NOT in bin\\: {rows.Count}");
        Console.WriteLine();
        Console.WriteLine("==== CLARION/SOFTVELOCITY-RELEVANT (candidates to add) ====");
        foreach (var r in rows.Where(r => r.clarionish).OrderByDescending(r => r.types))
            Console.WriteLine($"  {r.name,-42} types={r.types,-5} ns={r.topNs}");

        Console.WriteLine();
        Console.WriteLine("==== OTHER (generic controls / third-party — likely skip) ====");
        foreach (var r in rows.Where(r => !r.clarionish).OrderByDescending(r => r.types))
            Console.WriteLine($"  {r.name,-42} types={r.types,-5} ns={r.topNs}");
    }
}
