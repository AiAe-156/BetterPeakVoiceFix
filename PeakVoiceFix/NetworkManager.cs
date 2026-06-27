using Photon.Pun;
using Photon.Realtime;
using Photon.Voice.PUN;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using ExitGames.Client.Photon;
using TMPro;
using UnityEngine.UI;
using Steamworks;

namespace PeakVoiceFix
{
    public class CacheEntry
    {
        public string IP;
        public float LastSeenTime;
        public string PlayerName;
        public byte RemoteState;
        public string ModVersion;
    }

    public class SOSData
    {
        public int ActorNumber;
        public string PlayerName;
        public string TargetIP;
        public string OriginIP;
        public float ReceiveTime;
    }

    public static class NetworkManager
    {
        public static string TargetGameServer { get; private set; }
        public static bool ConnectedUsingHost { get; private set; } = true;
        public static bool IsBlindConnect { get; private set; } = false;

        public static int WrongIPCount { get; private set; } = 0;
        public static int ConnectionFailCount { get; private set; } = 0;
        public static int TotalRetryCount { get; private set; } = 0;

        public static string LastErrorMessage { get; private set; } = "";

        public static Dictionary<int, CacheEntry> PlayerCache = new Dictionary<int, CacheEntry>();
        public static List<SOSData> ActiveSOSList = new List<SOSData>();
        public static List<string> HostHistory = new List<string>();

        public static float LastScanTime { get; private set; } = 0f;

        private static string LastKnownHostIP = "";
        public static float LastHostUpdateTime { get; private set; } = 0f;
        private static string LastDecisionLog = "";
        private static ClientState lastClientState = ClientState.Disconnected;
        public static PunVoiceClient punVoice;
        private static float nextRetryTime = 0f;
        private static float lastPingPublishTime = 0f;
        private static float lastSOSTime = 0f;
        private static float nextSummaryLogTime = 0f;
        private static bool wasInRoom = false;
        private static float nextVoiceClientFindTime = 0f;
        private static float nextSOSManageTime = 0f;
        private static int lastPlayerCount = 0;
        // Scavenge 失败的 actor -> 上次尝试时间，用于负缓存，避免每帧重复全场景扫描。
        private static readonly Dictionary<int, float> scavengeFailTime = new Dictionary<int, float>();

        private const float SCAN_INTERVAL = 30f;
        private const float CACHE_TTL = 180f;
        private const float PING_PUBLISH_INTERVAL = 89f;
        private const float VOICE_CLIENT_FIND_INTERVAL = 1f;
        private const float SOS_MANAGE_INTERVAL = 0.5f;
        private const float SCAVENGE_RETRY_INTERVAL = 10f;
        private const string PROP_IP = "PVF_IP";
        private const string PROP_PING = "PVF_Ping";

        private const byte TYPE_SOS = 0;
        private const byte TYPE_LOG = 1;
        private const byte TYPE_STATE = 2;

        public static string GetPlayerName(int actorNumber)
        {
            string resultName = "Unknown";

            Photon.Realtime.Player player = null;
            if (PhotonNetwork.CurrentRoom != null)
                player = PhotonNetwork.CurrentRoom.GetPlayer(actorNumber);

            if (player != null && !string.IsNullOrEmpty(player.NickName))
            {
                resultName = player.NickName;
                UpdatePlayerCache(actorNumber, resultName);
                return resultName;
            }

            if (PlayerCache.ContainsKey(actorNumber))
            {
                string cached = PlayerCache[actorNumber].PlayerName;
                if (!string.IsNullOrEmpty(cached) && cached != "Unknown" && !cached.StartsWith("Player "))
                    return cached;
            }

            if ((resultName == "Unknown" || string.IsNullOrEmpty(resultName)) && player != null && !string.IsNullOrEmpty(player.UserId))
            {
                try
                {
                    if (ulong.TryParse(player.UserId, out ulong steamId64))
                    {
                        string steamName = SteamFriends.GetFriendPersonaName(new CSteamID(steamId64));
                        if (!string.IsNullOrEmpty(steamName) && steamName != "[unknown]")
                        {
                            resultName = steamName;
                            UpdatePlayerCache(actorNumber, resultName);
                            return resultName;
                        }
                    }
                }
                catch (Exception) { }
            }

            if (resultName == "Unknown" || string.IsNullOrEmpty(resultName))
            {
                // ScavengeNameFromScene 会做全场景 FindObjectsOfType<PhotonView>，开销大。
                // 失败后写负缓存，SCAVENGE_RETRY_INTERVAL 秒内不再重扫，避免热路径（如每帧 ManageSOSList）反复全场景扫描。
                if (!scavengeFailTime.TryGetValue(actorNumber, out float lastScavenge)
                    || Time.unscaledTime - lastScavenge > SCAVENGE_RETRY_INTERVAL)
                {
                    string scavengedName = ScavengeNameFromScene(actorNumber);
                    if (!string.IsNullOrEmpty(scavengedName))
                    {
                        scavengeFailTime.Remove(actorNumber);
                        UpdatePlayerCache(actorNumber, scavengedName);
                        return scavengedName;
                    }
                    scavengeFailTime[actorNumber] = Time.unscaledTime;
                }
            }

            if (resultName == "Unknown") return $"Player {actorNumber}";
            return resultName;
        }

