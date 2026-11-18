using System.Net;

namespace CommandHub.AddOn.PhoneDrop;

/// <summary>The page your phone opens: pair once, then send files to the PC or download what the PC shares.</summary>
public static class PhonePage
{
    public const string Icon = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><rect width="64" height="64" rx="14" fill="#1C1F27"/>
        <rect x="14" y="14" width="16" height="16" rx="3" fill="#F5B14C"/><rect x="34" y="14" width="16" height="16" rx="3" fill="#3A4150"/>
        <rect x="14" y="34" width="16" height="16" rx="3" fill="#3A4150"/><rect x="34" y="34" width="16" height="16" rx="3" fill="#3A4150"/></svg>
        """;

    public static string Html(string pcName) => Template.Replace("{{PC}}", WebUtility.HtmlEncode(pcName));

    private const string Template = """
<!doctype html>
<html lang="en"><head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="theme-color" content="#14161c">
<meta name="apple-mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-title" content="Phone drop">
<link rel="icon" href="/icon.svg"><link rel="apple-touch-icon" href="/icon.svg">
<title>Phone drop</title>
<style>
  :root { --bg:#14161c; --card:#1d2029; --raised:#272b36; --text:#eef0f4; --muted:#9aa3b2; --accent:#F5B14C; --ok:#4FC3D1; --bad:#F0605D; }
  * { box-sizing: border-box; }
  body { margin:0; background:var(--bg); color:var(--text); font:16px/1.4 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif; padding: max(20px, env(safe-area-inset-top)) 18px 40px; }
  h1 { font-size:13px; letter-spacing:.12em; color:var(--accent); margin:0 0 6px; font-weight:600; }
  .pc { font-size:28px; font-weight:300; margin:0 0 22px; }
  .card { background:var(--card); border-radius:16px; padding:18px; margin-bottom:14px; }
  .send { display:flex; align-items:center; justify-content:center; gap:10px; width:100%; padding:20px; border:0; border-radius:14px;
          background:var(--accent); color:#1b1b1b; font-size:18px; font-weight:600; cursor:pointer; }
  .hint { color:var(--muted); font-size:13px; margin-top:10px; }
  .row { padding:10px 0; border-bottom:1px solid #2a2e38; }
  .row:last-child { border-bottom:0; }
  .name { overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .meta { color:var(--muted); font-size:12px; margin-top:4px; display:flex; justify-content:space-between; }
  .bar { height:5px; background:var(--raised); border-radius:3px; margin-top:7px; overflow:hidden; }
  .bar > i { display:block; height:100%; width:0; background:var(--accent); transition:width .2s; }
  .done .bar > i { background:var(--ok); } .fail .bar > i { background:var(--bad); }
  a.dl { display:flex; justify-content:space-between; align-items:center; gap:10px; color:var(--text); text-decoration:none; padding:12px 0; border-bottom:1px solid #2a2e38; }
  a.dl:last-child { border-bottom:0; }
  a.dl span.go { color:var(--accent); font-weight:600; flex:none; }
  h2 { font-size:12px; letter-spacing:.1em; color:var(--muted); margin:0 0 6px; font-weight:600; }
  input.pin { width:100%; font-size:30px; letter-spacing:.4em; text-align:center; padding:14px; border-radius:12px; border:1px solid #333a48; background:var(--bg); color:var(--text); }
  .err { color:var(--bad); font-size:14px; min-height:20px; margin-top:8px; }
  .hidden { display:none; }
</style></head>
<body>
<h1>PHONE DROP</h1>
<p class="pc">{{PC}}</p>

<div id="pair" class="card hidden">
  <h2>PAIR WITH THIS PC</h2>
  <p class="hint" style="margin-top:0">Type the 6-digit code shown in Command hub, under Phone drop.</p>
  <input id="pin" class="pin" inputmode="numeric" maxlength="6" autocomplete="one-time-code" placeholder="000000">
  <div id="pinerr" class="err"></div>
</div>

<div id="main" class="hidden">
  <div class="card">
    <label class="send" for="files">⬆&nbsp; Send photos &amp; files</label>
    <input id="files" type="file" multiple class="hidden">
    <div class="hint">They go straight to the PC over your Wi-Fi. Keep this page open until they're done.</div>
    <div id="queue"></div>
  </div>
  <div class="card">
    <h2>FROM YOUR PC</h2>
    <div id="incoming"><div class="hint">Nothing yet. Drag files onto Phone drop on the PC and they'll appear here.</div></div>
  </div>
</div>

<script>
const $ = id => document.getElementById(id);
const params = new URLSearchParams(location.search);
let key = params.get('k') || localStorage.getItem('pd-key');
if (params.get('k')) { localStorage.setItem('pd-key', key); history.replaceState(null, '', '/'); }
// The key goes in a header (and a same-site cookie for downloads), never in addresses the phone keeps.
function remember(k) { key = k; localStorage.setItem('pd-key', k); document.cookie = 'pd=' + k + '; path=/; max-age=31536000; SameSite=Strict'; }
function forget() { key = null; localStorage.removeItem('pd-key'); document.cookie = 'pd=; path=/; max-age=0'; }
if (key) remember(key);
const H = () => ({ 'X-Key': key, 'X-Phone-Drop': '1' });

function size(b) { return b < 1024 ? b + ' B' : b < 1048576 ? (b / 1024).toFixed(0) + ' KB' : b < 1073741824 ? (b / 1048576).toFixed(1) + ' MB' : (b / 1073741824).toFixed(2) + ' GB'; }
function esc(s) { return s.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

function showPair() { $('main').classList.add('hidden'); $('pair').classList.remove('hidden'); $('pin').focus(); }
function showMain() { $('pair').classList.add('hidden'); $('main').classList.remove('hidden'); refresh(); }

$('pin').addEventListener('input', async () => {
  const pin = $('pin').value.replace(/\D/g, '');
  $('pin').value = pin; $('pinerr').textContent = '';
  if (pin.length !== 6) return;
  const r = await fetch('/api/pair', { method: 'POST', body: 'pin=' + pin, headers: { 'X-Phone-Drop': '1' } });
  if (r.ok) { remember((await r.json()).key); showMain(); }
  else if (r.status === 423) { $('pinerr').textContent = 'Typing a code is paused after too many wrong tries. Scan the QR code, or press New pairing code on the PC.'; $('pin').value = ''; }
  else { $('pinerr').textContent = 'That code didn\'t match. Check the one on the PC.'; $('pin').value = ''; }
});

async function refresh() {
  if (!key) return showPair();
  try {
    const r = await fetch('/api/state', { cache: 'no-store', headers: H() });
    if (r.status === 401) { forget(); return showPair(); }
    const s = await r.json();
    $('incoming').innerHTML = s.files.length === 0
      ? '<div class="hint">Nothing yet. Drag files onto Phone drop on the PC and they\'ll appear here.</div>'
      : s.files.map(f => `<a class="dl" href="/api/file/${f.id}" download="${esc(f.name)}"><span class="name">${esc(f.name)}<br><small style="color:var(--muted)">${size(f.size)}</small></span><span class="go">Download</span></a>`).join('');
  } catch { /* PC asleep or off the Wi-Fi: try again shortly */ }
}

$('files').addEventListener('change', () => {
  const files = [...$('files').files];
  $('files').value = '';
  for (const f of files) queue.push(addRow(f));
  pump();
});

const queue = [];
let busy = false;
function addRow(file) {
  const row = document.createElement('div');
  row.className = 'row';
  row.innerHTML = `<div class="name">${esc(file.name)}</div><div class="bar"><i></i></div><div class="meta"><span class="st">Waiting</span><span>${size(file.size)}</span></div>`;
  $('queue').prepend(row);
  return { file, row };
}

async function pump() {
  if (busy) return;
  busy = true;
  while (queue.length) await send(queue.shift());
  busy = false;
}

function send({ file, row }) {
  return new Promise(resolve => {
    const xhr = new XMLHttpRequest();
    const bar = row.querySelector('.bar > i'), st = row.querySelector('.st');
    const started = Date.now();
    xhr.open('POST', '/api/upload?name=' + encodeURIComponent(file.name));
    for (const [h, v] of Object.entries(H())) xhr.setRequestHeader(h, v);
    xhr.upload.onprogress = e => {
      if (!e.lengthComputable) return;
      const pct = e.loaded / e.total * 100, secs = (Date.now() - started) / 1000;
      bar.style.width = pct + '%';
      st.textContent = Math.floor(pct) + '%' + (secs > 1 ? '  ·  ' + size(e.loaded / secs) + '/s' : '');
    };
    xhr.onload = () => {
      if (xhr.status === 200) { row.classList.add('done'); bar.style.width = '100%'; st.textContent = 'Sent ✓'; }
      else if (xhr.status === 401) { row.classList.add('fail'); st.textContent = 'Pair again'; showPair(); }
      else { row.classList.add('fail'); st.textContent = 'Failed: ' + ((() => { try { return JSON.parse(xhr.responseText).error; } catch { return xhr.status; } })()); }
      resolve();
    };
    xhr.onerror = () => { row.classList.add('fail'); st.textContent = 'Lost the connection: is the PC still on this Wi-Fi?'; resolve(); };
    st.textContent = 'Sending…';
    xhr.send(file);
  });
}

if (key) showMain(); else showPair();
setInterval(refresh, 3000);
</script>
</body></html>
""";
}
