using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using Photon.Voice.Unity;
using UnityEngine;

namespace PeakVoiceFix
{
    /// <summary>
    /// Photon 应用身份守卫：快照本机真 AppIdRealtime / AppIdVoice，发现全局
    /// PhotonServerSettings.AppSettings 被改写时还原。
    ///
    /// 背景（2026-09-17 实测确认）：LocalMultiplayer 在每次 NetworkConnector.Start 把全局
    /// AppSettings 的两个 AppId 改写成它配置里的自建 Photon 应用（第二实例绕开 EOS 认证用）。
    /// 游戏连接在主菜单就完成认证、进房只是复用，所以不受影响；但语音客户端是进房后才
    /// 首连，ConnectVoice 一拷贝被污染的设置就连进另一个 Photon 应用的同名房——同区、
    /// 同房名却互不可见，双向静音，重连多少次都回不去。
    ///
    /// 工作方式：
    /// ① 插件 Awake 快照真值（此时任何场景补丁都不可能跑过，值必真）；
    /// ② Update 低频巡检全局设置，被改写即还原；
    /// ③ VoiceConnection.ConnectUsingSettings 挂 prefix，语音每次连接前先把传入/自存
    ///    settings 的 AppId 拨回快照值，挡住巡检间隙被抢的窗口。
    /// 还原规则是"与快照不同就拨回"，不限于空值——未配置时 LM 写空串、配置后写自建
    /// AppId，两种都是错房。
    /// </summary>
    internal static class PhotonSettingsGuard
    {
        private static string realRealtime;
        private static string realVoice;
        private static bool captured;
        private static bool deviatedLogged;   // 本轮污染已告警过（还原后再被写会再记）
        private static float nextCheckTime;
        private const float CHECK_INTERVAL = 1f;

        public static bool Enabled
        {
            get { return VoiceFix.EnableAppIdGuard == null || VoiceFix.EnableAppIdGuard.Value; }
        }

        /// <summary>VoiceFix.Awake 调用：尽早快照真值。</summary>
        public static void Init()
        {
            TryCapture();
        }

        /// <summary>VoiceFix.Update 调用：低频巡检全局设置。</summary>
        public static void Update()
        {
            if (!Enabled) return;
            if (Time.unscaledTime < nextCheckTime) return;
            nextCheckTime = Time.unscaledTime + CHECK_INTERVAL;
            if (!captured) TryCapture();
            else CheckGlobal();
        }

        private static AppSettings Global()
        {
            // ServerSettings 是 ScriptableObject：用 == 才能吃到 Unity 的 fake-null 语义
            ServerSettings ss = PhotonNetwork.PhotonServerSettings;
            if (ss == null) return null;
            return ss.AppSettings;
        }

        /// <summary>只采信非空值：快照拿晚了可能已是污染后的值，空串绝不能当真值存。</summary>
        private static void TryCapture()
        {
            try
            {
                var app = Global();
                if (app == null) return;
                if (realRealtime == null && !string.IsNullOrEmpty(app.AppIdRealtime)) realRealtime = app.AppIdRealtime;
                if (realVoice == null && !string.IsNullOrEmpty(app.AppIdVoice)) realVoice = app.AppIdVoice;
                if (!captured && realRealtime != null && realVoice != null)
                {
                    captured = true;
                    NetworkManager.DiagLog(L.Get("diag_appid_snapshot", realRealtime, realVoice));
                }
            }
            catch (System.Exception) { }
        }

        /// <summary>巡检全局设置：被改写就还原，并把改写值记进诊断（定位元凶就靠它）。</summary>
        private static void CheckGlobal()
        {
            try
            {
                var app = Global();
                if (app == null) return;
                bool badRt = realRealtime != null && app.AppIdRealtime != realRealtime;
                bool badVoice = realVoice != null && app.AppIdVoice != realVoice;
                if (!badRt && !badVoice) { deviatedLogged = false; return; }

                if (!deviatedLogged)
                {
                    deviatedLogged = true;
                    string msg = L.Get("diag_appid_restored",
                        badRt ? app.AppIdRealtime : "—",
                        badVoice ? app.AppIdVoice : "—");
                    NetworkManager.DiagLog(msg);
                    if (VoiceFix.logger != null) VoiceFix.logger.LogWarning(msg);
                }
                if (badRt) app.AppIdRealtime = realRealtime;
                if (badVoice) app.AppIdVoice = realVoice;
            }
            catch (System.Exception) { }
        }

        /// <summary>语音连接前兜底：把传入/自存 settings 的 AppId 拨回快照值，并顺手巡检全局。</summary>
        public static void SanitizeForVoiceConnect(VoiceConnection conn, AppSettings overwrite)
        {
            if (!Enabled || !captured) return;
            try
            {
                var s = overwrite ?? (conn != null ? conn.Settings : null);
                if (s != null)
                {
                    if (realVoice != null && s.AppIdVoice != realVoice) s.AppIdVoice = realVoice;
                    if (realRealtime != null && s.AppIdRealtime != realRealtime) s.AppIdRealtime = realRealtime;
                }
                CheckGlobal();
            }
            catch (System.Exception) { }
        }
    }

    /// <summary>
    /// 兜底 prefix：原版 ConnectVoice 和本 mod 的重连都走 ConnectUsingSettings，
    /// 在这里统一把 settings 拨回真值，巡检间隙被抢的窗口也进不了脏 AppId。
    /// 挂了也不影响主功能（巡检还在），所以走 TryPatch 挂载。
    /// </summary>
    [HarmonyPatch(typeof(VoiceConnection), nameof(VoiceConnection.ConnectUsingSettings))]
    internal static class VoiceConnectSettingsPatch
    {
        private static void Prefix(VoiceConnection __instance, AppSettings overwriteSettings)
        {
            PhotonSettingsGuard.SanitizeForVoiceConnect(__instance, overwriteSettings);
        }
    }
}
