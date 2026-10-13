# Google-account web multiplayer playtest

The Cloudflare playtest at `https://blocknations.moneymattersmedia.com/multiplayer` supports two to four people on the standard 7×7 board. The owner approved asynchronous saved turns, invitations to registered players by Google display name or exact email, in-game invitations without email delivery, and publication after validation. On 2026-10-11 the owner requested replacing the single-player selector with one frozen, evaluated learned opponent and the three-unit single-player roster. See the [learned-AI release record](Learned_AI_Browser_Playtest.md) for current publication status. As of 2026-10-12, ordinary single-player and multiplayer both use Warrior, Rider and Archer with Archer vision two; Scout recruitment is removed. Advanced mode is available to verified administrators and in the Unity Editor.

## Player flow

1. Sign in with Google. The existing tester approval gate still applies; friends must sign in and receive approval before they can play or appear in search.
2. Choose **Multiplayer** in the game menu.
3. Choose a **Lobby size** of two, three or four. **Create private lobby** starts an invitation-only lobby; **Find online match** uses the public queue. This joins the oldest lobby of that size with an open seat, or creates a new lobby. Repeated finds reuse your waiting lobby. Each seat shows its player and colour: Blue, Red, Green, Yellow.
4. The person who created the lobby is its host. Only the host can press **Start game with N players**, once at least two have joined. Filling all seats does not start automatically. The host can start a four-seat lobby with two or three joined players; the actual count freezes at start and unanswered invitations close. Only the host can cancel a waiting lobby.
5. **Invite to match** offers an existing hosted lobby with space, or a new private two-player match. Invitations reserve a seat when the recipient accepts. Search and the review dialog show verified accounts; no email is sent. Friends must have approved accounts.
6. Play and end the turn. Completed turn snapshots are saved online and can be reopened on another browser or device using the same account. Actions within an unfinished turn are not synchronized across devices.

The lobby and an open game receive live notifications for completed turns, joined/started/cancelled lobbies, invitations and friendships. The affected client fetches the accepted state immediately; ten-second polling remains a fallback. Returning to the tab, restoring connectivity and reconnecting also request fresh state, so a missed notification does not require a page reload. Opening links redirect to the lobby until the host starts it. The server rejects premature initialization, non-host starts, and joining after start. An active match advances through its actual seats, skipping resigned/eliminated players. Finished matches remain readable.

Four-player cities occupy the four inset corners: (1,1), (5,5), (1,5), (5,1). Three-player cities form a triangle **within the square board**: one edge midpoint and the two opposite corners. The match ID deterministically selects one of four rotations and six seat permutations, so concurrent initialization agrees and a particular colour does not always take the midpoint. This distributes the asymmetry across matches; it does not prove equal territorial access or competitive balance.

## Separate identity and transport

The existing Google admission Worker verifies the current account for every protected game/API request. It derives a stable opaque player ID from the verified Google identity and provides the Google display name. Browser input and old app seat claims cannot select an identity or reserve another person's seat. The random name/title generator stays implemented for the app and is not used by the account web release.

`Web/multiplayer/store.mjs` owns a private SQLite Durable Object, deployed as `blocknations-multiplayer` without public URLs or routes. Origin-scoped objects isolate production and preview matches, friendships, invitations and device subscriptions. The game Workers hold cross-script bindings to this private namespace. Admission/search stays in the existing `blocknations-access` service; Google credentials and Review cookies stay in their existing private services.

The new relay reserves seats and commits updates atomically. It checks match membership, current turn ownership, base sequence, board/protocol shape and app compatibility. Identical retries are idempotent; stale tabs receive a conflict and reload the accepted snapshot. Public queue matches and private invitation matches have separate creation paths. Match snapshots are still simulated by the Unity clients, as in the app PBp relay; this internal playtest is not a server-authoritative anti-cheat implementation.

`WebAccountClient` and `WebAccountTurnTransport` use same-origin `UnityWebRequest` calls with the existing encrypted, HttpOnly game cookie. No cookie value, OAuth token, email address or shared app PBp credential is given to the Unity client. Google names support spaces and Unicode, and labels render them as plain text. The web build disables the native share-code/reminder UI because membership and invitations are handled in the lobby.

`/api/multiplayer/live` accepts a same-origin WebSocket only after the existing Google approval check. The origin-scoped Durable Object uses hibernatable connections tagged with the verified player and session. It publishes small invalidations after the SQL transaction commits, only to affected accounts; no board snapshot, email or social payload is broadcast. Logout/account switching closes that session's sockets. Connections renew every fifteen minutes, and expired connections stop receiving invalidations. Full reads and all writes continue through the authenticated HTTP API.

The shared browser client in `Web/multiplayer/live-client.mjs` handles connection recovery and tab/page lifecycle. Menu refreshes are coalesced and retained while another request is busy or the page is hidden. The Unity bridge watches only the currently opened match and interrupts its polling wait when a notification arrives; ordinary save loading, seat ownership, fog and incoming-turn audio remain on the existing path. An unfinished local turn is not replaced by a generic menu refresh. Returning to another Unity scene removes the match subscription.

