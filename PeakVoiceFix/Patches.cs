using HarmonyLib;
using Photon.Realtime;
using ExitGames.Client.Photon;
using Photon.Pun;

namespace PeakVoiceFix.Patches
{
    [HarmonyPatch(typeof(LoadBalancingClient))]
    public class LoadBalancingClientPatch
    {
        // 32746 补丁已删除 (Redundant)

        [HarmonyPatch("OnEvent")]
        [HarmonyPrefix]
        public static void OnEventPrefix(EventData photonEvent)
        {
            // [Q3] 强制 186
            if (photonEvent.Code == 186)
            {
                NetworkManager.OnEvent(photonEvent);
            }
        }
    }
}