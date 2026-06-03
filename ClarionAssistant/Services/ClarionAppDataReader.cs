using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ClarionAssistant.Services
{
    /// <summary>
    /// Reads the global table (FILE) declarations from a generated Clarion app's PROGRAM module
    /// (&lt;app&gt;.clw). That file lists every dictionary file the app uses, with its driver, prefix,
    /// keys, and record fields — all plain text, no native/TPS access needed. Used by the Modern Data
    /// pad to show the tables/columns a procedure references.
    /// </summary>
    public static class ClarionAppDataReader
    {
        public sealed class FieldDef
        {
            public string Name;
            public string Type;
            public List<FieldDef> Children;
            // Populated only by the .txa source (ParseTxaProcedureData) — the APP's display metadata.
            public string Picture;
            public string Prompt;
            public string Header;
        }

        public sealed class TableDef
        {
            public string Name;
            public string Prefix = "";
            public readonly List<FieldDef> Fields = new List<FieldDef>();
            public readonly List<string> Keys = new List<string>();
        }

        /// <summary>Locate the generated &lt;app&gt;.clw (PROGRAM module) for the currently-open app, or null.</summary>
        public static string FindAppClwPath()
        {
            try
            {
                var info = new AppTreeService().GetAppInfo();
                if (info == null || !info.ContainsKey("fileName")) return null;
                string appFile = info["fileName"]?.ToString();
                if (string.IsNullOrEmpty(appFile)) return null;

                string dir = Path.GetDirectoryName(appFile);
                string baseName = Path.GetFileNameWithoutExtension(appFile);
                if (string.IsNullOrEmpty(baseName)) return null;
                string clwName = baseName + ".clw";

                // Primary: use the loaded .red redirection to find where generation puts the .clw
                // (this is the same resolution used to feed the CodeGraph). Try the common config sections.
                var red = RedFileService.Active;
                if (red != null)
                {
                    string viaRed = red.Resolve(clwName, "Debug", "Release", "Common")
                                 ?? red.Resolve(clwName);
                    if (!string.IsNullOrEmpty(viaRed) && File.Exists(viaRed)) return viaRed;
                }

                // Fallback: same directory as the .app.
                if (!string.IsNullOrEmpty(dir))
                {
                    string candidate = Path.Combine(dir, clwName);
                    if (File.Exists(candidate)) return candidate;
                    foreach (var f in Directory.GetFiles(dir, "*.clw"))
                        if (string.Equals(Path.GetFileNameWithoutExtension(f), baseName, StringComparison.OrdinalIgnoreCase))
                            return f;
                }

                return null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Parse FILE...RECORD...END blocks from a generated .clw. Tracks structure depth so nested
        /// GROUP/QUEUE members are flattened into the field list and the table closes on the FILE's END.
        /// </summary>
        public static List<TableDef> ParseTables(string clwPath)
        {
            var tables = new List<TableDef>();
            string[] lines;
            try { lines = File.ReadAllLines(clwPath); }
            catch { return tables; }

            TableDef cur = null;
            int depth = 0; // structure depth inside a FILE: FILE=1, RECORD/GROUP/QUEUE each +1
            foreach (var raw in lines)
            {
                string line = StripComment(raw);
                if (string.IsNullOrWhiteSpace(line)) continue;

                var m = Regex.Match(line, @"^(\s*)(\S+)\s*(.*)$");
                if (!m.Success) continue;
                string label = m.Groups[2].Value;
                string rest = m.Groups[3].Value.Trim();
                string restU = rest.ToUpperInvariant();

                if (cur == null)
                {
                    if (restU.StartsWith("FILE,") || restU == "FILE" || restU.StartsWith("FILE "))
                    {
                        cur = new TableDef { Name = label, Prefix = ExtractPre(rest) };
                        depth = 1;
                    }
                    continue;
                }

                if (restU.StartsWith("RECORD") || restU.StartsWith("GROUP") || restU.StartsWith("QUEUE"))
                {
                    depth++;
                    continue;
                }
                if (label.ToUpperInvariant() == "END" && rest.Length == 0)
                {
                    depth--;
                    if (depth <= 0) { tables.Add(cur); cur = null; depth = 0; }
                    continue;
                }
                if (restU.StartsWith("KEY(") || restU.StartsWith("INDEX("))
                {
                    cur.Keys.Add(label);
                    continue;
                }
                if (depth >= 2 && rest.Length > 0)
                {
                    cur.Fields.Add(new FieldDef { Name = label, Type = rest });
                }
            }
            return tables;
        }

        // Clarion statement/control keywords that can appear at column 1 in code but are NOT data.
        private static readonly HashSet<string> StatementKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DO","CASE","OF","OROF","IF","ELSIF","ELSE","END","LOOP","WHILE","UNTIL","EXIT","RETURN",
            "BREAK","CYCLE","BEGIN","EXECUTE","THEN","NEW","DISPOSE","ACCEPT","ASSERT","COMPILE","OMIT","SECTION"
        };

        private static readonly Regex StructOpener = new Regex(
            @"^(WINDOW|REPORT|MENUBAR|TOOLBAR|SHEET|TAB|MENU|OPTION|GROUP|QUEUE|RECORD|CLASS|VIEW|JOIN|MAP|MODULE|ITEMIZE)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // One level of the local-data structure walk. The root captures into the result list;
        // a QUEUE/GROUP pushes a capturing frame (members become Children); a leaf structure
        // (WINDOW/CLASS/…) or a MAP/MODULE pushes a skipping frame (interior ignored).
        private sealed class LocalFrame
        {
            public bool Skip;
            public List<FieldDef> Children;
            public HashSet<string> Seen;
        }

        // Structure-opening keywords, recognised both in "Label QUEUE" (named) and bare
        // "QUEUE,PRE(x)" / "MAP" (anonymous) forms. Matched against the rest-of-line OR the label.
        private static readonly Regex StructKwRx = new Regex(
            @"^(WINDOW|REPORT|MENUBAR|TOOLBAR|SHEET|TAB|MENU|OPTION|GROUP|QUEUE|RECORD|CLASS|VIEW|JOIN|MAP|MODULE|ITEMIZE)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string StructKw(string s)
        {
            var m = StructKwRx.Match(s ?? "");
            return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
        }

        private static bool IsStructKw(string labelU)
        {
            switch (labelU)
            {
                case "WINDOW": case "REPORT": case "MENUBAR": case "TOOLBAR": case "SHEET":
                case "TAB": case "MENU": case "OPTION": case "GROUP": case "QUEUE": case "RECORD":
                case "CLASS": case "VIEW": case "JOIN": case "MAP": case "MODULE": case "ITEMIZE":
                    return true;
                default: return false;
            }
        }

        /// <summary>
        /// Parse a procedure's local data declarations from its assembled embeditor source — the
        /// "Label TYPE" lines in the data section (between "&lt;Proc&gt; PROCEDURE" and CODE), in
        /// declaration order. QUEUE/GROUP structures are expanded: their members become nested
        /// Children (matching Clarion's native Data pad). Leaf structures (WINDOW/REPORT/CLASS/VIEW…)
        /// keep their own label but their interior is skipped; local MAP/MODULE blocks are skipped
        /// entirely so prototypes don't masquerade as data. Comments are stripped and names are
        /// deduped per level. This is reliable (unlike LSP documentSymbol on this buffer, which emits
        /// code-token noise).
        /// </summary>
        public static List<FieldDef> ParseLocalData(string source, string procName)
        {
            var outp = new List<FieldDef>();
            if (string.IsNullOrEmpty(source)) return outp;
            var lines = source.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            int start = 0;
            if (!string.IsNullOrEmpty(procName))
            {
                var rx = new Regex(@"^\s*" + Regex.Escape(procName) + @"\s+(PROCEDURE|FUNCTION)\b", RegexOptions.IgnoreCase);
                for (int i = 0; i < lines.Length; i++)
                    if (rx.IsMatch(lines[i])) { start = i + 1; break; }
            }

            var stack = new List<LocalFrame>
            {
                new LocalFrame { Skip = false, Children = outp, Seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) }
            };

            for (int i = start; i < lines.Length && i - start < 20000; i++)
            {
                var frame = stack[stack.Count - 1];
                string line = StripComment(lines[i]);
                if (line.Trim().Length == 0) continue;

                var m = Regex.Match(line, @"^(\s*)(\S+)\s*(.*)$");
                if (!m.Success) continue;
                string label = m.Groups[2].Value;
                string rest = m.Groups[3].Value.Trim();
                string restU = rest.ToUpperInvariant();
                string labelU = label.ToUpperInvariant();

                // Structure close — pop back to the enclosing level (root is never popped).
                if (labelU == "END" && rest.Length == 0)
                {
                    if (stack.Count > 1) stack.RemoveAt(stack.Count - 1);
                    continue;
                }

                // Only the root data section terminates the scan.
                if (stack.Count == 1 && !frame.Skip)
                {
                    if (labelU == "CODE") break;            // start of code
                    if (restU.StartsWith("ROUTINE")) break; // routines follow code — nothing past here is data
                }

                // Detect a structure opener: named ("Label QUEUE…") or anonymous ("QUEUE,…", "MAP").
                string kw = StructKw(restU);
                bool anon = false;
                if (kw == null && IsStructKw(labelU)) { kw = labelU; anon = true; }

                if (frame.Skip)
                {
                    // Inside ignored content: only track nesting so we know where it ends.
                    if (kw != null) stack.Add(new LocalFrame { Skip = true });
                    continue;
                }

                if (kw != null)
                {
                    if (kw == "QUEUE" || kw == "GROUP")
                    {
                        if (!anon && IsIdent(label) && frame.Seen.Add(label))
                        {
                            var node = new FieldDef { Name = label, Type = kw, Children = new List<FieldDef>() };
                            frame.Children.Add(node);
                            stack.Add(new LocalFrame { Skip = false, Children = node.Children, Seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) });
                        }
                        else
                        {
                            // Anonymous QUEUE/GROUP — members belong to the enclosing scope.
                            stack.Add(new LocalFrame { Skip = false, Children = frame.Children, Seen = frame.Seen });
                        }
                        continue;
                    }
                    if (kw == "MAP" || kw == "MODULE")
                    {
                        stack.Add(new LocalFrame { Skip = true }); // local prototypes are not data
                        continue;
                    }
                    // Leaf structure (WINDOW/REPORT/CLASS/VIEW/…): keep the label, skip the interior.
                    if (!anon && IsIdent(label) && frame.Seen.Add(label))
                        frame.Children.Add(new FieldDef { Name = label, Type = kw });
                    stack.Add(new LocalFrame { Skip = true });
                    continue;
                }

                if (labelU == "END") continue;                    // stray END with attrs — ignore
                if (StatementKeywords.Contains(labelU)) continue; // DO/CASE/OF/IF/LOOP/… are code, not data
                if (rest.Length > 0 && IsIdent(label) && frame.Seen.Add(label))
                    frame.Children.Add(new FieldDef { Name = label, Type = rest });
            }
            return outp;
        }

        /// <summary>
        /// Parse a procedure's Local Data from a whole-app TXA export's [DATA] section — the AUTHORITATIVE
        /// source (matches Clarion's native Data pad by construction: [DATA] lists only the procedure's
        /// registered data items, excluding embed-injected locals). Unlike the embeditor-source parse,
        /// this also yields the APP's display metadata: PICTURE / PROMPT / HEADER.
        ///
        /// Within the target [PROCEDURE] block (matched by the following NAME line), find [DATA] and walk
        /// each unit: skip the [SCREENCONTROLS]/[REPORTCONTROLS] sub-sections and their "! …" rep lines,
        /// read the "label  TYPE" declaration, then attach the metadata from the immediately-following
        /// "!!> …" line. QUEUE/GROUP opens a nested scope (members become Children) closed by an indented
        /// bare END; the GUID-only "!!>" that follows a structure's END attaches to nothing. The data
        /// region ends at the next section header that is NOT a control sub-section (e.g. [WINDOW],
        /// [CODE], [PROMPTS], or the next [PROCEDURE]).
        /// </summary>
        public static List<FieldDef> ParseTxaProcedureData(string txaText, string procName)
        {
            var outp = new List<FieldDef>();
            if (string.IsNullOrEmpty(txaText) || string.IsNullOrEmpty(procName)) return outp;
            var lines = txaText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            // 1) Locate [DATA] inside the [PROCEDURE] block whose NAME matches procName.
            int dataStart = -1;
            for (int i = 0; i < lines.Length && dataStart < 0; i++)
            {
                if (lines[i].Trim() != "[PROCEDURE]") continue;

                string name = null;
                int nameAt = -1;
                for (int j = i + 1; j < Math.Min(i + 6, lines.Length); j++)
                {
                    var mt = Regex.Match(lines[j], @"^\s*NAME\s+(.+?)\s*$");
                    if (mt.Success) { name = mt.Groups[1].Value.Trim(); nameAt = j; break; }
                }
                if (name == null || !string.Equals(name, procName, StringComparison.OrdinalIgnoreCase))
                    continue;

                for (int k = nameAt + 1; k < lines.Length; k++)
                {
                    string t = lines[k].Trim();
                    if (t == "[DATA]") { dataStart = k + 1; break; }
                    if (t == "[PROCEDURE]") break; // block has no [DATA]
                }
            }
            if (dataStart < 0) return outp;

            // 2) Walk the data region. stack[top] = the FieldDef list the current scope appends to.
            var stack = new List<List<FieldDef>> { outp };
            FieldDef lastDecl = null;

            for (int i = dataStart; i < lines.Length; i++)
            {
                string raw = lines[i];
                string trimmed = raw.Trim();
                if (trimmed.Length == 0) continue;

                // Metadata for the immediately-preceding declaration.
                if (trimmed.StartsWith("!!>"))
                {
                    if (lastDecl != null) ApplyTxaMeta(lastDecl, trimmed);
                    lastDecl = null;
                    continue;
                }
                // Screen/report control rep lines.
                if (trimmed.StartsWith("!")) continue;

                // Section headers: control sub-sections are part of [DATA]; anything else ends it.
                if (trimmed.StartsWith("["))
                {
                    if (trimmed == "[SCREENCONTROLS]" || trimmed == "[REPORTCONTROLS]") continue;
                    break;
                }

                var m = Regex.Match(raw, @"^(\s*)(\S+)\s*(.*)$");
                if (!m.Success) continue;
                string label = m.Groups[2].Value;
                string rest = m.Groups[3].Value.Trim();
                string labelU = label.ToUpperInvariant();

                // Indented bare END closes the current QUEUE/GROUP scope.
                if (labelU == "END" && rest.Length == 0)
                {
                    if (stack.Count > 1) stack.RemoveAt(stack.Count - 1);
                    lastDecl = null;
                    continue;
                }

                if (rest.Length == 0) continue; // no type → not a data declaration

                var node = new FieldDef { Name = label, Type = rest };
                stack[stack.Count - 1].Add(node);
                lastDecl = node;

                // QUEUE/GROUP opens a nested scope; its own metadata is on the following !!> line.
                string restU = rest.ToUpperInvariant();
                if (restU.StartsWith("QUEUE") || restU.StartsWith("GROUP"))
                {
                    node.Children = new List<FieldDef>();
                    stack.Add(node.Children);
                }
            }
            return outp;
        }

        // Pull PICTURE/PROMPT/HEADER off a "!!> GUID(...),PROMPT('...'),HEADER('...'),PICTURE(@...)" line.
        private static void ApplyTxaMeta(FieldDef f, string metaLine)
        {
            var pic = Regex.Match(metaLine, @"PICTURE\(([^)]*)\)", RegexOptions.IgnoreCase);
            if (pic.Success) f.Picture = pic.Groups[1].Value.Trim();

            var pr = Regex.Match(metaLine, @"PROMPT\('((?:[^']|'')*)'\)", RegexOptions.IgnoreCase);
            if (pr.Success) f.Prompt = pr.Groups[1].Value.Replace("''", "'");

            var hd = Regex.Match(metaLine, @"HEADER\('((?:[^']|'')*)'\)", RegexOptions.IgnoreCase);
            if (hd.Success) f.Header = hd.Groups[1].Value.Replace("''", "'");
        }

        /// <summary>Collect the procedure's ROUTINE names from its source (the "&lt;name&gt; ROUTINE" lines).</summary>
        public static List<string> ParseRoutines(string source, string procName)
        {
            var outp = new List<string>();
            if (string.IsNullOrEmpty(source)) return outp;
            var lines = source.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            int start = 0;
            if (!string.IsNullOrEmpty(procName))
            {
                var rx = new Regex(@"^\s*" + Regex.Escape(procName) + @"\s+(PROCEDURE|FUNCTION)\b", RegexOptions.IgnoreCase);
                for (int i = 0; i < lines.Length; i++)
                    if (rx.IsMatch(lines[i])) { start = i + 1; break; }
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = start; i < lines.Length; i++)
            {
                string label, rest;
                SplitLabelRest(StripComment(lines[i]), out label, out rest);
                if (label == null) continue;
                if (rest.ToUpperInvariant().StartsWith("ROUTINE") && IsIdent(label) && seen.Add(label))
                    outp.Add(label);
            }
            return outp;
        }

        /// <summary>
        /// Parse global variable declarations from the generated &lt;app&gt;.clw — the top-level "Label TYPE"
        /// items after the global MAP and outside FILE/structure blocks (those are shown as Tables).
        /// </summary>
        public static List<FieldDef> ParseGlobalData(string clwPath)
        {
            var outp = new List<FieldDef>();
            string[] lines;
            try { lines = File.ReadAllLines(clwPath); }
            catch { return outp; }

            int i = SkipMapBlock(lines);
            int depth = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (; i < lines.Length && outp.Count < 2000; i++)
            {
                string label, rest;
                SplitLabelRest(StripComment(lines[i]), out label, out rest);
                if (label == null) continue;
                string restU = rest.ToUpperInvariant();
                string labelU = label.ToUpperInvariant();

                if (depth > 0)
                {
                    if (restU.StartsWith("FILE") || StructOpener.IsMatch(restU)) depth++;
                    else if (labelU == "END" && rest.Length == 0) depth--;
                    continue;
                }

                if (labelU == "CODE") break;            // program code begins
                if (labelU == "END") continue;
                if (restU.StartsWith("FILE")) { depth = 1; continue; }   // a table — shown in the Tables scope
                if (StructOpener.IsMatch(restU))
                {
                    if (IsIdent(label) && seen.Add(label)) outp.Add(new FieldDef { Name = label, Type = FirstWord(rest) });
                    depth = 1;
                    continue;
                }
                if (StatementKeywords.Contains(labelU)) continue;
                if (rest.Length > 0 && IsIdent(label) && seen.Add(label))
                    outp.Add(new FieldDef { Name = label, Type = rest });
            }
            return outp;
        }

        /// <summary>Find the generated module .clw that contains a procedure (via the &lt;app&gt;.clw MAP).</summary>
        public static string FindModuleClwForProcedure(string procName)
        {
            if (string.IsNullOrWhiteSpace(procName)) return null;
            string appClw = FindAppClwPath();
            if (appClw == null) return null;
            string[] lines;
            try { lines = File.ReadAllLines(appClw); } catch { return null; }

            string dir = Path.GetDirectoryName(appClw);
            var moduleRx = new Regex(@"MODULE\(\s*'([^']+)'\s*\)", RegexOptions.IgnoreCase);
            string currentModule = null;
            foreach (var raw in lines)
            {
                string line = StripComment(raw);
                var mm = moduleRx.Match(line);
                if (mm.Success) { currentModule = mm.Groups[1].Value; continue; }
                string label, rest;
                SplitLabelRest(line, out label, out rest);
                if (label != null && currentModule != null &&
                    string.Equals(label, procName, StringComparison.OrdinalIgnoreCase))
                    return ResolveClwByName(currentModule, dir);
            }
            return null;
        }

        /// <summary>Parse module-scope data declarations from a module .clw (after the MAP, before the first PROCEDURE).</summary>
        public static List<FieldDef> ParseModuleData(string clwPath)
        {
            var outp = new List<FieldDef>();
            if (string.IsNullOrEmpty(clwPath)) return outp;
            string[] lines;
            try { lines = File.ReadAllLines(clwPath); } catch { return outp; }

            int i = SkipMapBlock(lines);
            int depth = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (; i < lines.Length && outp.Count < 2000; i++)
            {
                string label, rest;
                SplitLabelRest(StripComment(lines[i]), out label, out rest);
                if (label == null) continue;
                string restU = rest.ToUpperInvariant();
                string labelU = label.ToUpperInvariant();

                if (depth > 0)
                {
                    if (restU.StartsWith("FILE") || StructOpener.IsMatch(restU)) depth++;
                    else if (labelU == "END" && rest.Length == 0) depth--;
                    continue;
                }

                if (restU.StartsWith("PROCEDURE") || restU.StartsWith("FUNCTION")) break; // procedures begin
                if (labelU == "CODE") break;
                if (labelU == "END") continue;
                if (restU.StartsWith("FILE")) { depth = 1; continue; }
                if (StructOpener.IsMatch(restU))
                {
                    if (IsIdent(label) && seen.Add(label)) outp.Add(new FieldDef { Name = label, Type = FirstWord(rest) });
                    depth = 1;
                    continue;
                }
                if (StatementKeywords.Contains(labelU)) continue;
                if (rest.Length > 0 && IsIdent(label) && seen.Add(label))
                    outp.Add(new FieldDef { Name = label, Type = rest });
            }
            return outp;
        }

        private static string ResolveClwByName(string clwName, string fallbackDir)
        {
            if (string.IsNullOrEmpty(clwName)) return null;
            if (!clwName.EndsWith(".clw", StringComparison.OrdinalIgnoreCase)) clwName += ".clw";
            var red = RedFileService.Active;
            if (red != null)
            {
                string viaRed = red.Resolve(clwName, "Debug", "Release", "Common") ?? red.Resolve(clwName);
                if (!string.IsNullOrEmpty(viaRed) && File.Exists(viaRed)) return viaRed;
            }
            if (!string.IsNullOrEmpty(fallbackDir))
            {
                string c = Path.Combine(fallbackDir, clwName);
                if (File.Exists(c)) return c;
            }
            return null;
        }

        /// <summary>Return the line index just past the global/module MAP block (MAP … MODULE…END … END).</summary>
        private static int SkipMapBlock(string[] lines)
        {
            int i = 0;
            bool mapSeen = false;
            int mapDepth = 0;
            for (; i < lines.Length; i++)
            {
                string label, rest;
                SplitLabelRest(StripComment(lines[i]), out label, out rest);
                if (label == null) continue;
                string lu = label.ToUpperInvariant();
                if (!mapSeen)
                {
                    if (lu == "MAP") { mapSeen = true; mapDepth = 1; }
                    continue;
                }
                if (lu.StartsWith("MODULE") || lu == "MAP") mapDepth++;
                else if (lu == "END") { mapDepth--; if (mapDepth <= 0) return i + 1; }
            }
            return mapSeen ? lines.Length : 0;
        }

        private static void SplitLabelRest(string line, out string label, out string rest)
        {
            label = null; rest = "";
            var m = Regex.Match(line ?? "", @"^(\s*)(\S+)\s*(.*)$");
            if (m.Success) { label = m.Groups[2].Value; rest = m.Groups[3].Value.Trim(); }
        }

        private static bool IsIdent(string s)
        {
            return !string.IsNullOrEmpty(s) && Regex.IsMatch(s, @"^[A-Za-z_][A-Za-z0-9_]*$");
        }

        private static string FirstWord(string s)
        {
            var m = Regex.Match(s ?? "", @"^[A-Za-z]+");
            return m.Success ? m.Value : (s ?? "");
        }

        private static string ExtractPre(string fileAttrs)
        {
            var m = Regex.Match(fileAttrs, @"PRE\(\s*(\w*)\s*\)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : "";
        }

        private static string StripComment(string line)
        {
            if (line == null) return "";
            int bang = line.IndexOf('!');
            return bang >= 0 ? line.Substring(0, bang) : line;
        }
    }
}
