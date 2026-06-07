using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ClarionAssistant.Tools.IdeApiExtractor
{
    /// <summary>
    /// Regenerator for the bundled "Clarion IDE API" DocGraph library (ticket 8f1dda1c).
    /// Reflection-loads the 54 target IDE assemblies and writes one type-level chunk per type
    /// into the bundled docgraph.db. Re-runnable on Clarion upgrade (item 7).
    ///
    /// Usage:
    ///   IdeApiExtractor [--bin C:\Clarion12\bin] [--db &lt;path&gt;] [--dry-run] [--no-decompile] [--limit N]
    ///   --db        target DocGraph DB (default: %APPDATA%\ClarionAssistant\docgraph.db)
    ///   --dry-run   write to a temp DB instead of the real one; print stats; keep the temp file path
    ///   --no-decompile  skip ilspycmd bodies (faster)
    ///   --limit N   only process the first N target assemblies (smoke testing)
    /// </summary>
    internal static class Program
    {
        static int Main(string[] args)
        {
            string binDir = Arg(args, "--bin") ?? @"C:\Clarion12\bin";
            string fxDir = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319";
            bool dryRun = args.Contains("--dry-run");
            bool noDecompile = args.Contains("--no-decompile");
            int limit = int.TryParse(Arg(args, "--limit"), out var l) ? l : int.MaxValue;

            string dbPath = Arg(args, "--db") ?? DefaultDbPath();
            if (dryRun)
            {
                dbPath = Path.Combine(Path.GetTempPath(), "ide-api-dryrun.db");
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }

            if (!Directory.Exists(binDir)) { Console.Error.WriteLine("bin dir not found: " + binDir); return 2; }
            Console.WriteLine($"IdeApiExtractor — bin={binDir}");
            Console.WriteLine($"  target DB : {dbPath}{(dryRun ? "  (DRY RUN)" : "")}");
            Console.WriteLine($"  decompile : {(noDecompile ? "off" : "on (flagged types)")}");

            // ---- resolver + load context ----
            // bin\ first (wins simple-name collisions), then ALL of bin\Addins (deps like BinaryToText,
            // SharpZipLib live in sibling addin folders), then Framework v4 + WPF dir (WindowsBase etc.).
            string addinsDir = Path.Combine(binDir, "Addins");
            var resolverPaths = Directory.GetFiles(binDir, "*.dll").ToList();
            if (Directory.Exists(addinsDir))
                resolverPaths.AddRange(Directory.GetFiles(addinsDir, "*.dll", SearchOption.AllDirectories));
            if (Directory.Exists(fxDir))
            {
                resolverPaths.AddRange(Directory.GetFiles(fxDir, "*.dll"));
                string wpf = Path.Combine(fxDir, "WPF");
                if (Directory.Exists(wpf)) resolverPaths.AddRange(Directory.GetFiles(wpf, "*.dll"));
            }
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in resolverPaths)
            {
                var k = Path.GetFileNameWithoutExtension(p);
                if (!byName.ContainsKey(k)) byName[k] = p;
            }
            var mlc = new MetadataLoadContext(new PathAssemblyResolver(byName.Values), "mscorlib");

            // ---- resolve canonical paths for the 54 targets ----
            var targetPaths = new List<string>();
            foreach (var name in Targets.Assemblies)
            {
                if (byName.TryGetValue(name, out var path)) targetPaths.Add(path);
                else Console.Error.WriteLine("  WARN target not found: " + name);
            }
            targetPaths = targetPaths.Take(limit).ToList();

            var decompiler = noDecompile ? null : new Decompiler();
            if (decompiler != null && !decompiler.Available)
                Console.Error.WriteLine("  WARN ilspycmd not found — proceeding without decompiled bodies.");

            // ---- enumerate + render ----
            var allChunks = new List<DocChunk>();
            int typeCount = 0, asmCount = 0, decompiled = 0;
            var seenAsm = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in targetPaths)
            {
                Assembly asm;
                try { asm = mlc.LoadFromAssemblyPath(path); }
                catch (BadImageFormatException) { continue; }     // native — skip
                catch (Exception ex) { Console.Error.WriteLine($"  load fail {Path.GetFileName(path)}: {ex.Message}"); continue; }
                if (!seenAsm.Add(asm.GetName().Name)) continue;     // dedup by simple name
                asmCount++;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException r) { types = r.Types.Where(t => t != null).ToArray(); }

                foreach (var t in types)
                {
                    if (t.IsGenericParameter) continue;
                    typeCount++;
                    DocChunk[] chunks;
                    try { chunks = ReflectionRenderer.Render(t).ToArray(); }
                    catch (Exception ex) { Console.Error.WriteLine($"  render fail {t.FullName}: {ex.Message}"); continue; }

                    if (decompiler != null && decompiler.Available && Targets.DecompileFlag.Contains(t.FullName))
                    {
                        string body = decompiler.DecompileType(path, t.FullName);
                        if (body != null) { chunks[0].CodeExample = body; decompiled++; }
                    }
                    allChunks.AddRange(chunks);
                }
                Console.WriteLine($"  + {asm.GetName().Name,-40} types={types.Length}");
            }

            Console.WriteLine($"\nEnumerated {asmCount} assemblies, {typeCount} types → {allChunks.Count} chunks ({decompiled} decompiled bodies).");

            // ---- write ----
            using (var writer = new DocGraphWriter(dbPath))
            {
                long libId = writer.EnsureLibrary(
                    Targets.LibraryName, Targets.LibraryVendor, Targets.BuildStamp,
                    binDir + " (+Addins)", "reflection", "bundled,regenerable,ide-api,dotnet");
                writer.DeleteLibraryChunks(libId);
                int inserted = writer.InsertChunks(libId, allChunks);
                int indexed = writer.RebuildFtsIndex();
                Console.WriteLine($"Library id={libId}: inserted {inserted} chunks, FTS indexed {indexed} rows.");

                if (args.Contains("--verify"))
                {
                    Console.WriteLine("\n=== FTS verification (item-6 sample lookups) ===");
                    foreach (var q in new[] { "FileSchemaTree AND AddColumn", "EntityBrowserDetails AND AddParent",
                                              "IPadContent", "DDField", "DataDictionaryService" })
                    {
                        var hits = writer.QueryFts(q, 3);
                        Console.WriteLine($"  MATCH '{q}' → {hits.Count} hit(s)");
                        foreach (var h in hits) Console.WriteLine($"      [{h.topic}] {h.cls}  ({h.heading})");
                    }
                }
            }

            Console.WriteLine(dryRun ? $"DRY RUN complete. Temp DB: {dbPath}" : "Done.");
            return 0;
        }

        static string DefaultDbPath()
        {
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClarionAssistant");
            Directory.CreateDirectory(appData);
            return Path.Combine(appData, "docgraph.db");
        }

        static string Arg(string[] args, string key)
        {
            int i = Array.IndexOf(args, key);
            return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : null;
        }
    }
}