        public static string ScavengeNameFromScene(int actorNumber)
        {
            try
            {
                // 全场景扫描，开销大；此 Unity 版本无 FindObjectsByType(FindObjectSortMode) 重载，
                // 故仍用 FindObjectsOfType，其调用频率已由上层 scavengeFailTime 负缓存兜住。
#pragma warning disable CS0618
                var allViews = UnityEngine.Object.FindObjectsOfType<PhotonView>();
#pragma warning restore CS0618
                foreach (var view in allViews)
                {
                    if (view == null || view.OwnerActorNr != actorNumber) continue;
                    if (view.Owner != null && !string.IsNullOrEmpty(view.Owner.NickName)) return view.Owner.NickName;

                    var tmp = view.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (tmp != null && !string.IsNullOrEmpty(tmp.text)) return tmp.text;

                    var tmpWorld = view.GetComponentInChildren<TextMeshPro>(true);
                    if (tmpWorld != null && !string.IsNullOrEmpty(tmpWorld.text)) return tmpWorld.text;

                    var legacyText = view.GetComponentInChildren<Text>(true);
                    if (legacyText != null && !string.IsNullOrEmpty(legacyText.text)) return legacyText.text;
                }
            }
            catch (Exception) { }
            return null;
        }

        // [修改] 支持传入 version
        public static void UpdatePlayerCache(int actorNumber, string name, string ip = null, string version = null)
        {
            if (!PlayerCache.ContainsKey(actorNumber)) PlayerCache[actorNumber] = new CacheEntry();
            bool isNewNameValid = !string.IsNullOrEmpty(name) && name != "Unknown";
            if (isNewNameValid) PlayerCache[actorNumber].PlayerName = name;
            if (!string.IsNullOrEmpty(ip)) PlayerCache[actorNumber].IP = ip;
            if (!string.IsNullOrEmpty(version)) PlayerCache[actorNumber].ModVersion = version;
            PlayerCache[actorNumber].LastSeenTime = Time.unscaledTime;
        }

        public static bool IsGhost(int voiceActorNumber)
        {
            if (PhotonNetwork.CurrentRoom == null) return true;

            // Actor 直接命中优先：这是历史稳定逻辑，也能避免单人房误判。
            if (PhotonNetwork.CurrentRoom.GetPlayer(voiceActorNumber) != null) return false;

            if (punVoice == null || punVoice.Client == null || punVoice.Client.CurrentRoom == null) return true;
            var voicePlayers = punVoice.Client.CurrentRoom.Players;
            if (!voicePlayers.TryGetValue(voiceActorNumber, out var voicePlayer)) return true;

            // 仅当 Actor 失配时再尝试 UserId 兜底。
            string visitorUserId = voicePlayer.UserId;
            if (string.IsNullOrEmpty(visitorUserId)) return true;

            foreach (var gp in PhotonNetwork.PlayerList)
            {
                if (!string.IsNullOrEmpty(gp.UserId) && gp.UserId == visitorUserId) return false;
            }
            return true;
        }

