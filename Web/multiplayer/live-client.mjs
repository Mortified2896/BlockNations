// Shared by the lobby and the Unity page. A reconnect always triggers a fetch
// because socket notifications may be lost while a phone/tab is suspended.
export function installMultiplayerLive(root) {
  const listeners = new Set();
  let socket, retry, heartbeat, renewal, failures = 0, stopped = false, lastMessage = 0;
  function emit(event) { for (const listener of listeners) listener(event); }
  function clearTimers() {
    root.clearTimeout(retry); root.clearTimeout(renewal); root.clearInterval(heartbeat);
    retry = heartbeat = renewal = undefined;
  }
  function connect() {
    if (stopped || !listeners.size || root.document.hidden || root.navigator.onLine === false ||
        (socket && socket.readyState < 2)) return;
    root.clearTimeout(retry);
    const url = new URL('/api/multiplayer/live', root.location.href);
    url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
    let current;
    try { current = socket = new root.WebSocket(url.href); }
    catch { scheduleRetry(); return; }
    current.onopen = () => {
      if (current !== socket) return;
      failures = 0; lastMessage = Date.now();
      emit({ type: 'refresh' });
      heartbeat = root.setInterval(() => {
        if (Date.now() - lastMessage > 60000) current.close();
        else if (current.readyState === 1) current.send('ping');
      }, 25000);
      renewal = root.setTimeout(() => current.close(), 15 * 60 * 1000);
    };
    current.onmessage = ({ data }) => {
      if (current !== socket) return;
      lastMessage = Date.now();
      if (data === 'pong') return;
      let event;
      try { event = JSON.parse(data); } catch { return; }
      if (event?.type === 'lobby' || (event?.type === 'match' &&
          /^[a-f0-9]{32}$/.test(event.matchId) && Number.isSafeInteger(event.seq))) emit(event);
    };
    current.onerror = () => current.close();
    current.onclose = () => {
      if (current !== socket) return;
      socket = undefined; clearTimers(); scheduleRetry();
    };
  }
  function scheduleRetry() {
    if (stopped || !listeners.size) return;
    const delay = Math.min(30000, 1000 * 2 ** Math.min(failures++, 5));
    retry = root.setTimeout(connect, delay + Math.floor(Math.random() * 250));
  }
  function resume() {
    if (root.document.hidden || stopped) return;
    emit({ type: 'refresh' });
    connect();
  }
  root.document.addEventListener('visibilitychange', resume);
  root.addEventListener('focus', resume);
  root.addEventListener('online', resume);
  root.addEventListener('pagehide', () => {
    stopped = true; clearTimers();
    const previous = socket; socket = undefined; previous?.close();
  });
  root.addEventListener('pageshow', () => { stopped = false; resume(); });
  root.BlockNationsLive = {
    subscribe(listener) {
      listeners.add(listener); connect();
      return () => {
        listeners.delete(listener);
        if (!listeners.size) {
          clearTimers(); const previous = socket; socket = undefined; previous?.close();
        }
      };
    },
    watchMatch(matchId) {
      this.unwatchMatch();
      this.matchId = matchId; this.pending = false;
      this.unsubscribeMatch = this.subscribe(event => {
        if (event.type === 'refresh' || (event.type === 'match' && event.matchId === this.matchId)) this.pending = true;
      });
    },
    unwatchMatch() { this.unsubscribeMatch?.(); this.unsubscribeMatch = undefined; this.matchId = undefined; this.pending = false; },
    consumeMatchUpdate(matchId) {
      if (this.matchId !== matchId || !this.pending) return false;
      this.pending = false; return true;
    }
  };
}

export const liveClientScript = '(' + installMultiplayerLive.toString() + ')(window);';
