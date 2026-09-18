using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace PeakVoiceFix
{
    /// <summary>
    /// 区服控制：强制游戏连接到指定 Photon 区服，并提供各区延迟测速。
    ///
    /// 背景：裸连时 PEAK 按 Photon 测速结果选"最佳区"，国内网络下常被判到 eu/ussc（200ms+），
    /// 而 hk 明明在 Photon 给 PEAK 开通的列表里（asia,au,eu,hk,jp,sa,us,ussc,usw）。
    ///
    /// 作用范围有限，必须明确：强制区服只影响【自己开房】和主菜单的初始连接。
    /// 加入别人的房时，本体的 NetworkConnector/MainMenuJoinRoomPage 会 ConnectToRegion 切到房主的区，
    /// 我们不干预——干预就会进不去房（房间只存在于房主那个区）。
    /// </summary>
    internal static class RegionControl
    {
        public const string AUTO = "auto";

        /// Photon 后台实际给 PEAK 开通的区服。来源不是猜的：PlayerPrefs 里
        /// PUNCloudBestRegion / VoiceCloudBestRegion 两条缓存的第三段就是这个列表。
        public static readonly string[] KNOWN_REGIONS =
            { "asia", "au", "eu", "hk", "jp", "sa", "us", "ussc", "usw" };

        private static readonly Dictionary<string, string> ZH_NAME = new Dictionary<string, string>
        {
            { "asia", "亚洲/新加坡" }, { "au", "澳洲" },     { "eu", "欧洲" },
            { "hk", "香港" },          { "jp", "日本" },     { "sa", "南美" },
            { "us", "美东" },          { "ussc", "美中南" }, { "usw", "美西" },
        };

        // ===== 强制区服 =====
        private static string appliedRegion = null;   // 我们上一次写进 AppSettings 的值，用于撤销
        private static float connectAttemptTime = -1f;
        private static bool warnedThisAttempt = false;

        // ===== 测速 =====
        private static LoadBalancingClient pingClient;
        private static RegionHandler borrowedHandler;   // 借用 PUN 的 handler 时记着它，超时诊断要用
        private static bool pingBusy;
        private static bool pingRequested;
        private static bool pingDisconnecting;
        private static float pingStartTime;
        private static volatile bool pingDone;
        private static volatile string pingPending;

        // ===== 强制区服的延迟质量检查 =====
        private static bool qualityChecked = false;
        private static float qualityCheckTime = -1f;

        public static bool IsPinging => pingBusy;

        /// <summary>配置里选的区服，规范化成小写；未配置或非法值一律回落 AUTO。</summary>
        public static string Configured
        {
            get
            {
                string v = VoiceFix.ForcedRegion != null ? VoiceFix.ForcedRegion.Value : AUTO;
                if (string.IsNullOrEmpty(v)) return AUTO;
                v = v.Trim().ToLowerInvariant();
                if (v == AUTO) return AUTO;
                foreach (var r in KNOWN_REGIONS) if (r == v) return v;
                return AUTO;   // 填了没开通的区就当没填，别把人锁在连不上的区
            }
        }

        public static bool IsAuto => Configured == AUTO;

        /// <summary>把区服代码变成给人看的字符串，如 "hk 香港"。中文语言下才附中文名。</summary>
        public static string Describe(string code)
        {
            if (string.IsNullOrEmpty(code)) return "—";
            if (L.IsChinese && ZH_NAME.TryGetValue(code, out var zh)) return code + " " + zh;
            return code;
        }

        /// <summary>
        /// 在本体发起 Photon 连接之前把 FixedRegion 写进全局 AppSettings。
        /// 这里【故意】写全局对象——本体读的就是它，不写进去不生效。
        /// 与 NetworkManager 里"语音重连必须传副本"是两件不同的事，别混。
        /// </summary>
        public static void ApplyFixedRegion()
        {
            try
            {
                var app = PhotonNetwork.PhotonServerSettings != null
                    ? PhotonNetwork.PhotonServerSettings.AppSettings : null;
                if (app == null) return;

                connectAttemptTime = Time.unscaledTime;
                warnedThisAttempt = false;
                qualityChecked = false;
                qualityCheckTime = -1f;

                if (IsAuto)
                {
                    // 只撤销我们自己写过的值，不动用户/本体原本的配置
                    if (appliedRegion != null && app.FixedRegion == appliedRegion)
                    {
                        app.FixedRegion = "";
                        if (VoiceFix.logger != null) VoiceFix.logger.LogInfo("[区服] 已恢复自动选区");
                    }
                    appliedRegion = null;
                    return;
                }

                string want = Configured;
                if (app.FixedRegion != want)
                {
                    app.FixedRegion = want;
                    if (VoiceFix.logger != null)
                        VoiceFix.logger.LogInfo($"[区服] 强制连接到 {Describe(want)}（原设置: '{appliedRegion ?? ""}'）");
                }
                appliedRegion = want;
            }
            catch (Exception ex)
            {
                if (VoiceFix.logger != null) VoiceFix.logger.LogWarning($"[区服] 应用强制区服失败: {ex.Message}");
            }
        }

        /// <summary>每帧调用：连接超时提示 + 强制区服延迟体检 + 测速状态机。不依赖是否在房间里。</summary>
        public static void Update()
        {
            CheckConnectTimeout();
            CheckForcedRegionQuality();
            PumpPing();
        }

        /// <summary>
        /// 强制区服连上之后，把实测延迟和"自动选区"的历史成绩比一比。
        /// 这条是实测需求：裸连强制 hk 会到 400ms+，而自动选区（eu）只有 224ms ——
        /// 玩家很容易以为"选地理上近的区一定更快"，不说出来就会被坑。只提示，不动配置。
        /// </summary>
        private static void CheckForcedRegionQuality()
        {
            if (qualityChecked || IsAuto) return;

            var st = PhotonNetwork.NetworkClientState;
            bool onMaster = st == ClientState.ConnectedToMasterServer
                         || st == ClientState.JoinedLobby
                         || st == ClientState.Joined
                         || PhotonNetwork.InRoom;
            if (!onMaster) { qualityCheckTime = -1f; return; }

            // 刚连上时 RTT 还没稳定，等几秒再取值
            if (qualityCheckTime < 0f) { qualityCheckTime = Time.unscaledTime; return; }
            if (Time.unscaledTime - qualityCheckTime < 8f) return;

            qualityChecked = true;
            int now = PhotonNetwork.GetPing();
            if (now <= 0) return;

            string autoCode;
            int auto = ParseCachedBestPing(out autoCode);

            string msg = null;
            if (auto > 0 && now > auto + 60)
                msg = L.Get("region_worse", Describe(Configured), now, Describe(autoCode), auto);
            else if (now >= 300)
                msg = L.Get("region_high", Describe(Configured), now);

            if (msg == null) return;
            if (VoiceFix.logger != null) VoiceFix.logger.LogWarning(msg);
            if (VoiceUIManager.Instance != null)
            {
                VoiceUIManager.Instance.SetRegionWarning(msg);
                VoiceUIManager.Instance.AddLog("System", msg, true);
            }
        }

        /// <summary>从 PUN 的最佳区缓存里解出 "区服代码" 和它当时测到的延迟。格式: code;ping;可用区列表</summary>
        private static int ParseCachedBestPing(out string code)
        {
            code = null;
            try
            {
                string s = PhotonNetwork.BestRegionSummaryInPreferences;
                if (string.IsNullOrEmpty(s)) return -1;
                var parts = s.Split(';');
                if (parts.Length < 2) return -1;
                code = parts[0];
                int p;
                return int.TryParse(parts[1], out p) ? p : -1;
            }
            catch (Exception) { return -1; }
        }

        /// <summary>
        /// 强制了区服却迟迟连不上主服务器时提示一次。
        /// 按用户要求：只提示，不自动把配置改回自动——"选了就是选了"。
        /// </summary>
        private static void CheckConnectTimeout()
        {
            if (connectAttemptTime < 0f || warnedThisAttempt) return;
            if (IsAuto) { connectAttemptTime = -1f; return; }

            var st = PhotonNetwork.NetworkClientState;
            bool reachedMaster = st == ClientState.ConnectedToMasterServer
                              || st == ClientState.JoinedLobby
                              || st == ClientState.Joined
                              || PhotonNetwork.InRoom;
            if (reachedMaster) { connectAttemptTime = -1f; return; }

            if (Time.unscaledTime - connectAttemptTime < 20f) return;

            warnedThisAttempt = true;
            string msg = L.Get("region_timeout", Describe(Configured));
            if (VoiceFix.logger != null) VoiceFix.logger.LogWarning(msg);
            Debug.LogWarning("[PVF] " + msg);
            if (VoiceUIManager.Instance != null)
            {
                VoiceUIManager.Instance.SetRegionWarning(msg);
                VoiceUIManager.Instance.AddLog("System", msg, true);
            }
        }

        /// <summary>
        /// 测全部区服延迟。两条路：
        /// ① 优先【借用】PUN 自己的 RegionHandler —— 它已经从 NameServer 拿到了区列表，
        ///    我们只是让它多发一轮 UDP ping，不建连接、不改连接状态，也绕开了独立客户端的认证问题。
        /// ② PUN 还没连上时才起临时客户端。这条路必须自带 AuthValues：PEAK 用的是
        ///    CustomAuthenticationType.None + UserId，不带 UserId 的裸连在 NameServer 认证阶段会被拒，
        ///    表现就是 RegionHandler 永远为 null、最后只能超时。
        /// 两条路都传 AppSettings 副本，不污染全局。
        /// </summary>
        public static void StartPing()
        {
            if (pingBusy)
            {
                Report(L.Get("region_ping_busy"));
                return;
            }
            try
            {
                var rh = (PhotonNetwork.NetworkingClient != null)
                    ? PhotonNetwork.NetworkingClient.RegionHandler : null;
                if (rh != null && rh.EnabledRegions != null && rh.EnabledRegions.Count > 0)
                {
                    pingBusy = true;
                    pingDone = false;
                    pingPending = null;
                    pingStartTime = Time.unscaledTime;
                    borrowedHandler = rh;
                    // previousSummary 传 null → 全量 ping，而不是只复测上次的最佳区
                    if (!rh.PingMinimumOfRegions(OnPinged, null))
                    {
                        pingBusy = false;
                        borrowedHandler = null;
                        Report(L.Get("region_ping_failed"));
                        return;
                    }
                    Report(L.Get("region_ping_started"));
                    return;
                }

                var src = PhotonNetwork.PhotonServerSettings != null
                    ? PhotonNetwork.PhotonServerSettings.AppSettings : null;
                if (src == null) { Report(L.Get("region_ping_nosettings")); return; }

                var s = src.CopyTo(new AppSettings());
                s.FixedRegion = "";
                s.BestRegionSummaryFromStorage = "";

                pingClient = new LoadBalancingClient();
                pingClient.AuthValues = new AuthenticationValues
                {
                    AuthType = CustomAuthenticationType.None,
                    UserId = Guid.NewGuid().ToString()   // 用随机 id，避免和游戏自己的连接抢同一个 UserId
                };
                pingBusy = true;
                pingRequested = false;
                pingDisconnecting = false;
                pingDone = false;
                pingPending = null;
                pingStartTime = Time.unscaledTime;

                if (!pingClient.ConnectUsingSettings(s))
                {
                    CleanupPing();
                    Report(L.Get("region_ping_failed"));
                    return;
                }
                Report(L.Get("region_ping_started"));
            }
            catch (Exception ex)
            {
                CleanupPing();
                Report(L.Get("region_ping_error", ex.Message));
            }
        }

        private static void PumpPing()
        {
            if (!pingBusy) return;
            try
            {
                if (pingClient != null) pingClient.Service();   // 独立 client 必须自己驱动，PUN 不管它

                if (pingDone)
                {
                    string r = pingPending;
                    pingDone = false;
                    pingPending = null;
                    if (!string.IsNullOrEmpty(r)) Report(r);
                    if (pingClient != null)
                    {
                        pingDisconnecting = true;
                        try { if (pingClient.IsConnected) pingClient.Disconnect(); } catch { }
                    }
                    else
                    {
                        CleanupPing();   // 借用 PUN 的 handler，没有连接要收
                        return;
                    }
                }

                if (pingClient != null && !pingRequested && !pingDisconnecting && pingClient.RegionHandler != null)
                {
                    pingRequested = true;
                    pingClient.RegionHandler.PingMinimumOfRegions(OnPinged, null);
                }

                // 9 个区并行、每区最多 5 次 × 800ms，正常 5 秒内出结果；给到 45 秒纯属兜底。
                bool timedOut = Time.unscaledTime - pingStartTime > 45f;
                bool settled = pingDisconnecting && pingClient != null &&
                    pingClient.LoadBalancingPeer != null &&
                    pingClient.LoadBalancingPeer.PeerState == ExitGames.Client.Photon.PeerStateValue.Disconnected;

                if (settled || timedOut)
                {
                    if (timedOut && !pingDisconnecting) ReportTimeoutDiagnostics();
                    CleanupPing();
                }
            }
            catch (Exception ex)
            {
                CleanupPing();
                Report(L.Get("region_ping_error", ex.Message));
            }
        }

        /// <summary>
        /// 超时不能只说"超时"——把卡在哪一步说清楚，并把已经测到的部分结果倒出来。
        /// </summary>
        private static void ReportTimeoutDiagnostics()
        {
            var sb = new StringBuilder();
            sb.AppendLine(L.Get("region_ping_timeout"));
            try
            {
                var rh = borrowedHandler ?? (pingClient != null ? pingClient.RegionHandler : null);
                if (pingClient != null)
                {
                    sb.AppendLine($" client state: {pingClient.State}, cause: {pingClient.DisconnectedCause}");
                    sb.AppendLine($" regionHandler: {(pingClient.RegionHandler == null ? "null（没拿到区列表，多半是 NameServer 不通或认证被拒）" : "ok")}");
                }
                else
                {
                    sb.AppendLine(" mode: borrowed PUN RegionHandler");
                }
                if (rh != null && rh.EnabledRegions != null)
                {
                    sb.AppendLine(" " + L.Get("region_ping_partial"));
                    foreach (var r in rh.EnabledRegions)
                        if (r != null) sb.AppendLine($" {r.Code,-5} {FormatPing(r.Ping),7}  {Describe(r.Code)}");
                }
            }
            catch (Exception ex) { sb.AppendLine(" diag failed: " + ex.Message); }
            Report(sb.ToString());
        }

        /// <summary>
        /// PingMinimumOfRegions 的回调可能跑在 ping 线程上，所以这里只做纯字符串处理，
        /// 一行 Unity API 都不能碰（Time/Debug/GameObject 全部禁用），结果交给 PumpPing 输出。
        /// </summary>
        private static void OnPinged(RegionHandler handler)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== " + L.Get("region_ping_title") + " ===");
                var list = handler != null ? handler.EnabledRegions : null;
                if (list == null || list.Count == 0)
                {
                    sb.AppendLine(L.Get("region_ping_empty"));
                }
                else
                {
                    var copy = new List<Region>(list);
                    copy.Sort((a, b) => Rank(a.Ping).CompareTo(Rank(b.Ping)));
                    string best = (handler.BestRegion != null) ? handler.BestRegion.Code : null;
                    foreach (var r in copy)
                    {
                        if (r == null) continue;
                        string mark = (r.Code == best) ? " ★" : "";
                        sb.AppendLine($" {r.Code,-5} {FormatPing(r.Ping),7}{mark}  {Describe(r.Code)}");
                    }
                    sb.AppendLine(L.Get("region_ping_hint"));
                }
                pingPending = sb.ToString();
            }
            catch (Exception ex)
            {
                pingPending = "region ping parse failed: " + ex.Message;
            }
            pingDone = true;
        }

        // 未测到的区（Ping<=0）排到最后，不要让它们假装成 0ms 最优
        private static int Rank(int ping) => (ping <= 0) ? int.MaxValue : ping;

        private static string FormatPing(int ping)
        {
            if (ping <= 0) return "—";
            if (ping >= 3000) return ">3s";
            return ping + "ms";
        }

        private static void CleanupPing()
        {
            if (pingClient != null)
            {
                try { if (pingClient.IsConnected) pingClient.Disconnect(); } catch { }
                pingClient = null;
            }
            borrowedHandler = null;
            pingBusy = false;
            pingRequested = false;
            pingDisconnecting = false;
            pingDone = false;
            pingPending = null;
        }

        private static void Report(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            if (VoiceUIManager.Instance != null) VoiceUIManager.Instance.AddLog("System", msg, true);
            if (VoiceFix.logger != null) VoiceFix.logger.LogInfo(msg);
        }

        /// <summary>Alt+J 控制台用的当前区服快照：游戏区服、语音区服、强制设置、两条测速缓存。</summary>
        public static string GetStatusReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== " + L.Get("region_status_title") + " ===");
            sb.AppendLine($" {L.Get("region_game")}: {Describe(PhotonNetwork.CloudRegion)}");

            string vr = null;
            var pv = NetworkManager.punVoice;
            if (pv != null && pv.Client != null) vr = pv.Client.CloudRegion;
            sb.AppendLine($" {L.Get("region_voice")}: {Describe(vr)}");

            string room = (PhotonNetwork.CurrentRoom != null) ? PhotonNetwork.CurrentRoom.Name : null;
            if (!string.IsNullOrEmpty(room))
                sb.AppendLine($" {L.Get("region_room_code")}: {room}  ({L.Get("region_from_code")}: {DecodeRoomRegion(room)})");

            sb.AppendLine($" {L.Get("region_forced")}: {(IsAuto ? L.Get("region_auto") : Describe(Configured))}");

            try
            {
                sb.AppendLine($" {L.Get("region_cache_pun")}: {PhotonNetwork.BestRegionSummaryInPreferences}");
                sb.AppendLine($" {L.Get("region_cache_voice")}: {PlayerPrefs.GetString("VoiceCloudBestRegion", "")}");
            }
            catch (Exception) { }
            return sb.ToString();
        }

        /// <summary>
        /// 从房间码首字符推房主开房时的区服，仅用于显示。
        /// 本体的 Utilities.CodeToRegion 会把 '-'（未收录区的 fallback）Clamp 成 REGIONS[0]="us"，
        /// 所以这里自己解，不调它——'-' 一律当"未收录"，不假装是美国。
        /// </summary>
        public static string DecodeRoomRegion(string roomName)
        {
            if (string.IsNullOrEmpty(roomName)) return "—";
            char c = char.ToUpperInvariant(roomName[0]);
            if (c == '-') return L.Get("region_unlisted");
            // 本体那张表（含未开通的区），顺序即编码顺序：'A' + index
            string[] table = { "us", "usw", "ussc", "eu", "au", "za", "asia", "cae", "in", "jp", "sa", "kr", "tr", "ru", "rue" };
            int idx = c - 'A';
            if (idx < 0 || idx >= table.Length) return L.Get("region_unlisted");
            return Describe(table[idx]);
        }

    }

    /// <summary>
    /// 在本体每次发起 Photon 连接前写入 FixedRegion。
    /// 打无参重载即可覆盖全部入口（NetworkingUtilities.ConnectToPhotonNetwork 和 PhotonShim 都调它）。
    /// </summary>
    [HarmonyPatch(typeof(PhotonNetwork), nameof(PhotonNetwork.ConnectUsingSettings), new Type[0])]
    internal static class ForceRegionPatch
    {
        private static void Prefix()
        {
            RegionControl.ApplyFixedRegion();
        }
    }
}
