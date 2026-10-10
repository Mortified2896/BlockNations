# Player feedback

The main-menu panes display the build version at the top left. Both menu and normal gameplay have a Feedback button at the top right, inside the safe area. Gameplay does not display a version label; reports attach the version automatically.

Tap the menu version to copy the full displayed identifier, including the PBp protocol (for example, `v1.0.3 · PbP 5`). The label shows `Copied!` for two seconds, then returns to the version. Browser builds wait for clipboard confirmation; a rejected copy shows a retry message. When checking a candidate, verify the pasted text, repeat taps, label restoration, and leaving/reopening the menu. Check physical mobile browsers separately.

## Player flow

Tapping Feedback captures the rendered game view before opening the form. The browser reads the game canvas immediately after a Unity render, preserving the displayed colors; Editor/native builds use Unity screenshot capture. The screenshot contains only the reporting player's visible screen and UI. It does not serialize the board, save, hidden information, PBp credentials, or logs.

The browser form uses native HTML controls for keyboard entry, mobile keyboards, focus trapping, and scrolling. Editor/native builds use the shared UITK form. The player chooses Bug, Suggestion, or Other and writes a description. The form displays a screenshot preview, an Include screenshot toggle, and Remove screenshot. Capture failure still permits a text report. Images are reduced to at most 1280 pixels on the longest side and encoded as JPEG; the client attachment limit is 1 MB.

Send freezes the report while the request is in flight. A confirmed receipt displays Feedback sent and a Done button. Failed requests retain the text and attachment for retry. Exact retries reuse the report ID; the first accepted write wins. Editing a previously attempted report gives it a fresh ID. Written drafts are saved locally and restored on reopening/reload; screenshots remain in memory and are recaptured on reopening. Cancel retains the written draft, while success clears it.

The current submission path is the signed-in browser playtest. Editor/native builds can inspect the form and capture screenshots, but submission is disabled with an explanation until native tester authentication is designed. Native builds do not embed browser cookies or service secrets.

## Submission and administration

The browser posts JSON to the same-origin `/api/feedback` route. Existing tester authentication applies; only approved testers may submit. The route checks the request origin, report fields, body size and JPEG attachment. Each tester can create up to ten reports per hour; retries of saved reports do not consume another write.

Reports are stored privately in the access service's SQLite-backed Durable Object, in an additive `game_feedback` table separate from tester admission, sessions, saves and PBp state. Preview and production share the private store, and each report records its source origin. No new cloud storage binding or credentials are required. The 1.5 MB request cap also keeps rows below Cloudflare's [2 MB SQLite row limit](https://developers.cloudflare.com/durable-objects/platform/limits/). Existing authentication and PBp formats are unchanged.

Approved Review administrators can open `/admin/feedback` from the account or tester-management page. The inbox shows category, player display name, text, build, platform, screen, mode, turn, timestamp and an optional screenshot preview. Images are served through an administrator-only route, with private/no-store caching. Reports are retained until an explicit retention or deletion policy is implemented; there is no public report endpoint or automatic email notification.

## Validation and release checklist

- Run `npm test --prefix Web` for authenticated submission, rejected origins, invalid/oversized reports, text-only reporting, hourly limits, duplicate prevention, escaped report text, and administrator-only image access.
- Build an isolated credential-free browser artifact with `Tools/WebRelease/build.py`; use a separate output directory when validating a candidate so an existing release artifact is preserved.
- In menu and gameplay, inspect the button/version placement at desktop and phone sizes. Verify ordinary board input still works and the open form blocks taps underneath it.
- Verify the attached screenshot shows the screen before the form appeared, including the UI, and does not include the feedback dialog.
- Submit with and without a screenshot. Check the receipt, inbox and image. Simulate a failed acknowledgement, retry and check that the inbox still contains one report.
- Check that Cancel/reopen and reload restore written drafts; success clears them. Verify capture failure still allows a text report.
- Check physical iPhone Safari and Android Chrome separately before claiming device acceptance.

Publishing requires updating the access service (which owns the new table), the game Worker and the reviewed Unity web artifact together. An implementation/source push does not publish the feature to Cloudflare.

## Validation performed on 2026-10-10

All 22 access/feedback tests passed with the latest account-switching changes included. Unity 6000.4.0f1 produced the isolated WebGL candidate successfully; the credential audit found zero matches. Both the private access service and production game Worker passed Wrangler dry-run bundling; nothing was deployed.

Chromium desktop and a 390 x 844 phone-sized viewport were inspected. The complete Unity button-to-browser-form flow saved distinct menu, text-only and gameplay reports in the actual local Worker/SQLite store using synthetic approved identities. Received screenshots retained the rendered colors and HUD, omitted the dialog, and stayed within 1280 pixels. The private inbox loaded both images. Draft cancellation/reopening and page reload preserved written text; successful sends cleared it. A simulated failed acknowledgement retried the identical payload and produced one stored report. The open form blocked an attempted tap on the underlying gameplay controls; ordinary board selection worked after closing it.

Physical iPhone Safari, Android Chrome, native submission and live Cloudflare acceptance remain separate release checks.

Menu version copying was validated in an isolated WebGL candidate on 2026-10-10. Chrome displayed `Copied!`, pasted the exact `v1.0.4 · PbP 5` identifier, restored the version label, and accepted a repeat click. Six clipboard-bridge checks covered asynchronous confirmation, rejected writes, successful/failed legacy fallback, and synchronous exceptions. All 14 responsive/menu EditMode cases passed in the isolated project without Unity AI Assistant; runs in the main checkout were interrupted by that package's unrelated connection error. The candidate build succeeded and its credential audit found zero matches. This validation did not deploy the candidate or establish physical mobile acceptance.
