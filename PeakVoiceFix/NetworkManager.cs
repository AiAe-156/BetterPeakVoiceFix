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

        private const float SCAN_INTERVAL = 30f;
        private const float CACHE_TTL = 180f;
        private const float PING_PUBLISH_INTERVAL = 89f;
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
                string scavengedName = ScavengeNameFromScene(actorNumber);
                if (!string.IsNullOrEmpty(scavengedName))
                {
                    UpdatePlayerCache(actorNumber, scavengedName);
                    return scavengedName;
                }
            }

            if (resultName == "Unknown") return $"Player {actorNumber}";
            return resultName;
        }

        public static string ScavengeNameFromScene(int actorNumber)
        {
            try
            {
                var allViews = UnityEngine.Object.FindObjectsOfType<PhotonView>();
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

        public static bool IsGhost(int actorNumber)
        {
            if (PhotonNetwork.CurrentRoom == null) return true;
            return PhotonNetwork.CurrentRoom.GetPlayer(actorNumber) == null;
        }

        public static int GetGhostCount()
        {
            if (punVoice == null || punVoice.Client == null || punVoice.Client.CurrentRoom == null) return 0;
            int count = 0;
            foreach (var id in punVoice.Client.CurrentRoom.Players.Keys)
            {
                if (IsGhost(id)) count++;
            }
            return count;
        }

        public static void SystemUpdate()
        {
            if (punVoice == null)
            {
                var obj = GameObject.Find("VoiceClient");
                if (obj != null) punVoice = obj.GetComponent<PunVoiceClient>();
            }

            if (!PhotonNetwork.InRoom) return;

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
            if (VoiceFix.EnableDebugLogs != null && VoiceFix.EnableDebugLogs.Value) VoiceFix.logger.LogInfo(message);
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
            for (int i = ActiveSOSList.Count - 1; i >= 0; i--)
            {
                var sos = ActiveSOSList[i];
                if (Time.unscaledTime - sos.ReceiveTime > 60f) { ActiveSOSList.RemoveAt(i); continue; }
                if (GetPlayerName(sos.ActorNumber) == "Unknown") { ActiveSOSList.RemoveAt(i); continue; }
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
                    if (PhotonNetwork.IsMasterClient)
                    {
                        string majorityIP = GetMajorityIP(out int count);
                        string ipToUse = (!string.IsNullOrEmpty(majorityIP) && count >= 2) ? majorityIP : null;
                        SetGameServerAddress(punVoice.Client, ipToUse);
                    }

                    string retryTarget = TargetGameServer;
                    if (string.IsNullOrEmpty(retryTarget)) retryTarget = "Auto/Blind";

                    nextRetryTime = Time.unscaledTime;
                    ConnectionFailCount = 0;
                    punVoice.ConnectUsingSettings(PhotonNetwork.PhotonServerSettings.AppSettings);
                }
                WrongIPCount = 0; TotalRetryCount = 0;
            }
        }
    }
}