The reload-only board failure was a duplicate polling startup after submitting a turn. Loading the server's accepted response started a polling fetch, then the submit coroutine restarted polling and cancelled that fetch without clearing `isPlayByPostFetchInProgress`. Later fetch attempts returned immediately. Applying an accepted submit response now suppresses the load-time polling startup; the submit path starts the single loop after saving the accepted state. The regression scenario is: the remote player ends a turn, the viewer ends their own turn, then the remote player ends another turn. The viewer must receive that second remote turn without reopening the match. A one-handoff smoke test does not cover this failure.

Local validation on 2026-10-13 passed all 85 web checks and 17 release/build checks. Two independent synthetic accounts exercised the real Durable Object implementation and the exported Unity player. The original polling timer took 10.063 seconds to fetch a completed turn; the live build took 266 ms, then 192 ms for the next remote turn after the viewer submitted their own. Reconnect recovered a missed turn, the game remained usable with the socket removed, and reopening at 390×844 retained the accepted sequence. Desktop and phone-sized menu checks covered invitations, joins, starts, cancellation, friend acceptance, a stale in-flight response and an offline reconnect without page reloads. These local timings are not production latency or physical iPhone acceptance measurements.

The existing protocol-5 Unity snapshot and turn export/load paths are reused. The native relay, protocol-4 migration support, supported app imports, scene/prefab wiring and app save formats are preserved. New queues use `web-pbp-2-core3`; older `web-pbp-1` matches remain reopenable. A separate capacity table defaults existing matches to two seats without rewriting their snapshots. Two to four seats are the current playtest range, not a permanent player/team architecture limit. Incompatible future web changes must advance `WEB_MATCH_VERSION` and the matching client constant so new players cannot join old incompatible queues.

## Phone notifications and installation

The lobby offers explicit **Enable notifications**, **Turn off notifications** and **Send test notification** controls. There is no permission request on page load. Requests, joined matches, invitation acceptance, next turns and finished games can enqueue reminders.

- iPhone/iPad: iOS/iPadOS 16.4 or later, **Share → Add to Home Screen**, then open the installed icon and enable notifications in the lobby. A regular iPhone browser tab does not have the same Web Push support.
- Android: use a browser with Web Push support, such as Chrome, and allow notifications. Installing the app or adding its icon makes it easier to return; installation is not universally required on Android.
- Delivery depends on browser support, device notification/focus settings and connectivity. This release makes a best effort and does not promise native-app reliability.

The service worker is push-only: it does not intercept requests or cache game assets, authentication or match data. Unity's optional game-data cache is disabled in the isolated release build; browser-local saves still use IndexedDB. Only the manifest, icons and notification scripts are public shell assets. HTML, loader, framework, data, Wasm, release metadata and multiplayer APIs remain approval-gated.

Push uses RFC 8291 `aes128gcm` and RFC 8292 VAPID with Workers WebCrypto. The private multiplayer Worker holds VAPID keys; only the public key is returned for device registration. Subscriptions are associated with both the approved player and current game session. Logout/account switching removes subscriptions belonging to that session. Rebinding an endpoint to a different account cannot deliver the previous account's queued messages. Notification text contains generic game updates without email addresses or private board data.

The durable outbox retries transient provider failures, removes expired endpoints on 404/410, bounds attempts and drops day-old notifications. An accepted game turn does not wait for the push provider. Outbound endpoints are restricted to supported Apple, Google, Mozilla and Windows push providers.

## Build and deployment

```sh
python3 Tools/WebRelease/build.py --multiplayer --learned-ai
cd Web
npm ci --ignore-scripts
npm test
npm run deploy:access
npm run deploy:multiplayer
# Install the private VAPID_PUBLIC_KEY and VAPID_PRIVATE_KEY secrets on
# blocknations-multiplayer using Wrangler's secret workflow.
npm run deploy:preview
# After actual browser acceptance, publish the same generated artifact:
npm run deploy:production
```

The account build uses `BLOCKNATIONS_ACCOUNT_WEB`. The explicit `--learned-ai` release option also enables `BLOCKNATIONS_LEARNED_AI_WEB`, includes only the frozen release actor, requires the three-unit rules and uses its evaluated fixed probability sampling. New Play vs AI games have one opponent and no difficulty selector. Omitting this option reproduces the legacy account build without an actor. Both paths remove native PBp credentials, trainer code, training scenes/models and Unity AI Assistant from the isolated snapshot. This authorization covers the selected release; it does not authorize automatically replacing the live actor whenever training writes another checkpoint.

Keep VAPID keys, session secrets, Cloudflare tokens, build logs, browser fixtures and rollback receipts outside Git. On the release machine, the VAPID key file is `~/.config/blocknations/push.json`, directory mode 700 and file mode 600. Keep that key pair stable across releases so existing device subscriptions remain valid. Retain the existing shared admission `SESSION_SECRET`; multiplayer does not require rotating it or creating another Google OAuth client.

