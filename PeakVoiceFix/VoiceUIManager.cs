using BepInEx;
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
        private bool isDetailMode = false;
        private float detailModeExpiry = 0f;
        private float lastFontRetryTime;
        private string notificationMsg = "";
        private float notificationExpiry = 0f;
        private bool wasSinglePlayer = false;
        private float singlePlayerEnterTime = 0f;
        // 每个玩家的最终结论。Local=本机；其余对应 [] 里的标签。
        private enum Verdict { Local, Synced, Abnormal, Disconnected, Connecting, Connected, Mismatch }
        private struct PlayerClass
        {
            public int ActorNumber; public string Name; public string IP; public int Ping;
            public bool IsLocal; public bool IsHost; public bool IsMod; public byte RemoteState;
            public Verdict Verdict; public bool IsConnecting;
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
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            statsText = tObj.AddComponent<TextMeshProUGUI>();
            statsText.richText = true;
            statsText.raycastTarget = false;
            statsText.overflowMode = TextOverflowModes.Overflow;
            statsText.textWrappingMode = TextWrappingModes.NoWrap;
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
            filterScrollPosition = GUILayout.BeginScrollView(filterScrollPosition, GUILayout.Height(40));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(logFilterMode == 0 ? $"[★{L.Get("filter_all")}]" : L.Get("filter_all"), GUILayout.Width(60))) logFilterMode = 0;
            if (GUILayout.Button(logFilterMode == 1 ? $"[★{L.Get("filter_local")}]" : L.Get("filter_local"), GUILayout.Width(60))) logFilterMode = 1;
            if (PhotonNetwork.InRoom) { foreach (var p in PhotonNetwork.PlayerList) { if (p.IsLocal) continue; string fixedName = NetworkManager.GetPlayerName(p.ActorNumber); string nameShort = fixedName.Length > 9 ? fixedName.Substring(0, 9) : fixedName; string btnLabel = (logFilterMode == -1 && targetActorNumber == p.ActorNumber) ? $"[★{nameShort}]" : nameShort; if (GUILayout.Button(btnLabel, GUILayout.Width(80))) { logFilterMode = -1; targetActorNumber = p.ActorNumber; } } }
            GUILayout.EndHorizontal(); GUILayout.EndScrollView();
            debugScrollPosition = GUILayout.BeginScrollView(debugScrollPosition);
            foreach (var log in debugLogs) { if (logFilterMode == 1 && !log.IsLocal) continue; if (logFilterMode == -1) { string targetName = NetworkManager.GetPlayerName(targetActorNumber); if (log.Player != targetName) continue; } string color = log.IsLocal ? "cyan" : "yellow"; if (log.Player == "System") color = "white"; GUILayout.Label($"<color={color}>[{log.Time}] {log.Player}:</color> {log.Msg}", new GUIStyle(GUI.skin.label) { richText = true }); }
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

        private void ExportLogs(bool toFile) { StringBuilder sb = new StringBuilder(); sb.AppendLine($"=== Log Export ({DateTime.Now}) ==="); foreach (var log in debugLogs) sb.AppendLine($"[{log.Time}] {log.Player}: {log.Msg}"); if (toFile) { string path = Path.Combine(Paths.BepInExRootPath, "Log", "BetterVoiceFix_Dump.txt"); try { File.WriteAllText(path, sb.ToString()); AddLog("System", $"{L.Get("exported_to")}: {path}", true); } catch (Exception ex) { AddLog("System", $"{L.Get("export_failed")}: {ex.Message}", true); } } else { GUIUtility.systemCopyBuffer = sb.ToString(); AddLog("System", L.Get("copied"), true); } }
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

            // 快捷键处理
            if (Input.GetKeyDown(VoiceFix.GetToggleKey()))
            {
                if (isDetailMode) { isDetailMode = false; detailModeExpiry = 0f; }
                else { isDetailMode = true; detailModeExpiry = Time.unscaledTime + 10f; }
            }
            if ((Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.J))
                showDebugConsole = !showDebugConsole;

            // 详细模式超时自动关闭
            if (isDetailMode && Time.unscaledTime > detailModeExpiry)
                isDetailMode = false;

            // 定期清理 joinTimes（每60秒一次）
            CleanupJoinTimes();

            // 判断是否应该显示 UI
            bool inAirport = (currentSceneName == "Airport");
            bool inRoom = PhotonNetwork.InRoom;
            bool isMenuOpen = VoiceFix.HideOnMenu != null && VoiceFix.HideOnMenu.Value && Cursor.visible;
            bool shouldShow = false;

            if (inRoom && !isMenuOpen)
            {
                if (isDetailMode)
                    shouldShow = true;
                else
                {
                    bool hasNotification = Time.unscaledTime < notificationExpiry;

                    // AutoHideNormal：当所有人连接正常时，自动隐藏简易模式 UI
                    bool autoHide = VoiceFix.AutoHideNormal != null && VoiceFix.AutoHideNormal.Value && IsAllGood();

                    if (inAirport || hasNotification)
                    {
                        if (!autoHide || hasNotification)
                            shouldShow = true;
                    }
                }
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

            // 定时更新 UI 内容
            if (Time.unscaledTime > nextUiUpdateTime)
            {
                if (isDetailMode) UpdateContent_Detail();
                else UpdateContent_Normal();
                UpdateLayout();
                nextUiUpdateTime = Time.unscaledTime + 0.2f;
            }
        }
        public bool IsDetailModeActive() => isDetailMode;

        // [修改] 适配 string 类型的配置
        private void UpdateLayout()
        {
            if (statsText == null || VoiceFix.UIPositionSide == null) return;

            // 判断字符串是否为 "Right"
            bool isRight = (VoiceFix.UIPositionSide.Value == UIPositionEnum.Right);

            float targetX = isRight ? Mathf.Abs(VoiceFix.OffsetX_Right.Value) : Mathf.Abs(VoiceFix.OffsetX_Left.Value);
            float targetY = isRight ? Mathf.Abs(VoiceFix.OffsetY_Right.Value) : Mathf.Abs(VoiceFix.OffsetY_Left.Value);
            RectTransform rt = statsText.rectTransform;
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

        public void TriggerNotification(string playerName) { notificationMsg = $"<color={C_TEXT}>{playerName}:</color> {FormatStatusTag(L.Get("notification_disconnected"), C_YELLOW)}"; notificationExpiry = Time.unscaledTime + 5f; }
        public void ShowStatsTemporary() { notificationMsg = $"<color={C_YELLOW}>{L.Get("manual_operation")}</color>"; notificationExpiry = Time.unscaledTime + 5f; }
        private string GetLocalizedState(ClientState state) { switch (state) { case ClientState.Joined: return L.Get("ls_joined"); case ClientState.Disconnected: return L.Get("ls_disconnected"); default: return state.ToString(); } }
        // ====== 统一分类：每次 UI 刷新算一次，行渲染与聚合计数共用同一结果，杜绝自相矛盾 ======
        private void ClassifyPlayers()
        {
            _classified.Clear();
            if (PhotonNetwork.PlayerList == null) return;

            string myIP = GetCurrentIP();
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
                if (NetworkManager.PlayerCache.TryGetValue(actor, out var ce)) rState = ce.RemoteState;
                if (isLocal) { ip = myIP; ping = PhotonNetwork.GetPing(); }
                else
                {
                    if (p.CustomProperties.TryGetValue("PVF_IP", out var ipObj) && ipObj is string s) ip = s;
                    if (p.CustomProperties.TryGetValue("PVF_Ping", out var pgObj) && pgObj is int pg) ping = pg;
                }
                string name = NetworkManager.GetPlayerName(actor);

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
                    Verdict = v, IsConnecting = connecting
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
                case Verdict.Abnormal: label = L.Get("state_abnormal"); color = C_LOW_SAT_RED; break;
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

        private int CountMismatch()
        {
            int m = 0; foreach (var c in _classified) if (c.Verdict == Verdict.Mismatch) m++; return m;
        }

        private void GetPresenceCounts(out int inVoice, out int total)
        {
            inVoice = 0; total = _classified.Count;
            foreach (var c in _classified) if (IsVerdictInVoice(c)) inVoice++;
        }

        private string GetMyStateRaw(out string color)
        {
            if (IsVoiceConnected())
            {
                // 用真实语音房人数判孤立（铁证）：本机所在语音房只有自己、而游戏房有别人 → 真孤立。
                int voiceCount = (NetworkManager.punVoice != null && NetworkManager.punVoice.Client != null && NetworkManager.punVoice.Client.CurrentRoom != null)
                    ? NetworkManager.punVoice.Client.CurrentRoom.Players.Count : 1;
                bool roomHasOthers = PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.PlayerCount > 1;
                if (roomHasOthers && voiceCount <= 1) { color = C_YELLOW; return L.Get("state_isolated"); }
                color = C_GREEN; return L.Get("state_synced");
            }
            if (IsConnectingLocal()) { color = C_YELLOW; return L.Get("state_connecting"); }
            color = C_RED; return L.Get("state_disconnected");
        }

        private void AppendCommonStats(StringBuilder sb, bool forceShow)
        {
            bool isSinglePlayer = PhotonNetwork.OfflineMode || (PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.PlayerCount <= 1);
            if (isSinglePlayer) { if (!wasSinglePlayer) { singlePlayerEnterTime = Time.unscaledTime; wasSinglePlayer = true; } } else { wasSinglePlayer = false; }
            bool hideDetails = !forceShow && isSinglePlayer && (Time.unscaledTime > singlePlayerEnterTime + 10f);
            if (!hideDetails)
            {
                if (isSinglePlayer) sb.Append($"<color={C_TEXT}>{L.Get("voice_single_player")}</color>\n");
                else
                {
                    string myColor; string myStateRaw = GetMyStateRaw(out myColor); string displayState = myStateRaw;
                    if (NetworkManager.punVoice != null && NetworkManager.punVoice.Client != null) if (myStateRaw != L.Get("state_synced") && myStateRaw != L.Get("state_isolated")) displayState = GetLocalizedState(NetworkManager.punVoice.Client.State);
                    sb.Append($"<color={C_TEXT}>{L.Get("voice_local")}</color>");
                    if (IsVoiceConnected()) { if (PhotonNetwork.IsMasterClient) sb.Append($"<color={C_TEXT}>{L.Get("voice_connected")}</color>"); else sb.Append($"<color={C_TEXT}>{L.Get("voice_connected")}</color> {FormatStatusTag(myStateRaw, myColor)}"); } else sb.Append($"<color={myColor}>{displayState}</color>");
                    sb.Append("\n");

                    int mismatchCount = CountMismatch();
                    int N = _classified.Count;

                    // 修复: 添加 CurrentRoom 空检查，防止 NullReferenceException
                    int n = (NetworkManager.punVoice != null && NetworkManager.punVoice.Client != null
                        && NetworkManager.punVoice.Client.CurrentRoom != null)
                        ? NetworkManager.punVoice.Client.CurrentRoom.Players.Count : 0;

                    sb.Append($"<color={C_TEXT}>{L.Get("voice_count")}</color>");

                    string nColor = C_YELLOW;
                    if (n == 1 && N >= 3) nColor = C_RED;
                    else if (mismatchCount > 0) nColor = C_GHOST_GREEN;
                    else if (n == N) nColor = C_GREEN;

                    sb.Append($"<color={nColor}>{n}</color>");
                    sb.Append($"<color={C_TEXT}>/</color><color={C_TEXT}>{N}</color>");

                    if (mismatchCount > 0)
                    {
                        sb.Append($" <color={C_TEXT}>(</color><color={C_GHOST_GREEN}>{mismatchCount}</color><color={C_TEXT}> {L.Get("id_mismatch")})</color>");
                    }
                    sb.Append("\n");
                }
                if (VoiceFix.ShowPingInNormal != null && VoiceFix.ShowPingInNormal.Value) { int p = PhotonNetwork.GetPing(); string pColor = p < 100 ? C_GREEN : (p < 200 ? C_YELLOW : C_RED); sb.Append($"<color={C_TEXT}>{L.Get("local_ping")}</color><color={pColor}>{p}ms</color>\n"); }
            }
            if (Time.unscaledTime < notificationExpiry) sb.Append($"{notificationMsg}\n");
        }

        private void UpdateContent_Normal() { ClassifyPlayers(); var sb = _sb; sb.Clear(); AppendCommonStats(sb, false); SetStatsText(sb.ToString()); }

        // 仅在文本内容变化时才写 TMP，避免每 0.2s 无谓触发 TMP 网格重建。
        private void SetStatsText(string s)
        {
            if (s == lastStatsText) return;
            lastStatsText = s;
            statsText.text = s;
        }

        private void UpdateContent_Detail()
        {
            ClassifyPlayers();
            var sb = _sb; sb.Clear(); bool proMode = VoiceFix.ShowProfessionalInfo.Value; float alignX = VoiceFix.LatencyOffset.Value;

            sb.Append($"<align=\"center\"><size=120%><color={C_TEXT}>{L.Get("ui_title")} ({VoiceFix.MOD_VERSION})</color></size></align>\n");
            sb.Append($"<align=\"center\"><color={C_TEXT}>------------------</color></align>\n");
            string myIP = GetCurrentIP(); string myColor; string myStateRaw = GetMyStateRaw(out myColor);
            sb.Append($"<size=75%><color={C_TEXT}>{L.Get("local_server")}</color> "); if (PhotonNetwork.IsMasterClient && IsVoiceConnected()) { } else { string myStateText = FormatStatusTag(myStateRaw, myColor); sb.Append($"{myStateText} "); }
            if (proMode) sb.Append($" <color={C_TEXT}>{myIP}</color>"); sb.Append("\n");
            sb.Append($"<color={C_TEXT}>{L.Get("host_server")}</color> ");
            if (PhotonNetwork.IsMasterClient)
            {
                int joined, total; GetPresenceCounts(out joined, out total); int abnormal = total - joined; if (abnormal < 0) abnormal = 0;
                sb.Append($"<color={C_TEXT}>[</color><color={C_TEXT}>{L.Get("label_local")}</color><color={C_TEXT}>]</color> "); string syncNumColor = (joined >= total) ? C_GREEN : C_YELLOW; sb.Append($"<color={C_TEXT}>[</color><color={C_TEXT}>{L.Get("label_sync")}</color><color={syncNumColor}>{joined}/{total}</color><color={C_TEXT}>]</color> "); string diffColor = (abnormal > 0) ? C_LOW_SAT_RED : C_TEXT; sb.Append($"<color={C_TEXT}>[</color><color={C_TEXT}>{L.Get("label_abnormal")}</color><color={diffColor}>{abnormal}</color><color={C_TEXT}>]</color>");
            }
            else
            {
                var host = PhotonNetwork.MasterClient; string hostName = host != null ? NetworkManager.GetPlayerName(host.ActorNumber) : L.Get("unknown"); string hostIP = ""; if (host != null && NetworkManager.PlayerCache.TryGetValue(host.ActorNumber, out var hCache)) hostIP = hCache.IP; sb.Append($"<color={C_TEXT}>[{Truncate(hostName, 0, false)}]</color>"); if (!string.IsNullOrEmpty(hostIP)) sb.Append($"<color={C_TEXT}>: {hostIP}</color>");
            }
            sb.Append("</size>\n");
            if (PhotonNetwork.IsMasterClient)
            {
                string majIP = GetMajorityIP(out int cnt);
                if (cnt >= 2 && !string.IsNullOrEmpty(majIP) && majIP != myIP)
                    sb.Append($"<color={C_YELLOW}><size=85%>{L.Get("warn_majority", cnt)}</size></color>\n");
            }

            // 渲染玩家列表：复用统一分类结果（含每人 Verdict），不再用"IP 非空=hasModData"，也不渲染离场残留。
            var renderList = new List<PlayerClass>(_classified);
            if (VoiceFix.EnableVirtualTestPlayer != null && VoiceFix.EnableVirtualTestPlayer.Value)
            {
                string fakeNameRaw = VoiceFix.TestPlayerName != null ? VoiceFix.TestPlayerName.Value : "Test";
                renderList.Add(new PlayerClass { Name = L.Get("virtual_player"), IP = "", Ping = 0, IsMod = false, Verdict = Verdict.Connecting, IsConnecting = true });
                renderList.Add(new PlayerClass { Name = fakeNameRaw, IP = myIP, Ping = 50, IsMod = true, Verdict = Verdict.Synced });
            }
            renderList.Sort((a, b) => b.IsLocal.CompareTo(a.IsLocal));
            sb.Append($"<line-height=105%>"); foreach (var d in renderList) BuildPlayerEntry(sb, d, proMode, alignX); sb.Append("</line-height>");

            sb.Append($"<align=\"center\"><color={C_TEXT}>------------------</color></align>\n");
            AppendCommonStats(sb, true);

            if (NetworkManager.ActiveSOSList.Count > 0)
            {
                sb.Append($"<align=\"center\"><color={C_TEXT}>------------------</color></align>\n");
                sb.Append($"<color={C_YELLOW}>{L.Get("sos_snapshot")}</color>\n");
                string majIP = GetMajorityIP(out int majCnt);
                sb.Append($"<size=80%><color={C_TEXT}>{L.Get("sos_majority")}: {majIP} ({majCnt}{L.Get("sos_person")})</color></size>\n");

                foreach (var sos in NetworkManager.ActiveSOSList)
                {
                    sb.Append($"<size=80%><color={C_RED}>{L.Get("sos_detected", sos.PlayerName)}</color></size>\n");
                    string lastIP = string.IsNullOrEmpty(sos.OriginIP) ? L.Get("unknown") : sos.OriginIP;
                    sb.Append($"  <size=80%><color={C_TEXT}>{L.Get("sos_target")}: ({sos.TargetIP}) | {L.Get("sos_last")}: {lastIP}</color></size>\n");
                }
            }
            if (proMode) { sb.Append($"<align=\"center\"><color={C_TEXT}>------------------</color></align>\n"); string majIP = GetMajorityIP(out int cnt); float ago = Time.unscaledTime - NetworkManager.LastScanTime; sb.Append($"<size=80%><color={C_TEXT}>{L.Get("cache_snapshot")} ({ago:F0}{L.Get("seconds_ago")})</color>\n"); sb.Append($"<color={C_TEXT}>{L.Get("majority_server")}</color> <color={C_TEXT}>{majIP}</color> <color={C_TEXT}>({cnt}{L.Get("sos_person")})</color>\n"); var groups = NetworkManager.PlayerCache.GroupBy(x => x.Value.IP); foreach (var g in groups) { if (g.Key == majIP) continue; string ipLabel = string.IsNullOrEmpty(g.Key) ? L.Get("not_connected") : g.Key; var names = g.Select(x => x.Value.PlayerName).Take(3); string nameList = string.Join(",", names); sb.Append($"<color={C_TEXT}> - {ipLabel}: {nameList}</color>\n"); } if (NetworkManager.HostHistory.Count > 0) sb.Append($"<color={C_TEXT}>{L.Get("history")}</color> {NetworkManager.HostHistory[NetworkManager.HostHistory.Count - 1]}\n"); sb.Append("</size>"); }
            SetStatsText(sb.ToString());
        }

        // ... (其余方法保持不变)
        private string GetClientStateLocalized(ClientState state) { switch (state) { case ClientState.PeerCreated: return L.Get("cs_initializing"); case ClientState.Authenticating: return L.Get("cs_authenticating"); case ClientState.Authenticated: return L.Get("cs_authenticated"); case ClientState.Joining: return L.Get("cs_joining"); case ClientState.Joined: return L.Get("cs_joined"); case ClientState.Disconnecting: return L.Get("cs_disconnecting"); case ClientState.Disconnected: return L.Get("cs_disconnected"); case ClientState.ConnectingToGameServer: return L.Get("cs_connecting_game"); case ClientState.ConnectingToMasterServer: return L.Get("cs_connecting_master"); case ClientState.ConnectingToNameServer: return L.Get("cs_connecting_name"); default: return state.ToString(); } }
        private void BuildPlayerEntry(StringBuilder sb, in PlayerClass d, bool pro, float alignX)
        {
            int prefixWeight = (d.Verdict == Verdict.Connecting) ? 8 : 6;

            string statusTag;
            if (d.IsLocal) statusTag = FormatStatusTag(L.Get("label_local"), C_TEXT);
            else { string label, color; GetVerdictTag(d.Verdict, out label, out color); statusTag = FormatStatusTag(label, color); }

            string prefix = d.IsHost ? $"<color={C_GOLD}>{VoiceFix.HostSymbol.Value} </color>" : "";
            string truncatedName = Truncate(d.Name, prefixWeight, d.IsHost);

            string pingStr = "";
            if (d.Ping > 0)
            {
                string pingColor = d.Ping < 100 ? C_GREEN : (d.Ping < 200 ? C_YELLOW : C_RED);
                pingStr = $"<pos={alignX}><color={C_TEXT}>| {L.Get("detail_latency")}:</color><color={pingColor}>{d.Ping}ms</color>";
            }
            sb.Append($"{statusTag} {prefix}<size=100%><color={C_GREEN}>{truncatedName}</color></size>{pingStr}\n");

            // 第二行详细态：只有装了 mod 的人（含本机）才有，且需开启"显示详细连接"。没装的人不输出第二行。
            if (!pro || !d.IsMod) return;
            if (d.IsLocal)
            {
                if (d.IsConnecting)
                {
                    string localState = L.Get("detail_connecting_local");
                    if (NetworkManager.punVoice != null && NetworkManager.punVoice.Client != null) localState = GetClientStateLocalized(NetworkManager.punVoice.Client.State);
                    sb.Append($"<voffset=0.17em><size=80%><color={C_TEXT}>  » {localState}</color></size></voffset>\n");
                }
                return;
            }
            string line2;
            if (d.RemoteState != 0 && (ClientState)d.RemoteState != ClientState.Joined && (ClientState)d.RemoteState != ClientState.Disconnected)
                line2 = GetClientStateLocalized((ClientState)d.RemoteState);                       // 连接进行中的细分态
            else if (d.Verdict == Verdict.Disconnected)
                line2 = L.Get("ls_disconnected");                                                   // 断开
            else if (string.IsNullOrEmpty(d.IP))
                line2 = d.IsConnecting ? L.Get("detail_connecting_local") : L.Get("detail_waiting_data");
            else
                line2 = $"{L.Get("detail_joined_voice")}: {d.IP}";                                   // 已连入语音服: ip（异常时即对方所在的别的服务器）
            sb.Append($"<voffset=0.17em><size=80%><color={C_TEXT}>  » {line2}</color></size></voffset>\n");
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
            if (!IsVoiceConnected() || IsMismatch() || NetworkManager.TotalRetryCount > 0) return false;
            // 全员在语音（语音房人数 ≥ 游戏房人数）即视为正常；[错位] 是纯 ID 漂移、音频正常，不阻止自动隐藏。
            int voiceCount = (NetworkManager.punVoice != null && NetworkManager.punVoice.Client != null && NetworkManager.punVoice.Client.CurrentRoom != null)
                ? NetworkManager.punVoice.Client.CurrentRoom.Players.Count : 0;
            int gameCount = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.PlayerCount : 0;
            return voiceCount >= gameCount;
        }
        private string Truncate(string s, int prefixWeight, bool isHost) { if (string.IsNullOrEmpty(s)) return ""; int totalLimit = 26; if (VoiceFix.MaxTotalLength != null) totalLimit = VoiceFix.MaxTotalLength.Value; int nameLimit = totalLimit - prefixWeight; if (nameLimit < 6) nameLimit = 6; if (isHost) nameLimit -= 2; int currentLen = 0; for (int i = 0; i < s.Length; i++) { int charWeight = (s[i] > 255) ? 2 : 1; if (currentLen + charWeight > nameLimit) return s.Substring(0, i) + "..."; currentLen += charWeight; } return s; }

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
