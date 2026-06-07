using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ClarionAssistant.Tools.IdeApiExtractor
{
    /// <summary>
    /// Emits a single self-contained, offline, searchable HTML browser of the IDE API,
    /// grouped by assembly → type → members. Data is embedded as JSON; search/render is vanilla JS.
    /// </summary>
    internal static class HtmlExporter
    {
        public static void Write(string path, List<TypeDoc> types, string buildStamp)
        {
            // Group into assemblies for compact embedding.
            var asms = types
                .GroupBy(t => t.asm)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    asm = g.Key,
                    ver = g.First().asmVer,
                    types = g.OrderBy(t => t.full, StringComparer.OrdinalIgnoreCase).Select(t => new
                    {
                        f = t.full,
                        n = t.name,
                        k = t.kind,
                        v = t.vis,
                        ns = t.ns,
                        b = t.baseType,
                        i = t.ifaces,
                        d = t.body ? 1 : 0,
                        m = t.members.Select(mm => new { k = mm.k, s = mm.sig, n = mm.name }).ToList()
                    }).ToList()
                }).ToList();

            int typeCount = types.Count;
            int memberCount = types.Sum(t => t.members.Count);

            var json = JsonSerializer.Serialize(new { buildStamp, asms });
            // Prevent the embedded JSON from prematurely closing the <script> tag.
            json = json.Replace("<", "\\u003c").Replace(">", "\\u003e").Replace("&", "\\u0026");

            string html = Template
                .Replace("/*__STATS__*/", $"{asms.Count} assemblies · {typeCount:N0} types · {memberCount:N0} members · build {buildStamp}")
                .Replace("__IDE_API_DATA__", json);   // embedded as a JS object literal (valid JS; < > & are \uXXXX-escaped)

            File.WriteAllText(path, html, new UTF8Encoding(false));
        }

        // The page: sticky toolbar (search + filters + stats), then assembly sections.
        // Lazy rendering keeps the DOM small at 11k+ types.
        const string Template = @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>Clarion IDE API Browser</title>