        /// <summary>
        /// 判断游戏房间中的某个玩家是否也在语音房间中（Actor 优先，UserId 兜底）。
        /// </summary>
        public static bool IsPlayerInVoiceRoom(int gameActorNumber)
        {
            if (punVoice == null || punVoice.Client == null || punVoice.Client.CurrentRoom == null) return false;
            if (PhotonNetwork.CurrentRoom == null) return false;
            var gamePlayer = PhotonNetwork.CurrentRoom.GetPlayer(gameActorNumber);
            if (gamePlayer == null) return false;

            // 先看 Actor 是否直接存在。
            if (punVoice.Client.CurrentRoom.Players.ContainsKey(gameActorNumber)) return true;

            // Actor 不命中时，再尝试 UserId 兜底。
            string targetUserId = gamePlayer.UserId;
            if (string.IsNullOrEmpty(targetUserId)) return false;

            foreach (var kvp in punVoice.Client.CurrentRoom.Players)
            {
                if (!string.IsNullOrEmpty(kvp.Value.UserId) && kvp.Value.UserId == targetUserId) return true;
            }
            return false;
        }

        public static int GetGhostCount()
        {
            if (punVoice == null || punVoice.Client == null || punVoice.Client.CurrentRoom == null) return 0;
            if (PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.PlayerCount <= 1) return 0;

            int count = 0;
            foreach (var voiceActor in punVoice.Client.CurrentRoom.Players.Keys)
            {
                if (IsGhost(voiceActor)) count++;
            }
            return count;
        }

        /// <summary>
        /// 判断玩家是否装了本 mod：本机恒为是；远端看是否广播过 PVF_Ping 键（断线时也带，故 key 存在即装）
        /// 或缓存里收到过 STATE 事件带的 ModVersion。与 IP 值脱钩，避免"装了但断线发空 IP"被误判为没装。
        /// </summary>
        public static bool IsModUser(Photon.Realtime.Player p)
        {
            if (p == null) return false;
            if (p.IsLocal) return true;
            if (p.CustomProperties != null && p.CustomProperties.ContainsKey(PROP_PING)) return true;
            if (PlayerCache.TryGetValue(p.ActorNumber, out var ce) && !string.IsNullOrEmpty(ce.ModVersion)) return true;
            return false;
        }

        public static bool IsModUser(int actorNumber)
        {
            var p = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.GetPlayer(actorNumber) : null;
            if (p != null) return IsModUser(p);
            if (PlayerCache.TryGetValue(actorNumber, out var ce) && !string.IsNullOrEmpty(ce.ModVersion)) return true;
            return false;
        }

        /// <summary>
        /// 玩家确认离开游戏房间（不在 CurrentRoom 里）时，立即清掉其缓存/SOS/负缓存，
        /// 避免残留数据继续参与多数派统计与显示。由 SystemUpdate 在人数下降时调用。
        /// </summary>
        private static void PurgeDepartedActors()
        {
            if (PhotonNetwork.CurrentRoom == null) return;
            var gone = new List<int>();
            foreach (var k in PlayerCache.Keys)
                if (PhotonNetwork.CurrentRoom.GetPlayer(k) == null) gone.Add(k);
            foreach (var k in gone) { PlayerCache.Remove(k); scavengeFailTime.Remove(k); }
            ActiveSOSList.RemoveAll(s => PhotonNetwork.CurrentRoom.GetPlayer(s.ActorNumber) == null);
        }

        public static void SystemUpdate()
        {
            if (!PhotonNetwork.InRoom)
            {
                if (wasInRoom) ResetRoomScopedState();
                wasInRoom = false;
                return;
            }
            wasInRoom = true;

            // 离场即时清除：检测到游戏房人数下降，立刻清理离场玩家的缓存/SOS。
            int curPlayerCount = PhotonNetwork.PlayerList != null ? PhotonNetwork.PlayerList.Length : 0;
            if (curPlayerCount < lastPlayerCount) PurgeDepartedActors();
            lastPlayerCount = curPlayerCount;

            // 仅在房间内、按节流间隔查找 VoiceClient，避免菜单/连接前每帧全场景 GameObject.Find。
            if (punVoice == null && Time.unscaledTime >= nextVoiceClientFindTime)
            {
                nextVoiceClientFindTime = Time.unscaledTime + VOICE_CLIENT_FIND_INTERVAL;
                var obj = GameObject.Find("VoiceClient");
                if (obj != null) punVoice = obj.GetComponent<PunVoiceClient>();
            }

            if (punVoice != null && punVoice.Client != null)
            {
                ClientState currentState = punVoice.Client.State;
                if (currentState != lastClientState)
                {
                    string msg = $"{L.Get("log_state_change")}: {lastClientState} -> {currentState}";
                    BroadcastLog(msg);
                    SendStateSync(currentState);
                    if (currentState == ClientState.Joined) ConnectionFailCount = 0;
                    UpdateDataLayer(true);
                    lastClientState = currentState;
                }
            }

            UpdateDataLayer();
            HandleInputAndState();
            ManageSOSList();

            if (punVoice != null && punVoice.Client != null)
            {
                if (Time.unscaledTime >= nextRetryTime)
                {
                    if (PhotonNetwork.IsMasterClient) HandleHostLogic();
                    else HandleClientLogic();
                }
            }
        }

