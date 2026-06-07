using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ClarionAssistant.Tools.IdeApiExtractor
{
    internal sealed class DocChunk
    {
        public string ClassName;     // Type.FullName (unique)
        public string MethodName;    // null for type-level
        public string Topic;         // class / interface / struct / enum / delegate / static class
        public string Heading;       // simple readable name (generics rendered)
        public string Content;       // structured type card
        public string CodeExample;   // optional decompiled body
        public string Signature;     // declaration line
        public string Anchor;        // <assembly>@<version>
    }

    /// <summary>
    /// Renders a reflected Type into one (or more, when split) type-level DocChunks per CHUNK-SCHEMA.md.
    /// </summary>
    internal static class ReflectionRenderer
    {
        const int SplitThreshold = 8 * 1024;
        static readonly BindingFlags Members =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        public static IEnumerable<DocChunk> Render(Type t)
        {
            string asmName = t.Assembly.GetName().Name;
            string asmVer = t.Assembly.GetName().Version?.ToString() ?? "?";
            string anchor = asmName + "@" + asmVer;
            try { return RenderCore(t, asmName, asmVer, anchor); }
            catch (Exception ex)
            {
                // Never drop a type: emit a header-only stub when a member/base references an
                // unresolvable assembly (e.g. Microsoft.Build.* task base classes).
                string content = $"Type: {t.FullName}\nAssembly: {asmName} @ {asmVer}   Build: Clarion {Targets.BuildStamp}\n" +
                                 $"(partial — full member enumeration unavailable: {ex.GetType().Name})";
                return new[] { new DocChunk {
                    ClassName = t.FullName, MethodName = null, Topic = "type",
                    Heading = StripTick(t.Name), Content = content, Signature = StripTick(t.Name), Anchor = anchor
                } };
            }
        }

        static IEnumerable<DocChunk> RenderCore(Type t, string asmName, string asmVer, string anchor)
        {
            string topic = Kind(t);
            string heading = Readable(t);

            string header = BuildHeader(t, asmName, asmVer);
            string signature = BuildSignature(t, topic);

            var sections = BuildMemberSections(t);  // ordered (title, lines)

            // Assemble; split member sections across continuation chunks if content grows too large.
            var chunks = new List<DocChunk>();
            int totalParts = 1; // computed after first pass if split

            // First, flatten all member lines while tracking section boundaries.
            var flat = new List<string>();
            foreach (var (title, lines) in sections)
            {
                if (lines.Count == 0) continue;
                flat.Add(""); flat.Add(title + ":");
                flat.AddRange(lines.Select(l => "  " + l));
            }

            // If everything fits in one chunk, emit single.
            string single = header + string.Join("\n", flat);
            if (single.Length <= SplitThreshold || flat.Count == 0)
            {
                chunks.Add(new DocChunk
                {
                    ClassName = t.FullName, MethodName = null, Topic = topic,
                    Heading = heading, Content = single.TrimEnd(), Signature = signature, Anchor = anchor
                });
                return chunks;
            }

            // Split: partition flat member lines into chunks under threshold, keeping header on each.
            var buckets = new List<List<string>>();
            var cur = new List<string>();
            int curLen = header.Length;
            foreach (var line in flat)
            {
                if (curLen + line.Length + 1 > SplitThreshold && cur.Count > 0)
                {
                    buckets.Add(cur); cur = new List<string>(); curLen = header.Length;
                }
                cur.Add(line); curLen += line.Length + 1;
            }
            if (cur.Count > 0) buckets.Add(cur);
            totalParts = buckets.Count;

            for (int i = 0; i < buckets.Count; i++)
            {
                string h2 = totalParts > 1 ? $"{heading} (members {i + 1}/{totalParts})" : heading;
                string content = header + string.Join("\n", buckets[i]);
                chunks.Add(new DocChunk
                {
                    // heading carries the part suffix so the UNIQUE(...heading) key stays distinct per part
                    ClassName = t.FullName, MethodName = null, Topic = topic,
                    Heading = h2, Content = content.TrimEnd(), Signature = signature, Anchor = anchor
                });
            }
            return chunks;
        }

        /// <summary>Structured per-type description for the HTML browser. Never throws.</summary>
        public static TypeDoc Describe(Type t, bool hasBody)
        {
            var d = new TypeDoc
            {
                asm = t.Assembly.GetName().Name,
                asmVer = t.Assembly.GetName().Version?.ToString() ?? "?",
                ns = t.Namespace ?? "",
                full = t.FullName,
                name = Readable(t),
                kind = Kind(t),
                vis = Vis(t.Attributes),
                body = hasBody,
            };
            try
            {
                var bt = SafeBase(t);
                if (bt != null && bt.FullName != "System.Object" && !t.IsEnum) d.baseType = bt.FullName;
                foreach (var i in SafeInterfaces(t)) d.ifaces.Add(i.FullName);

                if (t.IsEnum)
                {
                    foreach (var f in Safe(() => (IEnumerable<FieldInfo>)t.GetFields(BindingFlags.Public | BindingFlags.Static)))
                        AddMember(d, "enum", () => { object v = null; try { v = f.GetRawConstantValue(); } catch { } return $"{f.Name} = {v}"; }, f.Name);
                    return d;
                }

                foreach (var c in Safe(() => t.GetConstructors(Members)))
                    AddMember(d, "ctor", () => $"[{MethodVis(c)}] .ctor({Params(c)})", ".ctor");
                foreach (var p in Safe(() => t.GetProperties(Members)).OrderBy(p => p.Name))
                    AddMember(d, "prop", () =>
                    {
                        var acc = p.GetAccessors(true).FirstOrDefault();
                        string gs = (p.CanRead ? "get; " : "") + (p.CanWrite ? "set;" : "");
                        return $"[{(acc != null ? MethodVis(acc) : "?")}] {T(p.PropertyType)} {p.Name} {{ {gs.Trim()} }}";
                    }, p.Name);
                foreach (var f in Safe(() => t.GetFields(Members)).OrderBy(f => f.Name))
                    AddMember(d, "field", () => $"[{FieldVis(f)}]{(f.IsStatic ? " static" : "")} {T(f.FieldType)} {f.Name}", f.Name);
                foreach (var e in Safe(() => t.GetEvents(Members)).OrderBy(e => e.Name))
                    AddMember(d, "event", () => $"{T(e.EventHandlerType)} {e.Name}", e.Name);
                foreach (var m in Safe(() => t.GetMethods(Members)).Where(m => !m.IsSpecialName).OrderBy(m => m.Name))
                    AddMember(d, "method", () => $"[{MethodVis(m)}]{(m.IsStatic ? " static" : "")}{(m.IsVirtual && !m.IsFinal ? " virtual" : "")} {T(m.ReturnType)} {m.Name}({Params(m)})", m.Name);
            }
            catch { /* partial type — keep whatever we gathered */ }
            return d;
        }

        static void AddMember(TypeDoc d, string kind, System.Func<string> sig, string name)
        {
            try { d.members.Add(new MemberDoc { k = kind, sig = sig(), name = name }); } catch { }
        }

        static string BuildHeader(Type t, string asmName, string asmVer)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Type: {t.FullName}");
            sb.AppendLine($"Kind: {Kind(t)}   Visibility: {Vis(t.Attributes)}");
            sb.AppendLine($"Assembly: {asmName} @ {asmVer}   Build: Clarion {Targets.BuildStamp}");
            if (t.IsNested) sb.AppendLine($"DeclaringType: {t.DeclaringType?.FullName}");
            var bt = SafeBase(t);
            if (bt != null && bt.FullName != "System.Object")
                sb.AppendLine($"Base: {bt.FullName}");
            var ifaces = SafeInterfaces(t);
            if (ifaces.Length > 0)
                sb.AppendLine($"Implements: {string.Join(", ", ifaces.Select(i => i.FullName))}");
            return sb.ToString();
        }

        static string BuildSignature(Type t, string topic)
        {
            var sb = new StringBuilder();
            sb.Append(Vis(t.Attributes)).Append(' ');
            if (t.IsAbstract && t.IsSealed) sb.Append("static ");
            else if (t.IsAbstract && !t.IsInterface) sb.Append("abstract ");
            else if (t.IsSealed && !t.IsValueType && !t.IsEnum) sb.Append("sealed ");
            sb.Append(topic == "static class" ? "class " : topic + " ");
            sb.Append(Readable(t));
            var parts = new List<string>();
            var bt = SafeBase(t);
            if (bt != null && bt.FullName != "System.Object" && !t.IsEnum) parts.Add(bt.FullName);
            parts.AddRange(SafeInterfaces(t).Select(i => i.FullName));
            if (parts.Count > 0) sb.Append(" : ").Append(string.Join(", ", parts));
            return sb.ToString();
        }

        static List<(string title, List<string> lines)> BuildMemberSections(Type t)
        {
            var result = new List<(string, List<string>)>();

            if (t.IsEnum)
            {
                var lines = new List<string>();
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    object val = null;
                    try { val = f.GetRawConstantValue(); } catch { }
                    lines.Add($"{f.Name} = {val}");
                }
                result.Add(("Members", lines));
                return result;
            }

            var ctors = new List<string>();
            foreach (var c in Safe(() => t.GetConstructors(Members)))
                Try(ctors, () => $"[{MethodVis(c)}] ({Params(c)})");
            result.Add(("Constructors", ctors));

            var props = new List<string>();
            foreach (var p in Safe(() => t.GetProperties(Members)).OrderBy(p => p.Name))
                Try(props, () =>
                {
                    var acc = p.GetAccessors(true).FirstOrDefault();
                    string gs = (p.CanRead ? "get; " : "") + (p.CanWrite ? "set;" : "");
                    return $"[{(acc != null ? MethodVis(acc) : "?")}] {T(p.PropertyType)} {p.Name} {{ {gs.Trim()} }}";
                });
            result.Add(("Properties", props));

            var fields = new List<string>();
            foreach (var f in Safe(() => t.GetFields(Members)).OrderBy(f => f.Name))
                Try(fields, () => $"[{FieldVis(f)}]{(f.IsStatic ? " static" : "")} {T(f.FieldType)} {f.Name}");
            result.Add(("Fields", fields));

            var events = new List<string>();
            foreach (var e in Safe(() => t.GetEvents(Members)).OrderBy(e => e.Name))
                Try(events, () => $"{T(e.EventHandlerType)} {e.Name}");
            result.Add(("Events", events));

            var methods = new List<string>();
            foreach (var m in Safe(() => t.GetMethods(Members)).Where(m => !m.IsSpecialName).OrderBy(m => m.Name))
                Try(methods, () => $"[{MethodVis(m)}]{(m.IsStatic ? " static" : "")}{(m.IsVirtual && !m.IsFinal ? " virtual" : "")} {T(m.ReturnType)} {m.Name}({Params(m)})");
            result.Add(("Methods", methods));

            return result;
        }

        // ---- helpers ----
        static string Params(MethodBase m) =>
            string.Join(", ", m.GetParameters().Select(p => T(p.ParameterType) + " " + p.Name));

        static Type[] SafeInterfaces(Type t) { try { return t.GetInterfaces(); } catch { return Array.Empty<Type>(); } }
        static Type SafeBase(Type t) { try { return t.BaseType; } catch { return null; } }
        static IEnumerable<T> Safe<T>(Func<IEnumerable<T>> f) { try { return f().ToList(); } catch { return Enumerable.Empty<T>(); } }
        // Render one member line; if a referenced type can't be resolved, skip that member rather than dropping the type.
        static void Try(List<string> sink, Func<string> render) { try { sink.Add(render()); } catch { } }

        static string Kind(Type t)
        {
            if (t.IsInterface) return "interface";
            if (t.IsEnum) return "enum";
            var bt = SafeBase(t);
            if (bt != null && bt.FullName == "System.MulticastDelegate") return "delegate";
            if (t.IsValueType) return "struct";
            if (t.IsAbstract && t.IsSealed) return "static class";
            return "class";
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
                default: return "internal";
            }
        }
        static string MethodVis(MethodBase m)
        {
            if (m.IsPublic) return "public";
            if (m.IsFamily) return "protected";
            if (m.IsFamilyOrAssembly) return "protected internal";
            if (m.IsFamilyAndAssembly) return "private protected";
            if (m.IsAssembly) return "internal";
            return "private";
        }
        static string FieldVis(FieldInfo f)
        {
            if (f.IsPublic) return "public";
            if (f.IsFamily) return "protected";
            if (f.IsFamilyOrAssembly) return "protected internal";
            if (f.IsFamilyAndAssembly) return "private protected";
            if (f.IsAssembly) return "internal";
            return "private";
        }

        static string Readable(Type t)
        {
            if (!t.IsGenericType) return StripTick(t.Name);
            var args = t.GetGenericArguments().Select(Readable);
            return StripTick(t.Name) + "<" + string.Join(", ", args) + ">";
        }
        static string StripTick(string n) { int i = n.IndexOf('`'); return i >= 0 ? n.Substring(0, i) : n; }

        static string T(Type t)
        {
            if (t == null) return "void";
            if (t.IsByRef) return "ref " + T(t.GetElementType());
            if (t.IsArray) return T(t.GetElementType()) + "[]";
            if (t.IsGenericParameter) return t.Name;
            if (t.IsGenericType)
            {
                string ns = t.IsNested ? (StripTick(t.DeclaringType?.Name) + ".") : (t.Namespace != null ? t.Namespace + "." : "");
                return ns + StripTick(t.Name) + "<" + string.Join(", ", t.GetGenericArguments().Select(T)) + ">";
            }
            return t.IsNested ? (StripTick(t.DeclaringType?.Name) + "." + t.Name)
                              : (t.Namespace != null ? t.Namespace + "." + t.Name : t.Name);
        }
    }
}
