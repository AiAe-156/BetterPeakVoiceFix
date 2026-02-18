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
        private string GetMyStateRaw(out string color)
        {
            int voicePlayerCount = 0; int total = 0;
            GetVoiceCounts(out voicePlayerCount, out total);
            if (IsVoiceConnected())
            {
                if (voicePlayerCount > 1) { color = C_GREEN; return L.Get("state_synced"); }
                if (voicePlayerCount == 1 && PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.PlayerCount > 1)
                { color = C_YELLOW; return L.Get("state_isolated"); }
                color = C_GREEN; return L.Get("state_synced");
            }
            if (IsConnectingLocal()) { color = C_YELLOW; return L.Get("state_connecting"); }
            color = C_RED; return L.Get("state_disconnected");
        }

        private void AppendCommonStats(StringBuilder sb, bool forceShow)
        {
            bool isSinglePlayer = PhotonNetwork.OfflineMode || (PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.MaxPlayers == 1);
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

                    sb.Append($"<color={C_TEXT}>{L.Get("voice_count")}</color>");

                    string nColor = C_YELLOW;
                    if (n == 1 && N >= 3) nColor = C_RED;
                    else if (ghostCount > 0) nColor = C_GHOST_GREEN;
                    else if (n == N) nColor = C_GREEN;

                    sb.Append($"<color={nColor}>{n}</color>");
                    sb.Append($"<color={C_TEXT}>/</color><color={C_TEXT}>{N}</color>");

                    if (ghostCount > 0)
                    {
                        sb.Append($" <color={C_TEXT}>(</color><color={C_GHOST_GREEN}>{ghostCount}</color><color={C_TEXT}> {L.Get("id_mismatch")})</color>");
                    }
                    sb.Append("\n");
                }
                if (VoiceFix.ShowPingInNormal != null && VoiceFix.ShowPingInNormal.Value) { int p = PhotonNetwork.GetPing(); string pColor = p < 100 ? C_GREEN : (p < 200 ? C_YELLOW : C_RED); sb.Append($"<color={C_TEXT}>{L.Get("local_ping")}</color><color={pColor}>{p}ms</color>\n"); }
            }
            if (Time.unscaledTime < notificationExpiry) sb.Append($"{notificationMsg}\n");
        }

        private void UpdateContent_Normal() { StringBuilder sb = new StringBuilder(); AppendCommonStats(sb, false); statsText.text = sb.ToString(); }

        private void UpdateContent_Detail()
        {
            StringBuilder sb = new StringBuilder(); bool proMode = VoiceFix.ShowProfessionalInfo.Value; float alignX = VoiceFix.LatencyOffset.Value;

            // [修改] 版本号 v1.0.0
            sb.Append($"<align=\"center\"><size=120%><color={C_TEXT}>{L.Get("ui_title")} (v1.0.2)</color></size></align>\n");
            sb.Append($"<align=\"center\"><color={C_TEXT}>------------------</color></align>\n");
            string myIP = GetCurrentIP(); string myColor; string myStateRaw = GetMyStateRaw(out myColor);
            sb.Append($"<size=75%><color={C_TEXT}>{L.Get("local_server")}</color> "); if (PhotonNetwork.IsMasterClient && IsVoiceConnected()) { } else { string myStateText = FormatStatusTag(myStateRaw, myColor); sb.Append($"{myStateText} "); }
            if (proMode) sb.Append($" <color={C_TEXT}>{myIP}</color>"); sb.Append("\n");
            sb.Append($"<color={C_TEXT}>{L.Get("host_server")}</color> ");
            if (PhotonNetwork.IsMasterClient)
            {
                int joined, total; GetVoiceCounts(out joined, out total); int abnormal = total - joined; if (abnormal < 0) abnormal = 0;
                sb.Append($"<color={C_TEXT}>[</color><color={C_TEXT}>{L.Get("label_local")}</color><color={C_TEXT}>]</color> "); string syncNumColor = (joined >= total) ? C_GREEN : C_YELLOW; sb.Append($"<color={C_TEXT}>[</color><color={C_TEXT}>{L.Get("label_sync")}</color><color={syncNumColor}>{joined}/{total}</color><color={C_TEXT}>]</color> "); string diffColor = (abnormal > 0) ? C_LOW_SAT_RED : C_TEXT; sb.Append($"<color={C_TEXT}>[</color><color={C_TEXT}>{L.Get("label_abnormal")}</color><color={diffColor}>{abnormal}</color><color={C_TEXT}>]</color>");
            }
            else
            {
                var host = PhotonNetwork.MasterClient; string hostName = host != null ? NetworkManager.GetPlayerName(host.ActorNumber) : L.Get("unknown"); string hostIP = ""; if (host != null && NetworkManager.PlayerCache.TryGetValue(host.ActorNumber, out var hCache)) hostIP = hCache.IP; sb.Append($"<color={C_TEXT}>[{Truncate(hostName, 0, false)}]</color>"); if (!string.IsNullOrEmpty(hostIP)) sb.Append($"<color={C_TEXT}>: {hostIP}</color>");
            }
            sb.Append("</size>\n");
            if (PhotonNetwork.IsMasterClient) { string majIP = GetMajorityIP(out int cnt); if (cnt > 2 && !string.IsNullOrEmpty(majIP) && majIP != myIP) sb.Append($"<color={C_YELLOW}><size=85%>{L.Get("warn_majority", cnt)}</size></color>\n"); }

            int ghostCount = NetworkManager.GetGhostCount();

            List<PlayerRenderData> renderList = new List<PlayerRenderData>();
            if (VoiceFix.EnableVirtualTestPlayer != null && VoiceFix.EnableVirtualTestPlayer.Value) { string fakeNameRaw = VoiceFix.TestPlayerName != null ? VoiceFix.TestPlayerName.Value : "Test"; renderList.Add(new PlayerRenderData { Name = L.Get("virtual_player"), IP = "", Ping = 0, IsLocal = false, IsHost = false, IsAlive = true, HasModData = false, IsInVoiceRoom = false }); renderList.Add(new PlayerRenderData { Name = fakeNameRaw, IP = myIP, Ping = 50, IsLocal = false, IsHost = false, IsAlive = true, HasModData = true, IsInVoiceRoom = true }); }
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

            if (NetworkManager.ActiveSOSList.Count > 0) { sb.Append($"<align=\"center\"><color={C_TEXT}>------------------</color></align>\n"); sb.Append($"<color={C_YELLOW}>{L.Get("sos_snapshot")}</color>\n"); string majIP = GetMajorityIP(out int majCnt); string hostStatus = (PhotonNetwork.IsMasterClient || IsIPMatch(majIP)) ? L.Get("state_synced") : majIP; sb.Append($"<size=80%><color={C_TEXT}>{L.Get("sos_majority")}: {majIP} ({majCnt}{L.Get("sos_person")})</color></size>\n"); foreach (var sos in NetworkManager.ActiveSOSList) { sb.Append($"<size=80%><color={C_RED}>{L.Get("sos_detected", sos.PlayerName)}</color></size>\n"); string lastIP = string.IsNullOrEmpty(sos.OriginIP) ? L.Get("unknown") : sos.OriginIP; sb.Append($"  <size=80%><color={C_TEXT}>{L.Get("sos_target")}: ({sos.TargetIP}) | {L.Get("sos_last")}: {lastIP}</color></size>\n"); } }
            if (proMode) { sb.Append($"<align=\"center\"><color={C_TEXT}>------------------</color></align>\n"); string majIP = GetMajorityIP(out int cnt); float ago = Time.unscaledTime - NetworkManager.LastScanTime; sb.Append($"<size=80%><color={C_TEXT}>{L.Get("cache_snapshot")} ({ago:F0}{L.Get("seconds_ago")})</color>\n"); sb.Append($"<color={C_TEXT}>{L.Get("majority_server")}</color> <color={C_TEXT}>{majIP}</color> <color={C_TEXT}>({cnt}{L.Get("sos_person")})</color>\n"); var groups = NetworkManager.PlayerCache.GroupBy(x => x.Value.IP); foreach (var g in groups) { if (g.Key == majIP) continue; string ipLabel = string.IsNullOrEmpty(g.Key) ? L.Get("not_connected") : g.Key; var names = g.Select(x => x.Value.PlayerName).Take(3); string nameList = string.Join(",", names); sb.Append($"<color={C_TEXT}> - {ipLabel}: {nameList}</color>\n"); } if (NetworkManager.HostHistory.Count > 0) sb.Append($"<color={C_TEXT}>{L.Get("history")}</color> {NetworkManager.HostHistory[NetworkManager.HostHistory.Count - 1]}\n"); sb.Append("</size>"); }
            statsText.text = sb.ToString();
        }

        // ... (其余方法保持不变)
        private string GetClientStateLocalized(ClientState state) { switch (state) { case ClientState.PeerCreated: return L.Get("cs_initializing"); case ClientState.Authenticating: return L.Get("cs_authenticating"); case ClientState.Authenticated: return L.Get("cs_authenticated"); case ClientState.Joining: return L.Get("cs_joining"); case ClientState.Joined: return L.Get("cs_joined"); case ClientState.Disconnecting: return L.Get("cs_disconnecting"); case ClientState.Disconnected: return L.Get("cs_disconnected"); case ClientState.ConnectingToGameServer: return L.Get("cs_connecting_game"); case ClientState.ConnectingToMasterServer: return L.Get("cs_connecting_master"); case ClientState.ConnectingToNameServer: return L.Get("cs_connecting_name"); default: return state.ToString(); } }
        private void BuildPlayerEntry(StringBuilder sb, string name, string ip, int ping, bool isLocal, bool isHost, bool pro, float alignX, bool isAlive, bool hasModData, bool isInVoiceRoom, bool hasGhosts, int actorNumber = -1, byte remoteState = 0)
        {
            int prefixWeight = 0;
            if (!hasModData)
            {
                string prefixNoMod = isHost ? $"<color={C_GOLD}>{VoiceFix.HostSymbol.Value} </color>" : ""; string statusLabel = L.Get("state_unknown"); string statusColor = C_TEXT; bool isConnectingState = false; string connectingDetail = "";
                if (isInVoiceRoom) { statusLabel = L.Get("state_connected"); statusColor = C_PALE_GREEN; }
                else if (remoteState != 0) { ClientState cState = (ClientState)remoteState; if (cState == ClientState.Disconnected || cState == ClientState.Disconnecting) { statusLabel = L.Get("state_disconnected"); statusColor = C_RED; } else if (cState == ClientState.Joined) { statusLabel = L.Get("state_connected"); statusColor = C_PALE_GREEN; } else { statusLabel = L.Get("state_connecting"); statusColor = C_YELLOW; isConnectingState = true; connectingDetail = GetClientStateLocalized(cState); if (actorNumber != -1) joinTimes[actorNumber] = Time.unscaledTime; } }
                else
                {
                    if (actorNumber != -1)
                    {
                        if (!joinTimes.ContainsKey(actorNumber)) joinTimes[actorNumber] = Time.unscaledTime;
                        if (Time.unscaledTime - joinTimes[actorNumber] < 25f) { statusLabel = L.Get("state_connecting"); statusColor = C_YELLOW; }
                        else
                        {
                            if (hasGhosts) { statusLabel = L.Get("state_mismatch"); statusColor = C_GHOST_GREEN; }
                            else { statusLabel = L.Get("state_disconnected"); statusColor = C_RED; }
                        }
                    }
                }
                prefixWeight = 8; string truncName = Truncate(name, prefixWeight, isHost); sb.Append($"{FormatStatusTag(statusLabel, statusColor)} {prefixNoMod}<size=100%><color={C_GREEN}>{truncName}</color></size>\n"); if (isConnectingState && pro) { sb.Append($"<voffset=0.17em><size=80%><color={C_TEXT}>  » {connectingDetail}</color></size></voffset>\n"); }
                return;
            }
            bool isLinked = !string.IsNullOrEmpty(ip); string statusLabel2 = ""; string statusColor2 = C_GREEN; bool isConnecting = false;
            if (!isLinked) { if (isAlive && ping > 0) { statusLabel2 = L.Get("state_connecting"); statusColor2 = C_YELLOW; isConnecting = true; prefixWeight = 8; } else { statusLabel2 = L.Get("state_disconnected"); statusColor2 = C_RED; prefixWeight = 6; } } else if (IsIPMatch(ip)) { statusLabel2 = L.Get("state_synced"); statusColor2 = C_GREEN; prefixWeight = 6; } else { statusLabel2 = L.Get("state_abnormal"); statusColor2 = C_LOW_SAT_RED; prefixWeight = 6; }
            if (isLocal) { prefixWeight = 6; if (IsConnectingLocal()) isConnecting = true; }
            if (!isAlive && !isLocal) { statusLabel2 = L.Get("state_left"); statusColor2 = C_GREY; prefixWeight = 6; }
            string statusTag = FormatStatusTag(statusLabel2, statusColor2); string prefix = isHost ? $"<color={C_GOLD}>{VoiceFix.HostSymbol.Value} </color>" : ""; if (isLocal) statusTag = $"<color={C_TEXT}>[</color><color={C_TEXT}>{L.Get("label_local")}</color><color={C_TEXT}>]</color>";
            string truncatedName = Truncate(name, prefixWeight, isHost); string nameColor = (!isAlive && !isLocal) ? C_GREY : C_GREEN; string pingStr = ""; string pingColor = ping < 100 ? C_GREEN : (ping < 200 ? C_YELLOW : C_RED); if (ping > 0 || (isAlive && string.IsNullOrEmpty(ip))) pingStr = $"<pos={alignX}><color={C_TEXT}>| {L.Get("detail_latency")}:</color><color={pingColor}>{ping}ms</color>";
            sb.Append($"{statusTag} {prefix}<size=100%><color={nameColor}>{truncatedName}</color></size>{pingStr}\n");
            if (pro && !isLocal)
            {
                string ipLabel = ip; string statePrefix = L.Get("state_connected");
                if (remoteState != 0 && (ClientState)remoteState != ClientState.Joined && (ClientState)remoteState != ClientState.Disconnected) { ipLabel = GetClientStateLocalized((ClientState)remoteState); statePrefix = L.Get("state_status"); sb.Append($"<voffset=0.17em><size=80%><color={C_TEXT}>  » {ipLabel}</color></size></voffset>\n"); }
                else { string lbl = isConnecting ? L.Get("detail_waiting_data") : (string.IsNullOrEmpty(ip) ? "N/A" : ip); if (isConnecting && string.IsNullOrEmpty(ip)) lbl = $"<color=grey>{L.Get("detail_fetching")}</color>"; string pfx = isConnecting ? L.Get("detail_connecting_to") : L.Get("detail_joined_voice"); sb.Append($"<voffset=0.17em><size=80%><color={C_TEXT}>  » {pfx}: {lbl}</color></size></voffset>\n"); }
            }
            else if (pro && isLocal && isConnecting) { string localState = L.Get("detail_connecting_local"); if (NetworkManager.punVoice != null && NetworkManager.punVoice.Client != null) localState = GetClientStateLocalized(NetworkManager.punVoice.Client.State); sb.Append($"<voffset=0.17em><size=80%><color={C_TEXT}>  » {localState}</color></size></voffset>\n"); }
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