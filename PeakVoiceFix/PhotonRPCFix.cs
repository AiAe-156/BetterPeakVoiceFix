using System;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using BepInEx;

namespace PeakVoiceFix
{
    /// <summary>
    /// [v0.2.1.1 侦探模式]
    /// 移除所有过滤条件，监听一切 RPC 以捕捉核心指令。
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
            // 必须开启 Debug 日志才工作
            if (VoiceFix.EnableDebugLogs == null || !VoiceFix.EnableDebugLogs.Value) return;

            // --- 过滤器 ---
            // 过滤掉极其频繁的垃圾信息，防止刷屏
            // 如果你发现控制台还是太乱，可以把这些关键词加多一点
            if (methodName.Contains("Transform") ||
                methodName.Contains("Movement") ||
                methodName.Contains("Time") ||
                methodName.Contains("Speak"))
            {
                return;
            }

            // --- 核心捕捉 ---
            // 打印所有 RPC 的名字和发送目标
            // 格式: [RPC捕捉] 方法名 | 目标类型
            if (VoiceFix.logger != null)
            {
                VoiceFix.logger.LogWarning($"[RPC捕捉] Name: {methodName} | Target: {target}");
            }

            // 注意：侦探模式下，暂时禁用了“修复逻辑”，因为我们要先找名字。
            // 等找到名字后，我们再把修复逻辑加回来。
        }
    }
}