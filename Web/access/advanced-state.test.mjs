import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { runInNewContext } from 'node:vm';

test('advanced browser diagnostics recover from an unsettled layout without stopping the game', () => {
  const library = {};
  const events = [];
  const previous = { scene: 'Advanced', round: 1 };
  const window = { advancedGameState: previous, dispatchEvent: event => events.push(event) };
  let warnings = 0;
  runInNewContext(readFileSync(new URL('../../Assets/Scripts/AdvancedMode/AdvancedWebState.jslib', import.meta.url), 'utf8'), {
    LibraryManager: { library },
    mergeInto: (target, methods) => Object.assign(target, methods),
    UTF8ToString: value => value,
    window,
    console: { warn: () => warnings++ },
    CustomEvent: class { constructor(type, options) { this.type = type; this.detail = options.detail; } },
  });

  assert.doesNotThrow(() => library.AdvancedPublishState('{"controls":[{"x":0.5,"y":Infinity}]}'));
  assert.equal(window.advancedGameState, previous);
  assert.equal(events.length, 0);
  assert.equal(warnings, 1);

  library.AdvancedPublishState('{"scene":"Advanced","round":2,"controls":[{"x":0.5,"y":0.8}]}');
  assert.equal(window.advancedGameState.round, 2);
  assert.equal(events.length, 1);
  assert.equal(events[0].type, 'advanced-game-state');
  assert.equal(events[0].detail, window.advancedGameState);
});