Before publication, record Worker versions and the staging mapping, audit the exported/decompressed bytes against known app credentials, and test two independent browser sessions. Preview and production must receive the same accepted game asset bytes. The staging service at `staging.blocknations.moneymattersmedia.com` remains a separate target.

## Validation evidence

Validation uses synthetic account identities and isolated SQLite for automated scenarios; no test invitations or emails are sent to real friends. The Node suite covers concurrent queue/seat reservation, concurrent turn writes, idempotent retries, stale conflicts, account turn ownership, reconnect, initialization, cancellation, social requests, invitation acceptance, exact-email privacy, preview isolation and the existing admission gate. Push payloads are decrypted and signatures verified by independent implementations, with integration checks for queued delivery, retries, expired devices and account/session revocation.

Desktop and 390×844 browser acceptance exercises the actual Unity export, lobby and service worker. Physical iPhone/Android notification delivery and Safari remain separate device checks. Existing Unity PBp compatibility, single-player save eligibility and multiplayer scroll tests provide regression coverage for the shared app code.

The initial release validation on 2026-10-11 (Hong Kong time) passed 52 Node checks, 16 build/audit checks and 26 Unity Editor regression checks. Two independent browser accounts recruited units, alternated turns, saved a unit movement, reopened the accepted board, accepted a friend request and a private match invitation, and resigned. Reopening the finished match retained the same sequence and revision. Single player completed a Normal AI turn and returned control for round two. The Cloudflare preview also passed Google-account matchmaking, recruitment, turn submission, reopening the saved turn and cancellation of its waiting test match. A synthetic browser push event displayed a notification; this does not establish push-provider delivery on a physical phone.

### Web Push transport repair: 2026-10-13

An iPhone report that neither turn reminders nor test notifications arrived exposed a server transport failure. The deployed `sendPush` called Workers' native `fetch` as an object method, `(env.PUSH_TEST ?? { fetch }).fetch(...)`. Cloudflare's runtime rejected the incorrect receiver with `Illegal invocation` before contacting a push provider. The outbox caught that exception and retried before eventually dropping the notification; the test button only reported that it was queued.

`sendPush` now calls native `fetch` directly while retaining the injected test-provider method and ten-second timeout. A new Miniflare regression exercises the native send path, independently decrypts the outbound Apple payload, and checks provider statuses 201, 410 and 503. It fails against the original implementation and passes with the fix. All 72 web checks passed. Previous encryption/signature runtime tests stopped at request construction, while outbox tests injected a provider; neither exercised native `fetch`.

Only the private `blocknations-multiplayer` Worker was deployed, as version `dc0570c3-2d82-444e-b592-6d8e7ee2d3bd`, replacing `7261446f-b8dc-469b-8be3-e6e199ba63a4`. The active deployment was verified at 100%, and the live bundle matched the tested build byte-for-byte. Both push secrets and the existing Durable Object namespace were preserved. The game Workers and Unity assets were not redeployed. Anonymous production session/release requests remained 403; notification shell scripts remained available. Physical iPhone notification receipt still requires device acceptance.

## Social account review and sharing

Approved players can find registered accounts by Google display name, partial email username before `@`, or an exact full email. Search results and social request reviews show verified addresses to distinguish identical display names. The review shows the actual signed-in sender and selected recipient before sending. Email is the account lookup identifier; stable Google-derived IDs continue to own friendships, seats and saved matches. Emails are excluded from Unity sessions and game snapshots.

Outgoing pending match invitations offer the native Share invite sheet where supported, plus a WhatsApp message link fallback. The player chooses the app/chat and sends the prepared message. No automated WhatsApp delivery or email is sent. Shared links open `/multiplayer`, retaining that destination through Google login and approval. They grant no access or seat; only the invited account can accept in its inbox. The game menu has one Multiplayer button. The web footer shows Account in the main menu and collapses during gameplay, giving its space back to the game canvas. Returning to the main menu restores Account.

## Compact multiplayer match list (2026-10-13)

The web lobby now follows the approved mobile mockup: active matches belonging to the viewer's current turn appear first with cyan highlighting and **Play**. Other active matches use grey rows and **View**. Lobbies have a separate section, and finished matches are collapsed by default. Turn presentation uses `seatIndex` and `currentTurnSeatIndex`, including before the initial snapshot exists; an unstarted lobby is never shown as a playable turn.

**New game** opens the existing two-, three-, or four-player public/private creation controls. Match details expose colored seats, host identity, friend/invitation controls, and the existing host-only start/cancel actions. Incoming invitations remain visible in a separate disclosure; an empty invitation section is hidden. Refresh retains the selected tab, finished-list disclosure, and match-details state.

The implementation changes presentation only, with no save, protocol, transport, turn progression, or admission changes. Local verification uses synthetic matches and accounts, plus the web regression suite; it does not establish physical iPhone acceptance or live deployment.
