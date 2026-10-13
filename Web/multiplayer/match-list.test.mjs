import test from 'node:test';
import assert from 'node:assert/strict';
import { matchState } from './match-list.mjs';
import { lobbyPage } from './page.mjs';

test('turn highlighting follows the viewer seat in a four-player match', () => {
  const match = { status: 'active', seatCount: 4, seatIndex: 2, currentTurnSeatIndex: 2, ready: true };
  assert.equal(matchState(match), 'your-turn');
  assert.equal(matchState({ ...match, currentTurnSeatIndex: 3 }), 'waiting');
  assert.equal(matchState({ ...match, seatIndex: 0 }), 'waiting');
});
test('an uninitialized active game highlights only its first turn owner', () => {
  const match = { status: 'active', ready: false, currentTurnSeatIndex: 0 };
  assert.equal(matchState({ ...match, seatIndex: 0 }), 'your-turn');
  assert.equal(matchState({ ...match, seatIndex: 1 }), 'waiting');
});
test('lobbies and finished matches never acquire a turn highlight', () => {
  for (const status of ['waiting', 'finished', 'cancelled']) {
    assert.equal(matchState({ status, seatIndex: 0, currentTurnSeatIndex: 0 }), status === 'waiting' ? 'lobbies' : 'finished');
  }
});
test('missing or unclaimed viewer seats cannot produce a your-turn signal', () => {
  for (const seatIndex of [undefined, null, -1, '0']) {
    assert.equal(matchState({ status: 'active', seatIndex, currentTurnSeatIndex: seatIndex }), 'waiting');
  }
});
test('compact page keeps creation and match details in labelled dialogs', async () => {
  const html = await lobbyPage({ display_name: '<script>Test</script>', email: 'private@example.invalid' }).text();
  assert.match(html, /&lt;script&gt;Test&lt;\/script&gt;/);
  assert.doesNotMatch(html, /private@example\.invalid/);
  for (const id of ['new-game-dialog', 'match-dialog', 'social-dialog']) {
    assert.match(html, new RegExp('<dialog id="' + id + '" aria-labelledby="'));
  }
  assert.match(html, /<option value="4">4 players/);
  const script = html.match(/<script nonce="[^"]+">([\s\S]*?)<\/script>/)[1];
  assert.doesNotThrow(() => new Function(script));
});
