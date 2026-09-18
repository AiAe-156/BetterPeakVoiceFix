# 1.1.2

## 中文

### 新增

- Steam 邀请加入时，房间名改按 UTF-8 读取（原版为 ASCII）：安装 BetterRoomShare 的房主发出的中文房名可以完整接收并正常加入。仅影响本机接收的房间名，不修改发送协议、其他玩家数据或游戏区服，原版邀请行为不变。
- 目标读取已是 UTF-8（例如同时安装 BetterRoomShare）时补丁保持原样，不会重复修改；目标方法签名或结构与原版不符时不做替换、只记录一次警告，其余功能不受影响。

### 更改

- 详细面板取消水平自动增宽：宽度上限 560（受屏幕与边距约束），长文案自动换行。
- 房间码与语音房名各自独占一行完整显示；语音房名严格比对「房间码 + `_voice_`」，不一致时标红提示，信息缺失时提示待确认，不再把缺失当作匹配。
- 面板区服改用实际连接数据，缺失显示"未知"，移除"未收录（多为港服）"的猜测性标注。
- 玩家名字按实际列宽截断，宽度不足时延迟另起一行；通知、历史、SOS、区服说明等动态名称一律按字面文本渲染，不影响联网数据。

### 说明

- 旧版房主已把中文房名变成问号的无法恢复；BetterRoomShare 房主与未装本模组的纯原版客机之间的兼容不在本补丁范围。

## English

### Added

- Steam invite join now reads the room name as UTF-8 (vanilla used ASCII): Chinese room names sent by a host running BetterRoomShare are received intact and can be joined. This only changes how the local client receives the room name — the send protocol, other players' data, and game regions are untouched, and vanilla invite behavior is unchanged.
- If the target read already uses UTF-8 (e.g., BetterRoomShare is also installed), the patch leaves it as-is without re-patching. If the target method's signature or structure no longer matches vanilla, nothing is replaced and a single warning is logged while the rest of the mod keeps working.

### Changed

- The detailed panel no longer grows horizontally: width is capped at 560 canvas units (constrained by screen and margins) and long text wraps.
- Room code and voice room name now each occupy their own line in full. The voice room name is strictly compared against `room code + "_voice_"`: mismatches are highlighted in red, and missing data is shown as unconfirmed instead of being treated as a match.
- Panel region display uses actual connection data and shows "Unknown" when missing, removing the speculative "unlisted (usually hk)" label.
- Player names are truncated to the real column width and the latency moves to its own line when space is tight. Dynamic names in notifications, history, SOS and region text are rendered as literal text without affecting networked data.

### Notes

- Room names already mangled into question marks by an older host cannot be recovered. Compatibility between a BetterRoomShare host and a pure-vanilla client without this mod is out of scope.

# 1.1.1

## 中文

### 修复

- 修复离线模式（单机/本地单人）下语音面板永远显示"初始化中..."的问题：离线模式本质是本地伪房间，游戏刻意不连接语音服务器，面板现在只显示一行"离线模式"，且不再被异常规则强制常显。
- 修复在机场按 J 打开的语音面板被带进海滩、雪山等游戏场景持续显示的问题：手动钉住的简易面板在离开机场或离开房间时自动还原为自动显隐规则；详细面板离开机场时立即关闭（常驻显示仅限机场）。
- 修复离线模式下 Alt+K 手动重连仍会对语音客户端发起真实连接的问题。
- 修复离线模式下"本机延迟"显示 0ms 假数据的问题。

## English

### Fixed

- Fixed the voice panel being stuck on "Initializing..." forever in offline mode: offline mode uses a local dummy room where the game intentionally never connects voice, so the panel now shows a single "Offline mode" line and is no longer force-pinned by the problem-detection rule.
- Fixed the voice panel opened with J in the airport staying visible in gameplay scenes (beach, alpine, etc.): a manually pinned simple panel now reverts to automatic visibility rules when leaving the airport or the room, and the detailed panel closes immediately when leaving the airport (persistent display is airport-only).
- Fixed Alt+K manual reconnect still initiating a real voice connection in offline mode.
- Fixed the local ping line showing a fake "0ms" in offline mode.

# 1.1.0

## 中文

### 新增

- 新增房间区服选择功能（仅供调试，正常情况下不建议使用）、区服状态查看和各区延迟测试；当指定房间区服无法连接或延迟更高时，会显示明确提示。
- 新增房主侧的新玩家语音接入提示；连接失败或玩家模组版本过旧时，会提供更明确的原因和处理建议。
- 专业信息新增多数派语音区服、对应的语音服务器 IP，以及使用该区服的玩家人数。

### 更改