        public static void SendStateSync(ClientState state)
        {
            byte stateByte = (byte)state;
            object[] content = new object[] { TYPE_STATE, stateByte, VoiceFix.MOD_VERSION };
            RaiseEventOptions opts = new RaiseEventOptions { Receivers = ReceiverGroup.Others };
            PhotonNetwork.RaiseEvent(186, content, opts, SendOptions.SendUnreliable);
        }

        public static void BroadcastLog(string message)
        {
            string myName = GetPlayerName(PhotonNetwork.LocalPlayer.ActorNumber);
            if (VoiceUIManager.Instance != null) VoiceUIManager.Instance.AddLog(myName, message, true);

            byte code = 186;
            object[] content = new object[] { TYPE_LOG, message };
            RaiseEventOptions opts = new RaiseEventOptions { Receivers = ReceiverGroup.Others };
            PhotonNetwork.RaiseEvent(code, content, opts, SendOptions.SendReliable);
        }

        private static void UpdateDataLayer(bool force = false)
        {
            bool timeToPublish = Time.unscaledTime - lastPingPublishTime > PING_PUBLISH_INTERVAL;
            if ((force || timeToPublish) && punVoice != null && punVoice.Client != null)
            {
                lastPingPublishTime = Time.unscaledTime;
                var props = new ExitGames.Client.Photon.Hashtable();
                props[PROP_PING] = PhotonNetwork.GetPing();

                string myIP = (punVoice.Client.State == ClientState.Joined) ? punVoice.Client.GameServerAddress : "";
                props[PROP_IP] = myIP;
                PhotonNetwork.LocalPlayer.SetCustomProperties(props);

                // [新增] 强制更新本机缓存 (包含版本号)，方便 Dump 查看自己
                UpdatePlayerCache(PhotonNetwork.LocalPlayer.ActorNumber, PhotonNetwork.LocalPlayer.NickName, myIP, VoiceFix.MOD_VERSION);
            }
            if (force) return;
            if (Time.unscaledTime - LastScanTime > SCAN_INTERVAL) { LastScanTime = Time.unscaledTime; ScanPlayers(); }
            if (Time.unscaledTime > nextSummaryLogTime) { PrintSummaryLog(); nextSummaryLogTime = Time.unscaledTime + 60f; }
        }

        private static void PrintSummaryLog()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{L.Get("log_cache_snapshot")}:{PlayerCache.Count}");
            foreach (var kvp in PlayerCache)
            {
                string ipFull = string.IsNullOrEmpty(kvp.Value.IP) ? "N/A" : kvp.Value.IP;
                sb.AppendLine($" - {kvp.Value.PlayerName}: {ipFull} (St:{kvp.Value.RemoteState})");
            }
            if (VoiceUIManager.Instance != null) VoiceUIManager.Instance.AddLog("System", sb.ToString(), true);
        }

        private static void ScanPlayers()
        {
            foreach (Photon.Realtime.Player p in PhotonNetwork.PlayerListOthers)
            {
                object ipObj = null;
                if (p.CustomProperties.TryGetValue(PROP_IP, out ipObj) && ipObj is string ip)
                {
                    string correctName = GetPlayerName(p.ActorNumber);
                    UpdatePlayerCache(p.ActorNumber, correctName, ip);
                    if (p.IsMasterClient)
                    {
                        if (!string.IsNullOrEmpty(ip)) LastHostUpdateTime = Time.unscaledTime;
                        if (!string.IsNullOrEmpty(LastKnownHostIP) && LastKnownHostIP != ip && !string.IsNullOrEmpty(ip))
                        {
                            string log = $"{L.Get("log_host_ip_change")}: {LastKnownHostIP} -> {ip}";
                            BroadcastLog(log);
                            if (HostHistory.Count > 5) HostHistory.RemoveAt(0);
                            HostHistory.Add($"[{DateTime.Now:HH:mm:ss}] {ip}");
                        }
                        LastKnownHostIP = ip;
                    }
                }
            }
            var expired = PlayerCache.Where(x => Time.unscaledTime - x.Value.LastSeenTime > CACHE_TTL).Select(x => x.Key).ToList();
            foreach (var k in expired) PlayerCache.Remove(k);
        }

