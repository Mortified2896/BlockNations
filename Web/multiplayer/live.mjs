// Live messages only invalidate state. Snapshots and social details still pass
// through the ordinary authenticated HTTP endpoints on every fetch.
export const LIVE_LIFETIME_MS = 15 * 60 * 1000;

export function acceptLive(ctx, actor) {
  const tag = "player:" + actor.id;
  if (ctx.getWebSockets(tag).length >= 12)
    return Response.json({ ok: false, error: "TOO_MANY_CONNECTIONS" }, { status: 429 });
  const [client, server] = Object.values(new WebSocketPair());
  server.serializeAttachment({ expires: Date.now() + LIVE_LIFETIME_MS });
  ctx.acceptWebSocket(server, [tag, "session:" + actor.sessionId]);
  return new Response(null, { status: 101, webSocket: client });
}

export function publishLive(ctx, changes) {
  for (const [playerId, events] of changes) {
    for (const socket of ctx.getWebSockets("player:" + playerId)) {
      try {
        if (socket.deserializeAttachment()?.expires <= Date.now()) {
          socket.close(4001, "Renew session");
          continue;
        }
        for (const event of events.values()) socket.send(JSON.stringify(event));
      } catch { /* A disconnected client recovers from HTTP on reconnect. */ }
    }
  }
}

export function revokeLive(ctx, sessionId) {
  for (const socket of ctx.getWebSockets("session:" + sessionId)) {
    try { socket.close(4001, "Session ended"); } catch { }
  }
}
