using System.Reflection;
using System.Text.Encodings.Web;

internal static class ConfigurationShell
{
    public static string Render(string title, string content, string active)
    {
        string version = typeof(ConfigurationShell).Assembly.GetName().Version?.ToString() ?? "unknown";
        string html = """
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>__TITLE__ · Configuration</title>
<style>__CSS__</style>
</head>
<body>
<header class="lcars-top">
  <div class="left-vertical"></div><div class="left-gold"></div><div class="bottom-arm"></div><div class="join-dot"></div>
  <div class="brand">
    <div class="section">Configuration</div>
    <div class="org">Food For The Poor Jamaica</div>
    <div class="datetime" id="brandDateTime"></div>
  </div>
  <div class="live-log"><div class="recent-title">Recent Activity</div><div class="recent-rows" id="liveLog">
    <div class="row"><span class="t">--:--:--</span> <span class="inf">INF</span> <span class="src">[Configuration]</span> Loading configuration authority</div>
    <div class="row"><span class="t">--:--:--</span> <span class="inf">INF</span> <span class="src">[Registration]</span> Loading lifecycle state</div>
    <div class="row"><span class="t">--:--:--</span> <span class="inf">INF</span> <span class="src">[Contracts]</span> Loading contracts</div>
  </div></div>
  <div class="top-buttons">
    <a class="blue" href="https://operations.ffpja.org">Operations</a>
    <a class="salmon" href="https://diagnostics.ffpja.org">Diagnostics</a>
    <a class="gold" href="/">Configuration</a>
    <a class="gray" href="/registration-lifecycle">Registration</a>
  </div>
</header>

<section class="workspace-shell">
  <div class="workspace-arm"><div class="workspace-title">__TITLE__</div></div>
  <div class="workspace-leg"></div>
  <nav class="workspace-nav">
    <a class="__APPS__" href="/">Applications</a>
    <a class="__CONTRACTS__" href="/contracts">Contracts</a>
    <a class="__RUNBOOKS__" href="/runbooks">Runbooks</a>
    <a class="__REGISTRATION__" href="/registration-lifecycle">Registration</a>
  </nav>
  <main class="detail-scroll">__CONTENT__</main>
</section>

<script>
const localDateTime=v=>{const d=v instanceof Date?v:new Date(v);return Number.isNaN(d.getTime())?String(v??'—'):d.toLocaleString(undefined,{dateStyle:'medium',timeStyle:'medium'});};
function recentStamp(v){const d=v?new Date(v):new Date();if(Number.isNaN(d.getTime()))return '--- -- ---- --:--:--';const mon=d.toLocaleString('en-US',{month:'short'});return mon+' '+String(d.getDate()).padStart(2,'0')+' '+d.getFullYear()+' '+String(d.getHours()).padStart(2,'0')+':'+String(d.getMinutes()).padStart(2,'0')+':'+String(d.getSeconds()).padStart(2,'0')}
function updateBrandClock(){const d=new Date(),target=document.getElementById('brandDateTime');if(!target)return;const month=d.toLocaleString('en-US',{month:'short'}).toUpperCase(),day=String(d.getDate()).padStart(2,'0'),year=d.getFullYear(),hours=String(d.getHours()).padStart(2,'0'),minutes=String(d.getMinutes()).padStart(2,'0'),tt=d.getHours()>=12?'PM':'AM';target.textContent=month+' '+day+' '+year+' '+hours+':'+minutes+' '+tt;}updateBrandClock();setInterval(updateBrandClock,1000);
document.querySelectorAll('.top-buttons a,.workspace-nav a').forEach(a=>a.addEventListener('pointerdown',()=>{a.classList.add('nav-flash');setTimeout(()=>a.classList.remove('nav-flash'),180)}));
const q=document.getElementById('search'),f=document.getElementById('statusFilter');
const filterRows=()=>document.querySelectorAll('#apps tbody tr').forEach(r=>r.style.display=(!q||r.innerText.toLowerCase().includes(q.value.toLowerCase()))&&(!f||!f.value||r.innerText.toLowerCase().includes(f.value.toLowerCase()))?'':'none');if(q)q.addEventListener('input',filterRows);if(f)f.addEventListener('change',filterRows);
document.querySelectorAll('#apps tbody tr').forEach(r=>{const a=r.querySelector('a[href^="/applications/"]');if(!a)return;r.classList.add('app-row');r.setAttribute('role','link');r.setAttribute('tabindex','0');const go=e=>{if(e&&e.target&&e.target.closest&&e.target.closest('a,button,input,select'))return;location.href=a.href;};r.addEventListener('click',go);r.addEventListener('keydown',e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();location.href=a.href;}});});
function setLog(level,source,message){const row=document.createElement('div');row.className='row';row.innerHTML='<span class="t">'+recentStamp()+'</span> <span class="'+level.toLowerCase()+'">'+level+'</span> <span class="src">['+source+']</span> '+message;liveLog.appendChild(row);while(liveLog.children.length>5)liveLog.removeChild(liveLog.firstElementChild);}
(async()=>{try{const h=await fetch('/health',{cache:'no-store'}).then(r=>r.ok?r.json():null);const summary=await fetch('/api/registration/summary',{cache:'no-store'}).then(r=>r.ok?r.json():null).catch(()=>null);const rows=[...document.querySelectorAll('#apps tbody tr')].filter(r=>r.querySelector('a[href^="/applications/"]'));const configured=rows.filter(r=>r.querySelector('.status.good')).length;const allConfigured=rows.length>0&&configured===rows.length;const healthy=String(h?.health??h?.status??'Healthy').toLowerCase()==='healthy';const attention=(summary?.attention??0)+(rows.length-configured);const ok=healthy&&attention===0;setLog(attention?'WRN':'INF','Registration',summary?((summary.registered??0)+' registered · '+(summary.attention??0)+' attention'):'Registration summary unavailable');setLog(allConfigured?'INF':'WRN','Contracts',(configured+' of '+rows.length+' configured'));}catch{setLog('ERR','Configuration','Status telemetry unavailable');}})();
document.querySelectorAll('time.local-datetime[datetime]').forEach(x=>{x.textContent=localDateTime(x.getAttribute('datetime'));});
</script>
</body>
</html>
""";
        return html
            .Replace("__TITLE__", HtmlEncoder.Default.Encode(title), StringComparison.Ordinal)
            .Replace("__CSS__", BaseCss + "\n" + GlossyCss.Value, StringComparison.Ordinal)
            .Replace("__APPS__", active == "applications" ? "active" : "", StringComparison.Ordinal)
            .Replace("__RUNBOOKS__", active == "runbooks" ? "active" : "", StringComparison.Ordinal)
            .Replace("__CONTRACTS__", active == "contracts" ? "active" : "", StringComparison.Ordinal)
            .Replace("__REGISTRATION__", active == "registration" ? "active" : "", StringComparison.Ordinal)
            .Replace("__VERSION__", HtmlEncoder.Default.Encode(version), StringComparison.Ordinal)
            .Replace("__CONTENT__", content, StringComparison.Ordinal);
    }

