using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using HarmonyLib;

namespace PeakVoiceFix
{
    // Only the receiving room-name argument is changed. Never patch SendRoomID.
    [HarmonyPatch]
    internal static class InviteRoomNameEncodingPatch
    {
        private const string DeserializerType = "Zorro.Core.Serizalization.BinaryDeserializer";
        private static int warned;

        private static MethodBase TargetMethod()
        {
            var type = typeof(SteamLobbyHandler);
            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(m => m.Name == "HandleMessage").ToArray();
            if (methods == null || methods.Length != 1)
                throw new MissingMethodException("SteamLobbyHandler.HandleMessage is not unique");
            var method = methods[0];
            var p = method.GetParameters();
            if (method.ReturnType != typeof(void) || p.Length != 3
                || p[0].ParameterType.FullName != "SteamLobbyHandler+MessageType"
                || p[1].ParameterType.FullName != DeserializerType
                || p[2].ParameterType.FullName != "Steamworks.CSteamID")
                throw new MissingMethodException("SteamLobbyHandler.HandleMessage signature changed");
            return method;
        }

        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter(RoomCodePatches.BRS_GUID)]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            try
            {
                int read = -1, store = -1;
                for (int i = 0; i < code.Count; i++)
                {
                    if (code[i].operand is MethodInfo m && m.Name == "ReadString"
                        && m.DeclaringType?.FullName == DeserializerType)
                    {
                        if (read >= 0 || m.IsStatic || m.ReturnType != typeof(string)
                            || m.GetParameters().Length != 1 || m.GetParameters()[0].ParameterType != typeof(Encoding))
                            return Skip(code, "room-name ReadString is ambiguous or changed");
                        read = i;
                    }
                    if (code[i].opcode == OpCodes.Stfld && code[i].operand is FieldInfo f
                        && f.DeclaringType?.FullName == "JoinSpecificRoomState" && f.Name == "RoomName")
                    {
                        if (store >= 0 || f.FieldType != typeof(string))
                            return Skip(code, "RoomName destination is ambiguous or changed");
                        store = i;
                    }
                }
                // Verified 2.4.c shape: ldarg.2 / encoding getter / ReadString / stloc room;
                // later ldloc room / stfld JoinSpecificRoomState.RoomName. Do not infer other shapes.
                if (read < 2 || read + 3 >= code.Count || store <= read + 3
                    || code[read - 2].opcode != OpCodes.Ldarg_2
                    || code[read - 1].opcode != OpCodes.Call
                    || code[read].opcode != OpCodes.Callvirt
                    || code[read + 2].opcode != OpCodes.Ldarg_0
                    || !IsField(code[read + 3], OpCodes.Ldflda, "m_currentlyWaitingForRoomID")
                    || !code.Take(read - 2).Any(c => IsField(c, OpCodes.Ldflda, "m_currentlyRequestingRoomID"))
                    || LocalIndex(code[read + 1], true) < 0
                    || LocalIndex(code[read + 1], true) != LocalIndex(code[store - 1], false)
                    || HasIncomingLabel(code[read - 1]) || HasIncomingLabel(code[read])
                    || code[read - 1].blocks.Count != 0 || code[read].blocks.Count != 0)
                    return Skip(code, "room-name read/destination structure changed");

                // Plain reflection, not AccessTools: works on any runtime and keeps
                // the patch independent of Harmony internals while patching.
                var ascii = typeof(Encoding).GetMethod("get_" + nameof(Encoding.ASCII));
                var utf8 = typeof(Encoding).GetMethod("get_" + nameof(Encoding.UTF8));
                if (Equals(code[read - 1].operand, utf8)) return code; // BRS already applied.
                if (!Equals(code[read - 1].operand, ascii))
                    return Skip(code, "unknown room-name encoding; left unchanged");
                // Clone only after all checks. Labels/exception blocks and every other operand stay intact.
                var replacement = new CodeInstruction(code[read - 1]);
                replacement.operand = utf8;
                code[read - 1] = replacement;
                return code;
            }
            catch (Exception ex)
            {
                return Skip(code, "inspection failed: " + ex);
            }
        }

        // BepInEx 5 Harmony exposes List<Label> from mscorlib; avoid that incompatible
        // compile-time type identity in netstandard2.1. This runs only while patching.
        private static readonly FieldInfo LabelsField = typeof(CodeInstruction).GetField("labels");
        private static bool HasIncomingLabel(CodeInstruction c) =>
            !(LabelsField?.GetValue(c) is ICollection labels) || labels.Count != 0;

        private static bool IsField(CodeInstruction c, OpCode opcode, string name) =>
            c.opcode == opcode && c.operand is FieldInfo f
            && f.DeclaringType?.FullName == "SteamLobbyHandler" && f.Name == name;

        private static int LocalIndex(CodeInstruction c, bool store)
        {
            if (c.opcode == (store ? OpCodes.Stloc_0 : OpCodes.Ldloc_0)) return 0;
            if (c.opcode == (store ? OpCodes.Stloc_1 : OpCodes.Ldloc_1)) return 1;
            if (c.opcode == (store ? OpCodes.Stloc_2 : OpCodes.Ldloc_2)) return 2;
            if (c.opcode == (store ? OpCodes.Stloc_3 : OpCodes.Ldloc_3)) return 3;
            if (c.opcode == (store ? OpCodes.Stloc : OpCodes.Ldloc)
                || c.opcode == (store ? OpCodes.Stloc_S : OpCodes.Ldloc_S))
            {
                if (c.operand is LocalBuilder local) return local.LocalIndex;
                if (c.operand is int index) return index;
                if (c.operand is byte small) return small;
            }
            return -1;
        }

        private static IEnumerable<CodeInstruction> Skip(List<CodeInstruction> code, string reason)
        {
            if (Interlocked.Exchange(ref warned, 1) == 0)
                VoiceFix.logger?.LogWarning("[邀请编码] " + reason + "; UTF-8 adaptation skipped, BVF core remains active.");
            return code;
        }
    }
}
