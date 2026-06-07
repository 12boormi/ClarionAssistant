using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ICSharpCode.SharpDevelop.Gui;

namespace ClarionAssistant.Services
{
    /// <summary>
    /// Adds a Local (current procedure) or Global variable to the open .app by driving Clarion's OWN managed
    /// add-variable flow on the docked "Data / Tables" pad (SoftVelocity ...FileSchematicPad → FileSchemaControl
    /// → FileSchemaTree). Proven end-to-end by the ticket-30bb3125 spike: select the scope node so
    /// CurrentDetails.AddParent resolves to that scope's FieldList, then invoke EntityBrowserDetails
    /// .AddItemEventHandler — which creates a DDField in that FieldList and shows Clarion's modal FieldForm.
    /// The user fills + OKs it; Clarion persists it; our Modern Data pad picks it up on its 750ms refresh.
    ///
    /// MANAGED reflection only — no native pointers, no Win32, no TXA. MUST be called on the UI thread (the
    /// FieldForm is shown via ShowDialog, and live IDE object access requires it).
    /// </summary>
    public static class FileSchemaVariableInserter
    {
        private const BindingFlags AllInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // "FileSchema" matches both FileSchemaPad and FileSchematicPad; "Schematic" is a fallback.
        private static readonly string[] PadTypeMarkers = { "FileSchema", "Schematic" };
        private const string TreeTypeMarker = "FileSchemaTree";
        private const string LocalNodeMarker = "LocalDataLabelNode";
        private const string GlobalNodeMarker = "GlobalAppDataLabelNode";

        public sealed class Result
        {
            public bool Ok;
            public string Message;
            public static Result Fail(string m) { return new Result { Ok = false, Message = m }; }
            public static Result Done(string m) { return new Result { Ok = true, Message = m }; }
        }

