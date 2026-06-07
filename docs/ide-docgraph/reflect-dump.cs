using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

class SchemaDump
{
    static readonly string BinDir = @"C:\Clarion12\bin";
    static readonly string FxDir  = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319";
    static readonly string AddinRoot = @"C:\Clarion12\bin\Addins\BackendBindings\ClarionBinding";

    // The 43 locked target assemblies (simple names).
    static readonly HashSet<string> Targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // SoftVelocity core (22)
        "Clarion.asl","Clarion.Core","Clarion.dct","Clarion.Options","Clarion.prj","Clarion.TplHelp",
        "SoftVelocity.Clarion.ClarionCodeProvider","SoftVelocity.Clarion.Classes","SoftVelocity.Clarion.CompilerMessages",
        "SoftVelocity.Clarion.Design","SoftVelocity.Clarion.FileIO","SoftVelocity.Clarion.Files",
        "SoftVelocity.Clarion.LINQToFileProvider","SoftVelocity.Clarion.LINQToFileProviderNetDrv",
        "SoftVelocity.Clarion.Procedures","SoftVelocity.Clarion.Runtime.Classes","SoftVelocity.Clarion.Runtime.Procedures",
        "SoftVelocity.CW.Build.Tasks","SoftVelocity.Ide.Core","Softvelocity.ReportConvert","SoftVelocity.ReportPreviewer","SoftVelocity.RWClasses",
        // managed Cla* (3)
        "ClaRunExt","Claprnet","ClarionDL",
        // SharpDevelop core (8)
        "ICSharpCode.SharpDevelop","ICSharpCode.TextEditor","ICSharpCode.NRefactory","ICSharpCode.SharpDevelop.Dom",
        "ICSharpCode.Core","ICSharpCode.SharpDevelop.Widgets","ICSharpCode.Build.Tasks","ICSharpCode.SharpDevelop.Sda",
        // SharpDevelop deps (5)
        "Mono.Cecil","SmartPropertyGrid","WeifenLuo.WinFormsUI.Docking","Aga.Controls","Skybound.VisualTips",
        // curated Addins (5)
        "CWBinding","ClarionParser","clarion.gen","ClarionNetWindow","ClarionNetReport",
    };

    // Representative types to dump in full (item 6 verify targets + samples).
    static readonly string[] SampleTypeNames = { "FileSchemaTree", "IPadContent", "EntityBrowserDetails", "FileSchemaPad" };

    static MetadataLoadContext Mlc()
    {
        var ordered = new List<string>();
        ordered.AddRange(Directory.GetFiles(BinDir, "*.dll"));
        ordered.AddRange(Directory.GetFiles(AddinRoot, "*.dll", SearchOption.AllDirectories));
        ordered.AddRange(Directory.GetFiles(FxDir, "*.dll"));
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in ordered)
        {
            var k = Path.GetFileNameWithoutExtension(p);
            if (!byName.ContainsKey(k)) byName[k] = p;
        }
        return new MetadataLoadContext(new System.Reflection.PathAssemblyResolver(byName.Values), "mscorlib");
    }

    static string Vis(TypeAttributes a)
    {
        switch (a & TypeAttributes.VisibilityMask)
        {
            case TypeAttributes.Public: case TypeAttributes.NestedPublic: return "public";
            case TypeAttributes.NestedFamily: return "protected";
            case TypeAttributes.NestedAssembly: return "internal";
            case TypeAttributes.NestedFamORAssem: return "protected internal";
            case TypeAttributes.NestedFamANDAssem: return "private protected";
            case TypeAttributes.NestedPrivate: return "private";
            default: return "internal"; // NotPublic top-level
        }
    }
    static string MethodVis(MethodBase m)
    {
        if (m.IsPublic) return "public";
        if (m.IsFamily) return "protected";
        if (m.IsAssembly) return "internal";
        if (m.IsFamilyOrAssembly) return "protected internal";
        if (m.IsFamilyAndAssembly) return "private protected";
        return "private";
    }

    static string T(Type t)
    {
        if (t == null) return "void";
        if (t.IsByRef) return "ref " + T(t.GetElementType());
        if (t.IsArray) return T(t.GetElementType()) + "[]";
        if (t.IsGenericParameter) return t.Name;
        if (t.IsGenericType)
        {
            string baseName = t.Name;
            int tick = baseName.IndexOf('`');
            if (tick >= 0) baseName = baseName.Substring(0, tick);
            var args = t.GetGenericArguments().Select(T);
            string ns = t.IsNested ? (t.DeclaringType?.Name + ".") : (t.Namespace + ".");
            return ns + baseName + "<" + string.Join(", ", args) + ">";
        }
        string full = t.IsNested ? (t.DeclaringType?.Name + "." + t.Name) : (t.Namespace + "." + t.Name);
        return full;
    }

    static string Kind(Type t)
    {
        if (t.IsInterface) return "interface";
        if (t.IsEnum) return "enum";
        if (t.IsValueType) return "struct";
        if (typeof(Delegate).IsAssignableFrom(t.BaseType) || (t.BaseType != null && t.BaseType.Name == "MulticastDelegate")) return "delegate";
        if (t.IsAbstract && t.IsSealed) return "static class";
        return "class";
    }

    static void DumpType(Type t, StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("================================================================");
        sb.AppendLine($"TYPE  {t.FullName}");
        sb.AppendLine($"  kind={Kind(t)}  visibility={Vis(t.Attributes)}  assembly={t.Assembly.GetName().Name}@{t.Assembly.GetName().Version}");
        if (t.IsNested) sb.AppendLine($"  declaringType={t.DeclaringType?.FullName}");
        if (t.BaseType != null && t.BaseType.FullName != "System.Object")
            sb.AppendLine($"  base={t.BaseType.FullName}");
        var ifaces = t.GetInterfaces();
        if (ifaces.Length > 0)
            sb.AppendLine($"  implements={string.Join(", ", ifaces.Select(i => i.FullName))}");

        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var c in t.GetConstructors(flags))
            sb.AppendLine($"  .ctor   [{MethodVis(c)}] ({string.Join(", ", c.GetParameters().Select(p => T(p.ParameterType) + " " + p.Name))})");

        foreach (var p in t.GetProperties(flags).OrderBy(p => p.Name))
        {
            var acc = p.GetAccessors(true).FirstOrDefault();
            string gs = (p.CanRead ? "get;" : "") + (p.CanWrite ? "set;" : "");
            sb.AppendLine($"  prop    [{(acc != null ? MethodVis(acc) : "?")}] {T(p.PropertyType)} {p.Name} {{ {gs} }}");
        }

        foreach (var f in t.GetFields(flags).OrderBy(f => f.Name))
            sb.AppendLine($"  field   [{(f.IsPublic ? "public" : f.IsFamily ? "protected" : f.IsAssembly ? "internal" : "private")}]{(f.IsStatic ? " static" : "")} {T(f.FieldType)} {f.Name}");

        foreach (var e in t.GetEvents(flags).OrderBy(e => e.Name))
            sb.AppendLine($"  event   {T(e.EventHandlerType)} {e.Name}");

        foreach (var m in t.GetMethods(flags).Where(m => !m.IsSpecialName).OrderBy(m => m.Name))
            sb.AppendLine($"  method  [{MethodVis(m)}]{(m.IsStatic ? " static" : "")}{(m.IsVirtual && !m.IsFinal ? " virtual" : "")} {T(m.ReturnType)} {m.Name}({string.Join(", ", m.GetParameters().Select(p => T(p.ParameterType) + " " + p.Name))})");
    }

    static void Main()
    {
        var mlc = Mlc();
        var loaded = new List<Assembly>();
        foreach (var path in Directory.GetFiles(BinDir, "*.dll")
                 .Concat(Directory.GetFiles(AddinRoot, "*.dll", SearchOption.AllDirectories)))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (!Targets.Contains(name)) continue;
            try { loaded.Add(mlc.LoadFromAssemblyPath(path)); } catch { }
        }
        // de-dup assemblies by simple name (curated Addins may collide with bin copies)
        loaded = loaded.GroupBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Loaded {loaded.Count} target assemblies for member enumeration.");

        // ---- Aggregate sizing pass ----
        long types=0, pubTypes=0, npTypes=0, methods=0, props=0, fields=0, events=0, ctors=0, nested=0;
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var asm in loaded)
        {
            Type[] ts;
            try { ts = asm.GetTypes(); } catch (ReflectionTypeLoadException r) { ts = r.Types.Where(x => x != null).ToArray(); }
            foreach (var t in ts)
            {
                types++;
                bool isPub = (t.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public
                          || (t.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedPublic;
                if (isPub) pubTypes++; else npTypes++;
                if (t.IsNested) nested++;
                try {
                    ctors += t.GetConstructors(flags).Length;
                    methods += t.GetMethods(flags).Count(m => !m.IsSpecialName);
                    props += t.GetProperties(flags).Length;
                    fields += t.GetFields(flags).Length;
                    events += t.GetEvents(flags).Length;
                } catch { }
            }
        }
        sb.AppendLine();
        sb.AppendLine("==== AGGREGATE SIZING (all 43 targets, DeclaredOnly, public+non-public) ====");
        sb.AppendLine($"  types         : {types}  (public {pubTypes} / non-public {npTypes} / nested {nested})");
        sb.AppendLine($"  constructors  : {ctors}");
        sb.AppendLine($"  methods       : {methods}");
        sb.AppendLine($"  properties    : {props}");
        sb.AppendLine($"  fields        : {fields}");
        sb.AppendLine($"  events        : {events}");
        long memberChunks = ctors + methods + props + fields + events;
        sb.AppendLine($"  => 1 type-chunk each = {types} type chunks");
        sb.AppendLine($"  => 1 member-chunk each = {memberChunks} member chunks");
        sb.AppendLine($"  => grand total if both = {types + memberChunks} chunks");

        // ---- Sample full dumps ----
        sb.AppendLine();
        sb.AppendLine("==== SAMPLE TYPE DUMPS (item-6 verify targets) ====");
        foreach (var asm in loaded)
        {
            Type[] ts;
            try { ts = asm.GetTypes(); } catch (ReflectionTypeLoadException r) { ts = r.Types.Where(x => x != null).ToArray(); }
            foreach (var t in ts)
            {
                if (SampleTypeNames.Any(n => string.Equals(t.Name, n, StringComparison.OrdinalIgnoreCase)))
                    DumpType(t, sb);
            }
        }

        Console.WriteLine(sb.ToString());
    }
}