- 改善房间码加入流程：修复香港区房间码可能被错误识别为美国区的问题；当房间码要求切换区服时，会等待新区服连接完成后再加入房间，避免因切换尚未完成而入房失败。

- 改善 Steam 邀请加入流程：首次没有收到房间信息时会每 8 秒自动重试，最多重试 4 次；仍然失败时会提示房主可能仍在菜单、加载中或已经掉线，也会提示可能存在区服连接问题，并给出重新邀请或改用房间码的建议。

  > 注：以上两项本不属于本模组的处理范围，但对正常加入游戏的影响较大，因此增加了对应修复，很难想象官方会犯这种错。

- 重构部分代码以提升语音连接与自动重连的稳定性，改善跨区房间、场景切换及手动重连后的语音问题。

- 重新设计语音状态面板。按 J 可依次切换“关闭 → 简易 → 详细”，简易状态只在机场常驻显示（可关闭）。

- 详细面板现在会显示房间区服、语音区服、语音服务器、房间与语音延迟、全员同步情况，以及每名玩家的语音连接状态。

- 提升玩家加入、离开和重连后的状态判断准确性，并更清楚地区分连接中、跨区、断开和未知状态。

- 优化面板间距、文字标签、玩家排列和默认布局，使界面更加紧凑、清晰。

### 移除

- 移除详细面板中重复显示的本机语音状态、语音人数、本机延迟和测速缓存信息；必要的状态与延迟仍会保留在对应玩家行中，区服测速信息则可通过 Alt+J 的“区服状态”查看。

## English

### Added

- Added game-region selection (for debugging only; not recommended for normal use), region status information, and latency tests for all available regions. Clear warnings are shown when the selected game region cannot be reached or has higher latency.
- Added host-side notifications for newly joined players' voice connection results. Connection failures and outdated mod versions now provide clearer causes and suggested actions.
- Professional information now shows the majority voice region, its voice server IP, and the number of players using that region.

### Changed

- Improved room-code joining: fixed Hong Kong room codes being incorrectly identified as US rooms. When a room code requires switching regions, the mod now waits for the new region connection to finish before joining, preventing failures caused by joining too early.

- Improved Steam Invite joining: if room information is not received, the request is retried automatically every 8 seconds, up to 4 times. If all attempts fail, the message explains that the host may still be in a menu, loading, or disconnected, or that the host's region may be unreachable, and suggests sending another invite or using a room code.

  > Note: The two fixes above are outside the intended scope of this mod, but they have a major impact on joining games, so corresponding fixes were added. It is difficult to imagine the official game making mistakes like these.

- Refactored parts of the mod to improve voice connection and automatic reconnection reliability, especially in cross-region rooms, after scene changes, and when using manual reconnect.

- Redesigned the voice status panel. Press J to cycle through “Hidden → Simple → Detailed”. Simple mode remains visible in the airport by default and can be disabled in the settings.

- The detailed panel now shows the room region, voice region, voice server, room and voice latency, overall synchronization status, and each player's voice connection state.

- Improved player-status accuracy after players join, leave, or reconnect, with clearer distinctions between connecting, cross-region, disconnected, and unknown states.

- Refined panel spacing, labels, player ordering, and default layout settings for a more compact and readable display.

### Removed

- Removed repeated local voice status, voice-player count, local latency, and latency-cache information from the detailed panel. Required status and latency information remains in the corresponding player rows, while region test information remains available through “Region Status” in the Alt+J console.

1.0.5
- Fixed the recurring status misattribution after a player rejoins: the game room and the voice room are separate Photon rooms with independently numbered actors, so actor-number matching could pin [Connected] on the wrong player once reconnects made the IDs drift (e.g. 2 -> 5 -> 8).
- Voice-room membership and ghost detection are now anchored to the game's own PhotonVoiceView speaker link (voice stream <-> game player), with UserId as a secondary match; same-number actor matching is only a last resort. Works purely client-side — other players do not need the mod.
- No UI changes: statuses, colors, and counts look the same, only the underlying matching is more accurate.

1.0.4
- Voice status now understands mixed-mod lobbies: each player is judged by whether they actually run the mod, so a modded-but-disconnected player is no longer mislabeled as "no mod".
- Non-modded players are inferred from the voice room — [Connected], [Mismatched] (in voice but ID-drifted), or [Disconnected].
- Fixed the false "Isolated" status and the contradictory "all Synced yet N mismatched": rows and counts now come from one consistent check.
- Players who leave are removed from the list immediately instead of lingering as "Left".
- The simple overlay can auto-hide again in mixed-mod lobbies.

1.0.3
- Performance: removed several per-frame scene scans and reduced overlay overhead (it now redraws only when its content changes) — lower CPU/GC cost, mainly in menus and during voice trouble.
- No feature or gameplay changes.

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