        /// <param name="scope">"local" (current procedure) or "global". Validated — anything else fails closed.</param>
        /// <param name="expectedProcedure">For LOCAL scope: the procedure the caller (Modern Data pad) is
        /// currently showing. The add is REFUSED unless the native tree's Local Data node belongs to this same
        /// procedure — the docked Clarion Data/Tables tree can be showing a different procedure than our pad,
        /// and a blind add would silently land in the wrong procedure. Ignored for global scope.</param>
        public static Result AddVariable(string scope, string expectedProcedure = null)
        {
            // Fail closed on any unexpected scope — never silently coerce an unknown value to Global.
            string s = (scope ?? "").Trim().ToLowerInvariant();
            if (s != "local" && s != "global")
                return Result.Fail("Unknown variable scope '" + scope + "'.");
            bool wantLocal = s == "local";
            string scopeName = wantLocal ? "Local" : "Global";
            try
            {
                var pad = FindFileSchemaPad();
                if (pad == null)
                    return Result.Fail("Clarion's Data / Tables pad isn't available. Open an application first.");

                var tree = FindTree(pad);
                if (tree == null)
                    return Result.Fail("Couldn't locate the Data / Tables tree.");

                var node = FindScopeNode(tree, wantLocal);
                if (node == null)
                    return Result.Fail(wantLocal
                        ? "No Local Data node found — open or focus a procedure first."
                        : "No Global Data node found in the current application.");

                // FAIL CLOSED on procedure mismatch for LOCAL: the native tree's Local Data node belongs to one
                // procedure, which may differ from the one our pad is showing (native + Modern focus diverge).
                // Adding blindly would land the variable in the WRONG procedure with no warning. We also fail
                // closed when the caller can't tell us which procedure it's showing (empty expectedProcedure) —
                // without that anchor we can't prove the target, so refuse rather than risk a wrong-procedure add.
                if (wantLocal)
                {
                    if (string.IsNullOrEmpty(expectedProcedure))
                        return Result.Fail("No active procedure to add a Local variable to — open or focus a procedure first.");
                    string nodeProc = LocalNodeProcedure(node);
                    if (!string.Equals(nodeProc, expectedProcedure, StringComparison.OrdinalIgnoreCase))
                        return Result.Fail("Clarion's Data pad is showing Local Data for '" + (nodeProc ?? "?")
                            + "', not '" + expectedProcedure + "'. Open/focus that procedure in Clarion, then try again.");
                }

                // Select the scope node so the tree builds CurrentDetails / AddParent for that FieldList.
                TrySetProp(tree, "SelectedNode", node);
                TrySetProp(tree, "CurrentNode", node);
                Application.DoEvents();   // let the tree's SelectionChanged drive CurrentDetails (proven required)

                var details = GetProp(tree, "CurrentDetails");
                if (details == null)
                    return Result.Fail("Couldn't open the " + scopeName + " data editor context.");

                var addParent = GetProp(details, "AddParent");
                if (addParent == null)
                    return Result.Fail("Couldn't target the " + scopeName + " data section (AddParent did not resolve).");

                // Guard: read-only dictionary/app.
                var dd = GetProp(addParent, "DataDictionary");
                if (dd != null && (GetProp(dd, "ReadOnly") as bool?) == true)
                    return Result.Fail("The application/dictionary is read-only.");

                // Guard: confirm we targeted the requested scope (IsLocal flag on the FieldList). For LOCAL we
                // require a definite IsLocal==true (a null/indeterminate flag fails closed rather than risk a
                // wrong-scope add); for GLOBAL we only reject a definite IsLocal==true.
                var isLocal = GetProp(addParent, "IsLocal") as bool?;
                if (wantLocal)
                {
                    if (isLocal != true)
                        return Result.Fail("Couldn't confirm the target is Local data — refused to avoid a wrong-scope add.");
                }
                else if (isLocal == true)
                    return Result.Fail("Resolved Local data when Global was requested.");

                // Fire Clarion's own add flow — pops the modal FieldForm; user fills + OKs (or cancels).
                var handler = FindMethod(details, "AddItemEventHandler", new[] { typeof(object), typeof(EventArgs) });
                if (handler != null)
                {
                    handler.Invoke(details, new object[] { tree, EventArgs.Empty });
                    return Result.Done("Opened the Add Variable form for " + DescribeScope(addParent, scopeName) + ".");
                }

                // Fallback: PerformClick the managed "Add Column" menu item.
                var add = FindAddMenuItem(tree);
                if (add != null)
                {
                    add.PerformClick();
                    return Result.Done("Opened the Add Variable form for " + DescribeScope(addParent, scopeName) + ".");
                }

                return Result.Fail("Couldn't find Clarion's add-variable command (AddItemEventHandler / Add Column).");
            }
            catch (Exception ex)
            {
                return Result.Fail("Add Variable failed: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        /// <summary>
        /// Edit an existing Local/Global variable: open Clarion's FieldForm for the named field. Clarion picks
        /// the mode itself - ChangeRecord (editable) for app-declared fields (DataStorageLocation.Application=0)
        /// or ViewRecord (read-only) for dictionary/template-derived fields (Location != 0) - so we don't gate it.
        /// Proven by the ticket-0aa0ec42 probe: resolve the DDField from the scope's FieldList (the live model,
        /// NOT the virtual tree nodes) and invoke tree.ShowCurrentItem(ddField, indirect:true). indirect:true skips
        /// the passupItemChosen guard so it always opens the form rather than navigating. Mutation only - the caller
        /// (Modern Data pad) refreshes via ScheduleAddRefresh, exactly like Add. UI thread (FieldForm is modal).
        /// </summary>
        public static Result EditVariable(string scope, string name, string expectedProcedure = null)
        {
            try
            {
                object tree, ddField; Result error;
                if (!ResolveTargetField(scope, name, expectedProcedure, out tree, out ddField, out error)) return error;

                // tree.ShowCurrentItem(ddField, indirect:true): 2 params, 2nd bool, 1st accepts the DDField.
                var show = FindMethodArgs(tree, "ShowCurrentItem",
                    p => p.Length == 2 && p[1].ParameterType == typeof(bool) && p[0].ParameterType.IsInstanceOfType(ddField));
                if (show == null) return Result.Fail("Couldn't find Clarion's edit-field command.");

                show.Invoke(tree, new object[] { ddField, true });   // modal FieldForm; Clarion persists on OK
                return Result.Done("Opened " + name + " for editing.");
            }
            catch (Exception ex)
            {
                return Result.Fail("Edit Variable failed: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        /// <summary>
        /// Delete an existing Local/Global variable. Resolves the DDField from the scope's FieldList and invokes
        /// tree.GetDetails(ddField).DeleteItem() - GetDetails(DataDictionaryItem) returns an EntityBrowserDetails
        /// whose Item == the field; DeleteItem() honors CanHaveDelete, computes Item.RemoveSideEffects(...) and pops
        /// Clarion's own ConfirmDeletionForm (or a Yes/No MessageBox) before removing. We ref-check details.Item ==
        /// ddField and refuse on mismatch, so a wrong resolution can never delete the wrong field. Mutation only -
        /// the caller refreshes via ScheduleAddRefresh. UI thread (the confirm dialog is modal).
        /// </summary>
        public static Result DeleteVariable(string scope, string name, string expectedProcedure = null)
        {
            try
            {
                object tree, ddField; Result error;
                if (!ResolveTargetField(scope, name, expectedProcedure, out tree, out ddField, out error)) return error;

                // tree.GetDetails(DataDictionaryItem): the 1-arg overload that accepts the DDField (the other 1-arg
                // overload takes a TreeNodeAdv, which a DDField is NOT an instance of).
                var getDetails = FindMethodArgs(tree, "GetDetails",
                    p => p.Length == 1 && p[0].ParameterType.IsInstanceOfType(ddField));
                if (getDetails == null) return Result.Fail("Couldn't resolve the field editor context for " + name + ".");
                var details = getDetails.Invoke(tree, new object[] { ddField });
                if (details == null) return Result.Fail("Couldn't resolve the field editor context for " + name + ".");

                // HARD guard: only delete when the resolved details actually targets our field.
                if (!ReferenceEquals(GetProp(details, "Item"), ddField))
                    return Result.Fail("Internal mismatch resolving '" + name + "' - delete refused to avoid removing the wrong field.");
                if ((GetProp(details, "CanHaveDelete") as bool?) == false)
                    return Result.Fail("'" + name + "' can't be deleted.");

                var deleteItem = FindMethodArgs(details, "DeleteItem", p => p.Length == 0);
                if (deleteItem == null) return Result.Fail("Couldn't find Clarion's delete-field command.");

                deleteItem.Invoke(details, null);   // modal confirm dialog; Clarion removes on confirm
                return Result.Done("Deleted " + name + ".");
            }
            catch (Exception ex)
            {
                return Result.Fail("Delete Variable failed: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        /// <summary>
        /// Shared resolution for Edit/Delete: scope-validate, resolve pad/tree/scope node, apply the LOCAL
        /// wrong-procedure fail-closed guard (same as AddVariable), warm CurrentDetails by selecting the scope
        /// node, reject a read-only app, then resolve the named DDField from the scope's FieldList (the live model).
        /// </summary>
        private static bool ResolveTargetField(string scope, string name, string expectedProcedure,
            out object tree, out object ddField, out Result error)
        {
            tree = null; ddField = null; error = null;

            string s = (scope ?? "").Trim().ToLowerInvariant();
            if (s != "local" && s != "global") { error = Result.Fail("Unknown variable scope '" + scope + "'."); return false; }
            bool wantLocal = s == "local";
            string scopeName = wantLocal ? "Local" : "Global";
            if (string.IsNullOrWhiteSpace(name)) { error = Result.Fail("No variable name supplied."); return false; }
            name = name.Trim();

            var pad = FindFileSchemaPad();
            if (pad == null) { error = Result.Fail("Clarion's Data / Tables pad isn't available. Open an application first."); return false; }
            tree = FindTree(pad);
            if (tree == null) { error = Result.Fail("Couldn't locate the Data / Tables tree."); return false; }

            var node = FindScopeNode(tree, wantLocal);
            if (node == null) { error = Result.Fail(wantLocal
                ? "No Local Data node found - open or focus a procedure first."
                : "No Global Data node found in the current application."); return false; }

            // Same fail-closed LOCAL guard as AddVariable: the native tree's Local Data node may belong to a
            // different procedure than the one our pad is showing - editing/deleting blindly would hit the wrong
            // procedure. Refuse on mismatch, and refuse when the caller can't name the on-screen procedure.
            if (wantLocal)
            {
                if (string.IsNullOrEmpty(expectedProcedure))
                { error = Result.Fail("No active procedure for a Local variable - open or focus a procedure first."); return false; }
                string nodeProc = LocalNodeProcedure(node);
                if (!string.Equals(nodeProc, expectedProcedure, StringComparison.OrdinalIgnoreCase))
                { error = Result.Fail("Clarion's Data pad is showing Local Data for '" + (nodeProc ?? "?")
                    + "', not '" + expectedProcedure + "'. Open/focus that procedure in Clarion, then try again."); return false; }
            }

            // Warm tree.CurrentDetails by selecting the scope node (the proven Add spine).
            TrySetProp(tree, "SelectedNode", node);
            TrySetProp(tree, "CurrentNode", node);
            Application.DoEvents();

            var list = GetProp(GetProp(node, "Tag"), "List");
            if (list == null) { error = Result.Fail("Couldn't read the " + scopeName + " data fields."); return false; }

            // Guard: read-only dictionary/app.
            var dd = GetProp(list, "DataDictionary");
            if (dd != null && (GetProp(dd, "ReadOnly") as bool?) == true)
            { error = Result.Fail("The application/dictionary is read-only."); return false; }

            ddField = FindField(list, name);
            if (ddField == null)
            { error = Result.Fail("Couldn't find variable '" + name + "' in " + scopeName + " data."); return false; }
            return true;
        }

        // Find a DDField in the FieldList (recursing into GROUP/QUEUE containers) by Name/CodeName. Clarion
        // requires unique field labels within a scope, so a name match is unambiguous.
        private static object FindField(object fieldList, string name)
        {
            foreach (var f in EnumerateFields(fieldList))
            {
                if (string.Equals(GetProp(f, "Name")?.ToString(), name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(GetProp(f, "CodeName")?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    return f;
            }
            return null;
        }

        private static IEnumerable<object> EnumerateFields(object fieldListOrContainer)
        {
            var fields = GetProp(fieldListOrContainer, "Fields") as IEnumerable;
            if (fields == null) yield break;
            foreach (var f in fields)
            {
                if (f == null) continue;
                yield return f;
                if (GetProp(f, "Fields") as IEnumerable != null)   // GROUP/QUEUE container - recurse
                    foreach (var c in EnumerateFields(f))
                        yield return c;
            }
        }

        // Find a method by name whose parameter list satisfies 'match' (lets us bind to ShowCurrentItem(item,bool)
        // / GetDetails(item) without referencing the SoftVelocity parameter types at compile time).
        private static MethodInfo FindMethodArgs(object obj, string name, Func<ParameterInfo[], bool> match)
        {
            if (obj == null) return null;
            for (var t = obj.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var m in t.GetMethods(AllInstance | BindingFlags.DeclaredOnly))
                {
                    if (!string.Equals(m.Name, name, StringComparison.Ordinal)) continue;
                    if (match(m.GetParameters())) return m;
                }
            }
            return null;
        }

        private static string DescribeScope(object addParent, string scopeName)
        {
            var label = GetProp(addParent, "CodeName") ?? GetProp(addParent, "Name");
            var s = label?.ToString();
            return string.IsNullOrEmpty(s) ? scopeName + " Data" : s;
        }

        // ---- resolution (mirrors the proven spike chain) -------------------------------------------------
        private static object FindFileSchemaPad()
        {
            var workbench = WorkbenchSingleton.Workbench;
            if (workbench == null) return null;
            var pads = GetProp(workbench, "PadContentCollection") as IEnumerable;
            if (pads == null) return null;

            foreach (var entry in pads)
            {
                if (entry == null) continue;
                var descType = entry.GetType().FullName ?? "";
                var title = GetProp(entry, "Title")?.ToString() ?? "";
                var content = GetProp(entry, "PadContent");
                var contentType = content?.GetType().FullName ?? "";

                bool match = PadTypeMarkers.Any(m =>
                    descType.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    title.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    contentType.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!match) continue;

                if (content != null && GetProp(content, "Control") != null) return content;
                if (GetProp(entry, "Control") != null) return entry;
                return content ?? entry;
            }
            return null;
        }

        private static object FindTree(object pad)
        {
            var control = GetProp(pad, "Control") as Control;
            return control == null ? null : FindControlByTypeMarker(control, TreeTypeMarker);
        }

        private static Control FindControlByTypeMarker(Control root, string marker)
        {
            if (root == null) return null;
            for (var t = root.GetType(); t != null && t != typeof(object); t = t.BaseType)
                if ((t.Name ?? "").IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return root;
            foreach (Control child in root.Controls)
            {
                var hit = FindControlByTypeMarker(child, marker);
                if (hit != null) return hit;
            }
            return null;
        }

        private static object FindScopeNode(object tree, bool wantLocal)
        {
            string marker = wantLocal ? LocalNodeMarker : GlobalNodeMarker;
            var root = GetProp(tree, "Root");
            if (root == null) return null;
            var stack = new Stack<object>();
            foreach (var c in Children(root)) stack.Push(c);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (n == null) continue;
                var tag = GetProp(n, "Tag");
                if (tag != null && (tag.GetType().Name ?? "").IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return n;
                foreach (var c in Children(n)) stack.Push(c);
            }
            return null;
        }

        private static IEnumerable Children(object node)
        {
            return (GetProp(node, "Children") as IEnumerable) ?? (GetProp(node, "Nodes") as IEnumerable) ?? new object[0];
        }

        // A Local Data node's Tag carries Label = "Local Data &lt;procedure&gt;" (English; the leading text is a
        // localizable caption). Clarion procedure names are single-token identifiers with no spaces and Clarion
        // appends the name last, so we take the TRAILING whitespace-delimited token as the procedure — robust to
        // a different/localized caption rather than depending on the exact English "Local Data " prefix. The
        // caller compares this to the rendered procedure and fails closed on mismatch, so a parse miss is safe.
        private static string LocalNodeProcedure(object node)
        {
            var tag = GetProp(node, "Tag");
            var label = GetProp(tag, "Label")?.ToString();
            if (string.IsNullOrEmpty(label)) return null;
            var parts = label.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? null : parts[parts.Length - 1];
        }

        private static ToolStripItem FindAddMenuItem(object tree)
        {
            var menu = GetProp(tree, "ContextMenuStrip") as ContextMenuStrip;
            if (menu == null) return null;
            foreach (ToolStripItem it in menu.Items)
            {
                if (it == null) continue;
                if (string.Equals(it.Name, "addField", StringComparison.OrdinalIgnoreCase)
                    || (it.Text != null && it.Text.IndexOf("Column", StringComparison.OrdinalIgnoreCase) >= 0))
                    return it;
            }
            return null;
        }

        // ---- tiny reflection helpers (public + non-public, walk base types) -----------------------------
        private static object GetProp(object obj, string name)
        {
            if (obj == null) return null;
            for (var t = obj.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                var p = t.GetProperty(name, AllInstance | BindingFlags.DeclaredOnly);
                if (p != null && p.CanRead && p.GetIndexParameters().Length == 0)
                {
                    try { return p.GetValue(obj, null); } catch { return null; }
                }
            }
            return null;
        }

        private static bool TrySetProp(object obj, string name, object value)
        {
            if (obj == null) return false;
            for (var t = obj.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                var p = t.GetProperty(name, AllInstance | BindingFlags.DeclaredOnly);
                if (p != null && p.CanWrite)
                {
                    try { p.SetValue(obj, value, null); return true; } catch { return false; }
                }
            }
            return false;
        }

        private static MethodInfo FindMethod(object obj, string name, Type[] sig)
        {
            if (obj == null) return null;
            for (var t = obj.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                var m = t.GetMethod(name, AllInstance | BindingFlags.DeclaredOnly, null, sig, null);
                if (m != null) return m;
            }
            return null;
        }
    }
}
