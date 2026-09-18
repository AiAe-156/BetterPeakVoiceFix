using System;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace PeakVoiceFix
{
    /// <summary>
    /// 本体联机链路的两处兜底。都做成【幂等】：看到结果已经是对的就不动手，
    /// 所以和 LengSword 的 BetterRoomShare 装在一起也不会打架，谁先谁后都无所谓。
    ///
    /// ① 房间码里未收录区服的 fallback 字符 '-'：
    ///    本体 Utilities.RegionToCode 对表里没有的区（hk 就没收录）返回 '-'，
    ///    而 CodeToRegion('-') 里 Mathf.Clamp(45-65, 0, 14) = 0 → 解出 REGIONS[0] = "us"。
    ///    后果是港服房的房间码把客机送去美国区找房，必然失败。
    ///
    /// ② 切区服后立刻加房：
    ///    MainMenuJoinRoomPage.SwapRegionAuthentication 是 Disconnect → ConnectToRegion → 立刻 JoinRoom，
    ///    此时还在连主服务器，加房必然失败。本体自己写了 WaitForPhotonState 协程却没接上（死代码）。
    /// </summary>
    internal static class RoomCodePatches
    {
        public const string BRS_GUID = "com.github.LengSword.BetterRoomShare";
        private static bool? brsPresent;

        /// <summary>BetterRoomShare 是否在场。只用于日志和冲突提示，兜底逻辑本身不依赖它。</summary>
        public static bool BetterRoomSharePresent
        {
            get
            {
                if (brsPresent.HasValue) return brsPresent.Value;
                try
                {
                    brsPresent = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(BRS_GUID);
                }
                catch (Exception) { brsPresent = false; }
                return brsPresent.Value;
            }
        }

        public static void Announce()
        {
            if (VoiceFix.logger == null) return;
            if (BetterRoomSharePresent)
                VoiceFix.logger.LogInfo("[房间码] 检测到 BetterRoomShare，兜底改为幂等模式（结果已正确则不介入）");
            string want = VoiceFix.UnknownRegionAs != null ? VoiceFix.UnknownRegionAs.Value : "";
            if (BetterRoomSharePresent && !string.IsNullOrEmpty(want) && want != "hk")
                VoiceFix.logger.LogWarning($"[房间码] 你把未收录区服设成了 '{want}'，但 BetterRoomShare 固定解析为 'hk'；" +
                                           "两者都在场时最终值取决于补丁执行顺序。");
        }
    }

    /// <summary>
    /// 未收录区服的房间码解析兜底。只在结果仍是错的（'-' 被 Clamp 成 REGIONS[0]）时才改，
    /// 已经被别的 mod 修正过就放手——所以顺序无关、装几个都安全。
    /// </summary>
    [HarmonyPatch]
    internal static class CodeToRegionFallbackPatch
    {
        // 本体那张表的第 0 项，也就是 '-' 被 Clamp 之后必然得到的错误结果
        private const string CLAMPED_WRONG = "us";

        private static MethodBase TargetMethod()
        {
            // 用类型名反射而不是硬引用 Utilities.dll：2.4.b 的 SteamManager 就从 Assembly-CSharp
            // 搬到了独立程序集，害得老 mod 直接抛 TypeLoadException。这里找不到就让 TryPatch 兜住。
            var t = AccessTools.TypeByName("Portningsbolaget.Utilities.Utilities");
            if (t == null) throw new Exception("Portningsbolaget.Utilities.Utilities 未找到");
            var m = AccessTools.Method(t, "CodeToRegion", new Type[] { typeof(char) });
            if (m == null) throw new Exception("CodeToRegion(char) 未找到");
            return m;
        }

        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(RoomCodePatches.BRS_GUID)]
        private static void Postfix(char __0, ref string __result)
        {
            if (__0 != '-') return;                       // 只管那个 fallback 字符
            string want = VoiceFix.UnknownRegionAs != null ? VoiceFix.UnknownRegionAs.Value : "";
            if (string.IsNullOrEmpty(want)) return;        // 留空 = 不猜，保持本体行为
            if (__result != CLAMPED_WRONG) return;         // 已经被修正过（别的 mod 或我们自己）
            __result = want.Trim().ToLowerInvariant();
        }
    }

    /// <summary>
    /// 切区服后等主服务器就绪再加房。包一层协程：外层等 ConnectedToMasterServer，内层跑本体原逻辑。
    /// 已经连上时第一次检查就直接通过，所以和 BetterRoomShare 的同名兜底叠在一起也只是多一层空转。
    /// </summary>
    [HarmonyPatch(typeof(MainMenuJoinRoomPage), "JoinRoomAndWaitForSpawn")]
    internal static class WaitMasterBeforeJoinPatch
    {
        private static void Postfix(ref System.Collections.IEnumerator __result)
        {
            if (VoiceFix.WaitMasterBeforeJoin == null || !VoiceFix.WaitMasterBeforeJoin.Value) return;
            __result = Wrap(__result);
        }

        private static System.Collections.IEnumerator Wrap(System.Collections.IEnumerator inner)
        {
            // 离线模式下 NetworkClientState 停在 Joined，等主服就绪的条件永不成立——直接跑原逻辑。
            if (!PhotonNetwork.OfflineMode)
            {
                float deadline = Time.realtimeSinceStartup + 20f;
                while (PhotonNetwork.NetworkClientState != Photon.Realtime.ClientState.ConnectedToMasterServer)
                {
                    if (Time.realtimeSinceStartup >= deadline)
                    {
                        string msg = L.Get("joinwait_timeout", PhotonNetwork.NetworkClientState.ToString(), PhotonNetwork.CloudRegion ?? "—");
                        if (VoiceFix.logger != null) VoiceFix.logger.LogWarning(msg);
                        if (VoiceUIManager.Instance != null) VoiceUIManager.Instance.SetRegionWarning(msg);
                        Peak.Network.NetworkingUtilities.EnteringRoom = false;
                        yield break;
                    }
                    yield return null;
                }
                NetworkManager.DiagLog(L.Get("joinwait_ready", PhotonNetwork.CloudRegion ?? "—"));
            }
            while (inner.MoveNext()) yield return inner.Current;
        }
    }
}
