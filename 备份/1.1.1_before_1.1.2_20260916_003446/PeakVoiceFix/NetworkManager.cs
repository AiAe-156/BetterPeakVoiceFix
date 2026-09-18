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
using HarmonyLib;
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
        public string VoiceRegion;      // 对方语音客户端所在区服，走 PVF_VR 广播
        public int VoicePing = -1;      // 对方语音连接的往返延迟，走 PVF_VP 广播；-1 = 未知
        public int GamePing = -1;       // 对方游戏连接的往返延迟，走 186 事件高频广播；-1 = 未知
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

        // Photon 的 Disconnect() 是异步的：调用后 PeerState 先变 Disconnecting，要等若干帧才落到 Disconnected。
        // 而 VoiceConnection.ConnectUsingSettings 明确要求 PeerState == Disconnected，否则打个警告直接 return false。
        // 旧实现在同一帧里先 Disconnect 再 Connect，等于每次"纠正"都被库拒掉、只把语音踢下线而没有连回来。
        // 这里把重连拆成两步：本帧只发起断开并登记意图，后续帧等 PeerState 落定再真正连接。
        private static bool reconnectPending = false;
        private static string reconnectPendingMode = "";
        private static float reconnectPendingDeadline = 0f;
        private const float RECONNECT_WAIT_TIMEOUT = 5f;

        // 本体 VoiceClientHandler.InitNetworkVoice() 负责重设语音兴趣组（OpChangeGroups + Recorder.InterestGroup = 0）。
        // 它只在"语音首次 Joined / Recorder 就绪 / Start 时已连上"三个时机被本体调用，而订阅状态变化事件那一步是
        // 有条件的（Start 时若已 Joined 就不订阅）。我们主动踢过一次之后若没人重设，就可能连着却发不出声。
        // 用反射 + fail-open：本体这几个版本反复搬迁程序集归属，硬引用一旦失效会在重连热路径上抛 TypeLoadException。
        private static MethodInfo initNetworkVoiceMethod;
        private static bool initNetworkVoiceResolved = false;

        private static float lastPingPublishTime = 0f;
        private static float lastSOSTime = 0f;
        private static float nextSummaryLogTime = 0f;
        private static bool wasInRoom = false;
        private static float nextVoiceClientFindTime = 0f;
        private static float nextSOSManageTime = 0f;
        private static int lastPlayerCount = 0;
        // Scavenge 失败的 actor -> 上次尝试时间，用于负缓存，避免每帧重复全场景扫描。
        private static readonly Dictionary<int, float> scavengeFailTime = new Dictionary<int, float>();

        // 每个队友第一次被看到的时刻。没装 mod 的人不会自报任何东西，判断"他是还在连、
        // 还是真的不在我的语音房"只能靠时间，所以这份表是 IsLocalIsolated 的唯一依据。
        private static readonly Dictionary<int, float> firstSeenTime = new Dictionary<int, float>();

        // 语音房 Actor ↔ 游戏房 Actor 桥接映射。游戏房和语音房是两个独立 Photon 房间、Actor 各自编号，
        // 直接拿号互查会在玩家重连后错位。PhotonVoiceView 把语音流(Speaker.RemoteVoice.PlayerId=语音房Actor)
        // 和游戏玩家(PhotonView.OwnerActorNr=游戏房Actor)天然绑定，以此为主键做跨房间匹配。
        private static readonly Dictionary<int, int> voiceToGameActor = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> gameToVoiceActor = new Dictionary<int, int>();
        private static float nextVoiceMapRefreshTime = 0f;
        private const float VOICE_MAP_REFRESH_INTERVAL = 1f;

        private const float SCAN_INTERVAL = 30f;
        private const float CACHE_TTL = 180f;
        // 89s 对"装没装 mod"这个标志够用，但面板现在还要显示队友的语音延迟——一分半钟前的数字没意义。
        // 30s 一次属性广播在 4 人房里每分钟 8 条，可以忽略。
        private const float PING_PUBLISH_INTERVAL = 30f;
        private const float VOICE_CLIENT_FIND_INTERVAL = 1f;
        private const float SOS_MANAGE_INTERVAL = 0.5f;
        private const float SCAVENGE_RETRY_INTERVAL = 10f;
        private const int MAX_FAIL_BEFORE_BACKOFF = 12;
        private const string PROP_IP = "PVF_IP";
        private const string PROP_PING = "PVF_Ping";
        // 语音区服 / 语音延迟。分叉的唯一变量就是区服，所以这两条比 IP 更能说明"谁跑到哪去了"。
        // 老版本 mod 不发这两个键，读的时候一律按缺失处理，不影响混装。
        public const string PROP_VREGION = "PVF_VR";
        public const string PROP_VPING = "PVF_VP";

        private const byte TYPE_SOS = 0;
        private const byte TYPE_LOG = 1;
        private const byte TYPE_STATE = 2;
        // 延迟走独立的轻量事件而不是自定义属性：属性是可靠广播、还会进房间状态，
        // 30 秒一次可以，3 秒一次就浪费了。延迟数字丢一两帧无所谓，所以用 Unreliable 事件。
        private const byte TYPE_PING = 3;
        private const float PING_EVENT_INTERVAL = 3f;
        private static float nextPingEventTime = 0f;

        // ===== 诊断日志的去重快照（只在值变化时才记一行）=====
        private static float nextDiagTime = 0f;
        private static string lastDiagVoiceRegion = null;
        private static string lastDiagVoiceRoom = null;
        private static bool lastDiagIsolated = false;
        private static string lastDiagRoomVoice = null;
        private static RoomVoiceSource lastDiagRoomVoiceSource = RoomVoiceSource.Unknown;
        private static readonly Dictionary<int, string> lastDiagCross = new Dictionary<int, string>();

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

        /// <summary>
        /// 基于场景中的 PhotonVoiceView 重建 语音Actor↔游戏Actor 映射（按节流间隔刷新）。
        /// </summary>
        private static void RefreshVoiceActorMap()
        {
            if (Time.unscaledTime < nextVoiceMapRefreshTime) return;
            nextVoiceMapRefreshTime = Time.unscaledTime + VOICE_MAP_REFRESH_INTERVAL;
            voiceToGameActor.Clear();
            gameToVoiceActor.Clear();
            try
            {
#pragma warning disable CS0618
                var views = UnityEngine.Object.FindObjectsOfType<PhotonVoiceView>();
#pragma warning restore CS0618
                foreach (var v in views)
                {
                    if (v == null || v.SpeakerInUse == null || !v.SpeakerInUse.IsLinked) continue;
                    var remote = v.SpeakerInUse.RemoteVoice;
                    if (remote == null) continue;
                    var pv = v.GetComponent<PhotonView>();
                    int gameActor = pv != null ? pv.OwnerActorNr : 0;
                    if (gameActor <= 0) continue;
                    voiceToGameActor[remote.PlayerId] = gameActor;
                    gameToVoiceActor[gameActor] = remote.PlayerId;
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 判断语音房里的某个连接是否是幽灵（不对应任何游戏房玩家）。
        /// 匹配顺序：Speaker 锚定 → UserId → 同号 Actor 仅在毫无可靠身份时兜底。
        /// </summary>
        public static bool IsGhost(int voiceActorNumber)
        {
            if (PhotonNetwork.CurrentRoom == null) return true;
            if (punVoice == null || punVoice.Client == null || punVoice.Client.CurrentRoom == null) return true;
            if (!punVoice.Client.CurrentRoom.Players.TryGetValue(voiceActorNumber, out var voicePlayer)) return true;
            if (voicePlayer.IsLocal) return false;

            RefreshVoiceActorMap();
            if (voiceToGameActor.TryGetValue(voiceActorNumber, out int gameActor))
                return PhotonNetwork.CurrentRoom.GetPlayer(gameActor) == null;

            string visitorUserId = voicePlayer.UserId;
            if (!string.IsNullOrEmpty(visitorUserId))
            {
                foreach (var gp in PhotonNetwork.PlayerList)
                {
                    if (!string.IsNullOrEmpty(gp.UserId) && gp.UserId == visitorUserId) return false;
                }
                return true;
            }

            // 无 Speaker 归属也无 UserId：退回同号匹配，但若该同号玩家的语音已锚定到别的语音位则视为幽灵。
            if (PhotonNetwork.CurrentRoom.GetPlayer(voiceActorNumber) != null)
            {
                if (gameToVoiceActor.TryGetValue(voiceActorNumber, out int va) && va != voiceActorNumber) return true;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 判断游戏房间中的某个玩家是否也在语音房间中。
        /// 匹配顺序：Speaker 锚定 → UserId → 同号 Actor 仅在毫无可靠身份时兜底。
        /// </summary>
        public static bool IsPlayerInVoiceRoom(int gameActorNumber)
        {
            if (punVoice == null || punVoice.Client == null || punVoice.Client.CurrentRoom == null) return false;
            if (PhotonNetwork.CurrentRoom == null) return false;
            var gamePlayer = PhotonNetwork.CurrentRoom.GetPlayer(gameActorNumber);
            if (gamePlayer == null) return false;
            if (gamePlayer.IsLocal) return punVoice.Client.State == ClientState.Joined;

            RefreshVoiceActorMap();
            if (gameToVoiceActor.TryGetValue(gameActorNumber, out int voiceActor))
                return punVoice.Client.CurrentRoom.Players.ContainsKey(voiceActor);

            string targetUserId = gamePlayer.UserId;
            if (!string.IsNullOrEmpty(targetUserId))
            {
                foreach (var kvp in punVoice.Client.CurrentRoom.Players)
                {
                    if (!string.IsNullOrEmpty(kvp.Value.UserId) && kvp.Value.UserId == targetUserId) return true;
                }
            }

            // 无 Speaker 归属也无 UserId 命中：退回同号匹配，但排除该语音位已确认属于他人的情况。
            if (punVoice.Client.CurrentRoom.Players.TryGetValue(gameActorNumber, out var vp))
            {
                if (voiceToGameActor.TryGetValue(gameActorNumber, out int owner) && owner != gameActorNumber) return false;
                if (!string.IsNullOrEmpty(vp.UserId) && vp.UserId != targetUserId) return false;
                return true;
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
            foreach (var k in gone) { PlayerCache.Remove(k); scavengeFailTime.Remove(k); firstSeenTime.Remove(k); }
            ActiveSOSList.RemoveAll(s => PhotonNetwork.CurrentRoom.GetPlayer(s.ActorNumber) == null);
        }

        /// <summary>
        /// 记录每个队友第一次出现的时刻。没装 mod 的人不广播任何东西，
        /// "过了入房宽限期还不在我的语音房"是唯一能判他不是"还在连"的办法。
        /// </summary>
        private static void TrackFirstSeen()
        {
            if (PhotonNetwork.PlayerList == null) return;
            foreach (var p in PhotonNetwork.PlayerList)
            {
                if (p == null || p.IsLocal) continue;
                if (!firstSeenTime.ContainsKey(p.ActorNumber))
                    firstSeenTime[p.ActorNumber] = Time.unscaledTime;
            }
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

            // 离线伪房间（JoinDummyRoom）：本体刻意让语音客户端停在 PeerCreated 永不连接，
            // 这里没有"语音问题"可修——整套监控/重连/广播全部跳过，
            // 否则 HandleHostLogic 会拿上一局遗留的 Disconnected 状态每 5 秒真连一次语音服。
            if (PhotonNetwork.OfflineMode) return;

            // 离场即时清除：检测到游戏房人数下降，立刻清理离场玩家的缓存/SOS。
            int curPlayerCount = PhotonNetwork.PlayerList != null ? PhotonNetwork.PlayerList.Length : 0;
            if (curPlayerCount < lastPlayerCount) PurgeDepartedActors();
            lastPlayerCount = curPlayerCount;
            TrackFirstSeen();

            // 按节流间隔获取语音客户端。不用 GameObject.Find("VoiceClient")：Find 只搜激活对象、且依赖对象名，
            // 而本体的 VoiceClientHandler 存在多实例竞争（日志里的 "Already Found VoiceClient, Destroying..."），
            // 按名字可能抓到即将被销毁的那一个。也不用 PunVoiceClient.Instance —— 它的 getter 在场景里找不到实例时
            // 会**新建**一个名为 PunVoiceClient 的对象，而本体 VoiceClientHandler.Awake 见到 Instance != 自己就会把
            // 自己销毁，等于我们一次读取就能把本体的语音客户端弄没。按类型找是唯一既准确又无副作用的方式。
            if (punVoice == null && Time.unscaledTime >= nextVoiceClientFindTime)
            {
                nextVoiceClientFindTime = Time.unscaledTime + VOICE_CLIENT_FIND_INTERVAL;
                try { punVoice = UnityEngine.Object.FindFirstObjectByType<PunVoiceClient>(); }
                catch (Exception) { punVoice = null; }
            }

            if (punVoice != null && punVoice.Client != null)
            {
                ClientState currentState = punVoice.Client.State;
                if (currentState != lastClientState)
                {
                    string msg = $"{L.Get("log_state_change")}: {lastClientState} -> {currentState}";
                    BroadcastLog(msg);
                    SendStateSync(currentState);
                    if (currentState == ClientState.Joined)
                    {
                        ConnectionFailCount = 0;
                        // 每次重新连上语音房都补一次兴趣组，覆盖"本体没订阅到状态变化"的情况。
                        TryInitNetworkVoice();
                    }
                    UpdateDataLayer(true);
                    lastClientState = currentState;
                }
            }

            // 待处理的重连（等断开落定）必须每帧推进，且优先于新一轮决策。
            ProcessPendingReconnect();


            UpdateDataLayer();
            PublishPingEvent();
            TrackDiagnostics();
            HandleInputAndState();
            ManageSOSList();

            if (punVoice != null && punVoice.Client != null && !reconnectPending)
            {
                if (Time.unscaledTime >= nextRetryTime)
                {
                    if (PhotonNetwork.IsMasterClient) HandleHostLogic();
                    else HandleClientLogic();
                }
            }
        }

        /// <summary>
        /// 高频广播自己的游戏/语音延迟和语音区服。3 秒一次、不可靠发送，丢包无所谓下一轮就补上。
        /// 面板要显示队友的实时延迟，靠 30 秒一次的自定义属性太陈旧。
        /// </summary>
        private static void PublishPingEvent()
        {
            if (Time.unscaledTime < nextPingEventTime) return;
            nextPingEventTime = Time.unscaledTime + PING_EVENT_INTERVAL;
            if (!PhotonNetwork.IsConnectedAndReady) return;
            if (PhotonNetwork.CurrentRoom == null || PhotonNetwork.CurrentRoom.PlayerCount <= 1) return;

            object[] content = new object[] { TYPE_PING, PhotonNetwork.GetPing(), LocalVoicePing, LocalVoiceRegion ?? "" };
            RaiseEventOptions opts = new RaiseEventOptions { Receivers = ReceiverGroup.Others };
            PhotonNetwork.RaiseEvent(186, content, opts, SendOptions.SendUnreliable);
        }

        public static void SendStateSync(ClientState state)        {
            byte stateByte = (byte)state;
            object[] content = new object[] { TYPE_STATE, stateByte, VoiceFix.MOD_VERSION };
            RaiseEventOptions opts = new RaiseEventOptions { Receivers = ReceiverGroup.Others };
            // 走可靠发送：这是边沿触发的，而且是对端 RemoteState / ModVersion 的唯一载体，
            // 丢一包要等到下次状态变化或 30 秒属性刷新才自愈。一局也就几条，代价可以忽略。
            PhotonNetwork.RaiseEvent(186, content, opts, SendOptions.SendReliable);
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

        /// <summary>本机语音客户端当前所在区服；未连接时为 null。</summary>
        public static string LocalVoiceRegion
        {
            get
            {
                if (punVoice == null || punVoice.Client == null) return null;
                string r = punVoice.Client.CloudRegion;
                return string.IsNullOrEmpty(r) ? null : r;
            }
        }

        /// <summary>本机语音连接的往返延迟；拿不到时 -1。注意这和 PhotonNetwork.GetPing() 是两条不同的连接。</summary>
        public static int LocalVoicePing
        {
            get
            {
                try
                {
                    if (punVoice == null || punVoice.Client == null) return -1;
                    if (punVoice.Client.State != ClientState.Joined) return -1;
                    var peer = punVoice.Client.LoadBalancingPeer;
                    return (peer != null) ? peer.RoundTripTime : -1;
                }
                catch (Exception) { return -1; }
            }
        }

        private static void UpdateDataLayer(bool force = false)
        {
            bool timeToPublish = Time.unscaledTime - lastPingPublishTime > PING_PUBLISH_INTERVAL;
            if (force || timeToPublish)
            {
                lastPingPublishTime = Time.unscaledTime;
                var props = new ExitGames.Client.Photon.Hashtable();
                int localActor = PhotonNetwork.LocalPlayer.ActorNumber;
                int gamePing = PhotonNetwork.GetPing();
                string voiceRegion = LocalVoiceRegion;
                int voicePing = LocalVoicePing;
                props[PROP_PING] = gamePing;

                // 这里刻意不再要求 punVoice 可用：PROP_PING 是"装了本 mod"的唯一在线标志（见 IsModUser），
                // 一旦漏发，正好是出故障的那个人在队友界面上退化成"没装 mod"的推断分支，把故障藏起来。
                bool voiceJoined = punVoice != null && punVoice.Client != null && punVoice.Client.State == ClientState.Joined;
                string myIP = voiceJoined ? punVoice.Client.GameServerAddress : "";
                props[PROP_IP] = myIP;
                props[PROP_VREGION] = voiceRegion ?? "";
                props[PROP_VPING] = voicePing;
                PhotonNetwork.LocalPlayer.SetCustomProperties(props);

                // 强制更新本机缓存（包含区服和双延迟），保证专业快照不会把已连接的本机误列为“未连接”。
                UpdatePlayerCache(localActor, PhotonNetwork.LocalPlayer.NickName, myIP, VoiceFix.MOD_VERSION);
                CacheEntry localEntry = PlayerCache[localActor];
                localEntry.VoiceRegion = voiceRegion;
                localEntry.VoicePing = voicePing;
                localEntry.GamePing = gamePing;
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

        /// <summary>
        /// 把某个玩家自报的语音区服/语音延迟收进缓存。老版本 mod 不发这两个键，
        /// 缺失时保持缓存原值不动（而不是清空），免得刚收到又被覆盖成未知。
        /// </summary>
        public static void CacheVoiceInfo(Photon.Realtime.Player p)
        {
            if (p == null || p.CustomProperties == null) return;
            if (!PlayerCache.TryGetValue(p.ActorNumber, out var ce))
            {
                ce = new CacheEntry { LastSeenTime = Time.unscaledTime };
                PlayerCache[p.ActorNumber] = ce;
            }
            object o;
            if (p.CustomProperties.TryGetValue(PROP_VREGION, out o) && o is string vr)
                ce.VoiceRegion = string.IsNullOrEmpty(vr) ? null : vr;
            if (p.CustomProperties.TryGetValue(PROP_VPING, out o) && o is int vp)
                ce.VoicePing = vp;
        }

        private static void ScanPlayers()
        {
            foreach (Photon.Realtime.Player p in PhotonNetwork.PlayerListOthers)
            {
                // 语音区服/语音延迟与 IP 是各自独立的键：装了新版但语音断线的人会发空 IP、
                // 却仍然发得出区服，所以这两条要在 IP 分支之外单独收。
                CacheVoiceInfo(p);

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
                ConnectVoiceNow(L.Get("log_host"));
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
                        PerformReconnect(modeName);   // 断开动作已并入 PerformReconnect
                    }
                    else if (WrongIPCount == 3) BroadcastLog(L.Get("log_compromise", currentIP));
                }
            }
            else if (state == ClientState.Disconnected)
            {
                // 不无限重试：连续失败超过阈值后拉长间隔，避免每个重试周期都刷一条 SOS 和日志。
                // ConnectionFailCount 在成功 Joined 或离开房间时归零，所以这只是退避不是放弃。
                ConnectionFailCount++;
                if (ConnectionFailCount > MAX_FAIL_BEFORE_BACKOFF)
                {
                    nextRetryTime = Time.unscaledTime + Mathf.Min(VoiceFix.RetryInterval.Value * 5f, 60f);
                    return;
                }
                PerformReconnect(modeName);
            }
        }

        /// <summary>
        /// 复刻本体 PunVoiceClient.ConnectVoice() 的连接前准备，然后发起语音连接。
        /// 旧实现直接把 PhotonNetwork.PhotonServerSettings.AppSettings 传给 ConnectUsingSettings，丢了三样东西：
        /// ① 区服 —— FixedRegion 为空时 LoadBalancingClient 会把 CloudRegion 置 null 并转去"选本机最优区"，
        ///    跨区房里必然与游戏所在区服分叉；两边各自建一个同名 &lt;房间码&gt;_voice_ 房，谁也听不见谁，
        ///    而本体的自检只比房名不比区服（FollowLeader），永远发现不了；
        /// ② 身份 —— 不带 AuthValues 就丢了本体的 UserId，语音房成员与游戏房玩家对不上号；
        /// ③ 序列化协议 —— 与 PUN 不一致时可能解不开对方的包。
        /// 另外那个 AppSettings 是全局对象的**引用**：ConnectUsingSettings 内部会把它存进 VoiceConnection.Settings
        /// 并写 BestRegionSummaryFromStorage，等于污染全局 PhotonServerSettings，所以一律传副本。
        /// </summary>
        private static bool ConnectVoiceNow(string mode)
        {
            if (punVoice == null || punVoice.Client == null) return false;
            try
            {
                var client = punVoice.Client;
                var settings = PhotonNetwork.PhotonServerSettings.AppSettings.CopyTo(new AppSettings());

                string region = PhotonNetwork.CloudRegion;
                if (!string.IsNullOrEmpty(region)) settings.FixedRegion = region;

                client.SerializationProtocol = PhotonNetwork.NetworkingClient.SerializationProtocol;
                if (PhotonNetwork.AuthValues != null)
                {
                    if (client.AuthValues == null) client.AuthValues = new AuthenticationValues();
                    client.AuthValues = PhotonNetwork.AuthValues.CopyTo(client.AuthValues);
                }
                client.AuthMode = PhotonNetwork.NetworkingClient.AuthMode;
                client.EncryptionMode = PhotonNetwork.NetworkingClient.EncryptionMode;

                BroadcastLog(L.Get("log_reconnect_go",
                    string.IsNullOrEmpty(mode) ? "Auto" : mode,
                    string.IsNullOrEmpty(region) ? "?" : region));

                return punVoice.ConnectUsingSettings(settings);
            }
            catch (Exception ex)
            {
                LastErrorMessage = ex.Message;
                if (VoiceFix.logger != null) VoiceFix.logger.LogError($"[PVF] ConnectVoiceNow: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 调本体的 VoiceClientHandler.InitNetworkVoice() 重设语音兴趣组；解析不到就静默跳过。
        /// </summary>
        private static void TryInitNetworkVoice()
        {
            try
            {
                if (!initNetworkVoiceResolved)
                {
                    initNetworkVoiceResolved = true;
                    Type t = AccessTools.TypeByName("VoiceClientHandler");
                    if (t != null) initNetworkVoiceMethod = AccessTools.Method(t, "InitNetworkVoice");
                }
                initNetworkVoiceMethod?.Invoke(null, null);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 推进待处理的重连：等 PeerState 真正落到 Disconnected 再发起连接，超时则放弃本轮（下一轮重试会再来）。
        /// </summary>
        private static void ProcessPendingReconnect()
        {
            if (!reconnectPending) return;
            if (punVoice == null || punVoice.Client == null) { reconnectPending = false; return; }

            var peer = punVoice.Client.LoadBalancingPeer;
            if (peer != null && peer.PeerState != PeerStateValue.Disconnected)
            {
                if (Time.unscaledTime > reconnectPendingDeadline)
                {
                    reconnectPending = false;
                    BroadcastLog(L.Get("log_reconnect_timeout"));
                }
                return;
            }

            reconnectPending = false;
            ConnectVoiceNow(reconnectPendingMode);
        }


        /// <summary>
        /// 该连哪台语音服。判据与面板的「房间语音服」同源（GetRoomVoiceServer，只统计别人的自报），
        /// 优先级仍是设计不变量里的多数优先：多数派(≥2) → 房主 → 盲连。
        ///
        /// 旧实现走 GetMajorityIP，而 PlayerCache 里含本机自己（UpdateDataLayer 每 30 秒写一次），
        /// 于是四人 2v2 分叉时 A=2、B=2 平票，胜者取决于字典遍历顺序；一旦选到自己那台，
        /// HandleClientLogic 就会认为 currentIP == TargetGameServer、清零计数器直接返回，
        /// **停止纠正一个真实存在的分叉**。改成只算别人之后，同样场景得到 B（过去就是 3:1）。
        ///
        /// 单人上报 / 本机推定 / 未知都不足以当决策依据，一律退盲连——盲连不是放弃：
        /// 区服 + 房名一致后 Photon 自会把人送到同一台机器（见设计不变量 3）。
        /// </summary>
        private static string DecideTargetIP(out string mode)
        {
            RoomVoiceSource src;
            int reporters;
            string ip = GetRoomVoiceServer(out src, out reporters);

            if (src == RoomVoiceSource.Majority && !string.IsNullOrEmpty(ip))
            {
                mode = L.Get("log_majority", reporters); ConnectedUsingHost = false; LogDecision(mode, ip); return ip;
            }
            if (src == RoomVoiceSource.Host && !string.IsNullOrEmpty(ip))
            {
                mode = L.Get("log_host"); ConnectedUsingHost = true; LogDecision(mode, ip); return ip;
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

        /// <summary>
        /// 按语音区服统计多数派。修好区服绑定后，区服才是"大家在不在一起"的决定变量，
        /// 比按 IP 统计更好懂（IP 只是该区服里房间落到了哪台机器）。只统计自报过区服的人。
        /// </summary>
        public static string GetMajorityRegion(out int maxCount)
        {
            maxCount = 0;
            if (PlayerCache.Count == 0) return null;
            var counts = new Dictionary<string, int>();
            foreach (var kvp in PlayerCache)
            {
                string r = kvp.Value.VoiceRegion;
                if (string.IsNullOrEmpty(r)) continue;
                int c;
                counts.TryGetValue(r, out c);
                counts[r] = c + 1;
            }
            string best = null;
            foreach (var kvp in counts) { if (kvp.Value > maxCount) { maxCount = kvp.Value; best = kvp.Key; } }
            return best;
        }

        /// <summary>「房间语音服」的可信来源，按可信度从高到低。</summary>
        public enum RoomVoiceSource
        {
            Majority,      // ≥2 人自报同一个
            Host,          // 房主自报（房主不是本机）
            SingleReport,  // 只有 1 个其他 mod 用户自报
            LocalGuess,    // 没人上报，只能拿本机自己的顶上
            Unknown        // 谁都没有，也包括本机没连上
        }

        /// <summary>
        /// 求「房间语音服」——即"全队应该在的那台语音服"。
        /// 只统计【别人】的自报：本机自己的 IP 不能当权威答案，否则本机连错时会把错的当成对的。
        /// 优先级刻意与 DecideTargetIP 一致（多数派 ≥2 → 房主 → 单人上报），这样面板显示和实际决策不会互相打脸。
        /// </summary>
        public static string GetRoomVoiceServer(out RoomVoiceSource source, out int reporters)
        {
            source = RoomVoiceSource.Unknown;
            reporters = 0;
            if (PhotonNetwork.CurrentRoom == null) return null;

            var counts = new Dictionary<string, int>();
            string hostIP = null;
            string anyIP = null;
            foreach (var p in PhotonNetwork.PlayerListOthers)
            {
                if (p == null || p.CustomProperties == null) continue;
                object o;
                if (!p.CustomProperties.TryGetValue(PROP_IP, out o)) continue;
                string ip = o as string;
                if (string.IsNullOrEmpty(ip)) continue;

                int c;
                counts.TryGetValue(ip, out c);
                counts[ip] = c + 1;
                if (p.IsMasterClient) hostIP = ip;
                if (anyIP == null) anyIP = ip;
            }

            string best = null; int max = 0;
            foreach (var kvp in counts) { if (kvp.Value > max) { max = kvp.Value; best = kvp.Key; } }

            if (max >= 2) { source = RoomVoiceSource.Majority; reporters = max; return best; }
            if (!string.IsNullOrEmpty(hostIP)) { source = RoomVoiceSource.Host; reporters = 1; return hostIP; }
            if (!string.IsNullOrEmpty(anyIP)) { source = RoomVoiceSource.SingleReport; reporters = 1; return anyIP; }

            // 没有任何人上报：本机连上了就拿自己的顶，但要标明这是推定值
            if (punVoice != null && punVoice.Client != null && punVoice.Client.State == ClientState.Joined)
            {
                string mine = punVoice.Client.GameServerAddress;
                if (!string.IsNullOrEmpty(mine)) { source = RoomVoiceSource.LocalGuess; return mine; }
            }
            return null;
        }

        /// <summary>
        /// 本机是否孤立：语音房里只有自己，而游戏房里有别人。
        /// 这条不依赖任何人上报，是"本机跑偏了"的铁证。
        /// </summary>
        public static bool IsLocalIsolated()
        {
            if (punVoice == null || punVoice.Client == null) return false;
            if (punVoice.Client.State != ClientState.Joined) return false;
            if (punVoice.Client.CurrentRoom == null) return false;
            if (PhotonNetwork.CurrentRoom == null) return false;
            if (PhotonNetwork.CurrentRoom.PlayerCount <= 1) return false;
            if (punVoice.Client.CurrentRoom.Players.Count > 1) return false;

            // 队友还在连的时候不能说"我孤立"——那时谁都不在一起，不能归责于本机。
            // 判据有两条，任一成立即可：
            //   ① 别人自报了非空语音服 IP（装了 mod）→ 他确实已经落在某台语音服上；
            //   ② 别人进房已超过入房宽限期 → 没装 mod 的人永远不会自报，只能靠时间排除"还在连"。
            // 1.6.1 只留了 ①，等于把"本机装了、房里其他人都没装"这个最常见的混装场景整个排除掉，
            // 而那正是本 mod 唯一的信息来源：那种局里本机真孤立时，GetRoomVoiceServer 会退到
            // LocalGuess（拿本机自己的地址顶上），面板于是拿"我 == 我"算出一个绿勾，
            // 还把 3 个队友标成 [断开]——责任箭头正好指反。恢复 1.5.0 定的"铁证"不变量。
            float grace = (VoiceFix.ConnectTimeout != null) ? VoiceFix.ConnectTimeout.Value : 25f;
            foreach (var p in PhotonNetwork.PlayerListOthers)
            {
                if (p == null) continue;
                object o;
                if (p.CustomProperties != null
                    && p.CustomProperties.TryGetValue(PROP_IP, out o) && o is string ip && !string.IsNullOrEmpty(ip))
                    return true;
                float seen;
                if (firstSeenTime.TryGetValue(p.ActorNumber, out seen) && Time.unscaledTime - seen > grace)
                    return true;
            }
            return false;
        }

        /// <summary>语音房名（本机语音客户端当前所在的房间名），用来和"房间码_voice_"对比是否残留在旧房。</summary>
        public static string LocalVoiceRoomName
        {
            get
            {
                if (punVoice == null || punVoice.Client == null || punVoice.Client.CurrentRoom == null) return null;
                return punVoice.Client.CurrentRoom.Name;
            }
        }

        /// <summary>供面板显示的重连统计。</summary>
        public static void GetRetryStats(out int total, out int fail, out int wrongIP)
        {
            total = TotalRetryCount; fail = ConnectionFailCount; wrongIP = WrongIPCount;
        }

        /// <summary>当前决策的模式名（多数派/跟随房主/盲连），面板"决策"那行用。</summary>
        public static string CurrentDecisionMode
        {
            get
            {
                if (IsBlindConnect) return L.Get("log_auto_blind");
                if (ConnectedUsingHost) return L.Get("log_host");
                // 与 DecideTargetIP 同源，否则"决策"行会报一个和实际决策不一样的人数
                RoomVoiceSource src;
                int reporters;
                GetRoomVoiceServer(out src, out reporters);
                return L.Get("log_majority", reporters);
            }
        }

        /// <summary>
        /// 关键状态的诊断日志。全部"仅在变化时记一次"，否则每秒刷屏；
        /// 只写本地（BepInEx 日志 + Alt+J 控制台），不广播——队友从自己面板就能看到我的状态，
        /// 没必要占用房间事件带宽。
        /// </summary>
        private static void TrackDiagnostics()
        {
            if (Time.unscaledTime < nextDiagTime) return;
            nextDiagTime = Time.unscaledTime + 1f;

            // ① 本机语音区服变化——分叉的唯一变量，最值得留痕
            string vr = LocalVoiceRegion;
            if (vr != lastDiagVoiceRegion)
            {
                DiagLog(L.Get("diag_vregion_change", lastDiagVoiceRegion ?? "—", vr ?? "—"));
                lastDiagVoiceRegion = vr;
            }

            // ② 语音房名变化——和"房间码_voice_"不一致就说明还挂在上一个房
            string vroom = LocalVoiceRoomName;
            if (vroom != lastDiagVoiceRoom)
            {
                DiagLog(L.Get("diag_vroom_change", lastDiagVoiceRoom ?? "—", vroom ?? "—"));
                lastDiagVoiceRoom = vroom;
            }

            // ③ 孤立状态翻转
            bool iso = IsLocalIsolated();
            if (iso != lastDiagIsolated)
            {
                int gameCount = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.PlayerCount : 0;
                int voiceCount = (punVoice != null && punVoice.Client != null && punVoice.Client.CurrentRoom != null)
                    ? punVoice.Client.CurrentRoom.Players.Count : 0;
                DiagLog(iso ? L.Get("diag_isolated", gameCount) : L.Get("diag_isolated_clear", voiceCount));
                lastDiagIsolated = iso;
            }

            // ④ 房间语音服的可信来源变化
            RoomVoiceSource src;
            int reporters;
            string rv = GetRoomVoiceServer(out src, out reporters);
            if (rv != lastDiagRoomVoice || src != lastDiagRoomVoiceSource)
            {
                if (string.IsNullOrEmpty(rv)) DiagLog(L.Get("diag_roomvoice_lost"));
                else DiagLog(L.Get("diag_roomvoice", rv, src.ToString()));
                lastDiagRoomVoice = rv;
                lastDiagRoomVoiceSource = src;
            }

            // ⑤ 每个队友的跨区状态翻转
            string roomRegion = PhotonNetwork.CloudRegion;
            if (PhotonNetwork.CurrentRoom != null && !string.IsNullOrEmpty(roomRegion))
            {
                foreach (var p in PhotonNetwork.PlayerListOthers)
                {
                    if (p == null) continue;
                    CacheEntry ce;
                    string theirs = PlayerCache.TryGetValue(p.ActorNumber, out ce) ? ce.VoiceRegion : null;
                    string prev;
                    lastDiagCross.TryGetValue(p.ActorNumber, out prev);
                    bool crossNow = !string.IsNullOrEmpty(theirs) && theirs != roomRegion;
                    string nowKey = crossNow ? theirs : null;
                    if (nowKey == prev) continue;

                    if (crossNow) DiagLog(L.Get("diag_cross", GetPlayerName(p.ActorNumber), theirs, roomRegion));
                    else if (!string.IsNullOrEmpty(prev)) DiagLog(L.Get("diag_cross_clear", GetPlayerName(p.ActorNumber), roomRegion));
                    lastDiagCross[p.ActorNumber] = nowKey;
                }
            }
        }

        /// <summary>诊断行：进 BepInEx 日志文件 + Alt+J 控制台，不广播。</summary>
        public static void DiagLog(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            if (VoiceFix.logger != null) VoiceFix.logger.LogInfo(msg);
            if (VoiceUIManager.Instance != null) VoiceUIManager.Instance.AddLog("System", msg, true);
        }

        private static void PerformReconnect(string mode)
        {
            if (punVoice == null || punVoice.Client == null) return;

            TotalRetryCount++; nextRetryTime = Time.unscaledTime + VoiceFix.RetryInterval.Value;
            if (Time.unscaledTime - lastSOSTime > 20f && PhotonNetwork.IsConnectedAndReady)
            {
                lastSOSTime = Time.unscaledTime;
                SendSOS(string.IsNullOrEmpty(TargetGameServer) ? "Unknown" : TargetGameServer);
            }

            // 只登记意图 + 发起断开，真正的连接留给 ProcessPendingReconnect。
            // 注意这里不再反射写 GameServerAddress：那个值在 ConnectUsingSettings 流程里会被主服务器
            // 返回的地址覆盖（LoadBalancingClient 收到 JoinRoom 响应时直接赋值），只有 ReconnectAndRejoin()
            // 才会拿它直连，而那条路我们不走。"跟上队友"靠的是区服 + 房名一致，Photon 自会把人送到同一台机器。
            reconnectPending = true;
            reconnectPendingMode = mode;
            reconnectPendingDeadline = Time.unscaledTime + RECONNECT_WAIT_TIMEOUT;

            if (punVoice.Client.State != ClientState.Disconnected) punVoice.Client.Disconnect();
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
                    else if (type == TYPE_PING)
                    {
                        // 高频延迟包：只更新数字，不碰状态判定
                        if (!PlayerCache.TryGetValue(senderActor, out var pe))
                        {
                            pe = new CacheEntry { PlayerName = senderName };
                            PlayerCache[senderActor] = pe;
                        }
                        if (data.Length >= 2 && data[1] is int gp) pe.GamePing = gp;
                        if (data.Length >= 3 && data[2] is int vp) pe.VoicePing = vp;
                        if (data.Length >= 4 && data[3] is string vr) pe.VoiceRegion = string.IsNullOrEmpty(vr) ? null : vr;
                        pe.LastSeenTime = Time.unscaledTime;
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
                    TargetGameServer = DecideTargetIP(out _);
                    ConnectionFailCount = 0;
                    PerformReconnect(L.Get("log_sos_manual"));
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
            firstSeenTime.Clear();
            voiceToGameActor.Clear();
            gameToVoiceActor.Clear();
            nextVoiceMapRefreshTime = 0f;

            // 语音客户端引用也要清：它是 DontDestroyOnLoad 的，本体在换房/切场景时可能换实例，
            // 旧引用留着会让后续判断全落在一个已经不参与语音的对象上。下一帧会按类型重新取。
            punVoice = null;
            reconnectPending = false;
            reconnectPendingMode = "";
            reconnectPendingDeadline = 0f;

            // 诊断快照也要清，否则换房后第一轮会把"上一局的值 → 新值"当成变化记一遍
            nextDiagTime = 0f;
            lastDiagVoiceRegion = null;
            lastDiagVoiceRoom = null;
            lastDiagIsolated = false;
            lastDiagRoomVoice = null;
            lastDiagRoomVoiceSource = RoomVoiceSource.Unknown;
            lastDiagCross.Clear();
            nextPingEventTime = 0f;
        }
    }
}
