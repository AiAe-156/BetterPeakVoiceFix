using System;
using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace PeakVoiceFix
{
    // 只恢复播放组件的失效引用；不改注册表、通信权限或玩家的静音/屏蔽选择。
    internal static class VoiceStateRecovery
    {
        internal enum Status { Healthy, Recovered, Missing }
        private sealed class Entry
        {
            internal bool Missing;
            internal float RecoveredUntil;
        }

        private static AccessTools.FieldRef<CharacterVoiceHandler, CharacterData> cachedData;
        private static readonly Dictionary<CharacterVoiceHandler, Entry> entries = new Dictionary<CharacterVoiceHandler, Entry>();
        private static readonly Dictionary<int, Status> actors = new Dictionary<int, Status>();
        private static readonly HashSet<CharacterVoiceHandler> seen = new HashSet<CharacterVoiceHandler>();
        private static readonly List<CharacterVoiceHandler> stale = new List<CharacterVoiceHandler>();
        private static Room room;
        private static float nextScan;
        private static bool unavailable;

        internal static bool HasNotice => actors.Count != 0;
        internal static Status GetStatus(int actor) => actors.TryGetValue(actor, out var status) ? status : Status.Healthy;

        internal static void Update()
        {
            if (unavailable) return;
            if (!PhotonNetwork.InRoom || PhotonNetwork.OfflineMode || !ReferenceEquals(room, PhotonNetwork.CurrentRoom))
            {
                entries.Clear();
                actors.Clear();
                room = PhotonNetwork.CurrentRoom;
                nextScan = 0f;
                if (!PhotonNetwork.InRoom || PhotonNetwork.OfflineMode) return;
            }
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.5f;
            try
            {
                if (cachedData == null)
                    cachedData = AccessTools.FieldRefAccess<CharacterVoiceHandler, CharacterData>("m_characterData");
                Scan();
            }
            catch (Exception ex)
            {
                unavailable = true;
                entries.Clear();
                actors.Clear();
                VoiceFix.logger.LogWarning("[语音状态恢复] 功能停用，其余语音功能继续运行: " + ex.Message);
            }
        }

        private static bool BelongsTo(CharacterData data, Photon.Realtime.Player owner)
        {
            if (data == null) return false;
            var view = data.GetComponentInParent<PhotonView>();
            return view != null && view.Owner == owner;
        }

        private static void Scan()
        {
            actors.Clear();
            seen.Clear();
            foreach (var character in Character.AllCharacters)
            {
                if (character == null || !character.gameObject.activeInHierarchy || character.IsLocal || character.isBot) continue;
                var view = character.photonView;
                var owner = view != null ? view.Owner : null;
                if (owner == null || owner.IsLocal || room.GetPlayer(owner.ActorNumber) != owner) continue;
                var handler = character.GetComponentInChildren<CharacterVoiceHandler>();
                if (handler == null || !handler.isActiveAndEnabled || !handler.Initialized) continue;
                seen.Add(handler);
                if (!entries.TryGetValue(handler, out var entry))
                    entries.Add(handler, entry = new Entry());

                // Unity 判空同时覆盖真实 null 和已销毁的旧角色组件。
                bool missing = cachedData(handler) == null;
                if (missing && !entry.Missing)
                    NetworkManager.DiagLog($"[语音状态] actor={owner.ActorNumber}: 引用失效");
                if (missing && VoiceFix.AutoRecoverVoiceState.Value)
                {
                    CharacterData replacement;
                    if (!character.TryGetGlobalPlayerData(out replacement) || !BelongsTo(replacement, owner))
                        replacement = character.data;
                    if (BelongsTo(replacement, owner))
                    {
                        cachedData(handler) = replacement;
                        missing = false;
                        entry.RecoveredUntil = Time.unscaledTime + 8f;
                        NetworkManager.DiagLog($"[语音状态] actor={owner.ActorNumber}: 已恢复引用（保留静音/屏蔽状态）");
                    }
                }
                if (!missing && entry.Missing)
                    entry.RecoveredUntil = Time.unscaledTime + 8f;
                entry.Missing = missing;
                var status = missing ? Status.Missing : Time.unscaledTime < entry.RecoveredUntil ? Status.Recovered : Status.Healthy;
                // 新旧角色短暂共存时，未恢复的异常优先显示。
                if (status != Status.Healthy && (!actors.TryGetValue(owner.ActorNumber, out var previous) || (int)status > (int)previous))
                    actors[owner.ActorNumber] = status;
            }
            stale.Clear();
            foreach (var pair in entries)
                if (!seen.Contains(pair.Key)) stale.Add(pair.Key);
            foreach (var handler in stale) entries.Remove(handler);
        }
    }
}