        private static void ManageSOSList()
        {
            // 无 SOS 时直接返回；有 SOS 时也节流到 ~0.5s 一次，
            // 避免每帧对每个 SOS 调用 GetPlayerName（其兜底可能触发全场景扫描）。
            if (ActiveSOSList.Count == 0) return;
            if (Time.unscaledTime < nextSOSManageTime) return;
            nextSOSManageTime = Time.unscaledTime + SOS_MANAGE_INTERVAL;

            for (int i = ActiveSOSList.Count - 1; i >= 0; i--)
            {
                var sos = ActiveSOSList[i];
                if (Time.unscaledTime - sos.ReceiveTime > 60f) { ActiveSOSList.RemoveAt(i); continue; }

                // GetPlayerName 在无法解析时通常返回 "Player {id}"，这里把该情况也视为未解析名字。
                string displayName = GetPlayerName(sos.ActorNumber);
                bool unresolvedName =
                    string.IsNullOrEmpty(displayName) ||
                    displayName == "Unknown" ||
                    displayName.StartsWith("Player ");

                if (unresolvedName)
                {
                    if (PhotonNetwork.CurrentRoom == null || PhotonNetwork.CurrentRoom.GetPlayer(sos.ActorNumber) == null)
                    {
                        ActiveSOSList.RemoveAt(i);
                        continue;
                    }
                }

                if (PhotonNetwork.CurrentRoom == null) continue;
                Photon.Realtime.Player p = PhotonNetwork.CurrentRoom.GetPlayer(sos.ActorNumber);
                object ipObj = null;
                if (p != null && p.CustomProperties.TryGetValue(PROP_IP, out ipObj) && ipObj is string ip && !string.IsNullOrEmpty(ip))
                    ActiveSOSList.RemoveAt(i);
            }
        }

        private static void HandleHostLogic()
        {
            if (punVoice.Client.State == ClientState.Disconnected)
            {
                BroadcastLog(L.Get("log_host_disconnected"));
                punVoice.ConnectUsingSettings(PhotonNetwork.PhotonServerSettings.AppSettings);
                nextRetryTime = Time.unscaledTime + 5f;
            }
        }

        private static void HandleClientLogic()
        {
            string bestIP = DecideTargetIP(out string modeName);
            if (!string.IsNullOrEmpty(bestIP))
            {
                if (ConnectionFailCount > 0 && (ConnectionFailCount % 6) >= 3)
                {
                    BroadcastLog(L.Get("log_loop_blind", ConnectionFailCount));
                    bestIP = null; modeName = $"{modeName}->BlindLoop";
                }
            }
            if (string.IsNullOrEmpty(bestIP))
            {
                IsBlindConnect = true; TargetGameServer = null;
                if (punVoice.Client.State == ClientState.Disconnected) PerformReconnect("Blind");
                return;
            }
            else { IsBlindConnect = false; TargetGameServer = bestIP; }

            string currentIP = punVoice.Client.GameServerAddress;
            ClientState state = punVoice.Client.State;

            if (state == ClientState.Joined)
            {
                if (currentIP == TargetGameServer) { WrongIPCount = 0; ConnectionFailCount = 0; TotalRetryCount = 0; return; }
                else
                {
                    WrongIPCount++;
                    if (WrongIPCount <= 2)
                    {
                        BroadcastLog($"{L.Get("log_wrong_freq", currentIP, TargetGameServer, WrongIPCount)}");
                        punVoice.Client.Disconnect(); PerformReconnect(modeName);
                    }
                    else if (WrongIPCount == 3) BroadcastLog(L.Get("log_compromise", currentIP));
                }
            }
            else if (state == ClientState.Disconnected) { ConnectionFailCount++; PerformReconnect(modeName); }
        }

