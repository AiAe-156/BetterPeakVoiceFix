using System;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PeakVoiceFix
{
    /// <summary>
    /// Steam 邀请的"要房间号"握手兜底。
    ///
    /// 本体流程：客机进大厅后往大厅广播一条 chat 消息问房间号，房主侧只在
    /// IsMasterClient && InRoom 时才回。而客机发出请求后会把 m_currentlyRequestingRoomID 置上，
    /// 之后【永不重发、没有超时、不弹任何提示】——房主那一刻在菜单里、在加载中或刚掉线，
    /// 客机就永久静默卡在主菜单，日志停在 "Requested Room ID" 之后什么都没有。
    ///
    /// 我们的做法：记下请求发出的时刻，超时就把那个"已发请求"标志清掉，本体下一帧自然会重发；
    /// 重试到上限仍无果则明确提示原因。全程 fail-open：反射拿不到字段就什么都不做。
    /// </summary>
    internal static class InviteHandshake
    {
        private const float TIMEOUT = 8f;
        private const int MAX_ATTEMPTS = 4;

        private static readonly FieldInfo fRequesting =
            AccessTools.Field(typeof(SteamLobbyHandler), "m_currentlyRequestingRoomID");
        private static readonly FieldInfo fWaiting =
            AccessTools.Field(typeof(SteamLobbyHandler), "m_currentlyWaitingForRoomID");

        private static float sentAt = -1f;
        private static int attempts = 0;

        public static bool Available => fRequesting != null && fWaiting != null;

        private static SteamLobbyHandler Handler
        {
            get
            {
                try { return GameHandler.GetService<SteamLobbyHandler>(); }
                catch (Exception) { return null; }
            }
        }

        /// <summary>本体每次发出"要房间号"的请求时调用（Postfix）。</summary>
        public static void NoteRequestSent()
        {
            sentAt = Time.unscaledTime;
            if (attempts == 0) attempts = 1;
            NetworkManager.DiagLog(L.Get("invite_requested", attempts));
        }

        public static void Update()
        {
            if (VoiceFix.EnableInviteRetry == null || !VoiceFix.EnableInviteRetry.Value) return;
            if (sentAt < 0f) return;

            // 已经进房、或本体已经不在等房间号了 → 收工
            if (PhotonNetwork.InRoom || !IsWaiting()) { Reset(); return; }
            if (Time.unscaledTime - sentAt < TIMEOUT) return;

            attempts++;
            if (attempts > MAX_ATTEMPTS) { Fail(); return; }

            sentAt = Time.unscaledTime;   // 无论本体是否真的重发，都重新计时，避免卡死在这一步
            if (ClearRequestingFlag())
                NetworkManager.DiagLog(L.Get("invite_retry", attempts, MAX_ATTEMPTS));
            else
                Fail();
        }

        private static void Fail()
        {
            string msg = L.Get("invite_failed", MAX_ATTEMPTS);
            if (VoiceFix.logger != null) VoiceFix.logger.LogWarning(msg);
            Debug.LogWarning("[PVF] " + msg);
            if (VoiceUIManager.Instance != null)
            {
                VoiceUIManager.Instance.SetRegionWarning(msg);
                VoiceUIManager.Instance.AddLog("System", msg, true);
            }
            Reset();
        }

        public static void Reset()
        {
            sentAt = -1f;
            attempts = 0;
        }

        private static bool IsWaiting()
        {
            try
            {
                var h = Handler;
                if (h == null || fWaiting == null) return false;
                object v = fWaiting.GetValue(h);
                if (v == null) return false;
                var p = v.GetType().GetProperty("IsSome", BindingFlags.Public | BindingFlags.Instance);
                return p != null && (bool)p.GetValue(v);
            }
            catch (Exception) { return false; }
        }

        /// <summary>把"已发送请求"标志置回 None，本体的 Update 就会重新发一次。</summary>
        private static bool ClearRequestingFlag()
        {
            try
            {
                var h = Handler;
                if (h == null || fRequesting == null) return false;
                Type t = fRequesting.FieldType;
                object none = null;
                var prop = t.GetProperty("None", BindingFlags.Public | BindingFlags.Static);
                if (prop != null) none = prop.GetValue(null);
                if (none == null)
                {
                    var fld = t.GetField("None", BindingFlags.Public | BindingFlags.Static);
                    if (fld != null) none = fld.GetValue(null);
                }
                if (none == null) return false;
                fRequesting.SetValue(h, none);
                return true;
            }
            catch (Exception) { return false; }
        }
    }

    [HarmonyPatch(typeof(SteamLobbyHandler), "RequestPhotonRoomID")]
    internal static class RequestRoomIDPatch
    {
        private static void Postfix() => InviteHandshake.NoteRequestSent();
    }
}
