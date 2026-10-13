import test from 'node:test';
import assert from 'node:assert/strict';
import { installMultiplayerLive } from './live-client.mjs';

function browser() {
  const sockets = [], timers = new Map();
  let timerId = 0;
  const root = new EventTarget();
  root.document = new EventTarget(); root.document.hidden = false;
  root.location = { href: 'https://game.test/multiplayer' };
  root.navigator = { onLine: true };
  root.setTimeout = (fn, delay) => { timers.set(++timerId, { fn, delay }); return timerId; };
  root.setInterval = (fn, delay) => { timers.set(++timerId, { fn, delay, repeat: true }); return timerId; };
  root.clearTimeout = root.clearInterval = id => timers.delete(id);
  root.WebSocket = class {
    readyState = 0;
    constructor(url) { this.url = url; sockets.push(this); }
    open() { this.readyState = 1; this.onopen(); }
    message(event) { this.onmessage({ data: JSON.stringify(event) }); }
    close() { this.readyState = 3; this.onclose?.(); }
    send(message) { this.sent = message; }
  };
  installMultiplayerLive(root);
  return { root, sockets, timers, retry() {
    const entry = [...timers].find(([, timer]) => timer.delay < 31000 && !timer.repeat);
    assert.ok(entry, 'Reconnect was scheduled');
    timers.delete(entry[0]); entry[1].fn();
  } };
}

test('a match notification wakes only the watched board and is consumed once', () => {
  const { root, sockets } = browser(), match = 'a'.repeat(32), other = 'b'.repeat(32);
  root.BlockNationsLive.watchMatch(match);
  assert.equal(sockets[0].url, 'wss://game.test/api/multiplayer/live');
  sockets[0].open();
  assert.equal(root.BlockNationsLive.consumeMatchUpdate(match), true); // Initial catch-up.
  sockets[0].message({ type: 'match', matchId: other, seq: 3 });
  sockets[0].message({ type: 'lobby' });
  assert.equal(root.BlockNationsLive.consumeMatchUpdate(match), false);
  sockets[0].message({ type: 'match', matchId: match, seq: 3 });
  sockets[0].message({ type: 'match', matchId: match, seq: 4 });
  assert.equal(root.BlockNationsLive.consumeMatchUpdate(other), false);
  assert.equal(root.BlockNationsLive.consumeMatchUpdate(match), true);
  assert.equal(root.BlockNationsLive.consumeMatchUpdate(match), false);
  root.BlockNationsLive.unwatchMatch();
  assert.equal(sockets[0].readyState, 3);
});

test('reconnect and browser return request fresh state even without a received event', () => {
  const fixture = browser(), { root, sockets } = fixture, events = [];
  root.BlockNationsLive.subscribe(event => events.push(event));
  sockets[0].open(); events.length = 0;
  sockets[0].close(); fixture.retry();
  assert.equal(sockets.length, 2);
  sockets[1].open();
  assert.deepEqual(events, [{ type: 'refresh' }]);
  events.length = 0;
  root.document.hidden = true;
  root.document.dispatchEvent(new Event('visibilitychange'));
  assert.equal(events.length, 0);
  root.document.hidden = false;
  root.document.dispatchEvent(new Event('visibilitychange'));
  assert.deepEqual(events, [{ type: 'refresh' }]);
  assert.equal(sockets.length, 2, 'Visible return reuses an open connection');
});

test('page suspension closes the connection and restored pages reconnect', () => {
  const { root, sockets, timers } = browser(), events = [];
  root.BlockNationsLive.subscribe(event => events.push(event)); sockets[0].open();
  root.dispatchEvent(new Event('pagehide'));
  assert.equal(sockets[0].readyState, 3);
  assert.equal(timers.size, 0);
  events.length = 0;
  root.dispatchEvent(new Event('pageshow'));
  assert.equal(sockets.length, 2);
  sockets[1].open();
  assert.ok(events.every(event => event.type === 'refresh'));
});

test('offline reconnect waits for connectivity and unsubscribe cancels retries', () => {
  const fixture = browser(), { root, sockets, timers } = fixture;
  const unsubscribe = root.BlockNationsLive.subscribe(() => {}); sockets[0].open();
  root.navigator.onLine = false; sockets[0].close(); fixture.retry();
  assert.equal(sockets.length, 1);
  root.navigator.onLine = true; root.dispatchEvent(new Event('online'));
  assert.equal(sockets.length, 2);
  sockets[1].close(); unsubscribe();
  assert.equal(timers.size, 0);
});
