using System;
using System.Collections.Generic;

namespace ClarionAssistant.Services
{
    /// <summary>
    /// Path B — Modern Embeditor: dev-controllable editor settings. A small typed model persisted via
    /// SettingsService (%APPDATA%\ClarionAssistant\settings.txt, keys "ModernEmbeditor.*") and shipped to
    /// Monaco as a JSON dict. These map to Monaco editor/model options:
    ///   TabSize/InsertSpaces -> model.updateOptions; FontSize/WordWrap/Minimap/AutoIndent -> editor.updateOptions.
    /// AutoIndent toggles Monaco's autoIndent between 'full' (uses the Clarion indentationRules in the HTML —
    /// indent after IF/LOOP/CASE/structures, outdent on END/'.') and 'keep' (just match the previous line).
    /// Format-on-demand ("Format Document") is intentionally NOT here — it needs a real Clarion formatter.
    /// </summary>
    public sealed class ModernEmbeditorSettings
    {
        public int TabSize = 2;
        public bool InsertSpaces = true;
        public bool AutoIndent = true;
        public bool WordWrap = false;
        public bool Minimap = true;
        public int FontSize = 13;

        private const string Prefix = "ModernEmbeditor.";

        /// <summary>Load from SettingsService, falling back to defaults for any missing/invalid key.</summary>
        public static ModernEmbeditorSettings Load()
        {
            var s = new ModernEmbeditorSettings();
            try
            {
                var sv = new SettingsService();
                s.TabSize = GetInt(sv, "TabSize", s.TabSize, 1, 16);
                s.InsertSpaces = GetBool(sv, "InsertSpaces", s.InsertSpaces);
                s.AutoIndent = GetBool(sv, "AutoIndent", s.AutoIndent);
                s.WordWrap = GetBool(sv, "WordWrap", s.WordWrap);
                s.Minimap = GetBool(sv, "Minimap", s.Minimap);
                s.FontSize = GetInt(sv, "FontSize", s.FontSize, 6, 48);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[ModernEmbeditorSettings] Load: " + ex.Message); }
            return s;
        }

        /// <summary>Persist all values. May throw SettingsLockedException (cross-process contention) — the caller surfaces it.</summary>
        public void Save()
        {
            var sv = new SettingsService();
            sv.Set(Prefix + "TabSize", Clamp(TabSize, 1, 16).ToString());
            sv.Set(Prefix + "InsertSpaces", InsertSpaces ? "true" : "false");
            sv.Set(Prefix + "AutoIndent", AutoIndent ? "true" : "false");
            sv.Set(Prefix + "WordWrap", WordWrap ? "true" : "false");
            sv.Set(Prefix + "Minimap", Minimap ? "true" : "false");
            sv.Set(Prefix + "FontSize", Clamp(FontSize, 6, 48).ToString());
        }

        /// <summary>Build a settings instance from the JS payload dict (validated + clamped).</summary>
        public static ModernEmbeditorSettings FromDict(IDictionary<string, object> d)
        {
            var s = new ModernEmbeditorSettings();
            if (d == null) return s;
            s.TabSize = Clamp(ToInt(d, "tabSize", s.TabSize), 1, 16);
            s.InsertSpaces = ToBool(d, "insertSpaces", s.InsertSpaces);
            s.AutoIndent = ToBool(d, "autoIndent", s.AutoIndent);
            s.WordWrap = ToBool(d, "wordWrap", s.WordWrap);
            s.Minimap = ToBool(d, "minimap", s.Minimap);
            s.FontSize = Clamp(ToInt(d, "fontSize", s.FontSize), 6, 48);
            return s;
        }

        /// <summary>Serialize for the JS bridge (keys match the HTML's applyEditorSettings).</summary>
        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                { "tabSize", TabSize },
                { "insertSpaces", InsertSpaces },
                { "autoIndent", AutoIndent },
                { "wordWrap", WordWrap },
                { "minimap", Minimap },
                { "fontSize", FontSize }
            };
        }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        private static int GetInt(SettingsService sv, string key, int dflt, int lo, int hi)
        {
            int v;
            string raw = sv.Get(Prefix + key);
            return (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out v)) ? Clamp(v, lo, hi) : dflt;
        }

        private static bool GetBool(SettingsService sv, string key, bool dflt)
        {
            string raw = sv.Get(Prefix + key);
            if (string.IsNullOrEmpty(raw)) return dflt;
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static int ToInt(IDictionary<string, object> d, string key, int dflt)
        {
            object o;
            if (d.TryGetValue(key, out o) && o != null)
            {
                try { return Convert.ToInt32(o); } catch { }
            }
            return dflt;
        }

        private static bool ToBool(IDictionary<string, object> d, string key, bool dflt)
        {
            object o;
            if (d.TryGetValue(key, out o) && o != null)
            {
                try { return Convert.ToBoolean(o); } catch { }
            }
            return dflt;
        }
    }
}
