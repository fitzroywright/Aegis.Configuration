using System.Reflection;
using System.Text.Encodings.Web;

internal static class ConfigurationShell
{
    public static string Render(string title, string content, string active)
    {
        string version = typeof(ConfigurationShell).Assembly.GetName().Version?.ToString() ?? "unknown";
        string navTitle = active switch
        {
            "overview" => "Overview",
            "applications" => "Applications",
            "contracts" => "Contracts",
            "runbooks" => "Runbooks",
            "logs" => "Logs",
            _ => title
        };
        string html = """
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>__TITLE__ · Configuration</title>
<style>__CSS__ .sound-toggle{margin-left:10px;margin-right:14px;border:0;border-left:3px solid #000;background:#071018;color:var(--title-gold);width:34px;height:26px;padding:4px 7px;cursor:pointer;z-index:6;display:grid;place-items:center}.sound-toggle svg{width:18px;height:18px;fill:currentColor}.sound-toggle:hover{background:#111d2a;color:var(--starship-white)}.sound-toggle:hover{background:#111d2a;color:var(--starship-white)}
</style>
</head>
<body>
<header class="lcars-top">
  <div class="left-vertical"></div><div class="left-gold"></div><div class="bottom-arm"></div><div class="join-dot"></div>
  <div class="brand">
    <div class="section">Configuration</div>
    <div class="org">Food For The Poor Jamaica</div>
    <div class="datetime" id="brandDateTime"></div><div class="brand-user" id="brandUser">SIGNED IN</div>
  </div>
  <div class="live-log"><div class="recent-title">Recent Logs</div><div class="recent-rows" id="liveLog">
    <div class="row"><span class="t">--:--:--</span> <span class="inf">INF</span> <span class="src">[Configuration]</span> Loading configuration authority</div>
    <div class="row"><span class="t">--:--:--</span> <span class="inf">INF</span> <span class="src">[Registration]</span> Loading lifecycle state</div>
    <div class="row"><span class="t">--:--:--</span> <span class="inf">INF</span> <span class="src">[Contracts]</span> Loading contracts</div>
  </div></div>
  <div class="top-buttons">
    <a class="blue" href="https://operations.ffpja.org">Operations</a>
    <a class="salmon" href="https://diagnostics.ffpja.org">Diagnostics</a>
    <a class="gold" href="/">Configuration</a>
    <a class="logout" href="/auth/logout">Logout</a>
  </div>
</header>

<section class="workspace-shell">
  <div class="workspace-arm"><div class="workspace-title">__NAV_TITLE__</div><div class="last-scan" id="lastScan">Last Scan: SCANNING</div></div>
  <div class="workspace-leg"></div>
  <nav class="workspace-nav">
    <a class="__OVERVIEW__" href="/">Overview</a>
    <a class="__APPS__" href="/applications">Applications</a>
    <a class="__CONTRACTS__" href="/contracts">Contracts</a>
    <a class="__RUNBOOKS__" href="/runbooks">Runbooks</a>
    <a class="__LOGS__" href="/logs">Logs</a>
  </nav>
  <button class="lcars-back" type="button" onclick="if(history.length>1){history.back()}else{location.href='/'}">Back</button>
  <main class="detail-scroll">__CONTENT__</main>
</section>

<script>
const localDateTime=v=>{const d=v instanceof Date?v:new Date(v);return Number.isNaN(d.getTime())?String(v??'—'):d.toLocaleString(undefined,{dateStyle:'medium',timeStyle:'medium'});};
function recentStamp(v){const d=v?new Date(v):new Date();if(Number.isNaN(d.getTime()))return '--- -- ---- --:--:--';const mon=d.toLocaleString('en-US',{month:'short'});return mon+' '+String(d.getDate()).padStart(2,'0')+' '+d.getFullYear()+' '+String(d.getHours()).padStart(2,'0')+':'+String(d.getMinutes()).padStart(2,'0')+':'+String(d.getSeconds()).padStart(2,'0')}
function seedRecentTimestamps(){document.querySelectorAll('#liveLog .t').forEach(x=>{if(!x.textContent||x.textContent.includes('--:--:--'))x.textContent=recentStamp();});}
seedRecentTimestamps();
async function loadBrandUser(){try{const r=await fetch('/auth/me',{cache:'no-store'});if(!r.ok)return;const x=await r.json();const e=document.getElementById('brandUser');if(e)e.textContent='User: '+(x.displayName||x.userName||'Signed in')+' · IP: '+(x.clientIp||'unknown');}catch{}}
loadBrandUser();
function updateBrandClock(){const d=new Date(),target=document.getElementById('brandDateTime');if(!target)return;const month=d.toLocaleString('en-US',{month:'short'}).toUpperCase(),day=String(d.getDate()).padStart(2,'0'),year=d.getFullYear(),hours=String(d.getHours()).padStart(2,'0'),minutes=String(d.getMinutes()).padStart(2,'0'),tt=d.getHours()>=12?'PM':'AM';target.textContent=month+' '+day+' '+year+' '+hours+':'+minutes+' '+tt;}updateBrandClock();setInterval(updateBrandClock,1000);
document.querySelectorAll('.top-buttons a,.workspace-nav a').forEach(a=>a.addEventListener('pointerdown',()=>{a.classList.add('nav-flash');setTimeout(()=>a.classList.remove('nav-flash'),180)}));
async function refreshRegisteredApplicationHealth(){const host=document.querySelector('.detail-scroll');if(!host)return;let banner=document.getElementById('registeredApplicationHealthBanner');if(!banner){banner=document.createElement('section');banner.id='registeredApplicationHealthBanner';banner.className='control-plane-health-banner';const own=document.getElementById('controlPlaneHealthBanner');own?.insertAdjacentElement('afterend',banner)??host.prepend(banner);}try{const r=await fetch('/api/registration/flow?limit=500',{cache:'no-store'});if(!r.ok)throw new Error('HTTP '+r.status);const x=await r.json(),journeys=x.journeys||x.records||[];const issues=journeys.filter(a=>String(a.state||'').toLowerCase()==='registered'&&a.health&&String(a.health).toLowerCase()!=='healthy');if(!issues.length){banner.style.display='none';return;}banner.classList.toggle('critical',issues.some(a=>['unhealthy','failed','critical'].includes(String(a.health||'').toLowerCase())));banner.innerHTML='<strong>REGISTERED APPLICATION HEALTH · '+issues.length+' ISSUE'+(issues.length===1?'':'S')+'</strong><div>'+issues.slice(0,8).map(a=>String(a.applicationId||a.displayName||'Application').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#039;'}[c]))+' — '+String(a.health||'Unknown').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#039;'}[c]))+(a.reason?' · '+String(a.reason).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#039;'}[c])):'' )).join('<br>')+'</div>';banner.style.display='block';}catch{banner.style.display='none';}}
refreshRegisteredApplicationHealth();setInterval(refreshRegisteredApplicationHealth,30000);
async function refreshControlPlaneHealthBanner(){const host=document.querySelector('.detail-scroll');if(!host)return;let banner=document.getElementById('controlPlaneHealthBanner');if(!banner){banner=document.createElement('section');banner.id='controlPlaneHealthBanner';banner.className='control-plane-health-banner';host.prepend(banner);}try{const response=await fetch('/health',{cache:'no-store'});if(!response.ok)throw new Error('HTTP '+response.status);const h=await response.json(),state=String(h.health||h.status||'Unknown'),summary=String(h.summary||'Health check returned no summary.').trim(),lower=state.toLowerCase();if(lower==='healthy'){banner.style.display='none';banner.classList.remove('critical','health-unavailable');return;}banner.classList.toggle('critical',lower==='unhealthy');banner.classList.remove('health-unavailable');banner.innerHTML='<strong>'+state.toUpperCase()+'</strong><div>'+String(summary).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#039;'}[c]))+'</div>';banner.style.display='block';}catch(error){banner.classList.remove('critical');banner.classList.add('health-unavailable');banner.innerHTML='<strong>HEALTH CHECK UNAVAILABLE</strong><div>'+String(error?.message||'Unable to read /health.')+'</div>';banner.style.display='block';}}
refreshControlPlaneHealthBanner();setInterval(refreshControlPlaneHealthBanner,30000);
const q=document.getElementById('search'),f=document.getElementById('statusFilter');const query=new URLSearchParams(location.search);if(f&&query.get('status'))f.value=query.get('status');
const filterRows=()=>document.querySelectorAll('#apps tbody tr').forEach(r=>r.style.display=(!q||r.innerText.toLowerCase().includes(q.value.toLowerCase()))&&(!f||!f.value||r.innerText.toLowerCase().includes(f.value.toLowerCase()))?'':'none');if(q)q.addEventListener('input',filterRows);if(f)f.addEventListener('change',filterRows);if(q||f)filterRows();const lifecycleState=query.get('state');if(lifecycleState){document.querySelectorAll('.table-panel tbody tr').forEach(r=>{const state=(r.querySelector('.status')?.textContent||'').trim();const attention=['IdentityConflict','Revoked','Rejected'];const recovery=['RecoveryRequested','RecoveryApproved'];const pending=['Pending','ChallengeIssued'];const show=lifecycleState==='Attention'?attention.includes(state):lifecycleState==='Recovery'?recovery.includes(state):lifecycleState==='Pending'?pending.includes(state):state===lifecycleState;r.style.display=show?'':'none';});}
document.querySelectorAll('#apps tbody tr').forEach(r=>{const a=r.querySelector('a[href^="/applications/"]');if(!a)return;r.classList.add('app-row');r.setAttribute('role','link');r.setAttribute('tabindex','0');const go=e=>{if(e&&e.target&&e.target.closest&&e.target.closest('a,button,input,select'))return;location.href=a.href;};r.addEventListener('click',go);r.addEventListener('keydown',e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();location.href=a.href;}});});
function setLog(level,source,message){const row=document.createElement('div');row.className='row';row.innerHTML='<span class="t">'+recentStamp()+'</span> <span class="'+level.toLowerCase()+'">'+level+'</span> <span class="src">['+source+']</span> '+message;liveLog.appendChild(row);while(liveLog.children.length>5)liveLog.removeChild(liveLog.firstElementChild);}
(async()=>{try{const h=await fetch('/health',{cache:'no-store'}).then(r=>r.ok?r.json():null);if(document.getElementById('lastScan'))document.getElementById('lastScan').textContent='Last Scan: '+localDateTime(h?.utc||new Date());const summary=await fetch('/api/registration/summary',{cache:'no-store'}).then(r=>r.ok?r.json():null).catch(()=>null);const rows=[...document.querySelectorAll('#apps tbody tr')].filter(r=>r.querySelector('a[href^="/applications/"]'));const configured=rows.filter(r=>r.querySelector('.status.good')).length;const allConfigured=rows.length>0&&configured===rows.length;const healthy=String(h?.health??h?.status??'Healthy').toLowerCase()==='healthy';const attention=(summary?.attention??0)+(rows.length-configured);const ok=healthy&&attention===0;setLog(attention?'WRN':'INF','Registration',summary?((summary.registered??0)+' registered · '+(summary.attention??0)+' attention'):'Registration summary unavailable');setLog(allConfigured?'INF':'WRN','Contracts',(configured+' of '+rows.length+' configured'));}catch{setLog('ERR','Configuration','Status telemetry unavailable');}})();
document.querySelectorAll('time.local-datetime[datetime]').forEach(x=>{x.textContent=localDateTime(x.getAttribute('datetime'));});
document.addEventListener('click',async e=>{
 const b=e.target.closest('[data-registration-action]');if(!b)return;
 e.preventDefault();
 const action=b.dataset.registrationAction,id=b.dataset.registrationId;
 const destructive=['reject','revoke','purge','conflict/replace-with-incoming'].includes(action);
 let reason='';
 if(destructive){const entered=prompt('Reason for this registration action:','');if(entered===null)return;reason=entered;if(!confirm('Apply '+action.replaceAll('/',' ')+' to this registration?'))return;}
 else if(!confirm('Apply '+action.replaceAll('/',' ')+' to this registration?'))return;
 b.disabled=true;
 try{
   const method=action==='purge'?'DELETE':'POST';
   const url=action==='purge'?'/api/registration/'+encodeURIComponent(id):'/api/registration/'+encodeURIComponent(id)+'/'+action;
   const res=await fetch(url,{method,headers:{'Content-Type':'application/json'},body:JSON.stringify({reason})});
   if(!res.ok){let detail='HTTP '+res.status;const t=await res.text();if(t){try{const p=JSON.parse(t);detail=p.detail||p.error||p.title||t;}catch{detail=t;}}throw new Error(detail);}
   location.reload();
 }catch(err){alert('Registration action failed: '+err.message);b.disabled=false;}
});

function installAegisConsoleAudio(){
  const key='aegis.console.sound',enabled=()=>localStorage.getItem(key)!=='off';
  let ctx=null;
  function tone(freq,duration,volume=.045,delay=0){
    if(!enabled())return;
    try{
      ctx=ctx||new (window.AudioContext||window.webkitAudioContext)();
      if(ctx.state==='suspended')ctx.resume();
      const t=ctx.currentTime+delay,o=ctx.createOscillator(),g=ctx.createGain();
      o.type='sine';o.frequency.setValueAtTime(freq,t);
      g.gain.setValueAtTime(0,t);g.gain.linearRampToValueAtTime(volume,t+.004);g.gain.exponentialRampToValueAtTime(.0001,t+duration);
      o.connect(g);g.connect(ctx.destination);o.start(t);o.stop(t+duration+.02);
    }catch{}
  }
  const cues={
    navigation:()=>{tone(1174.7,.025,.032);tone(1568,.032,.028,.032)},
    success:()=>{tone(659.3,.035,.035);tone(987.8,.04,.038,.047);tone(1318.5,.043,.032,.097)},
    warning:()=>{tone(587.3,.06,.045);tone(493.9,.072,.045,.095)},
    critical:()=>{tone(392,.08,.055);tone(329.6,.085,.055,.11);tone(261.6,.115,.06,.225)},
    complete:()=>{tone(784,.045,.032);tone(932.3,.045,.032,.05);tone(1174.7,.035,.03,.105)}
  };
  window.aegisAudio={play:name=>cues[name]?.(),enabled};
  const arm=document.querySelector('.workspace-arm');
  if(arm&&!document.getElementById('soundToggle')){
    const b=document.createElement('button');b.id='soundToggle';b.type='button';b.className='sound-toggle';
    const paint=()=>{b.innerHTML=enabled()?'<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 9v6h4l5 4V5L8 9H4zm11.5 3a3.5 3.5 0 0 0-1.5-2.87v5.74A3.5 3.5 0 0 0 15.5 12zm0-7.1v2.06a7 7 0 0 1 0 10.08v2.06a9 9 0 0 0 0-14.2z"/></svg>':'<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 9v6h4l5 4V5L8 9H4zm11.2.8 1.4 1.4 1.4-1.4 1.4 1.4-1.4 1.4 1.4 1.4-1.4 1.4-1.4-1.4-1.4 1.4-1.4-1.4 1.4-1.4-1.4-1.4 1.4-1.4z"/></svg>';b.title=enabled()?'Sound on':'Sound off';b.setAttribute('aria-label',b.title);b.setAttribute('aria-pressed',String(enabled()))};paint();
    b.onclick=()=>{localStorage.setItem(key,enabled()?'off':'on');paint();if(enabled())cues.success()};
    arm.appendChild(b);
  }
  document.querySelectorAll('.top-buttons a,.workspace-nav a,.lcars-back').forEach(x=>x.addEventListener('pointerdown',()=>cues.navigation()));
}
installAegisConsoleAudio();
</script>
</body>
</html>
""";
        return html
            .Replace("__TITLE__", HtmlEncoder.Default.Encode(title), StringComparison.Ordinal)
            .Replace("__NAV_TITLE__", HtmlEncoder.Default.Encode(navTitle), StringComparison.Ordinal)
            .Replace("__CSS__", BaseCss + "\n" + GlossyCss.Value, StringComparison.Ordinal)
            .Replace("__OVERVIEW__", active == "overview" ? "active" : "", StringComparison.Ordinal)
            .Replace("__APPS__", active == "applications" ? "active" : "", StringComparison.Ordinal)
            .Replace("__CONTRACTS__", active == "contracts" ? "active" : "", StringComparison.Ordinal)
            .Replace("__RUNBOOKS__", active == "runbooks" ? "active" : "", StringComparison.Ordinal)
            .Replace("__LOGS__", active == "logs" ? "active" : "", StringComparison.Ordinal)
            .Replace("__VERSION__", HtmlEncoder.Default.Encode(version), StringComparison.Ordinal)
            .Replace("__CONTENT__", content, StringComparison.Ordinal);
    }

