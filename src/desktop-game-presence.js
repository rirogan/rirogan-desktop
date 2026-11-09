(()=>{'use strict';
// Injected by Rirogan Windows only; requests are authorized by the current browser session + CSRF.
const endpoint='/dashboard/rirogan_desktop_presence.php';
let pending=false;
window.riroganDesktopPublishGame=async payload=>{
 if(pending||!location.pathname.startsWith('/dashboard/'))return {ok:false,skipped:true};
 pending=true;
 try{
  const read=await fetch(endpoint,{credentials:'same-origin',cache:'no-store',headers:{'X-Requested-With':'XMLHttpRequest'}});
  const raw=await read.text();
  if(!read.ok||!raw.trim())return {ok:false,stage:'auth',http:read.status};
  const boot=JSON.parse(raw);
  if(!boot.ok||!boot.consent)return {ok:false,consent:false};
  const form=new URLSearchParams();
  form.set('game', typeof payload?.game==='string'?payload.game.slice(0,70):'');
  form.set('elapsed',String(Math.max(0,Math.min(604800,Math.floor(Number(payload?.elapsed)||0)))));
  form.set('csrf',boot.csrf||'');
  const write=await fetch(endpoint,{method:'POST',credentials:'same-origin',cache:'no-store',headers:{'X-Requested-With':'XMLHttpRequest','Content-Type':'application/x-www-form-urlencoded;charset=UTF-8'},body:form.toString()});
  const rawWrite=await write.text();
  if(!rawWrite.trim())return {ok:false,stage:'write',http:write.status};
  return JSON.parse(rawWrite);
 }catch(e){return {ok:false,stage:'network',error:String(e?.message||e).slice(0,90)}}finally{pending=false}
};
})();
