using System;
using HarmonyLib;
using Photon.Pun;
namespace PeakVoiceFix
{
    /// <summary>
    /// Fixes buffered skeleton removal RPC to avoid stale buffered state after rejoin.
    /// </summary>
    [HarmonyPatch]
    public static class PhotonRPCFix
    {
        [HarmonyPatch(typeof(PhotonNetwork), "RPC", new Type[]
        {
            typeof(PhotonView),
            typeof(string),
            typeof(RpcTarget),
            typeof(Photon.Realtime.Player),
            typeof(bool),
            typeof(object[])
        })]
        [HarmonyPrefix]
        public static void PrePhotonNetworkRPC(PhotonView view, string methodName, ref RpcTarget target)
        {
            // Keep fix-only behavior: no detective logging.
            if (methodName == "RemoveSkeletonRPC" && target == RpcTarget.AllBuffered)
            {
                target = RpcTarget.All;
            }
        }
    }
}
