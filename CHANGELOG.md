1.0.2
- Fix the layout of changelog.md & mainfest.json
- Fixed issues related to ID drift.
- Removed the EnableDebugLogs configuration option.

1.0.0
- Breaking Change: Merged the Chinese and English editions into a single release for easier maintenance; language can now be selected in config and takes effect after restart.
- Added: New language config option to switch between Chinese and English (`Language`).
- Changed: Config file name is now `chuxiaaaa.Aiae.BetterPeakVoiceFix.cfg` to avoid conflicts with older versions.
- Changed: Unified `warn_majority` threshold with majority detection at `>=2`.
- Changed: Removed the "Enable ID Drift Fix" config option; the related fix logic remains enabled by default.
- Changed: Unified the `Alt+K` force-reconnect path to use the same decision chain: Majority (>=2) -> Follow Host -> Blind Connect.
- Fixed: `Reconnect Timeout (ConnectTimeout)` is now fully effective.
- Fixed: Room-scoped cache state (PlayerCache/SOS/HostHistory, etc.) is now cleared after leaving a room to avoid stale cross-room data affecting later decisions.
- Fixed: Added null safety for `Alt+K` when the voice client is not ready, preventing null reference errors.
- Fixed: Corrected SOS list cleanup for unresolved names; the previous `Unknown` branch was effectively unreachable in most cases.
- Improved: Improved overall code readability.

0.3.6
- Fixed: Resolved a crash where the UI could throw `NullReferenceException` every frame during startup/reconnect before the voice client had joined a room.
- Fixed: Resolved a crash caused by `PhotonNetwork.CurrentRoom` potentially being null in "Isolated" state checks.
- Fixed: Resolved a crash caused by `PhotonNetwork.CurrentRoom` potentially being null in SOS list management.
- Added: Fully implemented the "Auto-hide Simple UI" config option (the option existed before but had no effect).
- Added: `joinTimes` now cleans up entries for players who have left every 60 seconds to prevent stale data buildup in long sessions.
- Improved: Reformatted parts of compressed code to improve readability.

0.3.5

- Renamed "Show Detailed IP Option" to "Show Connected Voice Server IP and Details".
- Improved IP-related wording to avoid the previous "local IP" phrasing that could be mistaken as exposing personal IP.
- Because option names changed, users upgrading from older versions are advised to delete the old config file:
  `...\PEAK\BepInEx\Config\chuxiaaaa.Aiae.BetterVoiceFix.cfg`.
- Updated `README.md` documentation.

0.3.4 Released