<style>
  :root{--bg:#0f1116;--panel:#161922;--edge:#262b38;--fg:#d6dae3;--mut:#8b93a7;--acc:#5aa9ff;--hi:#ffd86b;
        --pub:#6fd28a;--np:#c98bdb;--kw:#7fb2ff;--mono:'Cascadia Code',Consolas,'SF Mono',Menlo,monospace}
  *{box-sizing:border-box}
  body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.45 -apple-system,Segoe UI,Roboto,sans-serif}
  header{position:sticky;top:0;z-index:5;background:var(--panel);border-bottom:1px solid var(--edge);padding:10px 16px}
  h1{font-size:15px;margin:0 0 8px;font-weight:600;letter-spacing:.2px}
  h1 .s{color:var(--mut);font-weight:400;font-size:12px;margin-left:8px}
  .bar{display:flex;gap:10px;align-items:center;flex-wrap:wrap}
  #q{flex:1;min-width:240px;background:var(--bg);border:1px solid var(--edge);color:var(--fg);
     padding:8px 12px;border-radius:8px;font-size:14px;outline:none}
  #q:focus{border-color:var(--acc)}
  .chips{display:flex;gap:6px;flex-wrap:wrap}
  .chip{font-size:12px;color:var(--mut);border:1px solid var(--edge);border-radius:20px;padding:3px 10px;cursor:pointer;user-select:none}
  .chip.on{color:#fff;border-color:var(--acc);background:rgba(90,169,255,.15)}
  .hint{color:var(--mut);font-size:12px}
  main{padding:8px 16px 60px;max-width:1100px;margin:0 auto}
  details.asm{border:1px solid var(--edge);border-radius:8px;margin:8px 0;background:var(--panel)}
  details.asm>summary{cursor:pointer;padding:9px 12px;font-weight:600;list-style:none;display:flex;justify-content:space-between;align-items:center}
  details.asm>summary::-webkit-details-marker{display:none}
  details.asm>summary .v{color:var(--mut);font-weight:400;font-size:12px}
  details.asm>summary .c{color:var(--acc);font-size:12px;font-weight:400}
  .tlist{padding:4px 8px 10px}
  details.ty{border-top:1px solid var(--edge)}
  details.ty>summary{cursor:pointer;padding:6px 6px;list-style:none;font-family:var(--mono);font-size:13px}
  details.ty>summary::-webkit-details-marker{display:none}
  .kind{display:inline-block;min-width:62px;color:var(--kw);font-size:11px}
  .tn{color:var(--fg)}
  .vis-public{color:var(--pub)} .vis-np{color:var(--np)}
  .badge{font-size:10px;color:#0f1116;background:var(--hi);border-radius:4px;padding:0 5px;margin-left:6px}
  .meta{color:var(--mut);font-size:11px;font-family:var(--mono);padding:2px 6px 6px 16px;white-space:pre-wrap}
  .members{font-family:var(--mono);font-size:12.5px;padding:2px 6px 8px 16px}
  .mrow{padding:1px 0;white-space:pre-wrap}
  .mk{display:inline-block;min-width:54px;color:var(--mut)}
  mark{background:var(--hi);color:#0f1116;border-radius:2px}
  .empty{color:var(--mut);padding:24px;text-align:center}
  .more{color:var(--mut);padding:10px;text-align:center;font-size:12px}
  a{color:var(--acc)}
</style>
</head>
<body>
<header>
  <h1>Clarion IDE API Browser <span class=""s"">/*__STATS__*/</span></h1>
  <div class=""bar"">
    <input id=""q"" placeholder=""Search type or member name… (e.g. FileSchemaTree, AddItem, IPadContent)"" autocomplete=""off"" spellcheck=""false"">
    <div class=""chips"" id=""kinds""></div>
    <label class=""chip"" id=""npToggle"">show non-public</label>
  </div>
  <div class=""hint"" id=""hint""></div>
</header>
<main id=""out""></main>
<script>
const DB = __IDE_API_DATA__;
const KINDS = ['class','static class','interface','struct','enum','delegate'];
const state = { q:'', kinds:new Set(KINDS), np:false };
const out = document.getElementById('out');
const hint = document.getElementById('hint');

// kind filter chips
const kc = document.getElementById('kinds');
KINDS.forEach(k=>{
  const c=document.createElement('span'); c.className='chip on'; c.textContent=k;
  c.onclick=()=>{ c.classList.toggle('on'); if(state.kinds.has(k))state.kinds.delete(k); else state.kinds.add(k); render(); };
  kc.appendChild(c);
});
const npT=document.getElementById('npToggle');
npT.onclick=()=>{ state.np=!state.np; npT.classList.toggle('on',state.np); render(); };

const q=document.getElementById('q');
let timer=null;
q.oninput=()=>{ clearTimeout(timer); timer=setTimeout(()=>{ state.q=q.value.trim().toLowerCase(); render(); },120); };

function esc(s){return (s||'').replace(/[&<>]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]));}
function hl(s,q){ if(!q) return esc(s); const i=(s||'').toLowerCase().indexOf(q); if(i<0) return esc(s);
  return esc(s.slice(0,i))+'<mark>'+esc(s.slice(i,i+q.length))+'</mark>'+esc(s.slice(i+q.length)); }
function visClass(v){ return v==='public'?'vis-public':'vis-np'; }
function typeVisible(t){ if(!state.kinds.has(t.k)) return false; if(!state.np && t.v!=='public') return false; return true; }

const MAX = 600; // cap rendered types when searching to keep the DOM snappy

function render(){
  const q=state.q;
  out.innerHTML='';
  if(q){
    // flat search across all assemblies: match type name/full or any member name
    const hits=[];
    for(const a of DB.asms){
      for(const t of a.types){
        if(!typeVisible(t)) continue;
        const tHit = t.f.toLowerCase().includes(q);
        const mHits = t.m.filter(m=>m.n.toLowerCase().includes(q));
        if(tHit || mHits.length){ hits.push({a,t,mHits,tHit}); if(hits.length>MAX+1) break; }
      }
      if(hits.length>MAX+1) break;
    }
    hint.textContent = hits.length>MAX ? `Showing first ${MAX} of many matches — refine your search.` : `${hits.length} match${hits.length===1?'':'es'}`;
    if(!hits.length){ out.innerHTML='<div class=""empty"">No matches.</div>'; return; }
    for(const h of hits.slice(0,MAX)) out.appendChild(typeCard(h.a,h.t,q,true,h.mHits));
  } else {
    hint.textContent='Click an assembly to expand. '+DB.asms.length+' assemblies.';
    for(const a of DB.asms){
      const visTypes=a.types.filter(typeVisible);
      if(!visTypes.length) continue;
      out.appendChild(asmSection(a,visTypes));
    }
  }
}

function asmSection(a,visTypes){
  const d=document.createElement('details'); d.className='asm';
  const s=document.createElement('summary');
  s.innerHTML=`<span>${esc(a.asm)} <span class=""v"">@ ${esc(a.ver)}</span></span><span class=""c"">${visTypes.length} types</span>`;
  d.appendChild(s);
  let built=false;
  d.addEventListener('toggle',()=>{ if(d.open && !built){ built=true; const w=document.createElement('div'); w.className='tlist';
    for(const t of visTypes) w.appendChild(typeCard(a,t,'',false,null)); d.appendChild(w);} });
  return d;
}

function typeCard(a,t,q,openMembers,mHits){
  const d=document.createElement('details'); d.className='ty'; if(openMembers) d.open=true;
  const s=document.createElement('summary');
  const badge = t.d? '<span class=""badge"">decompiled</span>':'';
  s.innerHTML=`<span class=""kind"">${esc(t.k)}</span> <span class=""tn ${visClass(t.v)}"">${hl(t.n,q)}</span>`+
              `<span class=""v"" style=""color:var(--mut);font-size:11px""> · ${esc(t.ns)}</span>${badge}`;
  d.appendChild(s);
  let built=false;
  const build=()=>{ if(built)return; built=true;
    const meta=document.createElement('div'); meta.className='meta';
    let mt=`${t.v} ${t.k} ${esc(t.f)}`;
    if(t.b) mt+=`\n: ${esc(t.b)}`;
    if(t.i && t.i.length) mt+=`\n  implements ${t.i.map(esc).join(', ')}`;
    meta.innerHTML=mt;
    d.appendChild(meta);
    const wrap=document.createElement('div'); wrap.className='members';
    const list = q ? t.m.filter(m=>m.n.toLowerCase().includes(q)) : t.m;
    const show = (q && list.length) ? list : t.m;
    if(!show.length){ wrap.innerHTML='<span style=""color:var(--mut)"">(no members)</span>'; }
    for(const m of show){
      const r=document.createElement('div'); r.className='mrow';
      r.innerHTML=`<span class=""mk"">${esc(m.k)}</span>${hl(m.s,q)}`;
      wrap.appendChild(r);
    }
    d.appendChild(wrap);
  };
  if(openMembers) build(); else d.addEventListener('toggle',()=>{ if(d.open) build(); });
  return d;
}

render();
</script>
</body>
</html>";
    }
}