        private static void SetGameServerAddress(LoadBalancingClient client, string ip)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                PropertyInfo prop = client.GetType().GetProperty("GameServerAddress", flags);
                if (prop != null && prop.CanWrite) { prop.SetValue(client, ip); return; }
                Type type = client.GetType();
                while (type != null)
                {
                    FieldInfo field = type.GetField("GameServerAddress", flags);
                    if (field == null) field = type.GetField("<GameServerAddress>k__BackingField", flags);
                    if (field != null) { field.SetValue(client, ip); return; }
                    type = type.BaseType;
                }
                if (VoiceFix.logger != null) VoiceFix.logger.LogError(L.Get("log_reflect_fail"));
            }
            catch (Exception ex) { if (VoiceFix.logger != null) VoiceFix.logger.LogError($"{L.Get("log_reflect_error")}: {ex}"); }
        }

        private static string DecideTargetIP(out string mode)
        {
            string majorityIP = GetMajorityIP(out int count);
            if (!string.IsNullOrEmpty(majorityIP) && count >= 2)
            {
                mode = L.Get("log_majority", count); ConnectedUsingHost = false; LogDecision(mode, majorityIP); return majorityIP;
            }
            if (!string.IsNullOrEmpty(LastKnownHostIP))
            {
                mode = L.Get("log_host"); ConnectedUsingHost = true; LogDecision(mode, LastKnownHostIP); return LastKnownHostIP;
            }
            mode = L.Get("log_auto_blind"); ConnectedUsingHost = false; LogDecision(mode, "Auto"); return null;
        }

        private static void LogDecision(string mode, string target)
        {
            string current = $"{mode}->{target}";
            if (current != LastDecisionLog) { BroadcastLog($"{L.Get("log_decision")}: {current}"); LastDecisionLog = current; }
        }

        public static string GetMajorityIP(out int maxCount)
        {
            maxCount = 0; if (PlayerCache.Count == 0) return null;
            var counts = new Dictionary<string, int>();
            foreach (var kvp in PlayerCache)
            {
                if (string.IsNullOrEmpty(kvp.Value.IP)) continue;
                if (!counts.ContainsKey(kvp.Value.IP)) counts[kvp.Value.IP] = 0;
                counts[kvp.Value.IP]++;
            }
            string best = null;
            foreach (var kvp in counts) { if (kvp.Value > maxCount) { maxCount = kvp.Value; best = kvp.Key; } }
            return best;
        }

        private static void PerformReconnect(string mode)
        {
            TotalRetryCount++; nextRetryTime = Time.unscaledTime + VoiceFix.RetryInterval.Value;
            if (Time.unscaledTime - lastSOSTime > 20f && PhotonNetwork.IsConnectedAndReady)
            {
                lastSOSTime = Time.unscaledTime;
                SendSOS(string.IsNullOrEmpty(TargetGameServer) ? "Unknown" : TargetGameServer);
            }
            if (punVoice.Client.State != ClientState.Disconnected) punVoice.Client.Disconnect();
            SetGameServerAddress(punVoice.Client, TargetGameServer);
            punVoice.ConnectUsingSettings(PhotonNetwork.PhotonServerSettings.AppSettings);
        }

        private static void SendSOS(string targetInfo)
        {
            string myCurrentVoiceIP = "Unknown";
            if (punVoice != null && punVoice.Client != null)
                myCurrentVoiceIP = punVoice.Client.GameServerAddress;

            if (string.IsNullOrEmpty(myCurrentVoiceIP)) myCurrentVoiceIP = "Disconnected";

            BroadcastLog($"{L.Get("log_sos_send")} -> {L.Get("log_sos_target")}:{targetInfo} | {L.Get("log_sos_local")}:{myCurrentVoiceIP}");

            object[] content = new object[] { TYPE_SOS, targetInfo, myCurrentVoiceIP };
            RaiseEventOptions opts = new RaiseEventOptions { Receivers = ReceiverGroup.Others };
            PhotonNetwork.RaiseEvent(186, content, opts, SendOptions.SendReliable);
        }

        public static void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code == 186)
            {
                int senderActor = photonEvent.Sender;
                string senderName = GetPlayerName(senderActor);

                if (photonEvent.CustomData is object[] data && data.Length >= 2)
                {
                    byte type = 0;
                    if (data[0] is byte b) type = b; else if (data[0] is int i) type = (byte)i;

                    if (type == TYPE_LOG)
                    {
                        string msg = data[1] as string;
                        if (VoiceUIManager.Instance != null) VoiceUIManager.Instance.AddLog(senderName, msg, false);
                    }
                    else if (type == TYPE_SOS)
                    {
                        string targetIP = data[1] as string;
                        string originIP = "Unknown(old)";
                        if (data.Length >= 3 && data[2] is string o) originIP = o;
                        else if (PlayerCache.ContainsKey(senderActor)) originIP = PlayerCache[senderActor].IP;

                        ActiveSOSList.RemoveAll(x => x.ActorNumber == senderActor);
                        ActiveSOSList.Add(new SOSData
                        {
                            ActorNumber = senderActor,
                            PlayerName = senderName,
                            TargetIP = targetIP,
                            OriginIP = originIP,
                            ReceiveTime = Time.unscaledTime
                        });

                        if (VoiceUIManager.Instance != null)
                        {
                            VoiceUIManager.Instance.AddLog("System", L.Get("log_sos_received", senderName, targetIP), true);
                            VoiceUIManager.Instance.TriggerNotification(senderName);
                        }
                    }
                    else if (type == TYPE_STATE)
                    {
                        byte state = 0;
                        if (data[1] is byte s) state = s;
                        else if (data[1] is int s2) state = (byte)s2;

                        string ver = "";
                        if (data.Length >= 3 && data[2] is string v) ver = v;

                        if (PlayerCache.ContainsKey(senderActor))
                        {
                            PlayerCache[senderActor].RemoteState = state;
                            if (!string.IsNullOrEmpty(ver)) PlayerCache[senderActor].ModVersion = ver;
                            PlayerCache[senderActor].LastSeenTime = Time.unscaledTime;
                        }
                        else
                        {
                            var entry = new CacheEntry
                            {
                                PlayerName = senderName,
                                LastSeenTime = Time.unscaledTime,
                                RemoteState = state,
                                ModVersion = ver
                            };
                            PlayerCache[senderActor] = entry;
                        }
                    }
                }
            }
        }

        private static void HandleInputAndState()
        {
            if (VoiceFix.EnableManualReconnect != null && VoiceFix.EnableManualReconnect.Value && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.K))
            {
                if (punVoice == null || punVoice.Client == null)
                {
                    BroadcastLog("[System] Alt+K ignored: Voice client not ready.");
                    return;
                }

                bool isConnected = (punVoice.Client.State == ClientState.Joined ||
                                    punVoice.Client.State == ClientState.ConnectingToGameServer ||
                                    punVoice.Client.State == ClientState.Authenticating);

                if (isConnected)
                {
                    BroadcastLog(L.Get("log_alt_k_disconnect"));
                    if (PhotonNetwork.IsConnectedAndReady) SendSOS(L.Get("log_sos_manual"));
                    punVoice.Client.Disconnect();
                    if (VoiceUIManager.Instance != null) VoiceUIManager.Instance.ShowStatsTemporary();
                }
                else
                {
                    BroadcastLog(L.Get("log_alt_k_reconnect"));
                    string manualTarget = DecideTargetIP(out _);
                    TargetGameServer = manualTarget;
                    SetGameServerAddress(punVoice.Client, manualTarget);

                    nextRetryTime = Time.unscaledTime;
                    ConnectionFailCount = 0;
                    punVoice.ConnectUsingSettings(PhotonNetwork.PhotonServerSettings.AppSettings);
                }
                WrongIPCount = 0; TotalRetryCount = 0;
            }
        }

        private static void ResetRoomScopedState()
        {
            PlayerCache.Clear();
            ActiveSOSList.Clear();
            HostHistory.Clear();

            TargetGameServer = null;
            ConnectedUsingHost = true;
            IsBlindConnect = false;

            WrongIPCount = 0;
            ConnectionFailCount = 0;
            TotalRetryCount = 0;

            LastErrorMessage = "";
            LastKnownHostIP = "";
            LastHostUpdateTime = 0f;
            LastScanTime = 0f;
            LastDecisionLog = "";
            lastClientState = ClientState.Disconnected;

            nextRetryTime = 0f;
            lastPingPublishTime = 0f;
            lastSOSTime = 0f;
            nextSummaryLogTime = 0f;
            nextVoiceClientFindTime = 0f;
            nextSOSManageTime = 0f;
            lastPlayerCount = 0;
            scavengeFailTime.Clear();
        }
    }
}
