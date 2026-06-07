using System.Collections.Generic;

namespace ClarionAssistant.Tools.IdeApiExtractor
{
    /// <summary>Structured per-type record used to build the searchable HTML browser.</summary>
    internal sealed class TypeDoc
    {
        public string asm;        // assembly simple name
        public string asmVer;     // assembly version
        public string ns;         // namespace
        public string full;       // FQ name
        public string name;       // readable simple name (generics rendered)
        public string kind;       // class / interface / struct / enum / delegate / static class
        public string vis;        // visibility
        public string baseType;   // base (null if object)
        public List<string> ifaces = new List<string>();
        public List<MemberDoc> members = new List<MemberDoc>();
        public bool body;         // has a decompiled body in the DB
    }

    internal sealed class MemberDoc
    {
        public string k;          // member kind: ctor/prop/field/event/method/enum
        public string sig;        // rendered signature line
        public string name;       // member name (for search)
    }
}
