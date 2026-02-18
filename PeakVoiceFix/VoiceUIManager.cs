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
        private class PlayerRenderData { public string Name; public string IP; public int Ping; public bool IsLocal; public bool IsHost; public bool IsAlive; public bool HasModData; public bool IsInVoiceRoom; public int ActorNumber; public byte RemoteState; }

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

        void OnGUI() { if (showDebugConsole) { GUI.skin.window.normal.background = Texture2D.blackTexture; GUI.backgroundColor = new Color(0, 0, 0, 0.85f); debugWindowRect = GUI.Window(999, debugWindowRect, DrawDebugWindow, "语音修复调试控制台 (Alt+J) | EvCode:186"); } }
        void DrawDebugWindow(int windowID)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("复制全部")) ExportLogs(false);
            if (GUILayout.Button("导出文件")) ExportLogs(true);
            if (GUILayout.Button("清空")) debugLogs.Clear();
            if (GUILayout.Button("打印语音底层名单")) DumpVoicePlayers();
            GUILayout.EndHorizontal();
            filterScrollPosition = GUILayout.BeginScrollView(filterScrollPosition, GUILayout.Height(40));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(logFilterMode == 0 ? "[★全部]" : "全部", GUILayout.Width(60))) logFilterMode = 0;
            if (GUILayout.Button(logFilterMode == 1 ? "[★本机]" : "本机", GUILayout.Width(60))) logFilterMode = 1;
            if (PhotonNetwork.InRoom) { foreach (var p in PhotonNetwork.PlayerList) { if (p.IsLocal) continue; string fixedName = NetworkManager.GetPlayerName(p.ActorNumber); string nameShort = fixedName.Length > 9 ? fixedName.Substring(0, 9) : fixedName; string btnLabel = (logFilterMode == -1 && targetActorNumber == p.ActorNumber) ? $"[★{nameShort}]" : nameShort; if (GUILayout.Button(btnLabel, GUILayout.Width(80))) { logFilterMode = -1; targetActorNumber = p.ActorNumber; } } }
            GUILayout.EndHorizontal(); GUILayout.EndScrollView();
            debugScrollPosition = GUILayout.BeginScrollView(debugScrollPosition);
            foreach (var log in debugLogs) { if (logFilterMode == 1 && !log.IsLocal) continue; if (logFilterMode == -1) { string targetName = NetworkManager.GetPlayerName(targetActorNumber); if (log.Player != targetName) continue; } string color = log.IsLocal ? "cyan" : "yellow"; if (log.Player == "System") color = "white"; GUILayout.Label($"<color={color}>[{log.Time}] {log.Player}:</color> {log.Msg}", new GUIStyle(GUI.skin.label) { richText = true }); }
            GUILayout.EndScrollView(); GUI.DragWindow(new Rect(0, 0, 10000, 20)); resizeHandleRect = new Rect(debugWindowRect.width - 20, debugWindowRect.height - 20, 20, 20); GUI.Label(resizeHandleRect, "◢"); Event e = Event.current; if (e.type == EventType.MouseDown && resizeHandleRect.Contains(e.mousePosition)) isResizing = true; else if (e.type == EventType.MouseUp) isResizing = false; else if (e.type == EventType.MouseDrag && isResizing) { debugWindowRect.width += e.delta.x; debugWindowRect.height += e.delta.y; if (debugWindowRect.width < 300) debugWindowRect.width = 300; if (debugWindowRect.height < 200) debugWindowRect.height = 200; }
        }

        private void DumpVoicePlayers()
        {
            if (NetworkManager.punVoice == null || NetworkManager.punVoice.Client == null || NetworkManager.punVoice.Client.CurrentRoom == null) { AddLog("System", "客户端未连接", true); return; }
            StringBuilder sb = new StringBuilder();
            var players = NetworkManager.punVoice.Client.CurrentRoom.Players;
            sb.AppendLine($"=== 语音底层名单 (Count: {players.Count}) ===");
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

                string ghostTag = isGhost ? " [幽灵]" : "";
                string verStr = string.IsNullOrEmpty(ver) ? "" : $" | Ver: {ver}";
                sb.AppendLine($" - ID: {id} | Name: {name} | IP: {ip}{verStr}{ghostTag}");
            }
            AddLog("System", sb.ToString(), true);
        }

        private void ExportLogs(bool toFile) { StringBuilder sb = new StringBuilder(); sb.AppendLine($"=== Log Export ({DateTime.Now}) ==="); foreach (var log in debugLogs) sb.AppendLine($"[{log.Time}] {log.Player}: {log.Msg}"); if (toFile) { string path = Path.Combine(Paths.BepInExRootPath, "Log", "BetterVoiceFix_Dump.txt"); try { File.WriteAllText(path, sb.ToString()); AddLog("System", $"已导出: {path}", true); } catch (Exception ex) { AddLog("System", $"失败: {ex.Message}", true); } } else { GUIUtility.systemCopyBuffer = sb.ToString(); AddLog("System", "已复制", true); } }
        void Update()
        {
            if (VoiceFix.ToggleUIKey == null) return;

            // 快捷键处理
            if (Input.GetKeyDown(VoiceFix.ToggleUIKey.Value))
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
            string scene = SceneManager.GetActiveScene().name;
            bool inAirport = (scene == "Airport");
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

            // 字体同步
            bool isBadFont = statsText.font == null || statsText.font.name.Contains("Liberation");
            if (isBadFont || Time.unscaledTime - lastFontRetryTime > 2f)
            {
                lastFontRetryTime = Time.unscaledTime;
                TrySyncFontFromGame();
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

            // 判断字符串是否为 "右侧"
            bool isRight = (VoiceFix.UIPositionSide.Value == "右侧");

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

        public void TriggerNotification(string playerName) { notificationMsg = $"<color={C_TEXT}>{playerName}:</color> {FormatStatusTag("连接断开", C_YELLOW)}"; notificationExpiry = Time.unscaledTime + 5f; }
        public void ShowStatsTemporary() { notificationMsg = $"<color={C_YELLOW}>[系统] 手动操作...</color>"; notificationExpiry = Time.unscaledTime + 5f; }
        private string GetLocalizedState(ClientState state) { switch (state) { case ClientState.Joined: return "已连接"; case ClientState.Disconnected: return "已断开"; default: return state.ToString(); } }
        private string GetMyStateRaw(out string color)
        {
            int voicePlayerCount = 0; int total = 0;
            GetVoiceCounts(out voicePlayerCount, out total);
            if (IsVoiceConnected())
            {
                if (voicePlayerCount > 1) { color = C_GREEN; return "同步"; }
                if (voicePlayerCount == 1 && PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.PlayerCount > 1)
                { color = C_YELLOW; return "孤立"; }
                color = C_GREEN; return "同步";
            }
            if (IsConnectingLocal()) { color = C_YELLOW; return "连接中"; }
            color = C_RED; return "断开";
        }

        private void AppendCommonStats(StringBuilder sb, bool forceShow)
        {
            bool isSinglePlayer = PhotonNetwork.OfflineMode || (PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.MaxPlayers == 1);
            if (isSinglePlayer) { if (!wasSinglePlayer) { singlePlayerEnterTime = Time.unscaledTime; wasSinglePlayer = true; } } else { wasSinglePlayer = false; }
            bool hideDetails = !forceShow && isSinglePlayer && (Time.unscaledTime > singlePlayerEnterTime + 10f);
            if (!hideDetails)
            {
                if (isSinglePlayer) sb.Append($"<color={C_TEXT}>本机语音: 单人游戏</color>\n");
                else
                {
                    string myColor; string myStateRaw = GetMyStateRaw(out myColor); string displayState = myStateRaw;
                    if (NetworkManager.punVoice != null && NetworkManager.punVoice.Client != null) if (myStateRaw != "同步" && myStateRaw != "孤立") displayState = GetLocalizedState(NetworkManager.punVoice.Client.State);
                    sb.Append($"<color={C_TEXT}>本机语音: </color>");
                    if (IsVoiceConnected()) { if (PhotonNetwork.IsMasterClient) sb.Append($"<color={C_TEXT}>已连接</color>"); else sb.Append($"<color={C_TEXT}>已连接</color> {FormatStatusTag(myStateRaw, myColor)}"); } else sb.Append($"<color={myColor}>{displayState}</color>");
                    sb.Append("\n");

                    int realJoined, total;
                    GetVoiceCounts(out realJoined, out total);
                    int ghostCount = NetworkManager.GetGhostCount();

                    // 修复: 添加 CurrentRoom 空检查，防止 NullReferenceException
                    int n = realJoined + ghostCount;
                    if (NetworkManager.punVoice != null && NetworkManager.punVoice.Client != null
                        && NetworkManager.punVoice.Client.CurrentRoom != null)
                    {
                        n = NetworkManager.punVoice.Client.CurrentRoom.Players.Count;
                    }
                    int N = total;

                    sb.Append($"<color={C_TEXT}>语音连接人数：</color>");

                    string nColor = C_YELLOW;
                    if (n == 1 && N >= 3) nColor = C_RED;
                    else if (ghostCount > 0) nColor = C_GHOST_GREEN;
                    else if (n == N) nColor = C_GREEN;

                    sb.Append($"<color={nColor}>{n}</color>");
                    sb.Append($"<color={C_TEXT}>/</color><color={C_TEXT}>{N}</color>");

                    if (ghostCount > 0)
                    {
                        sb.Append($" <color={C_TEXT}>(</color><color={C_GHOST_GREEN}>{ghostCount}</color><color={C_TEXT}> 人ID错位)</color>");
                    }
                    sb.Append("\n");
                }
                if (VoiceFix.ShowPingInNormal != null && VoiceFix.ShowPingInNormal.Value) { int p = PhotonNetwork.GetPing(); string pColor = p < 100 ? C_GREEN : (p < 200 ? C_YELLOW : C_RED); sb.Append($"<color={C_TEXT}>本机延迟: </color><color={pColor}>{p}ms</color>\n"); }
            }
            if (Time.unscaledTime < notificationExpiry) sb.Append($"{notificationMsg}\n");
        }

        private void UpdateContent_Normal() { StringBuilder sb = new StringBuilder(); AppendCommonStats(sb, false); statsText.text = sb.ToString(); }

        private void UpdateContent_Detail()
        {
            StringBuilder sb = new StringBuilder(); bool proMode = VoiceFix.ShowProfessionalInfo.Value; float alignX = VoiceFix.LatencyOffset.Value;

            // [修改] 版本号 v0.3.6
            sb.Append($"<align=\"center\"><size=120%><color={C_TEXT}>语音详细状态 (v0.3.6)</color></size></align>\n");
            sb.Append($"<align=\"center\"><color={C_TEXT}>------------------</color></align>\n");
            string myIP = GetCurrentIP(); string myColor; string myStateRaw = GetMyStateRaw(out myColor);
            sb.Append($"<size=75%><color={C_TEXT}>本机已连服务器:</color> "); if (PhotonNetwork.IsMasterClient && IsVoiceConnected()) { } else { string myStateText = FormatStatusTag(myStateRaw, myColor); sb.Append($"{myStateText} "); }
            if (proMode) sb.Append($" <color={C_TEXT}>{myIP}</color>"); sb.Append("\n");
            sb.Append($"<color={C_TEXT}>房主已连服务器:</color> ");
            if (PhotonNetwork.IsMasterClient)
            {
                int joined, total; GetVoiceCounts(out joined, out total); int abnormal = total - joined; if (abnormal < 0) abnormal = 0;
                sb.Append($"<color={C_TEXT}>[</color><color={C_TEXT}>本机</color><color={C_TEXT}>]</color> "); string syncNumColor = (joined >= total) ? C_GREEN : C_YELLOW; sb.Append($"<color={C_TEXT}>[</color><color={C_TEXT}>同步:</color><color={syncNumColor}>{joined}/{total}</color><color={C_TEXT}>]</color> "); string diffColor = (abnormal > 0) ? C_LOW_SAT_RED : C_TEXT; sb.Append($"<color={C_TEXT}>[</color><color={C_TEXT}>异常:</color><color={diffColor}>{abnormal}</color><color={C_TEXT}>]</color>");
            }
            else
            {
                var host = PhotonNetwork.MasterClient; string hostName = host != null ? NetworkManager.GetPlayerName(host.ActorNumber) : "未知"; string hostIP = ""; if (host != null && NetworkManager.PlayerCache.TryGetValue(host.ActorNumber, out var hCache)) hostIP = hCache.IP; sb.Append($"<color={C_TEXT}>[{Truncate(hostName, 0, false)}]</color>"); if (!string.IsNullOrEmpty(hostIP)) sb.Append($"<color={C_TEXT}>: {hostIP}</color>");
            }
            sb.Append("</size>\n");
            if (PhotonNetwork.IsMasterClient) { string majIP = GetMajorityIP(out int cnt); if (cnt > 2 && !string.IsNullOrEmpty(majIP) && majIP != myIP) sb.Append($"<color={C_YELLOW}><size=85%>⚠ [警告] 多数玩家({cnt}人)在另一频道!</size></color>\n"); }

            int ghostCount = NetworkManager.GetGhostCount();

            List<PlayerRenderData> renderList = new List<PlayerRenderData>();
            if (VoiceFix.EnableVirtualTestPlayer != null && VoiceFix.EnableVirtualTestPlayer.Value) { string fakeNameRaw = VoiceFix.TestPlayerName != null ? VoiceFix.TestPlayerName.Value : "Test"; renderList.Add(new PlayerRenderData { Name = "虚拟玩家1", IP = "", Ping = 0, IsLocal = false, IsHost = false, IsAlive = true, HasModData = false, IsInVoiceRoom = false }); renderList.Add(new PlayerRenderData { Name = fakeNameRaw, IP = myIP, Ping = 50, IsLocal = false, IsHost = false, IsAlive = true, HasModData = true, IsInVoiceRoom = true }); }
            HashSet<int> processedActors = new HashSet<int>();
            if (PhotonNetwork.PlayerList != null)
            {
                foreach (Photon.Realtime.Player p in PhotonNetwork.PlayerList)
                {
                    int actorNr = p.ActorNumber; processedActors.Add(actorNr); string ip = ""; int ping = 0; bool hasData = false; bool isLocal = p.IsLocal; bool isHost = p.IsMasterClient; bool inVoice = false;
                    if (NetworkManager.punVoice != null && NetworkManager.punVoice.Client != null && NetworkManager.punVoice.Client.CurrentRoom != null) inVoice = NetworkManager.punVoice.Client.CurrentRoom.Players.ContainsKey(actorNr);
                    if (isLocal) { ip = GetCurrentIP(); ping = PhotonNetwork.GetPing(); hasData = true; inVoice = IsVoiceConnected(); } else { object ipObj, pingObj; if (p.CustomProperties.TryGetValue("PVF_IP", out ipObj)) ip = (string)ipObj; if (p.CustomProperties.TryGetValue("PVF_Ping", out pingObj)) ping = (int)pingObj; if (!string.IsNullOrEmpty(ip)) hasData = true; }
                    string fixedName = NetworkManager.GetPlayerName(actorNr); byte rState = 0; if (NetworkManager.PlayerCache.ContainsKey(actorNr)) rState = NetworkManager.PlayerCache[actorNr].RemoteState;
                    renderList.Add(new PlayerRenderData { Name = fixedName, IP = ip, Ping = ping, IsLocal = isLocal, IsHost = isHost, IsAlive = true, HasModData = hasData, IsInVoiceRoom = inVoice, ActorNumber = actorNr, RemoteState = rState });
                }
            }
            foreach (var kvp in NetworkManager.PlayerCache) { int actorNr = kvp.Key; if (processedActors.Contains(actorNr)) continue; if (Time.unscaledTime - kvp.Value.LastSeenTime > 5f) continue; renderList.Add(new PlayerRenderData { Name = kvp.Value.PlayerName, IP = kvp.Value.IP, Ping = 0, IsLocal = false, IsHost = false, IsAlive = false, HasModData = true, IsInVoiceRoom = false, ActorNumber = actorNr, RemoteState = kvp.Value.RemoteState }); }
            renderList.Sort((a, b) => { return b.IsLocal.CompareTo(a.IsLocal); });
            sb.Append($"<line-height=105%>"); foreach (var d in renderList) BuildPlayerEntry(sb, d.Name, d.IP, d.Ping, d.IsLocal, d.IsHost, proMode, alignX, d.IsAlive, d.HasModData, d.IsInVoiceRoom, ghostCount > 0, d.ActorNumber, d.RemoteState); sb.Append("</line-height>");

            sb.Append($"<align=\"center\"><color={C_TEXT}>------------------</color></align>\n");
            AppendCommonStats(sb, true);

            if (NetworkManager.ActiveSOSList.Count > 0) { sb.Append($"<align=\"center\"><color={C_TEXT}>------------------</color></align>\n"); sb.Append($"<color={C_YELLOW}>[SOS快照]</color>\n"); string majIP = GetMajorityIP(out int majCnt); string hostStatus = (PhotonNetwork.IsMasterClient || IsIPMatch(majIP)) ? "同步" : majIP; sb.Append($"<size=80%><color={C_TEXT}>多数派: {majIP} ({majCnt}人)</color></size>\n"); foreach (var sos in NetworkManager.ActiveSOSList) { sb.Append($"<size=80%><color={C_RED}>检测到 {sos.PlayerName} 掉线</color></size>\n"); string lastIP = string.IsNullOrEmpty(sos.OriginIP) ? "未知" : sos.OriginIP; sb.Append($"  <size=80%><color={C_TEXT}>目标: ({sos.TargetIP}) | 上次: {lastIP}</color></size>\n"); } }
            if (proMode) { sb.Append($"<align=\"center\"><color={C_TEXT}>------------------</color></align>\n"); string majIP = GetMajorityIP(out int cnt); float ago = Time.unscaledTime - NetworkManager.LastScanTime; sb.Append($"<size=80%><color={C_TEXT}>[缓存快照] ({ago:F0}秒前)</color>\n"); sb.Append($"<color={C_TEXT}>多数派服务器:</color> <color={C_TEXT}>{majIP}</color> <color={C_TEXT}>({cnt}人)</color>\n"); var groups = NetworkManager.PlayerCache.GroupBy(x => x.Value.IP); foreach (var g in groups) { if (g.Key == majIP) continue; string ipLabel = string.IsNullOrEmpty(g.Key) ? "未连接" : g.Key; var names = g.Select(x => x.Value.PlayerName).Take(3); string nameList = string.Join(",", names); sb.Append($"<color={C_TEXT}> - {ipLabel}: {nameList}</color>\n"); } if (NetworkManager.HostHistory.Count > 0) sb.Append($"<color={C_TEXT}>[历史]</color> {NetworkManager.HostHistory[NetworkManager.HostHistory.Count - 1]}\n"); sb.Append("</size>"); }
            statsText.text = sb.ToString();
        }

        // ... (其余方法保持不变)
        private string GetClientStateLocalized(ClientState state) { switch (state) { case ClientState.PeerCreated: return "初始化中..."; case ClientState.Authenticating: return "验证中..."; case ClientState.Authenticated: return "已验证"; case ClientState.Joining: return "加入中..."; case ClientState.Joined: return "已连接"; case ClientState.Disconnecting: return "断开中..."; case ClientState.Disconnected: return "断开"; case ClientState.ConnectingToGameServer: return "连接到游戏服务器中..."; case ClientState.ConnectingToMasterServer: return "连接到主服务器中..."; case ClientState.ConnectingToNameServer: return "连接到名称服务器中..."; default: return state.ToString(); } }
        private void BuildPlayerEntry(StringBuilder sb, string name, string ip, int ping, bool isLocal, bool isHost, bool pro, float alignX, bool isAlive, bool hasModData, bool isInVoiceRoom, bool hasGhosts, int actorNumber = -1, byte remoteState = 0)
        {
            int prefixWeight = 0;
            if (!hasModData)
            {
                string prefixNoMod = isHost ? $"<color={C_GOLD}>{VoiceFix.HostSymbol.Value} </color>" : ""; string statusLabel = "未知"; string statusColor = C_TEXT; bool isConnectingState = false; string connectingDetail = "";
                if (isInVoiceRoom) { statusLabel = "已连接"; statusColor = C_PALE_GREEN; }
                else if (remoteState != 0) { ClientState cState = (ClientState)remoteState; if (cState == ClientState.Disconnected || cState == ClientState.Disconnecting) { statusLabel = "断开"; statusColor = C_RED; } else if (cState == ClientState.Joined) { statusLabel = "已连接"; statusColor = C_PALE_GREEN; } else { statusLabel = "连接中"; statusColor = C_YELLOW; isConnectingState = true; connectingDetail = GetClientStateLocalized(cState); if (actorNumber != -1) joinTimes[actorNumber] = Time.unscaledTime; } }
                else
                {
                    if (actorNumber != -1)
                    {
                        if (!joinTimes.ContainsKey(actorNumber)) joinTimes[actorNumber] = Time.unscaledTime;
                        if (Time.unscaledTime - joinTimes[actorNumber] < 25f) { statusLabel = "连接中"; statusColor = C_YELLOW; }
                        else
                        {
                            if (hasGhosts) { statusLabel = "错位"; statusColor = C_GHOST_GREEN; }
                            else { statusLabel = "断开"; statusColor = C_RED; }
                        }
                    }
                }
                prefixWeight = 8; string truncName = Truncate(name, prefixWeight, isHost); sb.Append($"{FormatStatusTag(statusLabel, statusColor)} {prefixNoMod}<size=100%><color={C_GREEN}>{truncName}</color></size>\n"); if (isConnectingState && pro) { sb.Append($"<voffset=0.17em><size=80%><color={C_TEXT}>  » {connectingDetail}</color></size></voffset>\n"); }
                return;
            }
            bool isLinked = !string.IsNullOrEmpty(ip); string statusLabel2 = ""; string statusColor2 = C_GREEN; bool isConnecting = false;
            if (!isLinked) { if (isAlive && ping > 0) { statusLabel2 = "连接中"; statusColor2 = C_YELLOW; isConnecting = true; prefixWeight = 8; } else { statusLabel2 = "断开"; statusColor2 = C_RED; prefixWeight = 6; } } else if (IsIPMatch(ip)) { statusLabel2 = "同步"; statusColor2 = C_GREEN; prefixWeight = 6; } else { statusLabel2 = "异常"; statusColor2 = C_LOW_SAT_RED; prefixWeight = 6; }
            if (isLocal) { prefixWeight = 6; if (IsConnectingLocal()) isConnecting = true; }
            if (!isAlive && !isLocal) { statusLabel2 = "已退"; statusColor2 = C_GREY; prefixWeight = 6; }
            string statusTag = FormatStatusTag(statusLabel2, statusColor2); string prefix = isHost ? $"<color={C_GOLD}>{VoiceFix.HostSymbol.Value} </color>" : ""; if (isLocal) statusTag = $"<color={C_TEXT}>[</color><color={C_TEXT}>本机</color><color={C_TEXT}>]</color>";
            string truncatedName = Truncate(name, prefixWeight, isHost); string nameColor = (!isAlive && !isLocal) ? C_GREY : C_GREEN; string pingStr = ""; string pingColor = ping < 100 ? C_GREEN : (ping < 200 ? C_YELLOW : C_RED); if (ping > 0 || (isAlive && string.IsNullOrEmpty(ip))) pingStr = $"<pos={alignX}><color={C_TEXT}>| 延迟:</color><color={pingColor}>{ping}ms</color>";
            sb.Append($"{statusTag} {prefix}<size=100%><color={nameColor}>{truncatedName}</color></size>{pingStr}\n");
            if (pro && !isLocal)
            {
                string ipLabel = ip; string statePrefix = "已连接";
                if (remoteState != 0 && (ClientState)remoteState != ClientState.Joined && (ClientState)remoteState != ClientState.Disconnected) { ipLabel = GetClientStateLocalized((ClientState)remoteState); statePrefix = "状态"; sb.Append($"<voffset=0.17em><size=80%><color={C_TEXT}>  » {ipLabel}</color></size></voffset>\n"); }
                else { string lbl = isConnecting ? "(等待数据...)" : (string.IsNullOrEmpty(ip) ? "N/A" : ip); if (isConnecting && string.IsNullOrEmpty(ip)) lbl = "<color=grey>获取中...</color>"; string pfx = isConnecting ? "正在连接" : "已连入语音服"; sb.Append($"<voffset=0.17em><size=80%><color={C_TEXT}>  » {pfx}: {lbl}</color></size></voffset>\n"); }
            }
            else if (pro && isLocal && isConnecting) { string localState = "连接中..."; if (NetworkManager.punVoice != null && NetworkManager.punVoice.Client != null) localState = GetClientStateLocalized(NetworkManager.punVoice.Client.State); sb.Append($"<voffset=0.17em><size=80%><color={C_TEXT}>  » {localState}</color></size></voffset>\n"); }
        }

        private void GetVoiceCounts(out int joined, out int total)
        {
            joined = 0; total = 0;
            if (PhotonNetwork.PlayerList != null)
            {
                total = PhotonNetwork.PlayerList.Length;
                if (NetworkManager.punVoice != null && NetworkManager.punVoice.Client != null && NetworkManager.punVoice.Client.CurrentRoom != null)
                {
                    foreach (var id in NetworkManager.punVoice.Client.CurrentRoom.Players.Keys)
                    {
                        if (!NetworkManager.IsGhost(id)) joined++;
                    }
                }
                else
                {
                    foreach (var p in PhotonNetwork.PlayerList) { if (p.IsLocal) { if (IsVoiceConnected()) joined++; } else if (NetworkManager.PlayerCache.TryGetValue(p.ActorNumber, out var c) && !string.IsNullOrEmpty(c.IP)) joined++; }
                }
            }
        }

        private string FormatStatusTag(string text, string colorHex) => $"<color={C_TEXT}>[</color><color={colorHex}>{text}</color><color={C_TEXT}>]</color>";
        private LoadBalancingClient GetVoiceClient() { if (NetworkManager.punVoice != null) return NetworkManager.punVoice.Client; var obj = GameObject.Find("VoiceClient"); if (obj != null) { var v = obj.GetComponent<PunVoiceClient>(); if (v != null) return v.Client; } return null; }
        private string GetCurrentIP() { var c = GetVoiceClient(); return (c != null && c.State == ClientState.Joined) ? c.GameServerAddress : ""; }
        private bool IsVoiceConnected() { var c = GetVoiceClient(); return c != null && c.State == ClientState.Joined; }
        private bool IsConnectingLocal() { var c = GetVoiceClient(); return c != null && (c.State == ClientState.ConnectingToGameServer || c.State == ClientState.Authenticating); }
        private bool IsMismatch() { if (NetworkManager.WrongIPCount > 2) return true; string target = NetworkManager.TargetGameServer; string current = GetCurrentIP(); return !string.IsNullOrEmpty(target) && !string.IsNullOrEmpty(current) && target != current; }
        private bool IsIPMatch(string otherIP) => otherIP == GetCurrentIP();
        private string GetMajorityIP(out int c) { return NetworkManager.GetMajorityIP(out c); }
        private bool IsAllGood()
        {
            if (!IsVoiceConnected() || IsMismatch() || NetworkManager.TotalRetryCount > 0) return false;
            // 检查是否所有人都已连接且无幽灵
            int joined, total;
            GetVoiceCounts(out joined, out total);
            int ghostCount = NetworkManager.GetGhostCount();
            return joined >= total && ghostCount == 0;
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