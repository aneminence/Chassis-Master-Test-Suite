# Smart LAN Windows implementation and audit (2026-11-09)

## Existing baseline
- Baseline 93ba0d2: remote WS hello/token, sample subscription and manual connection already implemented.
- Existing reconnect used fixed Host and retried authentication failures; no discovery or identity pinning.
- Existing preferences held only one successful endpoint and plaintext token.

## Increment implemented
- CMTSLanDiscovery: bounded gateway/history candidates plus DNS-SD candidates; all require correlated device.discover response, compatible version, expected endpoint/type and authRequired.
- Discovery carries no credential. Existing device.hello authorization remains mandatory.
- Dialog automatically searches on opening, supports refresh/cancellation, verified results, known-device priority and manual fallback. Selecting a previously paired ID restores its saved token; new IDs clear token.
- History bounded to 20 identities; successful connections only.
- Reconnect rediscovery by stable ID; actual hello identity must match selected/previously connected ID. Auth/protocol failures stop retrying. Connect and discovery operations have deadlines.
- Backoff resets after a connected session drops. Local recording behavior untouched.

## Review / known limits
- IPv4, default gateway probe port 8787, fixed mDNS path /cmts/v1. Custom port manual entry remains supported.
- DNS-SD is untrusted discovery, not cryptographic identity authentication. ws:// and inherited plaintext preferences retain LAN trust boundary; do not claim MITM protection.
- First pairing uses existing token copied from phone, not an unsolicited approval dialog.
- History prioritizes currently discovered known IDs; offline histories are not advertised as online.
- Windows QR scanning intentionally outside scope.
- No GNSS/IMU/vehicle channel, reset, chart, recorder or GSpot parsing changes.

## Verification
- Debug compile succeeded using separate output directory because user's running app locks normal Debug exe; existing NU1701 SkiaSharp warning.
- Unit test sources include malformed/correlated discovery response and mDNS packet parsing; full test execution passed 176/176 outside sandbox (parent validation).
- Real hotspot/no-cellular, USB GNSS, lockscreen, repeated network switches and long duration acceptance pending hardware testing.

## 2026-11-10 follow-up
- Discovery list moved above manual fields; fixed footer keeps connection actions/status visible while fields scroll. Resizable window respects desktop work area.
- Selection gives explicit feedback; double-click attempts connection. Missing token focuses the token field and explains first pairing without closing dialog or bypassing authorization.
- Optional deviceModel displays model alongside user label; absent field keeps old-device compatibility.
- Debug build to separate smart-lan-win-v2 directory passed (existing NU1701 warnings); full test suite 180/180 passed outside sandbox.
- UI/device field validation on real phone pending parent integration.

## Latest product decision: personal LAN direct connection
Supersedes token-required UX descriptions above for current Android builds. User explicitly requested direct double-click connection without token or phone approval. Discovery now accepts authRequired=false, clears any saved credential for these devices, and proceeds with existing hello. Old authRequired=true devices retain advanced manual credential fallback. No device.pair implementation remains. Main list shows only product model and IP; technical fields are collapsed under advanced manual connection. v3 Debug build and 180/180 tests passed. Open LAN access is not authenticated identity or encrypted transport.
