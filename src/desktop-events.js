/* Rirogan Desktop 1.3: mensagens individuais via SSE autenticado.
 * O servidor mantém conexão curta e reconecta automaticamente. O fallback 1.2 só roda
 * quando EventSource não existe ou o canal SSE não funciona na hospedagem.
 * Apenas top-level do dashboard e domínio HTTPS principal.
 */
(function () {
  'use strict';
  if (window.top !== window || location.origin !== 'https://rirogan.com' ||
      !location.pathname.startsWith('/dashboard/')) return;
  window.__riroganDesktop = true;
  if (!window.chrome || !window.chrome.webview || typeof window.chrome.webview.postMessage !== 'function') return;

  // Estado da chamada: encaminha somente booleanos, jamais dados de áudio ou conversa.
  // O WebView2 recebe isso para controlar Push-to-Talk e a bandeja do Windows.
  let lastVoiceState = '';
  function reportVoice() {
    try {
      const d = window.RirogCallPage && window.RirogCallPage.desktop;
      const info = d && typeof d.status === 'function' ? d.status() : { active: false, mic: false };
      const state = JSON.stringify({ type: 'rirogan-desktop-voice', active: !!info.active, mic: !!info.mic });
      if (state !== lastVoiceState || !!info.active) {
        lastVoiceState = state;
        window.chrome.webview.postMessage(JSON.parse(state));
      }
    } catch (_) {}
  }
  let voiceTimer;
  function voiceLoop() {
    reportVoice();
    let activeCall = false;
    try { activeCall = !!(window.RirogCallPage && window.RirogCallPage.active()); } catch (_) {}
    voiceTimer = setTimeout(voiceLoop, activeCall ? 1300 : (document.hidden ? 6500 : 2800));
  }
  window.addEventListener('pagehide', () => clearTimeout(voiceTimer), { once: true });
  voiceLoop();
  let active = true, cursor = null, source = null, fallbackTimer = null;
  let sseFailures = 0, sseOpened = false, fallbackStarted = false;
  const lastIds = new Set();
  const oldCounts = new Map();
  let fallbackInitialized = false;

  function post(title, body, user, id) {
    if (!/^[\p{L}\p{N}_.-]{1,80}$/u.test(user)) return;
    if (!document.hidden && document.hasFocus()) return;
    try {
      window.chrome.webview.postMessage({
        type: 'rirogan-desktop-notification', category: 'dm',
        eventId: id == null ? '' : 'dm-' + String(id),
        title: String(title).slice(0, 120), body: String(body).slice(0, 200),
        href: 'mensagens.php?com=' + encodeURIComponent(user)
      });
    } catch (_) {}
  }

  function onMessage(event) {
    let m;
    try { m = JSON.parse(event.data); } catch (_) { return; }
    const id = Number(m.id);
    if (!Number.isSafeInteger(id) || id <= 0) return;
    cursor = Math.max(cursor || 0, id);
    if (lastIds.has(id)) return;
    lastIds.add(id);
    if (lastIds.size > 250) lastIds.delete(lastIds.values().next().value);
    post('Mensagem de ' + String(m.name || m.user || 'Rirogan'), String(m.body || 'Nova mensagem'), String(m.user || ''), id);
  }

  function stopSse() { if (source) { source.close(); source = null; } }
  function startSse() {
    if (!active || typeof EventSource === 'undefined' || fallbackStarted) { startFallback(); return; }
    stopSse();
    const query = cursor === null ? '' : '?after=' + encodeURIComponent(String(cursor));
    try { source = new EventSource('/dashboard/desktop_dm_events.php' + query, { withCredentials: true }); }
    catch (_) { startFallback(); return; }
    source.addEventListener('ready', function (e) {
      try { const n = Number(JSON.parse(e.data).cursor); if (Number.isSafeInteger(n) && n >= 0) cursor = Math.max(cursor || 0, n); } catch (_) {}
    });
    source.addEventListener('cursor', function (e) {
      try { const n = Number(JSON.parse(e.data).cursor); if (Number.isSafeInteger(n) && n >= 0) cursor = Math.max(cursor || 0, n); } catch (_) {}
    });
    source.addEventListener('dm', onMessage);
    source.addEventListener('server-error', function () { stopSse(); startFallback(); });
    source.onopen = function () { sseFailures = 0; sseOpened = true; };
    source.onerror = function () {
      sseFailures += 1;
      // EventSource já reconecta quando a conexão PHP termina normalmente.
      // Se não conseguir estabelecer SSE, retorna ao método anterior.
      if ((!sseOpened && sseFailures >= 3) || sseFailures >= 7) {
        stopSse(); startFallback();
      }
    };
  }

  // Compatibilidade com hospedagens que fecham/bloqueiam conexões SSE.
  async function fallbackPoll() {
    if (!active || !fallbackStarted) return;
    try {
      const response = await fetch('/dashboard/dm_list.php', {
        credentials: 'same-origin', cache: 'no-store',
        headers: { 'X-Requested-With': 'XMLHttpRequest' }
      });
      if (!response.ok) return;
      const data = await response.json();
      if (!data || data.ok !== true || !Array.isArray(data.convs)) return;
      const now = new Map();
      for (const c of data.convs) {
        const id = Number(c.id), unread = Math.max(0, Number(c.unread) || 0);
        if (!Number.isSafeInteger(id) || id <= 0) continue;
        now.set(id, unread);
        if (fallbackInitialized && unread > (oldCounts.get(id) || 0) && !c.mine) {
          post('Mensagem de ' + String(c.nome || c.user || 'Rirogan'), String(c.snip || 'Nova mensagem'), String(c.user || ''), null);
        }
      }
      oldCounts.clear();
      now.forEach((n, id) => oldCounts.set(id, n));
      fallbackInitialized = true;
    } catch (_) {
      // Falhas temporárias não devem gerar notificações falsas.
    }
  }
  function fallbackLoop() {
    if (!active || !fallbackStarted) return;
    fallbackPoll().finally(function () {
      if (active && fallbackStarted) fallbackTimer = setTimeout(fallbackLoop, document.hidden ? 12000 : 18000);
    });
  }
  function startFallback() {
    if (fallbackStarted || !active) return;
    stopSse(); fallbackStarted = true; fallbackLoop();
  }
  function stopAll() { active = false; stopSse(); if (fallbackTimer) clearTimeout(fallbackTimer); }
  window.addEventListener('pagehide', stopAll, { once: true });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', startSse, { once: true });
  else startSse();
})();
