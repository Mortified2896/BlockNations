import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { runInNewContext } from 'node:vm';
import { seal, SESSION_COOKIE } from '../access/tokens.mjs';

// Exercise Wrangler's compiler, rather than the unbundled source functions.
// Its default keepNames transform injects Worker-only helpers into toString().
const { build } = createRequire(import.meta.resolve('wrangler'))('esbuild');
const origin = 'https://blocknations.moneymattersmedia.com';

function browser() {
  const root = new EventTarget();
  root.document = new EventTarget(); root.document.hidden = false;
  root.location = { href: origin + '/multiplayer' };
  root.navigator = { onLine: true };
  root.setTimeout = root.setInterval = () => 1;
  root.clearTimeout = root.clearInterval = () => {};
  root.WebSocket = class { constructor(url) { this.url = url; this.readyState = 0; } };
  return root;
}

for (const configName of ['wrangler.production.jsonc', 'wrangler.jsonc']) {
  test(configName + ' compiled lobby and gameplay scripts boot in a browser', async () => {
    const config = JSON.parse(await readFile(new URL('../' + configName, import.meta.url), 'utf8'));
    const bundle = await build({
      absWorkingDir: new URL('..', import.meta.url).pathname,
      entryPoints: [config.main], bundle: true, write: false, format: 'esm',
      platform: 'browser', keepNames: config.keep_names ?? true, logLevel: 'silent',
    });
    const { default: worker } = await import('data:text/javascript;base64,' + Buffer.from(bundle.outputFiles[0].text).toString('base64'));
    const secret = 'synthetic-bundle-check-only-'.repeat(3);
    const session = await seal(secret, { kind: 'session', aud: origin,
      uid: 'synthetic-user', sid: 'synthetic-session', exp: Date.now() + 60000 });
    const env = { SESSION_SECRET: secret, GAME_ORIGIN: origin, GAME_ORIGINS: JSON.stringify([origin]),
      REVIEW: { fetch: () => Response.json({}) }, ASSETS: { fetch: () => new Response('') },
      ACCESS_STORE: { idFromName: name => name, get: () => ({ fetch: () => Response.json({
        auth_user_id: 'synthetic-user', display_name: 'Bundle QA', email: 'qa@example.test', status: 'approved',
      }) }) },
    };
    const get = path => worker.fetch(new Request(origin + path, { headers: { Cookie: SESSION_COOKIE + '=' + session } }), env);
    const lobby = await get('/multiplayer');
    assert.equal(lobby.status, 200);
    const html = await lobby.text();
    assert.match(html, /<script nonce="[^"]+" src="\/Feedback.js"><\/script>/);
    const inline = html.match(/<script nonce="[^"]+">([\s\S]*?)<\/script>/)[1];
    assert.doesNotThrow(() => new Function(inline));
    const scripts = [inline.slice(0, inline.indexOf('const VERSION=')), await (await get('/MultiplayerLive.js')).text()];
    for (const script of scripts) {
      const root = browser();
      runInNewContext(script, { window: root, URL });
      assert.equal(typeof root.BlockNationsLive?.subscribe, 'function');
      assert.doesNotThrow(() => root.BlockNationsLive.subscribe(() => {}));
    }
    const embedded = await (await get('/multiplayer?embedded=1')).text();
    assert.doesNotMatch(embedded, /src="\/Feedback.js"/);
    assert.match(embedded, /frame-ancestors|Multiplayer/);
  });
}
