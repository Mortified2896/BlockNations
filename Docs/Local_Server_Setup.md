# Block Nations Local Server Setup

Run the authenticated Node/Express PBp relay for local development. The supported entry point is `server.js` through `npm start`. For external hosting, see [VPS deployment](VPS_Deploy_MVP.md); for endpoint shapes, see [the HTTP contract](HTTP_PBp_Transport_Contract.md).

Last reviewed: 2026-10-07.

## Requirements

- Node.js and npm. The current ESLint lockfile requires Node `^20.19.0 || ^22.13.0 || >=24` for development checks.
- Port 8080 available, or a different `PORT` configured.
- A local development shared secret supplied to both server and clients.
- Unity 6000.4.0f1 for the client project.

## Install and configure authentication

From the checkout:

```bash
cd /Users/Jo/GitHub/BlockNations
npm ci
mkdir -p UserSettings
```

Place a chosen development-only secret in `UserSettings/pbp-api-key.default` using your local editor or secret manager. This ignored file is the default Editor credential source for URLs other than the designated staging URL. It must match the server's `PBP_SHARED_SECRET`; do not put the value in Git, screenshots, or copied terminal output.

Load the ignored file into the server process environment without printing it:

```bash
export PBP_SHARED_SECRET="$(cat UserSettings/pbp-api-key.default)"
npm start
```

Default listener: `0.0.0.0:8080`. Optional `HOST` and `PORT` environment variables select another address/port. Without a shared secret, the process can start and health can pass, but protected PBp requests return 401.

## Configure clients

In the development checkout, set `Assets/Resources/PbpTransportSettings.asset` → `playByPostBaseUrl` through the Inspector:

- Same-machine Editor: `http://127.0.0.1:8080`.
- LAN device: `http://<development-machine-LAN-IP>:8080`.

On macOS, `ipconfig getifaddr en0` can identify the Wi-Fi interface's address; use the active interface if different. A device's `127.0.0.1` points to that device, not the development machine.

Restore the shared URL after local testing rather than committing a LAN address. Client credentials depend on build/platform: Editor environment/project-file overrides, macOS standalone provisioning, and mobile release settings are described in [VPS deployment](VPS_Deploy_MVP.md). A release build containing the live credential will not authenticate to a server using a different local secret; use the appropriate development provisioning.

## Check health and authenticated access

In a separate terminal:

```bash
cd /Users/Jo/GitHub/BlockNations
curl --fail --silent --show-error http://127.0.0.1:8080/healthz
```

Expected response: `{"ok":true}`. This proves listener availability, not PBp authentication or save compatibility. Verify protected access through the game's server-status/create/join flow without exposing the credential in diagnostic commands.

## Local playtest

1. Start the relay and leave its terminal running.
2. Enter a recognizable typed name in each client's Profile before Multiplayer.
3. Create a PBp game using a currently supported 2–4-seat setting and share its join code.
4. Join from another client, claiming an available seat.
5. Submit a turn and verify that the next eligible participant receives it with correct local POV and turn ownership.

Clients must use compatible app/protocol versions and matching server credentials. For this direct LAN setup, devices need connectivity to the development machine, an open firewall path, and platform network permissions. HTTPS through an external proxy/tunnel is separate hosting work.

## Persistence and stopping

Turns and claims are stored under `data/PlayByPost/Turns/<game-id-hash>/`, ignored by Git. Do not delete that data as documentation cleanup.

Stop the foreground relay with Ctrl+C. Restarting preserves stored data; it resets in-memory rate-limit buckets.

## Troubleshooting

- Failed health: check the listener, selected address/port, and firewall.
- Health succeeds but PBp reports unauthorized: check the server environment and the actual platform-specific client credential source.
- Device cannot connect: check LAN reachability and the device URL; do not use Editor loopback on a phone.
- Version rejection: see [compatibility policy](PBp-Compatibility.md).
- Conflicting simultaneous submissions: the [known relay overwrite bug](Development_Followups.md) is unresolved. Fix it before renewed online playtesting.
