using BepInEx;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using Photon.Voice.PUN;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PeakVoiceFix
{
    public class VoiceUIManager : MonoBehaviour
    {
        public static VoiceUIManager Instance;
        public TextMeshProUGUI statsText;
        private Canvas myCanvas;
        private bool showDebugConsole = false;
        private Rect debugWindowRect = new Rect(20, 20, 600, 400);
        private Vector2 debugScrollPosition;
        private Vector2 filterScrollPosition;
        private struct LogEntry { public string Time; public string Player; public string Msg; public bool IsLocal; }
        private List<LogEntry> debugLogs = new List<LogEntry>();
        private int logFilterMode = 0;
        private int targetActorNumber = -1;
        private bool isResizing = false;
        private Rect resizeHandleRect;
        private Dictionary<int, float> joinTimes = new Dictionary<int, float>();
        private string currentSceneName = "";
        private string lastStatsText = null;
        private readonly StringBuilder _sb = new StringBuilder(512);

        private const string C_GREEN = "#90EE90";
        private const string C_PALE_GREEN = "#98FB98";
        private const string C_YELLOW = "#F0E68C";
        private const string C_RED = "#FF6961";
        private const string C_LOW_SAT_RED = "#CD5C5C";
        private const string C_TEXT = "#dfdac2";
        private const string C_GREY = "#808080";
        private const string C_GOLD = "#ffd700";
        private const string C_GHOST_GREEN = "#b2d3b2";

        private float nextUiUpdateTime = 0f;
        // 显示模式三态，J 键循环：关闭 → 简易 → 详细 → 关闭。
        // 不再用"按一下亮 10 秒"的临时窗口——那种设计下用户没法把面板固定住看。
        private enum DisplayMode { Off, Simple, Detail }
        private DisplayMode displayMode = DisplayMode.Simple;
        // 简易态同时承担“机场/异常时自动出现”和“玩家按 J 主动打开”两种用途。
        // 单独记住主动打开，避免非机场一切正常时第一次按 J 被自动隐藏规则吃掉。
        private bool simpleManuallyShown = false;
        // 场景/房间边沿检测：机场里按 J 钉住的简易面板不带出机场；离开房间则手动状态整体作废。
        private bool wasInAirport = false;
        private bool wasInRoom = false;
        private bool isDetailMode => displayMode == DisplayMode.Detail;
        private float detailExpiry = 0f;

        private static string ModeName(DisplayMode m)
        {
            switch (m)
            {
                case DisplayMode.Simple: return L.Get("mode_simple");
                case DisplayMode.Detail: return L.Get("mode_detail");
                default: return L.Get("mode_off");
            }
        }

        /// <summary>机场里且开了「机场常驻简易UI」时，详细态不限时——那种场合人就是在盯面板看。</summary>
        private bool DetailShouldPersist()
        {
            bool airportPersist = VoiceFix.AutoHideNormal != null && VoiceFix.AutoHideNormal.Value;
            return airportPersist && currentSceneName == "Airport";
        }
        private float lastFontRetryTime;
        private string notificationMsg = "";
        private float notificationExpiry = 0f;

        // 区服警告：强制区服连不上时要在主菜单也能看到，所以独立于 notificationMsg（后者只在房内显示）。
        private string regionWarnMsg = "";
        private float regionWarnExpiry = 0f;

        public void SetRegionWarning(string msg)
        {
            regionWarnMsg = msg;
            regionWarnExpiry = Time.unscaledTime + 25f;
        }
        // 每个玩家的最终结论。Local=本机；其余对应 [] 里的标签。
        private enum Verdict { Local, Synced, Abnormal, Disconnected, Connecting, Connected, Mismatch }
        private struct PlayerClass
        {
            public int ActorNumber; public string Name; public string IP; public int Ping;
            public bool IsLocal; public bool IsHost; public bool IsMod; public byte RemoteState;
            public Verdict Verdict; public bool IsConnecting;
            public string VoiceRegion;   // 语音客户端所在区服；null = 不知道（没装 mod 或老版本）
            public int VoicePing;        // 语音连接延迟；-1 = 未知
            public string ModVersion;    // 用于"版本过旧"提示
            public bool CrossRegion;     // 语音区服 ≠ 房间区服（同一游戏房必然同区，所以房间区服就是本机游戏区服）
        }
        private readonly List<PlayerClass> _classified = new List<PlayerClass>();

        public static void CreateGlobalInstance()
        {
            if (Instance != null) return;
            GameObject go = new GameObject("BetterVoiceFix_UI");
            DontDestroyOnLoad(go);
            Canvas c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 9999;
            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            Instance = go.AddComponent<VoiceUIManager>();
            Instance.myCanvas = c;
            Instance.InitText();
        }

        private void InitText()
        {
            GameObject tObj = new GameObject("StatusText");
            tObj.transform.SetParent(transform, false);
            ContentSizeFitter fitter = tObj.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            statsText = tObj.AddComponent<TextMeshProUGUI>();
            statsText.richText = true;
            statsText.raycastTarget = false;
            statsText.overflowMode = TextOverflowModes.Overflow;
            statsText.textWrappingMode = TextWrappingModes.Normal;
            statsText.parseCtrlCharacters = false;
            UpdateLayout();
            TrySyncFontFromGame();
        }
        public void AddLog(string player, string msg, bool isLocal) { if (debugLogs.Count > 300) debugLogs.RemoveAt(0); debugLogs.Add(new LogEntry { Time = DateTime.Now.ToString("HH:mm:ss"), Player = player, Msg = msg, IsLocal = isLocal }); debugScrollPosition.y = float.MaxValue; }

        void OnGUI() { if (showDebugConsole) { GUI.skin.window.normal.background = Texture2D.blackTexture; GUI.backgroundColor = new Color(0, 0, 0, 0.85f); debugWindowRect = GUI.Window(999, debugWindowRect, DrawDebugWindow, L.Get("ui_debug_title")); } }
        void DrawDebugWindow(int windowID)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L.Get("btn_copy_all"))) ExportLogs(false);
            if (GUILayout.Button(L.Get("btn_export"))) ExportLogs(true);
            if (GUILayout.Button(L.Get("btn_clear"))) debugLogs.Clear();
            if (GUILayout.Button(L.Get("btn_dump"))) DumpVoicePlayers();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L.Get("btn_region_status"))) AddLog("System", RegionControl.GetStatusReport(), true);
            if (GUILayout.Button(L.Get("btn_region_ping"))) RegionControl.StartPing();
            GUILayout.EndHorizontal();
            filterScrollPosition = GUILayout.BeginScrollView(filterScrollPosition, GUILayout.Height(40));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(logFilterMode == 0 ? $"[★{L.Get("filter_all")}]" : L.Get("filter_all"), GUILayout.Width(60))) logFilterMode = 0;
            if (GUILayout.Button(logFilterMode == 1 ? $"[★{L.Get("filter_local")}]" : L.Get("filter_local"), GUILayout.Width(60))) logFilterMode = 1;
            if (PhotonNetwork.InRoom) { foreach (var p in PhotonNetwork.PlayerList) { if (p.IsLocal) continue; string fixedName = NetworkManager.GetPlayerName(p.ActorNumber); string nameShort = fixedName.Length > 9 ? fixedName.Substring(0, 9) : fixedName; string btnLabel = (logFilterMode == -1 && targetActorNumber == p.ActorNumber) ? $"[★{nameShort}]" : nameShort; if (GUILayout.Button(btnLabel, new GUIStyle(GUI.skin.button) { richText = false }, GUILayout.Width(80))) { logFilterMode = -1; targetActorNumber = p.ActorNumber; } } }
            GUILayout.EndHorizontal(); GUILayout.EndScrollView();
            debugScrollPosition = GUILayout.BeginScrollView(debugScrollPosition);
            foreach (var log in debugLogs) { if (logFilterMode == 1 && !log.IsLocal) continue; if (logFilterMode == -1) { string targetName = NetworkManager.GetPlayerName(targetActorNumber); if (log.Player != targetName) continue; } string color = log.IsLocal ? "cyan" : "yellow"; if (log.Player == "System") color = "white"; GUILayout.Label($"[{log.Time}] {log.Player}: {log.Msg}", new GUIStyle(GUI.skin.label) { richText = false, wordWrap = true }); }
            GUILayout.EndScrollView(); GUI.DragWindow(new Rect(0, 0, 10000, 20)); resizeHandleRect = new Rect(debugWindowRect.width - 20, debugWindowRect.height - 20, 20, 20); GUI.Label(resizeHandleRect, "◢"); Event e = Event.current; if (e.type == EventType.MouseDown && resizeHandleRect.Contains(e.mousePosition)) isResizing = true; else if (e.type == EventType.MouseUp) isResizing = false; else if (e.type == EventType.MouseDrag && isResizing) { debugWindowRect.width += e.delta.x; debugWindowRect.height += e.delta.y; if (debugWindowRect.width < 300) debugWindowRect.width = 300; if (debugWindowRect.height < 200) debugWindowRect.height = 200; }
        }

        private void DumpVoicePlayers()
        {
            if (NetworkManager.punVoice == null || NetworkManager.punVoice.Client == null || NetworkManager.punVoice.Client.CurrentRoom == null) { AddLog("System", L.Get("client_not_connected"), true); return; }
            StringBuilder sb = new StringBuilder();
            var players = NetworkManager.punVoice.Client.CurrentRoom.Players;
            sb.AppendLine($"=== {L.Get("voice_player_list")} (Count: {players.Count}) ===");
            foreach (var kvp in players)
            {
                int id = kvp.Key;
                string name = NetworkManager.GetPlayerName(id);
                bool isGhost = NetworkManager.IsGhost(id);

                string ip = "N/A";
                string ver = "";
                if (NetworkManager.PlayerCache.ContainsKey(id))
                {
                    ip = NetworkManager.PlayerCache[id].IP;
                    ver = NetworkManager.PlayerCache[id].ModVersion;
                }

                string ghostTag = isGhost ? $" {L.Get("ghost_tag")}" : "";
                string verStr = string.IsNullOrEmpty(ver) ? "" : $" | Ver: {ver}";
                sb.AppendLine($" - ID: {id} | Name: {name} | IP: {ip}{verStr}{ghostTag}");
            }
            AddLog("System", sb.ToString(), true);
        }

        private void ExportLogs(bool toFile) { StringBuilder sb = new StringBuilder(); sb.AppendLine($"=== Log Export ({DateTime.Now}) ==="); foreach (var log in debugLogs) sb.AppendLine($"[{log.Time}] {log.Player}: {log.Msg}"); if (toFile) { try { string dir = Path.Combine(Paths.BepInExRootPath, "PVF_Dumps"); Directory.CreateDirectory(dir); string path = Path.Combine(dir, $"BetterVoiceFix_Dump_{DateTime.Now:yyyyMMdd_HHmmss}.txt"); File.WriteAllText(path, sb.ToString()); AddLog("System", $"{L.Get("exported_to")}: {path}", true); } catch (Exception ex) { AddLog("System", $"{L.Get("export_failed")}: {ex.Message}", true); } } else { GUIUtility.systemCopyBuffer = sb.ToString(); AddLog("System", L.Get("copied"), true); } }
        void OnEnable()
        {
            currentSceneName = SceneManager.GetActiveScene().name;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        void OnDisable()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        }

        private void OnActiveSceneChanged(Scene from, Scene to)
        {
            currentSceneName = to.name;
        }

        void Update()
        {
            if (VoiceFix.ToggleUIKey == null) return;

            // 快捷键：关闭 → 简易 → 详细 → 关闭
            if (Input.GetKeyDown(VoiceFix.GetToggleKey()))
            {
                var prev = displayMode;
                // 当前若是“潜在简易态”但画布被自动隐藏，第一次按键只负责把简易面板真正显示出来。
                // 这样非机场的可见循环仍然严格是：关闭 → 简易 → 详细 → 关闭。
                if (displayMode == DisplayMode.Simple && !simpleManuallyShown && !myCanvas.enabled)
                {
                    prev = DisplayMode.Off;
                    simpleManuallyShown = true;
                }
                else if (displayMode == DisplayMode.Off)
                {
                    displayMode = DisplayMode.Simple;
                    simpleManuallyShown = true;
                }
                else if (displayMode == DisplayMode.Simple)
                {
                    displayMode = DisplayMode.Detail;
                    simpleManuallyShown = false;
                }
                else
                {
                    displayMode = DisplayMode.Off;
                    simpleManuallyShown = false;
                }
                // 详细态限时展示：机场里且开了「机场常驻」时不限时（那时人本来就在看面板）
                detailExpiry = (displayMode == DisplayMode.Detail && !DetailShouldPersist())
                    ? Time.unscaledTime + 10f : 0f;
                NetworkManager.DiagLog(L.Get("diag_mode_change", ModeName(prev), ModeName(displayMode)));
            }
            if ((Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.J))
                showDebugConsole = !showDebugConsole;

            // 定期清理 joinTimes（每60秒一次）
            CleanupJoinTimes();

            // 场景名每帧直接读，不依赖 activeSceneChanged 事件——PEAK 的场景切换方式下那个事件不一定触发，
            // 事件漏一次就会让"机场"判断永久失准。
            currentSceneName = SceneManager.GetActiveScene().name;

            bool inAirport = (currentSceneName == "Airport");
            bool inRoom = PhotonNetwork.InRoom;

            // 边沿检测：手动钉住的简易面板不跟出场景，离开机场/离房即还回自动规则。
            bool leftAirport = wasInAirport && !inAirport;
            bool leftRoom = wasInRoom && !inRoom;
            wasInAirport = inAirport;
            wasInRoom = inRoom;
            if (leftAirport || leftRoom) simpleManuallyShown = false;
            // 详细态不跨房间复活：回默认简易态，由新房间的自动规则决定显隐。
            if (leftRoom && displayMode == DisplayMode.Detail) { displayMode = DisplayMode.Simple; detailExpiry = 0f; }

            // 详细态：机场常驻场景下 detailExpiry 为 0 永不到点；离开机场立即关闭；
            // 非机场场景手动打开的详细态仍是 10 秒临时窗。
            if (displayMode == DisplayMode.Detail)
            {
                if (DetailShouldPersist()) detailExpiry = 0f;
                else if (leftAirport || (detailExpiry > 0f && Time.unscaledTime > detailExpiry))
                {
                    displayMode = DisplayMode.Off;
                    detailExpiry = 0f;
                    NetworkManager.DiagLog(L.Get("diag_mode_change", L.Get("mode_detail"), L.Get("mode_off")));
                }
                else if (detailExpiry <= 0f) detailExpiry = Time.unscaledTime + 10f;
            }
            // ESC 菜单是否开着：以本体 GUIManager.InPauseMenu 为准，见 IsPauseMenuOpen 的注释。
            bool isMenuOpen = VoiceFix.HideOnMenu != null && VoiceFix.HideOnMenu.Value && IsPauseMenuOpen(inAirport);
            bool shouldShow = false;
            bool hasRegionWarn = Time.unscaledTime < regionWarnExpiry;
            bool hasNotification = Time.unscaledTime < notificationExpiry;

            if (inRoom && !isMenuOpen)
            {
                switch (displayMode)
                {
                    case DisplayMode.Detail:
                        shouldShow = true;
                        break;
                    case DisplayMode.Simple:
                        // 主动按 J 打开的简易态必须可见；自动态仍只在机场、异常或通知时出现。
                        bool airportPersist = VoiceFix.AutoHideNormal != null && VoiceFix.AutoHideNormal.Value && inAirport;
                        shouldShow = simpleManuallyShown || airportPersist || !IsAllGood() || hasNotification;
                        break;
                    default:
                        shouldShow = hasNotification;   // 关闭状态下仍然让掉线通知能弹出来
                        break;
                }
            }
            else if (hasRegionWarn && !isMenuOpen)
            {
                // 强制区服连不上时人还卡在主菜单，房内那套显示条件到不了这里，所以单独放行。
                shouldShow = true;
            }

            if (myCanvas.enabled != shouldShow)
                myCanvas.enabled = shouldShow;
            if (!shouldShow) return;

            // 字体同步：整体节流到 0.5s，且仅在字体异常时才 FindFirstObjectByType。
            // 旧逻辑里 isBadFont 会绕过节流，字体一直同步不上时会每帧扫场景。
            if (Time.unscaledTime - lastFontRetryTime > 0.5f)
            {
                lastFontRetryTime = Time.unscaledTime;
                bool isBadFont = statsText.font == null || statsText.font.name.Contains("Liberation");
                if (isBadFont) TrySyncFontFromGame();
            }

            // 定时更新 UI 内容（先内容后布局：面板宽度按刚生成的文本量）
            if (Time.unscaledTime > nextUiUpdateTime)
            {
                if (!inRoom) UpdateContent_RegionWarnOnly();
                else if (isDetailMode) UpdateContent_Detail();
                else UpdateContent_Normal();
                UpdateLayout();
                nextUiUpdateTime = Time.unscaledTime + 0.2f;
            }
        }

        // ===== ESC 菜单检测 =====
        // 本体的权威判据是 GUIManager.InPauseMenu（即 pauseMenu.activeSelf），
        // GUIManager.UpdatePaused / UpdateWindowStatus 与 PauseMenuHandler 全都以它为准。
        // 旧代码拿 Cursor.visible 当代理，代价是两处误判：
        //   ① 机场里光标常亮，只能加 !inAirport 例外 → 机场里按 ESC 面板不隐藏（本次修的就是这个，
        //      而机场恰好是简易 UI 常驻的地方，所以在那儿最显眼）；
        //   ② 主菜单光标也常亮 → 「强制区服连不上」的提示在主菜单被一并吃掉，
        //      而主菜单正是那条提示唯一该出现的地方（人就卡在那里）。
        // Cursor.visible 还会被地图/背包轮盘/Modal 一并拉起，语义上根本不等于"ESC 菜单开着"。
        // 反射解析一次并缓存成委托（这是每帧路径，不走 PropertyInfo.GetValue）；解析不到就退回旧判据，fail-open。
        private static Func<bool> pauseMenuGetter;
        private static bool pauseMenuResolved;

        private static bool IsPauseMenuOpen(bool inAirport)
        {
            if (!pauseMenuResolved)
            {
                pauseMenuResolved = true;
                try
                {
                    var t = AccessTools.TypeByName("GUIManager");
                    var getter = (t != null) ? AccessTools.PropertyGetter(t, "InPauseMenu") : null;
                    if (getter != null && getter.IsStatic && getter.ReturnType == typeof(bool))
                        pauseMenuGetter = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter);
                }
                catch (Exception) { pauseMenuGetter = null; }
                if (pauseMenuGetter == null && VoiceFix.logger != null)
                    VoiceFix.logger.LogWarning("[UI] 反射不到 GUIManager.InPauseMenu，ESC 隐藏退回 Cursor.visible 旧判据");
            }
            if (pauseMenuGetter != null)
            {
                // InPauseMenu 是 instance?.pauseMenu.activeSelf ?? false：主菜单场景里没有实例，返回 false。
                try { return pauseMenuGetter(); }
                catch (Exception) { return false; }
            }
            return Cursor.visible && !inAirport;
        }

        /// <summary>不在房间时唯一会显示的内容：区服警告。</summary>
        private void UpdateContent_RegionWarnOnly()
        {
            var sb = _sb; sb.Clear();
            sb.Append($"<color={C_YELLOW}>{PanelText.Literal(regionWarnMsg)}</color>");
            SetStatsText(sb.ToString());
        }

        // [修改] 适配 string 类型的配置
        private void UpdateLayout()
        {
            if (statsText == null || VoiceFix.UIPositionSide == null) return;

            // 判断字符串是否为 "Right"
            bool isRight = (VoiceFix.UIPositionSide.Value == UIPositionEnum.Right);

            float targetX = isRight ? Mathf.Abs(VoiceFix.OffsetX_Right.Value) : Mathf.Abs(VoiceFix.OffsetX_Left.Value);
            float targetY = isRight ? Mathf.Abs(VoiceFix.OffsetY_Right.Value) : Mathf.Abs(VoiceFix.OffsetY_Left.Value);
            // Canvas reference units, not screen pixels. Keep the configured dock/offset unless off-screen.
            float canvasWidth = ((RectTransform)myCanvas.transform).rect.width;
            if (canvasWidth <= 0) canvasWidth = Screen.width / Mathf.Max(myCanvas.scaleFactor, 0.001f);
            targetX = Mathf.Min(targetX, Mathf.Max(0f, (canvasWidth - 1f) * 0.5f));
            RectTransform rt = statsText.rectTransform;
            // 宽度跟上一版一样按内容自适应（等价原 PreferredSize），只是多了上限：
            // 内容短则窄，长内容在上限处换行，不再把面板顶宽。
            float cap = PanelText.PanelWidth(canvasWidth, targetX);
            float natural = statsText.GetPreferredValues(statsText.text ?? string.Empty, Mathf.Infinity, Mathf.Infinity).x;
            if (!(natural > 1f)) natural = 1f; // 空文本/字体未就绪/NaN 时保底
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Min(natural, cap));
            if (isRight)
            {
                rt.anchorMin = new Vector2(1, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 1);
                rt.anchoredPosition = new Vector2(-targetX, -targetY); statsText.alignment = TextAlignmentOptions.TopLeft;
            }
            else
            {
                rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = new Vector2(targetX, -targetY); statsText.alignment = TextAlignmentOptions.TopLeft;
            }
            statsText.fontSize = VoiceFix.FontSize.Value;
        }

        public void TriggerNotification(string playerName) { notificationMsg = $"<color={C_TEXT}>{PanelText.Literal(playerName)}:</color> {FormatStatusTag(L.Get("notification_disconnected"), C_YELLOW)}"; notificationExpiry = Time.unscaledTime + 5f; }
        public void ShowStatsTemporary() { notificationMsg = $"<color={C_YELLOW}>{L.Get("manual_operation")}</color>"; notificationExpiry = Time.unscaledTime + 5f; }
        // ====== 统一分类：每次 UI 刷新算一次，行渲染与聚合计数共用同一结果，杜绝自相矛盾 ======
        private void ClassifyPlayers()
        {
            _classified.Clear();            if (PhotonNetwork.PlayerList == null) return;

            string myIP = GetCurrentIP();
            string roomRegion = PhotonNetwork.CloudRegion;   // 同一游戏房必然同区服，所以这就是"房间区服"
            float connectTimeout = (VoiceFix.ConnectTimeout != null) ? VoiceFix.ConnectTimeout.Value : 25f;
            int voiceRoomCount = (NetworkManager.punVoice != null && NetworkManager.punVoice.Client != null && NetworkManager.punVoice.Client.CurrentRoom != null)
                ? NetworkManager.punVoice.Client.CurrentRoom.Players.Count : 0;

            // Pass 1：能直接判定的先判；没装且对不上、过宽限的标记 pending，留到 Pass 2 按名额定。
            // positivelyInVoice = 已确定占用"我的语音房"一个连接的人（本机已连 / mod 同IP / 没装但映射命中）。
            int positivelyInVoice = 0;
            var pendingIdx = new List<int>();
            foreach (var p in PhotonNetwork.PlayerList)
            {
                int actor = p.ActorNumber;
                bool isLocal = p.IsLocal;
                bool isHost = p.IsMasterClient;
                bool isMod = NetworkManager.IsModUser(p);
                string ip = ""; int ping = 0; byte rState = 0;
                string vRegion = null; int vPing = -1; string modVer = null;
                if (NetworkManager.PlayerCache.TryGetValue(actor, out var ce))
                {
                    rState = ce.RemoteState;
                    vRegion = ce.VoiceRegion;
                    vPing = ce.VoicePing;
                    modVer = ce.ModVersion;
                    if (ce.GamePing > 0) ping = ce.GamePing;   // 3 秒一次的事件包，比属性新
                }
                if (isLocal)
                {
                    ip = myIP; ping = PhotonNetwork.GetPing();
                    vRegion = NetworkManager.LocalVoiceRegion;   // 本机直接读，不靠广播
                    vPing = NetworkManager.LocalVoicePing;
                    modVer = VoiceFix.MOD_VERSION;
                }
                else
                {
                    if (p.CustomProperties.TryGetValue("PVF_IP", out var ipObj) && ipObj is string s) ip = s;
                    // 属性里的 ping 只作兜底：事件包（3s）比属性（30s）新得多
                    if (ping <= 0 && p.CustomProperties.TryGetValue("PVF_Ping", out var pgObj) && pgObj is int pg) ping = pg;
                    // 属性包可能比缓存更新，这里以属性包为准
                    if (p.CustomProperties.TryGetValue(NetworkManager.PROP_VREGION, out var vrObj) && vrObj is string vrs)
                        vRegion = string.IsNullOrEmpty(vrs) ? null : vrs;
                    if (p.CustomProperties.TryGetValue(NetworkManager.PROP_VPING, out var vpObj) && vpObj is int vpi)
                        vPing = vpi;
                }
                string name = NetworkManager.GetPlayerName(actor);
                bool crossRegion = !string.IsNullOrEmpty(vRegion) && !string.IsNullOrEmpty(roomRegion) && vRegion != roomRegion;

                Verdict v = Verdict.Disconnected; bool connecting = false; bool pending = false;
                if (isLocal)
                {
                    v = Verdict.Local; connecting = IsConnectingLocal();
                    if (IsVoiceConnected()) positivelyInVoice++;
                }
                else if (isMod)
                {
                    // 装了 mod：信其自报 STATE / IP
                    if (rState != 0)
                    {
                        ClientState cs = (ClientState)rState;
                        if (cs == ClientState.Disconnected || cs == ClientState.Disconnecting) v = Verdict.Disconnected;
                        else if (cs == ClientState.Joined) v = ClassifyModByIP(ip, actor, connectTimeout, out connecting);
                        else { v = Verdict.Connecting; connecting = true; }
                    }
                    else v = ClassifyModByIP(ip, actor, connectTimeout, out connecting);
                    if (v == Verdict.Synced) positivelyInVoice++;   // 同 IP = 在我的语音房
                }
                else
                {
                    // 没装 mod：无自报，靠语音房映射 + 配平推断
                    if (NetworkManager.IsPlayerInVoiceRoom(actor)) { v = Verdict.Connected; positivelyInVoice++; }     // 对得上 → 在语音
                    else if (WithinGrace(actor, connectTimeout)) { v = Verdict.Connecting; connecting = true; }        // 入房宽限
                    else pending = true;                                                                              // 对不上、过宽限 → Pass 2
                }

                _classified.Add(new PlayerClass
                {
                    ActorNumber = actor, Name = name, IP = ip, Ping = ping,
                    IsLocal = isLocal, IsHost = isHost, IsMod = isMod, RemoteState = rState,
                    Verdict = v, IsConnecting = connecting,
                    VoiceRegion = vRegion, VoicePing = vPing, ModVersion = modVer, CrossRegion = crossRegion
                });
                if (pending) pendingIdx.Add(_classified.Count - 1);
            }

            // Pass 2：语音房里尚未归属的连接名额，分给"没装且对不上"的人 → [错位]（在语音、漂移）；
            // 名额不够则 [断开]（不在语音）。名额排除了 mod 同IP 占用的位，故漂移的 mod 用户不会误占。
            int ghostBudget = voiceRoomCount - positivelyInVoice;
            if (ghostBudget < 0) ghostBudget = 0;
            foreach (int idx in pendingIdx)
            {
                var pc = _classified[idx];
                if (ghostBudget > 0) { ghostBudget--; pc.Verdict = Verdict.Mismatch; }
                else pc.Verdict = Verdict.Disconnected;
                _classified[idx] = pc;
            }

            // 分类结果出来之后才能判断新人有没有接上语音，所以放在这里而不是 Update 里
            TrackJoinNotices();
        }

        private Verdict ClassifyModByIP(string ip, int actor, float connectTimeout, out bool connecting)
        {
            connecting = false;
            if (string.IsNullOrEmpty(ip))
            {
                if (WithinGrace(actor, connectTimeout)) { connecting = true; return Verdict.Connecting; }
                return Verdict.Disconnected;
            }
            if (IsIPMatch(ip)) return Verdict.Synced;
            return Verdict.Abnormal;       // 自报 IP 与本机不同 = 在别的服务器
        }

        // 入房宽限期：首次见到记时间，ConnectTimeout 内视为连接中。
        private bool WithinGrace(int actor, float connectTimeout)
        {
            if (!joinTimes.ContainsKey(actor)) joinTimes[actor] = Time.unscaledTime;
            return Time.unscaledTime - joinTimes[actor] < connectTimeout;
        }

        private void GetVerdictTag(Verdict v, out string label, out string color)
        {
            switch (v)
            {
                case Verdict.Synced: label = L.Get("state_synced"); color = C_GREEN; break;
                // 跨区和断开同色：两者后果一样——听不见。
                case Verdict.Abnormal: label = L.Get("state_abnormal"); color = C_RED; break;
                case Verdict.Disconnected: label = L.Get("state_disconnected"); color = C_RED; break;
                case Verdict.Connecting: label = L.Get("state_connecting"); color = C_YELLOW; break;
                case Verdict.Connected: label = L.Get("state_connected"); color = C_PALE_GREEN; break;
                case Verdict.Mismatch: label = L.Get("state_mismatch"); color = C_GHOST_GREEN; break;
                default: label = L.Get("state_unknown"); color = C_TEXT; break;
            }
        }

        // 在语音 = Synced/Connected/Mismatch（错位是 ID 漂移，人仍在语音）；本机看实际连接。
        private bool IsVerdictInVoice(in PlayerClass c)
        {
            if (c.IsLocal) return IsVoiceConnected();
            return c.Verdict == Verdict.Synced || c.Verdict == Verdict.Connected || c.Verdict == Verdict.Mismatch;
        }

        private void GetPresenceCounts(out int inVoice, out int total)
        {
            inVoice = 0; total = _classified.Count;
            foreach (var c in _classified) if (IsVerdictInVoice(c)) inVoice++;
        }

        private static string PingColor(int p) => p < 100 ? C_GREEN : (p < 200 ? C_YELLOW : C_RED);

        // 延迟槽固定 6 字符宽（最大 "9999ms"），数值右对齐让每列的 "-" 和 ms 天然对齐；
        // 超过 9999 显示红 ×，<=0 给空槽保住列位（由调用方决定要不要连 "-" 一起输出）。
        private static string FmtPingSlot(int p)
        {
            if (p <= 0) return "      ";
            if (p > 9999) return $"<color={C_RED}>   ×  </color>";
            return $"<color={PingColor(p)}>{p,4}ms</color>";
        }

        // ===== 新加入玩家通知（只有房主看得到）=====
        private class JoinNotice
        {
            public int Actor;
            public string Name;
            public float JoinTime;
            public float ResolvedTime = -1f;   // 判定出结果的时刻；-1 = 还在等
            public bool Success;
            public string Detail;
        }
        private readonly List<JoinNotice> joinNotices = new List<JoinNotice>();
        private readonly HashSet<int> knownActors = new HashSet<int>();

        /// <summary>
        /// 跟踪新加入的玩家有没有接上语音。成功后留 3 秒，失败后留 10 秒。
        /// 没装 mod 的人判"成功"天生慢——只能靠语音房映射推断，还要过入房宽限期，
        /// 所以他们停在"正在连接"的时间会明显更久，这是数据来源决定的，优化不掉。
        /// </summary>
        private void TrackJoinNotices()
        {
            if (PhotonNetwork.CurrentRoom == null)
            {
                joinNotices.Clear();
                knownActors.Clear();
                return;
            }
            float now = Time.unscaledTime;
            float timeout = (VoiceFix.ConnectTimeout != null) ? VoiceFix.ConnectTimeout.Value : 25f;
            bool isHost = PhotonNetwork.IsMasterClient;

            foreach (var c in _classified)
            {
                // knownActors 无论是不是房主都要维护，否则中途变成房主会把全场当新人
                bool isNew = knownActors.Add(c.ActorNumber);
                if (isNew && isHost && !c.IsLocal)
                    joinNotices.Add(new JoinNotice { Actor = c.ActorNumber, Name = c.Name, JoinTime = now });
            }
            knownActors.RemoveWhere(a => PhotonNetwork.CurrentRoom.GetPlayer(a) == null);
            if (!isHost) { joinNotices.Clear(); return; }

            for (int i = joinNotices.Count - 1; i >= 0; i--)
            {
                var n = joinNotices[i];
                if (PhotonNetwork.CurrentRoom.GetPlayer(n.Actor) == null) { joinNotices.RemoveAt(i); continue; }

                if (n.ResolvedTime < 0f)
                {
                    int idx = _classified.FindIndex(x => x.ActorNumber == n.Actor);
                    if (idx >= 0 && IsVerdictInVoice(_classified[idx]))
                    {
                        n.Success = true;
                        n.ResolvedTime = now;
                        n.Detail = BuildJoinDetail(_classified[idx]);
                        NetworkManager.DiagLog(L.Get("diag_newjoin_ok", n.Name, (now - n.JoinTime).ToString("F0")));
                    }
                    else if (now - n.JoinTime > timeout)
                    {
                        n.Success = false;
                        n.ResolvedTime = now;
                        NetworkManager.DiagLog(L.Get("diag_newjoin_fail", n.Name, (now - n.JoinTime).ToString("F0")));
                    }
                }
                else if (now - n.ResolvedTime > (n.Success ? 3f : 10f))
                {
                    joinNotices.RemoveAt(i);
                }
            }
        }

        private string BuildJoinDetail(in PlayerClass c)
        {
            if (c.IsMod && !string.IsNullOrEmpty(c.VoiceRegion))
            {
                string d = string.IsNullOrEmpty(c.VoiceRegion) ? L.Get("unknown") : RegionControl.Describe(c.VoiceRegion);
                if (c.VoicePing > 0) d += $" {c.VoicePing}ms";
                return d;
            }
            return L.Get("state_synced");
        }

        private void AppendNewJoinLines(StringBuilder sb)
        {
            if (joinNotices.Count == 0) return;
            foreach (var n in joinNotices)
            {
                string body;
                if (n.ResolvedTime < 0f) body = $"<color={C_YELLOW}>{L.Get("newjoin_connecting")}</color>";
                else if (n.Success) body = $"<color={C_GREEN}>{PanelText.Literal(n.Detail)}</color>";
                else body = $"<color={C_RED}>{L.Get("newjoin_failed")}</color>";
                sb.Append($"<size=85%><color={C_TEXT}>{L.Get("newjoin")} </color><color={C_GREEN}>{PanelText.Literal(n.Name)}</color><color={C_TEXT}> - </color>{body}</size>\n");
            }
        }

        /// <summary>
        /// 简易模式第一行：「房间/语音地区: 游戏区服 / 语音区服 ✓」。
        /// ✓/× 判据是 IP 比较（区服相同也可能连错），出错时括号里放【错误原因】而不是 IP。
        /// </summary>
        private void AppendLocalStateLine(StringBuilder sb)
        {
            // 离线伪房间：本体刻意让语音客户端停在 PeerCreated 永不连接，没有"语音状态"可报。
            if (PhotonNetwork.OfflineMode)
            {
                sb.Append($"<color={C_TEXT}>{L.Get("hdr_local_state")} {L.Get("voice_offline")}</color>\n");
                return;
            }
            string roomRegion = PhotonNetwork.CloudRegion;
            string myIP = GetCurrentIP();

            sb.Append($"<color={C_TEXT}>{L.Get("hdr_local_state")} {DisplayRegion(roomRegion)} / </color>");

            if (string.IsNullOrEmpty(myIP))
            {
                var c = GetVoiceClient();
                string st = (c != null) ? GetClientStateLocalized(c.State) : L.Get("detail_connecting_local");
                sb.Append($"<color={C_YELLOW}>{st}</color>\n");
                return;
            }

            NetworkManager.RoomVoiceSource src; int rep;
            string roomVoice = NetworkManager.GetRoomVoiceServer(out src, out rep);
            bool isolated = NetworkManager.IsLocalIsolated();
            bool ok = !isolated && (string.IsNullOrEmpty(roomVoice) || roomVoice == myIP);
            string myRegion = NetworkManager.LocalVoiceRegion ?? roomRegion;

            sb.Append($"<color={C_GREEN}>{DisplayRegion(myRegion)}</color>");
            sb.Append(ok ? $" <color={C_GREEN}>✓</color>" : $" <color={C_RED}>×</color>");
            if (!ok)
            {
                string reason = isolated ? L.Get("voice_only_local") : L.Get("err_voice_connect");
                sb.Append($"<color={C_TEXT}> (</color><color={C_RED}>{reason}</color><color={C_TEXT}>)</color>");
            }
            sb.Append("\n");
        }

        private void AppendLocalPingLine(StringBuilder sb)
        {
            if (VoiceFix.ShowPingInNormal == null || !VoiceFix.ShowPingInNormal.Value) return;
            if (PhotonNetwork.OfflineMode) return;
            int p = PhotonNetwork.GetPing();
            if (p <= 0) return;
            int vp = NetworkManager.LocalVoicePing;
            sb.Append($"<color={C_TEXT}>{L.Get("hdr_local_ping")} </color><color={PingColor(p)}>{p}ms</color>");
            if (vp > 0) sb.Append($"<color={C_TEXT}> - </color><color={PingColor(vp)}>{vp}ms</color>");
            sb.Append("\n");
        }

        private void UpdateContent_Normal()
        {
            ClassifyPlayers();
            var sb = _sb; sb.Clear();

            bool isSinglePlayer = PhotonNetwork.OfflineMode || (PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.PlayerCount <= 1);

            // 显示与否完全交给 shouldShow（机场常驻 / 有异常 / 有通知）决定。
            // 以前这里还有一层"单人待够 10 秒就清空内容"，结果是 canvas 开着、内容为空，
            // 看起来就是"简易UI怎么按 J 都不出来"。两套隐藏逻辑重叠，去掉这一层。
            AppendLocalStateLine(sb);
            AppendLocalPingLine(sb);
            if (!isSinglePlayer)
            {
                sb.Append($"<color={C_TEXT}>{L.Get("hdr_room_status")} </color>");
                AppendRoomStatusLine(sb, null);
                AppendNewJoinLines(sb);
            }
            if (Time.unscaledTime < notificationExpiry) sb.Append($"{notificationMsg}\n");
            SetStatsText(sb.ToString());
        }

        // 仅在文本内容变化时才写 TMP，避免每 0.2s 无谓触发 TMP 网格重建。
        private void SetStatsText(string s)
        {
            if (s == lastStatsText) return;
            lastStatsText = s;
            statsText.text = s;
        }

        /// <summary>
        /// 顶部区服对比行：「房间区服 / 我的语音区服」+ 一致性标记。
        /// 同一个游戏房必然在同一区服，所以本机的游戏区服就是房间区服，不需要额外广播。
        /// </summary>
        /// <summary>
        /// 顶部两行：房间区服（+游戏服 IP）／房间语音服（+语音服 IP）+ ✓/×。
        /// 一行放不下，所以拆开。
        ///
        /// 房主视角特别处理：这游戏一切以房主为准，房主连的就是标准答案，
        /// 所以房主看自己这两行不上色、不打勾、也不标"本机推定"——没有提醒的必要。
        /// 唯一例外是房主自己都没连上语音，那才需要提示。
        /// </summary>
        private void AppendRegionHeader(StringBuilder sb, bool proMode)
        {
            string roomRegion = PhotonNetwork.CloudRegion;
            string myIP = GetCurrentIP();
            bool amHost = PhotonNetwork.IsMasterClient;
            NetworkManager.RoomVoiceSource src;
            int reporters;
            string roomVoiceIP = NetworkManager.GetRoomVoiceServer(out src, out reporters);
            bool isolated = NetworkManager.IsLocalIsolated();

            sb.Append("<size=75%>");
            sb.Append($"<color={C_TEXT}>{L.Get("hdr_room_region")} {DisplayRegion(roomRegion)}</color>");
            if (proMode)
            {
                string gameIP = PhotonNetwork.ServerAddress;
                if (!string.IsNullOrEmpty(gameIP)) sb.Append($"<color={C_TEXT}> ({gameIP})</color>");
            }
            sb.Append("</size>\n");

            sb.Append("<size=75%>");
            sb.Append($"<color={C_TEXT}>{L.Get("hdr_room_voice")} </color>");

            if (string.IsNullOrEmpty(roomVoiceIP))
            {
                // 无人上报：写未知。"语音房里只有我、游戏房有别人"是铁证，能断定本机跑偏
                sb.Append($"<color={C_YELLOW}>{L.Get("rv_unknown")}</color>");
                if (isolated) sb.Append($" <color={C_RED}>× {L.Get("local_isolated")}</color>");
                else if (string.IsNullOrEmpty(myIP)) sb.Append($" <color={C_YELLOW}>?</color>");
            }
            else if (amHost && !string.IsNullOrEmpty(myIP) && !isolated)
            {
                // 房主且自己连上了：这就是金标准，平铺直叙，不上色不打勾
                string voiceRegion = GetReportedRoomVoiceRegion(src, roomVoiceIP);
                sb.Append($"<color={C_TEXT}>{DisplayRegion(voiceRegion)}</color>");
                if (proMode) sb.Append($"<color={C_TEXT}> ({roomVoiceIP})</color>");
            }
            else
            {
                bool match = !string.IsNullOrEmpty(myIP) && myIP == roomVoiceIP && !isolated;
                // 一致 → 全绿；不一致 → 这段转黄（它是正确答案，但当前整体异常），× 用红
                string bodyColor = match ? C_GREEN : C_YELLOW;
                string voiceRegion = GetReportedRoomVoiceRegion(src, roomVoiceIP);

                sb.Append($"<color={bodyColor}>{DisplayRegion(voiceRegion)}</color>");
                sb.Append(match ? $" <color={C_GREEN}>✓</color>" : $" <color={C_RED}>×</color>");
                if (proMode) sb.Append($"<color={C_TEXT}> (</color><color={bodyColor}>{roomVoiceIP}</color><color={C_TEXT}>)</color>");
                if (isolated) sb.Append($" <color={C_RED}>{L.Get("local_isolated")}</color>");
                else if (src == NetworkManager.RoomVoiceSource.LocalGuess) sb.Append($"<color={C_TEXT}> {L.Get("src_local_guess")}</color>");
                else if (src == NetworkManager.RoomVoiceSource.Host) sb.Append($"<color={C_TEXT}> {L.Get("src_host")}</color>");
                else if (src == NetworkManager.RoomVoiceSource.SingleReport) sb.Append($"<color={C_TEXT}> {L.Get("src_single")}</color>");
            }
            sb.Append("</size>\n");
        }

        /// <summary>
        /// 「房间语音服」那台机器在哪个区。本机推定时就是本机的语音区服；
        /// 别人上报时用上报者的语音区服，实在没有就退回房间区服（同区是绝大多数情况）。
        /// </summary>
        private string GetReportedRoomVoiceRegion(NetworkManager.RoomVoiceSource src, string roomVoiceIP)
        {
            if (roomVoiceIP == GetCurrentIP()) return NetworkManager.LocalVoiceRegion;
            foreach (var kvp in NetworkManager.PlayerCache)
            {
                if (kvp.Value == null) continue;
                if (kvp.Value.IP == roomVoiceIP && !string.IsNullOrEmpty(kvp.Value.VoiceRegion))
                    return kvp.Value.VoiceRegion;
            }
            return null;
        }

        /// <summary>
        /// 「[同步:n/N] [异常:m]」。两个模式共用同一个方法，口径也就永远一致：
        /// n = 分类判定"在我的语音房里"的人数，N = 房间总人数，m = N-n。
        /// 与每行的状态标签同源（都来自一次 ClassifyPlayers），不会出现行与汇总互相打脸。
        /// 所有人都看得到，不再只给房主。
        /// </summary>
        private void AppendRoomStatusLine(StringBuilder sb, string sizeTag)
        {
            int joined, total;
            GetPresenceCounts(out joined, out total);
            int abnormal = total - joined; if (abnormal < 0) abnormal = 0;

            if (!string.IsNullOrEmpty(sizeTag)) sb.Append(sizeTag);
            string syncColor = (joined >= total) ? C_GREEN : C_YELLOW;
            string diffColor = (abnormal > 0) ? C_RED : C_TEXT;
            sb.Append($"<color={C_TEXT}>[{L.Get("label_sync")}</color><color={syncColor}>{joined}/{total}</color><color={C_TEXT}>] [{L.Get("label_abnormal")}</color><color={diffColor}>{abnormal}</color><color={C_TEXT}>]</color>");
            if (!string.IsNullOrEmpty(sizeTag)) sb.Append("</size>");
            sb.Append("\n");
        }

        private void UpdateContent_Detail()
        {
            var sb = _sb; sb.Clear();
            // 离线伪房间没有语音可诊断：标题 + 一行说明即可，区服/玩家行全是假数据。
            if (PhotonNetwork.OfflineMode)
            {
                sb.Append($"<align=\"center\"><size=120%><color={C_TEXT}>{L.Get("ui_title")} ({VoiceFix.MOD_VERSION})</color></size></align>\n");
                sb.Append($"<align=\"center\"><color={C_TEXT}>------------------</color></align>\n");
                sb.Append($"<color={C_TEXT}>{L.Get("voice_offline")}</color>\n");
                SetStatsText(sb.ToString());
                return;
            }
            ClassifyPlayers();
            bool proMode = VoiceFix.ShowProfessionalInfo.Value; float alignX = GetLatencyColumn();

            sb.Append($"<align=\"center\"><size=120%><color={C_TEXT}>{L.Get("ui_title")} ({VoiceFix.MOD_VERSION})</color></size></align>\n");
            sb.Append($"<align=\"center\"><color={C_TEXT}>------------------</color></align>\n");
            string myIP = GetCurrentIP();

            AppendRegionHeader(sb, proMode);
            AppendRoomStatusLine(sb, "<size=75%>");

            if (PhotonNetwork.IsMasterClient)
            {
                // 判据用 GetRoomVoiceServer：它只统计别人的自报，且是实时读属性。
                // 旧判据走 PlayerCache（含本机自己、30 秒才刷一次），强制重连的瞬间会把
                // "3 秒前的自己"算成另一个人，于是误报"2 人在另一频道"。
                NetworkManager.RoomVoiceSource wsrc; int wrep;
                string wmaj = NetworkManager.GetRoomVoiceServer(out wsrc, out wrep);
                if (wsrc == NetworkManager.RoomVoiceSource.Majority && wrep >= 2
                    && !string.IsNullOrEmpty(myIP) && wmaj != myIP)
                    sb.Append($"<color={C_YELLOW}><size=85%>{L.Get("warn_majority", wrep)}</size></color>\n");
            }

            sb.Append($"<align=\"center\"><color={C_TEXT}>------------------</color></align>\n");
            string columnHeader = L.Get("col_status") + "  " + L.Get("col_name");
            bool headerFits = alignX > 0 && MeasureText(columnHeader, 100) + 8f <= alignX
                && alignX + MeasureText(L.Get("col_ping"), 100) <= statsText.rectTransform.rect.width;
            string headerBreak = headerFits ? $"<pos={alignX.ToString(CultureInfo.InvariantCulture)}>" : "\n";
            sb.Append($"<size=100%><color={C_TEXT}>{columnHeader}</color>{headerBreak}<color={C_TEXT}>{L.Get("col_ping")}</color></size>\n");
            sb.Append("<line-height=50%>\n</line-height>");

            // 渲染玩家列表：复用统一分类结果（含每人 Verdict），不再用"IP 非空=hasModData"，也不渲染离场残留。
            var renderList = new List<PlayerClass>(_classified);
            if (VoiceFix.EnableVirtualTestPlayer != null && VoiceFix.EnableVirtualTestPlayer.Value)
            {
                string fakeNameRaw = VoiceFix.TestPlayerName != null ? VoiceFix.TestPlayerName.Value : "Test";
                renderList.Add(new PlayerClass { Name = L.Get("virtual_player"), IP = "", Ping = 0, VoicePing = -1, IsMod = false, Verdict = Verdict.Connecting, IsConnecting = true });
                renderList.Add(new PlayerClass { Name = fakeNameRaw, IP = myIP, Ping = 50, VoicePing = 55, IsMod = true, Verdict = Verdict.Synced, VoiceRegion = PhotonNetwork.CloudRegion });
                // 第三个样本专门用来看"跨区 + 版本过旧"那两行长什么样，方便调对齐和边距
                renderList.Add(new PlayerClass
                {
                    Name = "CrossRegionSample", IP = "1.2.3.4:5056", Ping = 420, VoicePing = 445,
                    IsMod = true, Verdict = Verdict.Abnormal,
                    VoiceRegion = (PhotonNetwork.CloudRegion == "jp") ? "eu" : "jp",
                    ModVersion = "v1.0.5", CrossRegion = true
                });
            }
            // 次键用 ActorNumber：List.Sort 不稳定，PhotonNetwork.PlayerList 的顺序也不保证，
            // 只按 IsLocal 排会让队友行逐帧换位，顺带白触发 TMP 网格重建。
            renderList.Sort((a, b) =>
            {
                int c = b.IsLocal.CompareTo(a.IsLocal);
                return (c != 0) ? c : a.ActorNumber.CompareTo(b.ActorNumber);
            });
            sb.Append($"<line-height=105%>"); foreach (var d in renderList) BuildPlayerEntry(sb, d, proMode, alignX); sb.Append("</line-height>");

            if (NetworkManager.ActiveSOSList.Count > 0)
            {
                sb.Append($"<align=\"center\"><color={C_TEXT}>------------------</color></align>\n");
                sb.Append($"<color={C_YELLOW}>{L.Get("sos_snapshot")}</color>\n");
                string majIP = GetMajorityIP(out int majCnt);
                sb.Append($"<size=80%><color={C_TEXT}>{L.Get("sos_majority")}: {majIP} ({majCnt}{L.Get("sos_person")})</color></size>\n");

                foreach (var sos in NetworkManager.ActiveSOSList)
                {
                    sb.Append($"<size=80%><color={C_RED}>{L.Get("sos_detected", PanelText.Literal(sos.PlayerName))}</color></size>\n");
                    string lastIP = string.IsNullOrEmpty(sos.OriginIP) ? L.Get("unknown") : sos.OriginIP;
                    sb.Append($"  <size=80%><color={C_TEXT}>{L.Get("sos_target")}: ({sos.TargetIP}) | {L.Get("sos_last")}: {lastIP}</color></size>\n");
                }
            }
            if (proMode) AppendCacheSnapshot(sb);
            SetStatsText(sb.ToString());
        }

        /// <summary>
        /// 专业模式底部的缓存快照。主显示改成"多数派区服"——区服才是大家在不在一起的决定变量，
        /// IP 只是该区服里房间落到了哪台机器上，作为附注保留。
        /// </summary>
        private void AppendCacheSnapshot(StringBuilder sb)
        {
            sb.Append($"<align=\"center\"><color={C_TEXT}>------------------</color></align>\n");
            float ago = Time.unscaledTime - NetworkManager.LastScanTime;
            sb.Append($"<size=75%><color={C_TEXT}>{L.Get("cache_snapshot")} ({ago:F0}{L.Get("seconds_ago")})</color>\n");

            // ① 决策：mod 当前认为该连哪台、走的哪条分支。
            //    决策链只在客机侧跑（房主走 HandleHostLogic），所以房主不显示这行，
            //    否则会拿初值凑出一个"跟房主 → 盲连"的假结论。
            if (!PhotonNetwork.IsMasterClient)
            {
                string target = NetworkManager.TargetGameServer;
                bool everDecided = !string.IsNullOrEmpty(target) || NetworkManager.IsBlindConnect || NetworkManager.TotalRetryCount > 0;
                sb.Append($"<color={C_TEXT}>{L.Get("snap_decision")} </color>");
                if (!everDecided)
                {
                    sb.Append($"<color={C_TEXT}>{L.Get("snap_no_decision")}</color>\n");
                }
                else
                {
                    sb.Append($"<color={C_GREEN}>{NetworkManager.CurrentDecisionMode}</color>");
                    sb.Append($"<color={C_TEXT}> → {(string.IsNullOrEmpty(target) ? L.Get("snap_blind") : target)}</color>\n");
                }

                int rCnt2;
                string majRegion2 = NetworkManager.GetMajorityRegion(out rCnt2);
                if (!string.IsNullOrEmpty(majRegion2))
                {
                    string majVoiceIP = NetworkManager.PlayerCache.Values
                        .Where(x => x != null && x.VoiceRegion == majRegion2 && !string.IsNullOrEmpty(x.IP))
                        .GroupBy(x => x.IP)
                        .OrderByDescending(x => x.Count())
                        .ThenBy(x => x.Key, StringComparer.Ordinal)
                        .Select(x => x.Key)
                        .FirstOrDefault();
                    sb.Append($"<color={C_TEXT}>{L.Get("majority_region", DisplayRegion(majRegion2), majVoiceIP ?? L.Get("unknown"), rCnt2)}</color>\n");
                }
            }

            int rt, rf, rw;
            NetworkManager.GetRetryStats(out rt, out rf, out rw);
            string retryColor = (rt > 0 || rf > 0) ? C_YELLOW : C_TEXT;
            sb.Append($"<color={C_TEXT}>{L.Get("snap_retry")} </color><color={retryColor}>{L.Get("snap_retry_fmt", rt, rf, rw)}</color>\n");

            // ② 两个口径的人数：语音房实际 vs 分类判定。不一致就是判定漂移
            int joined, total;
            GetPresenceCounts(out joined, out total);
            int actual = (NetworkManager.punVoice != null && NetworkManager.punVoice.Client != null
                && NetworkManager.punVoice.Client.CurrentRoom != null)
                ? NetworkManager.punVoice.Client.CurrentRoom.Players.Count : 0;
            int ghosts = NetworkManager.GetGhostCount();
            string vrColor = (actual == joined) ? C_TEXT : C_YELLOW;
            sb.Append($"<color={C_TEXT}>{L.Get("snap_voiceroom")} </color><color={vrColor}>{L.Get("snap_voiceroom_fmt", actual, joined, total, ghosts)}</color>\n");

            // ③ 房间码 / 语音房名：语音房名应恒等于「房间码_voice_」，不等就是还挂在上一个房。
            //    `_voice_` 就是完整后缀，后面本来不该有内容。
            string room = (PhotonNetwork.CurrentRoom != null) ? PhotonNetwork.CurrentRoom.Name : null;
            string vroom = NetworkManager.LocalVoiceRoomName;
            bool? nameOK = PanelText.RoomNamesMatch(room, vroom);
            string nameColor = nameOK == false ? C_RED : C_TEXT;
            sb.Append($"<color={nameColor}>{L.Get("snap_roomcode")}\n{PanelText.Literal(string.IsNullOrEmpty(room) ? L.Get("unknown") : room)}</color>\n");
            sb.Append($"<color={nameColor}>{L.Get("snap_voicename")}\n{PanelText.Literal(string.IsNullOrEmpty(vroom) ? L.Get("unknown") : vroom)}</color>\n");
            if (nameOK == false) sb.Append($"<color={C_RED}>{L.Get("room_name_mismatch")}</color>\n");
            else if (!nameOK.HasValue) sb.Append($"<color={C_YELLOW}>{L.Get("room_name_pending")}</color>\n");

            sb.Append($"<color={C_TEXT}>{L.Get("snap_forced")} {(RegionControl.IsAuto ? RegionControl.AUTO : DisplayRegion(RegionControl.Configured))}</color>\n");

            int rCnt;
            string majRegion = NetworkManager.GetMajorityRegion(out rCnt);
            // 多数派区服已经跟着决策行显示了（客机视角），这里只列"不在多数派那一档"的人
            // 只按区服分组。原来的键是 VoiceRegion ?? IP，混用两种东西：只上报了 IP、没上报区服的人
            // 拿一个 IP 当键去和 majRegion（区服）比，永远对不上，于是每次都被列进"不在多数派"里。
            // 本机缓存现在会写入实际区服；其他没有区服字段的人只代表“未上报”，不能据此断言未连接。
            var groups = NetworkManager.PlayerCache.GroupBy(x => x.Value.VoiceRegion ?? "");
            foreach (var g in groups)
            {
                if (g.Key == majRegion) continue;
                string label = string.IsNullOrEmpty(g.Key) ? L.Get("region_not_reported") : DisplayRegion(g.Key);
                var names = g.Select(x => PanelText.Literal(x.Value.PlayerName)).Take(3);
                sb.Append($"<color={C_TEXT}> - {label}: {string.Join(",", names)}</color>\n");
            }
            if (NetworkManager.HostHistory.Count > 0)
                sb.Append($"<color={C_TEXT}>{L.Get("history")}</color> {PanelText.Literal(NetworkManager.HostHistory[NetworkManager.HostHistory.Count - 1])}\n");
            sb.Append("</size>");
        }

        // ... (其余方法保持不变)
        private string GetClientStateLocalized(ClientState state) { switch (state) { case ClientState.PeerCreated: return L.Get("cs_initializing"); case ClientState.Authenticating: return L.Get("cs_authenticating"); case ClientState.Authenticated: return L.Get("cs_authenticated"); case ClientState.Joining: return L.Get("cs_joining"); case ClientState.Joined: return L.Get("cs_joined"); case ClientState.Disconnecting: return L.Get("cs_disconnecting"); case ClientState.Disconnected: return L.Get("cs_disconnected"); case ClientState.ConnectingToGameServer: return L.Get("cs_connecting_game"); case ClientState.ConnectingToMasterServer: return L.Get("cs_connecting_master"); case ClientState.ConnectingToNameServer: return L.Get("cs_connecting_name"); default: return state.ToString(); } }
        private void BuildPlayerEntry(StringBuilder sb, in PlayerClass d, bool pro, float alignX)
        {
            string statusTag;
            if (d.IsLocal) statusTag = FormatStatusTag(L.Get("label_local"), C_TEXT);
            else { string label, color; GetVerdictTag(d.Verdict, out label, out color); statusTag = FormatStatusTag(label, color); }

            string prefix = d.IsHost ? $"<color={C_GOLD}>{PanelText.Literal(VoiceFix.HostSymbol.Value)} </color>" : "";
            string leading = statusTag + " " + prefix;
            float leadingWidth = MeasureText(leading, 80);
            float panelWidth = statsText.rectTransform.rect.width;
            string ping = "";
            if (d.Ping > 0)
            {
                ping = $"<color={C_TEXT}>| </color><mspace=0.6em>{FmtPingSlot(d.Ping)}";
                if (d.VoicePing > 0) ping += $"<color={C_TEXT}> - </color>{FmtPingSlot(d.VoicePing)}";
                ping += "</mspace>";
            }
            bool inline = alignX > 0 && leadingWidth + MeasureText("名字…", 80) + 8f <= alignX
                && alignX + MeasureText(ping, 80) <= panelWidth;
            float nameWidth = (inline && ping.Length > 0 ? alignX - 8f : panelWidth) - leadingWidth;
            // At very narrow widths keep the name readable on its own line, then the latency below.
            if (nameWidth < MeasureText("…", 80))
            {
                leading += "\n";
                nameWidth = panelWidth;
                inline = false;
            }
            int maxWeight = VoiceFix.MaxTotalLength != null ? VoiceFix.MaxTotalLength.Value : 26;
            string name = PanelText.FitName(d.Name, nameWidth, maxWeight, text => MeasureText(text, 80));
            string pingBreak = inline ? $"<pos={alignX.ToString(CultureInfo.InvariantCulture)}>" : "\n";
            sb.Append($"<size=80%>{leading}<color={C_GREEN}>{name}</color>{(ping.Length > 0 ? pingBreak + ping : "")}</size>\n");

            // 第二行：本机始终有；其余只在需要解释的状态下出（等待/跨区/断开）。
            // 同步和错位不出——错位≈连接正常，同步没什么要解释的。
            bool needLine2 = d.IsLocal
                          || d.Verdict == Verdict.Connecting
                          || d.Verdict == Verdict.Abnormal
                          || d.Verdict == Verdict.Disconnected;
            if (!needLine2) return;

            string line2, lineColor;
            BuildLine2(d, pro, out line2, out lineColor);
            if (string.IsNullOrEmpty(line2)) return;
            sb.Append($"<voffset=0.26em><size=60%>    » {line2}</size></voffset>\n");
        }

        /// <summary>
        /// 第二行内容：一句话说清"这人的语音落在哪"或"为什么判不了"。
        /// 本机连错时那个 IP 无视专业模式强制显示——它是排查的起点。
        /// </summary>
        private void BuildLine2(in PlayerClass d, bool pro, out string text, out string color)
        {
            color = C_TEXT;
            string myIP = GetCurrentIP();
            bool localBroken = string.IsNullOrEmpty(myIP);   // 本机没连上语音，对别人的判定就不可信

            if (d.IsLocal)
            {
                if (string.IsNullOrEmpty(myIP))
                {
                    // 没连上：显示连接进度而不是编一个区服出来
                    var c = GetVoiceClient();
                    string st = (c != null) ? GetClientStateLocalized(c.State) : L.Get("detail_connecting_local");
                    text = $"<color={C_YELLOW}>{st}</color>";
                    return;
                }
                NetworkManager.RoomVoiceSource src; int rep;
                string roomVoice = NetworkManager.GetRoomVoiceServer(out src, out rep);
                bool match = string.IsNullOrEmpty(roomVoice) || roomVoice == myIP;
                bool isolated = NetworkManager.IsLocalIsolated();
                // 房主连上了就是金标准：平铺直叙，不上色不打勾
                if (PhotonNetwork.IsMasterClient && !isolated)
                {
                    text = $"<color={C_TEXT}>{L.Get("line2_voice_server")} {DisplayRegion(d.VoiceRegion)}</color>";
                    if (pro) text += $"<color={C_TEXT}> ({myIP})</color>";
                    return;
                }
                text = FormatVoiceServerLine(d.VoiceRegion, myIP, match && !isolated, pro || !match || isolated);
                if (isolated) text += $"  <color={C_RED}>{L.Get("voice_only_local")}</color>";
                return;
            }

            // 装了 mod 的人在连接过程中：直接报他自报的细分状态
            if (d.RemoteState != 0)
            {
                ClientState cs = (ClientState)d.RemoteState;
                if (cs != ClientState.Joined && cs != ClientState.Disconnected)
                {
                    text = $"<color={C_YELLOW}>{GetClientStateLocalized(cs)}</color>";
                    return;
                }
            }

            if (d.Verdict == Verdict.Connecting)
            {
                text = $"<color={C_YELLOW}>{L.Get("newjoin_connecting")}</color>";
                return;
            }

            // 跨区：他自报了语音服，和本机不一致
            if (d.Verdict == Verdict.Abnormal && !string.IsNullOrEmpty(d.IP))
            {
                string t = FormatVoiceServerLine(d.VoiceRegion, d.IP, false, pro);
                if (IsOlderVersion(d.ModVersion))
                    t += $"  <color={C_YELLOW}>{L.Get("mod_outdated", d.ModVersion)}</color>";
                text = t;
                return;
            }

            // 断开：本机自己也没连上时，这个判定不可信，必须说明
            if (d.Verdict == Verdict.Disconnected)
            {
                text = localBroken && !d.IsMod
                    ? $"<color={C_YELLOW}>{L.Get("line2_cannot_judge")}</color>"
                    : $"<color={C_YELLOW}>{L.Get("line2_not_in_voice")}</color>";
                return;
            }
            text = null;
        }

        /// <summary>「连接的语音服: eu 欧洲 ✓ (IP)」，符号在括号前，符号本身与括号一律米白。</summary>
        private string FormatVoiceServerLine(string region, string ip, bool ok, bool showIP)
        {
            string regionText = string.IsNullOrEmpty(region) ? L.Get("unknown") : DisplayRegion(region);
            var s = new StringBuilder();
            s.Append($"<color={C_TEXT}>{L.Get("line2_voice_server")} </color>");
            s.Append($"<color={C_GREEN}>{regionText}</color>");
            s.Append(ok ? $" <color={C_GREEN}>✓</color>" : $" <color={C_RED}>×</color>");
            if (showIP && !string.IsNullOrEmpty(ip))
                s.Append($"<color={C_TEXT}> (</color><color={(ok ? C_GREEN : C_RED)}>{ip}</color><color={C_TEXT}>)</color>");
            return s.ToString();
        }

        /// <summary>
        /// 队友的 mod 版本是否比本机旧。混装是修好区服绑定后唯一剩下的分叉来源，
        /// 而我们改不了对方的连接，所以能做的就是把"他该更新"这件事说出来。
        /// </summary>
        private bool IsOlderVersion(string otherVersion)
        {
            if (string.IsNullOrEmpty(otherVersion)) return false;
            try
            {
                string a = otherVersion.TrimStart('v', 'V');
                string b = VoiceFix.PLUGIN_VERSION;
                if (a == b) return false;
                var va = new Version(a);
                var vb = new Version(b);
                return va < vb;
            }
            catch (Exception) { return false; }
        }

        private string FormatStatusTag(string text, string colorHex) => $"<color={C_TEXT}>[</color><color={colorHex}>{text}</color><color={C_TEXT}>]</color>";
        private LoadBalancingClient GetVoiceClient() { return NetworkManager.punVoice != null ? NetworkManager.punVoice.Client : null; }
        private string GetCurrentIP() { var c = GetVoiceClient(); return (c != null && c.State == ClientState.Joined) ? c.GameServerAddress : ""; }
        private bool IsVoiceConnected() { var c = GetVoiceClient(); return c != null && c.State == ClientState.Joined; }
        private bool IsConnectingLocal() { var c = GetVoiceClient(); return c != null && (c.State == ClientState.ConnectingToGameServer || c.State == ClientState.Authenticating); }
        private bool IsMismatch() { if (NetworkManager.WrongIPCount > 2) return true; string target = NetworkManager.TargetGameServer; string current = GetCurrentIP(); return !string.IsNullOrEmpty(target) && !string.IsNullOrEmpty(current) && target != current; }
        private bool IsIPMatch(string otherIP) => otherIP == GetCurrentIP();
        private string GetMajorityIP(out int c) { return NetworkManager.GetMajorityIP(out c); }
        private bool IsAllGood()
        {
            // 离线模式没有语音可监控，视为正常——否则面板被"异常自动出现"规则钉死常显。
            if (PhotonNetwork.OfflineMode) return true;
            if (!IsVoiceConnected() || IsMismatch() || NetworkManager.TotalRetryCount > 0) return false;
            // 全员在语音（语音房人数 ≥ 游戏房人数）即视为正常；[错位] 是纯 ID 漂移、音频正常，不阻止自动隐藏。
            int voiceCount = (NetworkManager.punVoice != null && NetworkManager.punVoice.Client != null && NetworkManager.punVoice.Client.CurrentRoom != null)
                ? NetworkManager.punVoice.Client.CurrentRoom.Players.Count : 0;
            int gameCount = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.PlayerCount : 0;
            return voiceCount >= gameCount;
        }
        private static string DisplayRegion(string region) => PanelText.Literal(
            string.IsNullOrEmpty(region) ? L.Get("unknown") : RegionControl.Describe(region));

        private float MeasureText(string text, int percent) => statsText.GetPreferredValues(
            $"<size={percent}%>{text}</size>", Mathf.Infinity, Mathf.Infinity).x;

        private float GetLatencyColumn()
        {
            float width = statsText.rectTransform.rect.width;
            float pingWidth = Mathf.Max(MeasureText("| <mspace=0.6em>9999ms - 9999ms</mspace>", 80),
                MeasureText(L.Get("col_ping"), 100));
            float x = Mathf.Min(VoiceFix.LatencyOffset.Value, width - pingWidth - 4f);
            return x > MeasureText(L.Get("col_status") + "  " + L.Get("col_name"), 100) + 8f ? x : -1f;
        }


        private float lastJoinTimesCleanup = 0f;
        private void CleanupJoinTimes()
        {
            if (Time.unscaledTime - lastJoinTimesCleanup < 60f) return;
            lastJoinTimesCleanup = Time.unscaledTime;
            if (PhotonNetwork.CurrentRoom == null) { joinTimes.Clear(); return; }
            var keysToRemove = new List<int>();
            foreach (var kvp in joinTimes)
            {
                if (PhotonNetwork.CurrentRoom.GetPlayer(kvp.Key) == null)
                    keysToRemove.Add(kvp.Key);
            }
            foreach (var key in keysToRemove) joinTimes.Remove(key);
        }
        private void TrySyncFontFromGame() { if (statsText == null) return; var originalLog = UnityEngine.Object.FindFirstObjectByType<PlayerConnectionLog>(); if (originalLog != null && originalLog.text != null) { statsText.font = originalLog.text.font; statsText.fontSharedMaterial = originalLog.text.fontSharedMaterial; } }
    }
}