    private const string BaseCss = """
:root{--black:#000;--alpha-blue:#5588ee;--arctic-ice:#66ccff;--arctic-snow:#99ccff;--beta-blue:#7799dd;--night-cloud:#344470;--night-rain:#455580;--radioactive:#88eeff;--starship-white:#f3f3fc;--sunset-red:#ff3500;--peach:#99ccff;--salmon:#7799dd;--gold:#88eeff;--blue:#5588ee;--cyan:#66ccff;--green:#88eeff;--yellow:#99ccff;--red:#ff3500;--white:#f3f3fc;--muted:#99ccff;--line:#455580;--line-soft:#344470;--panel:#071020;--title-gold:#ffcc66;--rail-width:96px;--top-arm-height:14px;--workspace-arm-height:48px;--elbow-radius:18px;--scroll-arrow-space:18px;--top-right-inset:18px;--header-gap:18px}
*{box-sizing:border-box}html,body{width:100%;height:100%;margin:0;overflow:hidden;background:#000;color:var(--white);font:14px/1.35 "Arial Narrow","Roboto Condensed","Segoe UI",Arial,sans-serif}body{position:relative}a{color:inherit;text-decoration:none}button,input,select,textarea{font:inherit}
.lcars-top{position:fixed;left:8px;right:var(--top-right-inset);top:8px;height:140px;z-index:20;background:#000}.left-vertical{position:absolute;left:0;top:0;width:var(--rail-width);height:140px;background:var(--alpha-blue);border-radius:28px 0 0 28px}.left-gold,.join-dot{display:none}.bottom-arm{position:absolute;left:var(--rail-width);right:0;top:126px;height:var(--top-arm-height);background:var(--alpha-blue);z-index:1;border-radius:0 9px 9px 0}.lcars-top::after{content:"";position:absolute;left:var(--rail-width);top:calc(126px - var(--elbow-radius));width:var(--elbow-radius);height:var(--elbow-radius);background:radial-gradient(circle at 100% 0,transparent 0 calc(var(--elbow-radius) - 1px),var(--alpha-blue) var(--elbow-radius));z-index:4;pointer-events:none}
.brand{position:absolute;left:calc(var(--rail-width) + 18px);top:8px;width:320px;height:96px;z-index:3}.brand .aegis{display:none}.brand .section{font-size:36px;line-height:1;color:var(--arctic-ice);font-weight:900;text-transform:uppercase;margin-top:10px}.brand .org{font-size:10px;color:var(--arctic-snow);font-weight:800;text-transform:uppercase;margin-top:5px;max-width:220px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.brand .datetime{margin-top:8px;font-weight:900;text-transform:uppercase;color:var(--arctic-ice);letter-spacing:.04em;font-size:14px;white-space:nowrap}
.live-log{position:absolute;left:470px;top:8px;right:calc(250px + 50px + var(--header-gap));height:108px;overflow:hidden;font:13px/1.35 ui-monospace,SFMono-Regular,Consolas,monospace;padding:0 12px 4px}.live-log .recent-title{height:18px;background:var(--title-gold);color:#071018;font:900 10px/18px Arial,sans-serif;letter-spacing:.12em;text-transform:uppercase;padding:0 8px;margin:0 0 3px;border-radius:3px 3px 0 0;width:max-content;min-width:118px}.live-log .recent-rows{height:84px;overflow:hidden}.live-log .row{white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.live-log .t{display:inline-block;min-width:176px;color:var(--arctic-snow)}.live-log .inf{color:var(--green);font-weight:900}.live-log .wrn{color:var(--yellow);font-weight:900}.live-log .err{color:var(--red);font-weight:900}.live-log .src{color:var(--cyan)}
.top-buttons{position:absolute;right:50px;top:18px;width:250px;height:72px;display:grid;grid-template-columns:1fr 1fr;gap:8px 10px;padding-left:12px}.top-buttons a,.top-buttons button{border:0;border-radius:8px;font-weight:900;text-transform:uppercase;color:#071018;cursor:pointer;font-size:12px;display:flex;align-items:center;justify-content:center;line-height:1;padding:0 8px;box-shadow:inset 0 1px 0 rgba(255,255,255,.28),0 1px 0 rgba(0,0,0,.55)}.top-buttons .gold{background:var(--gold)}.top-buttons .blue{background:var(--blue)}.top-buttons .salmon{background:var(--salmon)}.top-buttons .gray{background:var(--peach)}
.workspace-shell{position:fixed;left:8px;right:14px;top:calc(148px + var(--scroll-arrow-space));bottom:var(--scroll-arrow-space);min-height:0;overflow:hidden;z-index:10}.workspace-leg{position:absolute;left:0;top:0;bottom:0;width:var(--rail-width);background:var(--alpha-blue);border-radius:28px 0 0 28px}.workspace-arm{position:absolute;left:var(--rail-width);right:0;top:0;height:var(--workspace-arm-height);background:var(--title-gold);display:flex;align-items:center;padding:0;z-index:3;border-radius:0 9px 9px 0}.workspace-arm::before{content:"";position:absolute;left:0;top:0;width:var(--elbow-radius);height:100%;background:var(--alpha-blue);z-index:0}.workspace-title{position:relative;z-index:1;height:100%;display:flex;align-items:center;font-weight:900;text-transform:uppercase;color:#071018;letter-spacing:.055em;font-size:30px;line-height:1;margin-left:var(--elbow-radius);padding:0 1ch;background:none;border-left:6px solid #000;border-right:6px solid #000}.workspace-shell::after{content:"";position:absolute;left:var(--rail-width);top:var(--workspace-arm-height);width:var(--elbow-radius);height:var(--elbow-radius);background:radial-gradient(circle at 100% 100%,transparent 0 calc(var(--elbow-radius) - 1px),var(--alpha-blue) var(--elbow-radius));z-index:5;pointer-events:none}
.workspace-nav{position:absolute;left:0;top:calc(var(--workspace-arm-height) + var(--scroll-arrow-space));width:var(--rail-width);z-index:2}.workspace-nav a{display:flex;align-items:center;height:48px;padding-left:11px;color:#071018;font-weight:900;text-transform:uppercase;font-size:12px;border-bottom:3px solid #000;transition:background .12s ease,color .12s ease,filter .12s ease}.workspace-nav a.active{font-size:13px}.top-buttons a{transition:background .12s ease,color .12s ease,filter .12s ease}.top-buttons a.nav-flash,.workspace-nav a.nav-flash{background:var(--title-gold)!important;color:#071018!important;filter:brightness(1.08)}
.detail-scroll{position:absolute;left:var(--rail-width);right:0;top:calc(var(--workspace-arm-height) + var(--scroll-arrow-space));bottom:var(--scroll-arrow-space);min-height:0;overflow-y:auto;overflow-x:hidden;overscroll-behavior:contain;background:#000;padding:18px 22px 24px 24px;scrollbar-color:var(--salmon) #111}.detail-scroll::-webkit-scrollbar{width:10px}.detail-scroll::-webkit-scrollbar-track{background:#090909}.detail-scroll::-webkit-scrollbar-thumb{background:var(--salmon);border-radius:10px}
.page-head{display:flex;justify-content:space-between;gap:20px;align-items:flex-start;margin-bottom:16px}.page-head h1{font-size:31px;margin:0;color:var(--blue)}.page-head p{margin:4px 0 0;color:var(--starship-white)}.toolbar{display:flex;gap:10px;margin:10px 0}.toolbar input{flex:1}.toolbar input,.toolbar select,.toolbar button,.primary,dialog button{background:#071722;color:white;border:1px solid var(--line);border-radius:8px;padding:11px}.primary{background:var(--alpha-blue)!important;color:#071018!important;font-weight:800}
.metrics{display:grid;grid-template-columns:repeat(5,1fr);gap:13px;margin:14px 0}.metric{min-height:82px;border:1px solid var(--line-soft);border-top:2px solid var(--line);border-radius:10px;display:grid;grid-template-columns:55px 1fr;grid-template-rows:1fr 1fr;align-items:center;padding:8px;background:linear-gradient(180deg,rgba(12,34,52,.68),rgba(2,10,16,.78))}.metric span{grid-row:1/3;width:42px;height:42px;border-radius:50%;display:grid;place-items:center;font-size:24px;background:var(--alpha-blue)}.metric b{font-size:20px}.metric small{color:var(--muted)}
.workspace{display:grid;grid-template-columns:minmax(680px,1.45fr) minmax(360px,.75fr);gap:14px}.panel,.detail{border:1px solid var(--line-soft);border-top:2px solid var(--line);border-radius:10px;background:linear-gradient(180deg,rgba(8,27,42,.5),rgba(1,7,12,.4));overflow:hidden}.table-panel{overflow:auto}table{width:100%;border-collapse:collapse}th{text-align:left;color:var(--arctic-ice);background:#071b29;padding:10px;font-size:11px}td{padding:11px 10px;border-bottom:1px solid var(--line-soft);white-space:nowrap}.status{display:inline-block;border:1px solid;border-radius:8px;padding:5px 8px;font-weight:700}.status.good{color:var(--radioactive);border-color:var(--radioactive)}.status.warn{color:var(--title-gold);border-color:var(--title-gold)}
.detail{padding:16px}.detail-head{display:flex;gap:12px;align-items:center;border-bottom:1px solid var(--line-soft);padding-bottom:13px}.bigicon{width:58px;height:58px;border-radius:9px;background:var(--alpha-blue);display:grid;place-items:center;font-size:30px}.facts{display:grid;grid-template-columns:repeat(2,1fr);margin:10px 0}.facts div{padding:8px;border-right:1px solid var(--line-soft)}.facts small,.facts b{display:block}.facts small{color:var(--muted);font-size:10px}.tabs{display:flex;gap:18px;border-bottom:1px solid var(--line-soft);padding:10px 0}.tabs b{color:var(--arctic-ice);border-bottom:2px solid var(--alpha-blue);padding-bottom:8px}.req{display:grid;grid-template-columns:1fr auto 28px;gap:8px;padding:9px;border-bottom:1px solid var(--line-soft)}.req small{display:block;color:var(--muted)}.req code{color:var(--arctic-ice)}.req i{width:22px;height:22px;border-radius:50%;display:grid;place-items:center;font-style:normal}.req i.ok{background:var(--night-rain)}.req i.bad{background:var(--sunset-red)}.lower{display:grid;grid-template-columns:1.2fr .8fr;gap:14px;margin-top:14px}.lower .panel{padding:12px}.activity-row{display:grid;grid-template-columns:16px 1fr auto;gap:8px;padding:7px;border-bottom:1px solid var(--line-soft)}.quick{display:grid;grid-template-columns:1fr 1fr;gap:8px}.quick a,.actions a{border:1px solid var(--line);border-radius:8px;padding:9px}.empty,.muted{color:var(--muted)}.empty{padding:20px;text-align:center}dialog{background:var(--panel);color:white;border:1px solid var(--line);border-radius:10px;max-width:520px;padding:22px}dialog::backdrop{background:#000a}
@media(max-width:1250px){.brand{width:280px}.live-log{left:410px;right:calc(225px + 50px + 14px)}.top-buttons{right:50px;width:225px}.workspace{grid-template-columns:1fr}}@media(max-width:900px){.metrics{grid-template-columns:repeat(2,1fr)}}
""";
}
