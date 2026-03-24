using System.Net;

namespace PCGuardianRemote;

internal static class TerminalPage
{
    public static string Generate(string hostname)
    {
        var h = WebUtility.HtmlEncode(hostname);
        return "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>PC Guardian Remote - " + h + "</title>" +
            "<style>*{margin:0;padding:0;box-sizing:border-box}" +
            "body{background:#0a0a0b;color:#e4e4e7;font-family:'Segoe UI',system-ui,sans-serif;height:100vh;display:flex;flex-direction:column}" +
            ".header{background:#18181b;padding:12px 20px;display:flex;align-items:center;justify-content:space-between;border-bottom:1px solid #27272a}" +
            ".header h1{font-size:16px;font-weight:600}" +
            ".dot{width:8px;height:8px;border-radius:50%;display:inline-block;margin-right:6px}" +
            ".dot.on{background:#10b981}.dot.off{background:#ef4444}" +
            ".status{display:flex;align-items:center;gap:8px;font-size:13px}" +
            ".ps{display:flex;flex-direction:column;align-items:center;justify-content:center;flex:1;gap:16px}" +
            ".ps h2{font-size:20px;font-weight:600}" +
            ".ps input{background:#27272a;border:1px solid #3f3f46;color:#fff;padding:12px 20px;font-size:18px;border-radius:8px;text-align:center;width:200px;letter-spacing:4px}" +
            ".ps button{background:#6366f1;color:#fff;border:none;padding:10px 32px;border-radius:8px;cursor:pointer;font-size:14px;font-weight:500}" +
            ".ps button:hover{background:#818cf8}" +
            ".ps .err{color:#ef4444;font-size:13px;display:none}" +
            ".tabs{display:flex;background:#18181b;border-bottom:1px solid #27272a}" +
            ".tabs button{background:none;border:none;color:#71717a;padding:10px 20px;cursor:pointer;font-size:13px;font-weight:500;border-bottom:2px solid transparent}" +
            ".tabs button.active{color:#e4e4e7;border-bottom-color:#6366f1}" +
            ".panel{flex:1;display:none;flex-direction:column}.panel.active{display:flex}" +
            "#tout{flex:1;background:#09090b;padding:16px;font-family:'Cascadia Code','Consolas',monospace;font-size:13px;line-height:1.6;overflow-y:auto;white-space:pre-wrap;word-break:break-all;color:#a1a1aa}" +
            "#trow{background:#18181b;padding:8px 16px;display:flex;align-items:center;gap:8px;border-top:1px solid #27272a}" +
            ".prompt{color:#6366f1;font-family:'Cascadia Code','Consolas',monospace;font-size:13px;font-weight:600}" +
            "#tinp{flex:1;background:#27272a;border:1px solid #3f3f46;color:#e4e4e7;padding:8px 12px;font-family:'Cascadia Code','Consolas',monospace;font-size:13px;border-radius:6px;outline:none}" +
            "#tinp:focus{border-color:#6366f1}" +
            ".mets{padding:20px;display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:12px}" +
            ".mc{background:#18181b;border:1px solid #27272a;border-radius:8px;padding:16px}" +
            ".mc .l{font-size:11px;color:#71717a;text-transform:uppercase;letter-spacing:0.5px;margin-bottom:4px}" +
            ".mc .v{font-size:20px;font-weight:600}" +
            ".slog{flex:1;padding:16px;overflow-y:auto}" +
            ".slog .e{padding:6px 0;border-bottom:1px solid #1a1a1e;font-size:13px;display:flex;gap:12px}" +
            ".slog .ts{color:#71717a;min-width:70px}.slog .msg{color:#a1a1aa}.slog .cmd{color:#6366f1}.slog .err-log{color:#ef4444}" +
            ".footer{background:#18181b;padding:8px 20px;font-size:11px;color:#52525b;border-top:1px solid #27272a;display:flex;justify-content:space-between}" +
            "</style></head><body>" +
            "<div class=\"header\"><h1>PC Guardian Remote</h1><div class=\"status\"><span class=\"dot off\" id=\"cd\"></span><span id=\"ct\">Disconnected</span></div></div>" +
            "<div class=\"ps\" id=\"ps\"><h2>Enter PIN</h2><input type=\"password\" id=\"pi\" maxlength=\"20\" placeholder=\"PIN\" autofocus><button onclick=\"verifyPin()\">Connect</button><div class=\"err\" id=\"pe\">Invalid PIN</div></div>" +
            "<div id=\"app\" style=\"display:none;flex:1;flex-direction:column\">" +
            "<div class=\"tabs\"><button class=\"active\" onclick=\"showTab('terminal',this)\">Terminal</button><button onclick=\"showTab('dashboard',this)\">Info</button><button onclick=\"showTab('log',this)\">Session Log</button></div>" +
            "<div class=\"panel active\" id=\"terminal\"><div id=\"tout\"></div><div id=\"trow\"><span class=\"prompt\">PS &gt;</span><input type=\"text\" id=\"tinp\" placeholder=\"Type a PowerShell command...\" autocomplete=\"off\"></div></div>" +
            "<div class=\"panel\" id=\"dashboard\"><div class=\"mets\" id=\"mets\"></div></div>" +
            "<div class=\"panel\" id=\"log\"><div class=\"slog\" id=\"slog\"><div id=\"slog-empty\" style=\"color:#52525b;padding:20px;text-align:center\">Session log appears here</div></div></div>" +
            "</div>" +
            "<div class=\"footer\"><span>" + h + "</span><span>PC Guardian Remote</span></div>" +
            "<script>" +
            "var pn='',ws=null,wsc=false,ch=[],hi=-1;" +
            "function $(id){return document.getElementById(id)}" +
            "function verifyPin(){pn=$('pi').value;fetch('/api/metrics?pin='+encodeURIComponent(pn)).then(function(r){if(r.ok){$('ps').style.display='none';$('app').style.display='flex';connectWS();refreshM();setInterval(refreshM,5000);log('Connected')}else{$('pe').style.display='block'}}).catch(function(){$('pe').style.display='block'})}" +
            "function connectWS(){if(ws&&ws.readyState<2)try{ws.close()}catch(e){}var p=location.protocol==='https:'?'wss:':'ws:';ws=new WebSocket(p+'//'+location.host+'/shell?pin='+encodeURIComponent(pn));ws.onopen=function(){wsc=true;log('Shell connected');$('cd').className='dot on';$('ct').textContent='Connected'};ws.onmessage=function(e){$('tout').textContent+=e.data;$('tout').scrollTop=$('tout').scrollHeight};ws.onclose=function(){$('cd').className='dot off';if(wsc){log('Shell disconnected','err-log');$('ct').textContent='Reconnecting...';wsc=false;setTimeout(connectWS,5000)}else{$('ct').textContent='Connecting...';setTimeout(connectWS,3000)}};ws.onerror=function(){}}" +
            "function sendCmd(){var i=$('tinp');if(!i.value.trim()||!ws||ws.readyState!==1)return;var cmd=i.value;ch.unshift(cmd);hi=-1;log('$ '+cmd,'cmd');ws.send(cmd+'\\n');i.value=''}" +
            "function refreshM(){fetch('/api/metrics?pin='+encodeURIComponent(pn)).then(function(r){return r.json()}).then(function(d){var m=$('mets');m.textContent='';var items=[['Host',d.host],['OS',d.os],['Uptime',Math.floor(d.uptime/60)+'h '+Math.floor(d.uptime%60)+'m'],['Updated',new Date(d.timestamp).toLocaleTimeString()]];items.forEach(function(x){var c=document.createElement('div');c.className='mc';var l=document.createElement('div');l.className='l';l.textContent=x[0];var v=document.createElement('div');v.className='v';v.textContent=x[1];c.appendChild(l);c.appendChild(v);m.appendChild(c)})}).catch(function(){})}" +
            "function showTab(id,btn){document.querySelectorAll('.panel').forEach(function(p){p.classList.remove('active')});document.querySelectorAll('.tabs button').forEach(function(b){b.classList.remove('active')});$(id).classList.add('active');btn.classList.add('active');if(id==='terminal')$('tinp').focus()}" +
            "function log(msg,cls){var sl=$('slog');var emp=$('slog-empty');if(emp)emp.remove();var d=document.createElement('div');d.className='e';var t=document.createElement('span');t.className='ts';t.textContent=new Date().toLocaleTimeString();var m=document.createElement('span');m.className='msg '+(cls||'');m.textContent=msg;d.appendChild(t);d.appendChild(m);sl.appendChild(d);sl.scrollTop=sl.scrollHeight}" +
            "$('pi').addEventListener('keydown',function(e){if(e.key==='Enter')verifyPin()});" +
            "$('tinp').addEventListener('keydown',function(e){if(e.key==='Enter'){sendCmd();return}if(e.key==='ArrowUp'&&ch.length){e.preventDefault();hi=Math.min(hi+1,ch.length-1);e.target.value=ch[hi]}if(e.key==='ArrowDown'){e.preventDefault();hi=Math.max(hi-1,-1);e.target.value=hi>=0?ch[hi]:''}});" +
            "</script></body></html>";
    }
}