    private const string BaseCss = """
:root{--black:#000;--alpha-blue:#5588ee;--arctic-ice:#66ccff;--arctic-snow:#99ccff;--beta-blue:#7799dd;--night-cloud:#344470;--night-rain:#455580;--radioactive:#88eeff;--starship-white:#f3f3fc;--sunset-red:#ff3500;--peach:#99ccff;--salmon:#7799dd;--gold:#88eeff;--blue:#5588ee;--cyan:#66ccff;--green:#88eeff;--yellow:#99ccff;--red:#ff3500;--white:#f3f3fc;--muted:#99ccff;--line:#455580;--line-soft:#344470;--panel:#071020;--title-gold:#ffcc66;--rail-width:160px;--top-arm-height:14px;--workspace-arm-height:48px;--elbow-radius:18px;--scroll-arrow-space:18px;--top-right-inset:18px;--header-gap:18px}
*{box-sizing:border-box}html,body{width:100%;height:100%;margin:0;overflow:hidden;background:#000;color:var(--white);font:14px/1.35 "Arial Narrow","Roboto Condensed","Segoe UI",Arial,sans-serif}body{position:relative}a{color:inherit;text-decoration:none}button,input,select,textarea{font:inherit}
.lcars-top{position:fixed;left:8px;right:var(--top-right-inset);top:8px;height:140px;z-index:20;background:#000}.left-vertical{position:absolute;left:0;top:0;width:var(--rail-width);height:140px;background:var(--alpha-blue);border-radius:28px 0 0 28px}.left-gold,.join-dot{display:none}.bottom-arm{position:absolute;left:var(--rail-width);right:0;top:126px;height:var(--top-arm-height);background:var(--alpha-blue);z-index:1;border-radius:0 9px 9px 0}.lcars-top::after{content:"";position:absolute;left:var(--rail-width);top:calc(126px - var(--elbow-radius));width:var(--elbow-radius);height:var(--elbow-radius);background:radial-gradient(circle at 100% 0,transparent 0 calc(var(--elbow-radius) - 1px),var(--alpha-blue) var(--elbow-radius));z-index:4;pointer-events:none}
.brand{position:absolute;left:calc(var(--rail-width) + 18px);top:8px;width:270px;height:96px;z-index:3}.brand .aegis{display:none}.brand .section{font-size:30px;white-space:nowrap;line-height:1;color:var(--arctic-ice);font-weight:900;text-transform:uppercase;margin-top:10px}.brand .org{font-size:10px;color:var(--arctic-snow);font-weight:800;text-transform:uppercase;margin-top:5px;max-width:220px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.brand .datetime{margin-top:8px;font-weight:900;text-transform:uppercase;color:var(--arctic-ice);letter-spacing:.04em;font-size:14px;white-space:nowrap}.brand .brand-user{margin-top:4px;color:var(--arctic-snow);font:800 10px/1 Arial,sans-serif;letter-spacing:.04em;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:260px}
.live-log{position:absolute;left:465px;top:8px;right:calc(250px + 50px + var(--header-gap));height:108px;overflow:hidden;font:13px/1.35 ui-monospace,SFMono-Regular,Consolas,monospace;padding:0}.live-log .recent-title{display:flex;align-items:center;width:100%;height:18px;background:var(--title-gold);color:#071018;font:900 10px/18px Arial,sans-serif;letter-spacing:.12em;text-transform:uppercase;padding:0 8px;margin:0;border-radius:3px 3px 0 0}.live-log .recent-rows{height:90px;overflow:hidden;padding:3px 10px 0;scrollbar-width:none;-ms-overflow-style:none}.live-log .recent-rows::-webkit-scrollbar{display:none;width:0;height:0}.live-log .row{white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.live-log .t{display:inline-block;min-width:176px;color:var(--arctic-snow)}.live-log .inf{color:var(--green);font-weight:900}.live-log .wrn{color:var(--yellow);font-weight:900}.live-log .err{color:var(--red);font-weight:900}.live-log .src{color:var(--cyan)}
.top-buttons{position:absolute;right:50px;top:18px;width:250px;height:72px;display:grid;grid-template-columns:1fr 1fr;gap:8px 10px;padding-left:12px}.top-buttons a,.top-buttons button{border:0;border-radius:8px;font-weight:900;text-transform:uppercase;color:#071018;cursor:pointer;font-size:12px;display:flex;align-items:center;justify-content:center;line-height:1;padding:0 8px;box-shadow:inset 0 1px 0 rgba(255,255,255,.28),0 1px 0 rgba(0,0,0,.55)}.top-buttons .gold{background:var(--gold)}.top-buttons .blue{background:var(--blue)}.top-buttons .salmon{background:var(--salmon)}.top-buttons .gray{background:var(--peach)}.top-buttons .logout{background:var(--sunset-red);color:#fff}
.workspace-shell{position:fixed;left:8px;right:14px;top:calc(148px + var(--scroll-arrow-space));bottom:var(--scroll-arrow-space);min-height:0;overflow:hidden;z-index:10}.workspace-leg{position:absolute;left:0;top:0;bottom:0;width:var(--rail-width);background:var(--alpha-blue);border-radius:28px 0 0 28px}.workspace-arm{position:absolute;left:var(--rail-width);right:0;top:0;height:var(--workspace-arm-height);background:var(--title-gold);display:flex;align-items:center;padding:0;z-index:3;border-radius:0 9px 9px 0}.workspace-arm::before{content:"";position:absolute;left:0;top:0;width:var(--elbow-radius);height:100%;background:var(--alpha-blue);z-index:0}.workspace-title{position:relative;z-index:1;height:100%;display:flex;align-items:center;font-weight:900;text-transform:uppercase;color:#071018;letter-spacing:.055em;font-size:30px;line-height:1;margin-left:var(--elbow-radius);padding:0 1ch;background:none;border-left:6px solid #000;border-right:6px solid #000}.workspace-arm .last-scan{margin-left:auto;margin-right:12px;color:#3b2f16;font:700 9px/1 Arial,sans-serif;letter-spacing:.06em;text-transform:uppercase;white-space:nowrap;opacity:.9}.workspace-shell::after{content:"";position:absolute;left:var(--rail-width);top:var(--workspace-arm-height);width:var(--elbow-radius);height:var(--elbow-radius);background:radial-gradient(circle at 100% 100%,transparent 0 calc(var(--elbow-radius) - 1px),var(--alpha-blue) var(--elbow-radius));z-index:5;pointer-events:none}
.workspace-nav{position:absolute;left:0;top:calc(var(--workspace-arm-height) + var(--scroll-arrow-space));width:var(--rail-width);z-index:2}.workspace-nav a{display:flex;align-items:center;gap:6px;height:48px;padding:0 6px 0 9px;color:#071018;font-weight:900;text-transform:uppercase;font-size:10px;line-height:1.05;white-space:normal;overflow-wrap:normal;word-break:normal;hyphens:none;border-bottom:3px solid #000;transition:background .12s ease,color .12s ease,filter .12s ease}.workspace-nav a:not(.active)::before{content:"◆";display:inline-block;flex:0 0 14px;width:14px;font-size:9px;line-height:1;text-align:center}.workspace-nav a.active{font-size:12px;font-weight:800;background:var(--title-gold);color:#071018}.lcars-back{position:absolute;left:0;bottom:0;width:var(--rail-width);height:48px;z-index:7;border:0;border-top:3px solid #000;border-radius:0 0 0 28px;background:var(--alpha-blue);color:#071018;font:900 12px/1 "Arial Narrow","Roboto Condensed","Segoe UI",Arial,sans-serif;text-transform:uppercase;text-align:left;padding:0 0 0 11px;cursor:pointer}.lcars-back:hover,.lcars-back:focus-visible{background:var(--title-gold);outline:none}.lcars-back:active{background:var(--title-gold);filter:brightness(1.08)}.top-buttons a{transition:background .12s ease,color .12s ease,filter .12s ease}.top-buttons a.nav-flash,.workspace-nav a.nav-flash{background:var(--title-gold)!important;color:#071018!important;filter:brightness(1.08)}
.detail-scroll{position:absolute;left:var(--rail-width);right:0;top:calc(var(--workspace-arm-height) + var(--scroll-arrow-space));bottom:var(--scroll-arrow-space);min-height:0;overflow-y:auto;overflow-x:hidden;overscroll-behavior:contain;background:#000;padding:18px 22px 24px 24px;scrollbar-color:var(--salmon) #111}.detail-scroll::-webkit-scrollbar{width:10px}.detail-scroll::-webkit-scrollbar-track{background:#090909}.detail-scroll::-webkit-scrollbar-thumb{background:var(--salmon);border-radius:10px}
.control-plane-health-banner{display:none;margin:0 0 14px;padding:11px 13px;border:1px solid #c88a27;border-top:3px solid #e5a83f;border-radius:9px;background:#6d4911;color:#ffe1a3}.control-plane-health-banner strong{display:block;color:#ffd27a;font-size:12px;letter-spacing:.08em;text-transform:uppercase;margin-bottom:3px}.control-plane-health-banner.critical{background:#4a1711;border-color:#b94832;border-top-color:#e25b40;color:#ffd0c7}.control-plane-health-banner.critical strong{color:#ffb0a1}.control-plane-health-banner.health-unavailable{background:#30280e;border-color:#8f7620;color:#f0dfa0}
.page-head{display:flex;justify-content:space-between;gap:20px;align-items:flex-start;margin-bottom:16px}.page-head h1{font-size:31px;margin:0;color:var(--blue)}.page-head p{margin:4px 0 0;color:var(--starship-white)}.toolbar{display:flex;gap:10px;margin:10px 0}.toolbar input{flex:1}.toolbar input,.toolbar select,.toolbar button,.primary,dialog button{background:#071722;color:white;border:1px solid var(--line);border-radius:8px;padding:11px}.primary{background:var(--alpha-blue)!important;color:#071018!important;font-weight:800}
.metrics{display:grid;grid-template-columns:repeat(5,1fr);gap:13px;margin:14px 0}.metric{position:relative;min-height:82px;border:1px solid var(--line-soft);border-top:2px solid var(--line);border-radius:10px;display:grid;grid-template-columns:55px 1fr;grid-template-rows:1fr 1fr;align-items:center;padding:8px 30px 8px 8px;background:linear-gradient(180deg,rgba(12,34,52,.68),rgba(2,10,16,.78));cursor:pointer;transition:transform .12s ease,border-color .12s ease,background .12s ease}.metric::after{content:"›";position:absolute;right:12px;top:50%;transform:translateY(-50%);color:var(--arctic-ice);font-size:21px;font-weight:900}.metric:hover,.metric:focus-visible{transform:translateY(-1px);border-color:var(--arctic-ice);background:linear-gradient(180deg,rgba(18,49,72,.78),rgba(4,15,24,.86));outline:none}.metric.amber,.metric.issue-card{background:#6d4911;border-color:#c88a27;box-shadow:inset 0 1px 0 rgba(255,224,160,.10)}.metric.amber span,.metric.issue-card span{background:#c88a27;color:#071018}.metric.amber b,.metric.issue-card b{color:#ffd27a}.metric.amber small,.metric.issue-card small{color:#ffe1a3}.metric.amber::after,.metric.issue-card::after{color:#ffd27a}.metric.amber:hover,.metric.issue-card:hover,.metric.amber:focus-visible,.metric.issue-card:focus-visible{border-color:#e5a83f;background:#7a5214}.metric span{grid-row:1/3;width:42px;height:42px;border-radius:50%;display:grid;place-items:center;font-size:24px;background:var(--alpha-blue)}.metric b{font-size:20px}.metric small{color:var(--muted)}
.workspace{display:grid;grid-template-columns:minmax(680px,1.45fr) minmax(360px,.75fr);gap:14px;align-items:start}.panel,.detail{border:1px solid var(--line-soft);border-top:2px solid var(--line);border-radius:10px;background:linear-gradient(180deg,rgba(8,27,42,.5),rgba(1,7,12,.4));overflow:hidden}.table-panel{max-height:calc(100vh - 430px);overflow:auto;overscroll-behavior:contain;scrollbar-color:var(--salmon) #111}.table-panel::-webkit-scrollbar{width:10px;height:10px}.table-panel::-webkit-scrollbar-track{background:#090909}.table-panel::-webkit-scrollbar-thumb{background:var(--salmon);border-radius:10px}.table-panel thead th{position:sticky;top:0;z-index:2}table{width:100%;border-collapse:collapse}th{text-align:left;color:var(--arctic-ice);background:#071b29;padding:10px;font-size:11px}td{padding:11px 10px;border-bottom:1px solid var(--line-soft);white-space:nowrap}.status{display:inline-block;border:1px solid;border-radius:8px;padding:5px 8px;font-weight:700}.status.good{color:var(--radioactive);border-color:var(--radioactive)}.status.warn{color:var(--title-gold);border-color:var(--title-gold)}
.detail{padding:16px}.detail-head{display:flex;gap:12px;align-items:center;border-bottom:1px solid var(--line-soft);padding-bottom:13px}.bigicon{width:58px;height:58px;border-radius:9px;background:var(--alpha-blue);display:grid;place-items:center;font-size:30px}.facts{display:grid;grid-template-columns:repeat(2,1fr);margin:10px 0}.facts div{padding:8px;border-right:1px solid var(--line-soft)}.facts small,.facts b{display:block}.facts small{color:var(--muted);font-size:10px}.tabs{display:flex;gap:18px;border-bottom:1px solid var(--line-soft);padding:10px 0}.tabs b{color:var(--arctic-ice);border-bottom:2px solid var(--alpha-blue);padding-bottom:8px}.req{display:grid;grid-template-columns:1fr auto 28px;gap:8px;padding:9px;border-bottom:1px solid var(--line-soft)}.runbook-row{cursor:pointer;transition:background .12s ease}.runbook-row:hover,.runbook-row:focus-visible{background:#0b1b28;outline:none}.runbook-open{color:var(--arctic-ice);font-weight:800}.row-actions{display:flex;gap:6px;justify-content:flex-end;flex-wrap:wrap}.row-action{border:1px solid var(--line);border-radius:7px;padding:6px 9px;background:#0b1b28;color:var(--starship-white);font-weight:800;font-size:11px;cursor:pointer}.row-action:hover,.row-action:focus-visible{border-color:var(--arctic-ice);outline:none}.row-action.approve{color:var(--radioactive)}.row-action.danger{color:var(--title-gold);border-color:#6b5324}.row-action.danger:hover{color:#fff;background:#3a190f;border-color:var(--sunset-red)}.orphan-help{display:grid;grid-template-columns:auto 1fr auto;gap:14px;align-items:center;padding:11px 13px;margin:0 0 12px}.orphan-help b{color:var(--title-gold);white-space:nowrap}.orphan-help span{color:var(--muted);font-size:12px}.orphan-help a{border:1px solid var(--line);border-radius:8px;padding:8px 10px;color:var(--arctic-ice);font-weight:800;white-space:nowrap}.req small{display:block;color:var(--muted)}.req code{color:var(--arctic-ice)}.req i{width:22px;height:22px;border-radius:50%;display:grid;place-items:center;font-style:normal}.req i.ok{background:var(--night-rain)}.req i.bad{background:var(--sunset-red)}.lower{display:grid;grid-template-columns:1.2fr .8fr;gap:14px;margin-top:14px}.lower .panel{padding:12px}.activity-row{display:grid;grid-template-columns:16px 1fr auto;gap:8px;padding:7px;border-bottom:1px solid var(--line-soft)}.quick{display:grid;grid-template-columns:1fr 1fr;gap:8px}.quick a,.actions a{border:1px solid var(--line);border-radius:8px;padding:9px}.empty,.muted{color:var(--muted)}.empty{padding:20px;text-align:center}dialog{background:var(--panel);color:white;border:1px solid var(--line);border-radius:10px;max-width:520px;padding:22px}dialog::backdrop{background:#000a}
@media(max-width:1250px){.brand{width:245px}.brand .section{font-size:27px}.live-log{left:430px;right:calc(225px + 50px + 14px)}.top-buttons{right:50px;width:225px}.workspace{grid-template-columns:1fr}}@media(max-width:900px){.metrics{grid-template-columns:repeat(2,1fr)}}
""";
}
