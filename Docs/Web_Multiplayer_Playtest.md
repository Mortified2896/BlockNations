# Google-account web multiplayer playtest

The Cloudflare playtest at `https://blocknations.moneymattersmedia.com/multiplayer` supports two people on the standard 7×7 board. The owner approved asynchronous saved turns, invitations to registered players by Google display name or exact email, in-game invitations without email delivery, and publication after validation. The web release keeps Normal, Rider Focus and Hard available for single player. The separate learned-AI candidate remains on hold for review.

## Player flow

1. Sign in with Google. The existing tester approval gate still applies; friends must sign in and receive approval before they can play or appear in search.
2. Choose **Multiplayer** in the game menu or below the canvas.
3. **Find online match** joins the oldest open public match, or creates one for the next person to join. Repeated finds reuse the same waiting match. Waiting matches remain joinable until cancelled.
4. Play and end the turn. The accepted turn snapshot is stored online; either player can reopen the match from **Your matches** on another browser or device signed into the same Google account. Completed turns are the cloud save boundary. Actions within an unfinished turn are not synchronized across devices.
5. Use **Add friend** beside a match opponent, or search for a registered player's name or exact email. The recipient accepts the friend request. **Invite to match** creates an in-game request; **Accept & play** opens a private two-person match with the sender in seat zero. No email is sent, and searches return names and opaque player IDs without revealing account email addresses.

The visible lobby refreshes every ten seconds. A waiting game can be cancelled before the second player joins. Requests can be declined or cancelled, and either person can remove a friendship. Resignation uses the existing game rules and is available on the resigning player's turn. Finished matches remain readable and do not resubmit their terminal state when reopened.

## Separate identity and transport

The existing Google admission Worker verifies the current account for every protected game/API request. It derives a stable opaque player ID from the verified Google identity and provides the Google display name. Browser input and old app seat claims cannot select an identity or reserve another person's seat. The random name/title generator stays implemented for the app and is not used by the account web release.

`Web/multiplayer/store.mjs` owns a private SQLite Durable Object, deployed as `blocknations-multiplayer` without public URLs or routes. Origin-scoped objects isolate production and preview matches, friendships, invitations and device subscriptions. The game Workers hold cross-script bindings to this private namespace. Admission/search stays in the existing `blocknations-access` service; Google credentials and Review cookies stay in their existing private services.

The new relay reserves seats and commits updates atomically. It checks match membership, current turn ownership, base sequence, board/protocol shape and app compatibility. Identical retries are idempotent; stale tabs receive a conflict and reload the accepted snapshot. Public queue matches and private invitation matches have separate creation paths. Match snapshots are still simulated by the Unity clients, as in the app PBp relay; this internal playtest is not a server-authoritative anti-cheat implementation.

`WebAccountClient` and `WebAccountTurnTransport` use same-origin `UnityWebRequest` calls with the existing encrypted, HttpOnly game cookie. No cookie value, OAuth token, email address or shared app PBp credential is given to the Unity client. Google names support spaces and Unicode, and labels render them as plain text. The web build disables the native share-code/reminder UI because membership and invitations are handled in the lobby.

The existing protocol-5 Unity snapshot and turn export/load paths are reused. The native relay, protocol-4 migration support, supported app imports, scene/prefab wiring and app save formats are preserved. This first web queue's two-person setting is a playtest choice, not a permanent player/team architecture limit. Incompatible future web changes must advance `WEB_MATCH_VERSION` and the matching client constant so new players cannot join old incompatible queues.

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
python3 Tools/WebRelease/build.py --multiplayer
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

The account build uses `BLOCKNATIONS_ACCOUNT_WEB`. It removes native PBp credentials, training code/models and the unapproved learned-AI resource from its separate build snapshot. Without `--multiplayer`, the existing learned single-player candidate build remains available and is still held for review. Do not publish it merely as part of this multiplayer rollout.

Keep VAPID keys, session secrets, Cloudflare tokens, build logs, browser fixtures and rollback receipts outside Git. On the release machine, the VAPID key file is `~/.config/blocknations/push.json`, directory mode 700 and file mode 600. Keep that key pair stable across releases so existing device subscriptions remain valid. Retain the existing shared admission `SESSION_SECRET`; multiplayer does not require rotating it or creating another Google OAuth client.

Before publication, record Worker versions and the staging mapping, audit the exported/decompressed bytes against known app credentials, and test two independent browser sessions. Preview and production must receive the same accepted game asset bytes. The staging service at `staging.blocknations.moneymattersmedia.com` remains a separate target.

## Validation evidence

Validation uses synthetic account identities and isolated SQLite for automated scenarios; no test invitations or emails are sent to real friends. The Node suite covers concurrent queue/seat reservation, concurrent turn writes, idempotent retries, stale conflicts, account turn ownership, reconnect, initialization, cancellation, social requests, invitation acceptance, exact-email privacy, preview isolation and the existing admission gate. Push payloads are decrypted and signatures verified by independent implementations, with integration checks for queued delivery, retries, expired devices and account/session revocation.

Desktop and 390×844 browser acceptance exercises the actual Unity export, lobby and service worker. Physical iPhone/Android notification delivery and Safari remain separate device checks. Existing Unity PBp compatibility, single-player save eligibility and multiplayer scroll tests provide regression coverage for the shared app code.

The initial release validation on 2026-10-11 (Hong Kong time) passed 52 Node checks, 16 build/audit checks and 26 Unity Editor regression checks. Two independent browser accounts recruited units, alternated turns, saved a unit movement, reopened the accepted board, accepted a friend request and a private match invitation, and resigned. Reopening the finished match retained the same sequence and revision. Single player completed a Normal AI turn and returned control for round two. The Cloudflare preview also passed Google-account matchmaking, recruitment, turn submission, reopening the saved turn and cancellation of its waiting test match. A synthetic browser push event displayed a notification; this does not establish push-provider delivery on a physical phone.
