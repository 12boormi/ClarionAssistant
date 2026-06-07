using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

class Spike
{
    static readonly string BinDir = @"C:\Clarion12\bin";
    static readonly string FxDir  = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319";

    // Vendor classification for the target set.
    static string Vendor(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.StartsWith("softvelocity") || n.StartsWith("clarion.") || n == "clarionet")
            return "SoftVelocity";
        if (n.StartsWith("cla"))            // ClaIde, ClaAnyScreenNet, Claprnet, plus many native Cla* runtime/driver dlls
            return "Clarion-Cla*";
        if (n.StartsWith("icsharpcode"))
            return "SharpDevelop";
        if (n.StartsWith("aga.controls") || n.StartsWith("weifenluo") || n.StartsWith("smartpropertygrid")
            || n.StartsWith("mono.cecil") || n.StartsWith("skybound") || n.StartsWith("cntbuilder"))
            return "SharpDevelop-dep";
        if (n.StartsWith("devexpress") || n.StartsWith("awssdk") || n.StartsWith("log4net")
            || n.StartsWith("microsoft.web.webview2") || n.StartsWith("system.")
            || n.StartsWith("seriousbit") || n.StartsWith("autoupdater") || n.StartsWith("stringfunctions"))
            return "ThirdParty";
        return "Other";
    }

    static void Main()
    {
        var binDlls = Directory.GetFiles(BinDir, "*.dll");
        // Resolver: every dll in bin + the .NET Framework v4 dir (for mscorlib/System/etc).
        var resolverPaths = new List<string>();
        resolverPaths.AddRange(binDlls);
        resolverPaths.AddRange(Directory.GetFiles(FxDir, "*.dll"));
        // de-dup by file name (PathAssemblyResolver rejects duplicate simple names)
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in resolverPaths)
        {
            var key = Path.GetFileNameWithoutExtension(p);
            if (!byName.ContainsKey(key)) byName[key] = p;
        }
        var resolver = new System.Reflection.PathAssemblyResolver(byName.Values);
        var mlc = new MetadataLoadContext(resolver, coreAssemblyName: "mscorlib");

        var rows = new List<(string name, string vendor, string status, int types, int depFails, string detail)>();

        foreach (var path in binDlls.OrderBy(p => p))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            string vendor = Vendor(name);
            try
            {
                var asm = mlc.LoadFromAssemblyPath(path);
                int typeCount = 0, depFail = 0;
                string detail = "";
                try
                {
                    typeCount = asm.GetTypes().Length;
                }
                catch (ReflectionTypeLoadException rtle)
                {
                    typeCount = rtle.Types?.Count(t => t != null) ?? 0;
                    depFail = rtle.LoaderExceptions?.Length ?? 0;
                    detail = rtle.LoaderExceptions?.FirstOrDefault()?.Message ?? "";
                }
                string status = depFail == 0 ? "OK" : "OK-PARTIAL";
                rows.Add((name, vendor, status, typeCount, depFail, detail));
            }
            catch (BadImageFormatException)
            {
                rows.Add((name, vendor, "NATIVE", 0, 0, "no managed metadata (Win32 PE)"));
            }
            catch (Exception ex)
            {
                rows.Add((name, vendor, "LOAD-FAIL", 0, 0, ex.GetType().Name + ": " + ex.Message));
            }
        }

        // ---- Report ----
        Console.WriteLine("=== MetadataLoadContext SPIKE — C:\\Clarion12\\bin ===");
        Console.WriteLine("Total DLLs scanned: " + rows.Count);
        Console.WriteLine();

        string[] targetVendors = { "SoftVelocity", "Clarion-Cla*", "SharpDevelop", "SharpDevelop-dep" };
        Console.WriteLine("--- TARGET-SET assemblies (managed load results) ---");
        foreach (var v in targetVendors)
        {
            var grp = rows.Where(r => r.vendor == v).ToList();
            if (grp.Count == 0) continue;
            Console.WriteLine($"\n[{v}]  ({grp.Count} dlls)");
            foreach (var r in grp.OrderBy(r => r.status).ThenBy(r => r.name))
            {
                string line = $"  {r.status,-11} {r.name,-38} types={r.types,-5}";
                if (r.depFails > 0) line += $" depFails={r.depFails}";
                if (r.status != "OK" && r.detail.Length > 0)
                    line += "  :: " + (r.detail.Length > 80 ? r.detail.Substring(0, 80) : r.detail);
                Console.WriteLine(line);
            }
        }

        Console.WriteLine("\n--- SUMMARY BY STATUS (target set only) ---");
        var targetRows = rows.Where(r => targetVendors.Contains(r.vendor)).ToList();
        foreach (var g in targetRows.GroupBy(r => r.status).OrderBy(g => g.Key))
            Console.WriteLine($"  {g.Key,-11}: {g.Count()}");
        Console.WriteLine($"  TOTAL target : {targetRows.Count}");
        Console.WriteLine($"  managed-OK   : {targetRows.Count(r => r.status.StartsWith("OK"))}");
        Console.WriteLine($"  native       : {targetRows.Count(r => r.status == "NATIVE")}");
        Console.WriteLine($"  load-fail    : {targetRows.Count(r => r.status == "LOAD-FAIL")}");

        Console.WriteLine("\n--- DEPENDENCY-RESOLUTION FAILURES (any partial loads) ---");
        var partials = rows.Where(r => r.status == "OK-PARTIAL" || r.status == "LOAD-FAIL").ToList();
        if (partials.Count == 0)
            Console.WriteLine("  NONE — all managed assemblies fully resolved their dependencies.");
        else
            foreach (var r in partials)
                Console.WriteLine($"  {r.name} ({r.vendor}): {r.status} depFails={r.depFails} :: {r.detail}");

        Console.WriteLine("\n--- THIRD-PARTY / EXCLUDED (for reference) ---");
        foreach (var g in rows.Where(r => r.vendor == "ThirdParty" || r.vendor == "Other")
                              .GroupBy(r => r.vendor))
            Console.WriteLine($"  {g.Key}: {g.Count()} dlls ({g.Count(r => r.status.StartsWith("OK"))} managed, {g.Count(r => r.status=="NATIVE")} native)");
    }
}
