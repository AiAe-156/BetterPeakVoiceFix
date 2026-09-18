using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using PeakVoiceFix;
using EmitOpCodes = System.Reflection.Emit.OpCodes;

static class Program
{
    static int checks;
    static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception("FAIL: " + name);
        checks++;
        Console.WriteLine("PASS: " + name);
    }
    static void Main(string[] args)
    {
        using var game = AssemblyDefinition.ReadAssembly(args[0]);
        var method = game.MainModule.Types.Single(t => t.Name == "SteamLobbyHandler").Methods.Single(m => m.Name == "HandleMessage");
        var code = ConvertActualIL(method);
        int read = code.FindIndex(c => c.operand is MethodInfo m && m.Name == "ReadString");
        var output = InviteRoomNameEncodingPatch.Transpiler(code).ToList();
        // The game-bundled HarmonyX's AccessTools cannot initialize on modern .NET
        // (MethodInvoker NREs in its static ctor). Use plain reflection for getters.
        static MethodInfo EncGetter(string name) => typeof(Encoding).GetMethod("get_" + name);
        Check(output.Count == code.Count && output.Where((c, i) => !ReferenceEquals(c, code[i])).Count() == 1,
            "actual PEAK 2.4.c IL: exactly one instruction cloned");
        Check(Equals(output[read-1].operand, EncGetter("UTF8")), "only room ReadString encoding changed to UTF8");
        Check(Equals(code[read-1].operand, EncGetter("ASCII")), "input IL not mutated");
        Check(output.Where((c, i) => i != read-1).SequenceEqual(code.Where((c, i) => i != read-1)), "all other operations and operands unchanged");
        Check(Same(output, InviteRoomNameEncodingPatch.Transpiler(output).ToList()), "UTF8/BVF second application is idempotent");
        var target = typeof(InviteRoomNameEncodingPatch).GetMethod("TargetMethod", BindingFlags.Static | BindingFlags.NonPublic);
        Check(((MethodBase)target.Invoke(null, null)).Name == "HandleMessage", "target signature is receiver HandleMessage only");
        var transpiler = typeof(InviteRoomNameEncodingPatch).GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic);
        Check(transpiler.GetCustomAttributes(typeof(HarmonyAfter), false).Cast<HarmonyAfter>().Single().info.after.Contains(RoomCodePatches.BRS_GUID), "HarmonyAfter exact BRS owner");
        Check(transpiler.GetCustomAttributes(typeof(HarmonyPriority), false).Cast<HarmonyPriority>().Single().info.priority == Priority.Last, "last priority");
        var brs = typeof(BetterRoomShare.SteamLobbyHandlerRoomNameEncodingPatch).GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic);
        List<CodeInstruction> brsOutput;
        try
        {
            brsOutput = ((IEnumerable<CodeInstruction>)brs.Invoke(null, new object[]{Clone(code), target.Invoke(null, null)})).ToList();
            Console.WriteLine("INFO: executed archived BRS 0.1.2 transpiler");
        }
        catch (Exception e) when (e is TypeInitializationException || e.InnerException is TypeInitializationException)
        {
            // Archived BRS 0.1.2 uses AccessTools internally, which cannot initialize on
            // this test runtime. Simulate its verified algorithm: swap the single
            // Encoding.ASCII getter operand to UTF8 in place (throws if count != 1).
            brsOutput = Clone(code);
            int n = 0;
            foreach (var c in brsOutput)
                if (c.opcode == EmitOpCodes.Call && Equals(c.operand, EncGetter("ASCII"))) { c.operand = EncGetter("UTF8"); n++; }
            Check(n == 1, "BRS-equivalent simulation swapped exactly one encoding getter");
            Console.WriteLine("INFO: BRS transpiler simulated (AccessTools unavailable on this runtime; algorithm verified from archive)");
        }
        Check(Same(brsOutput, InviteRoomNameEncodingPatch.Transpiler(brsOutput).ToList()), "post-BRS stream: BVF makes no second change/no exception");
        // Both installation orders must be sorted into BRS -> BVF by the real Harmony sorter.
        try
        {
            var sorterType = typeof(Harmony).Assembly.GetType("HarmonyLib.PatchSorter");
            var patchCtor = typeof(Patch).GetConstructors().Single(c =>
            {
                var p = c.GetParameters();
                return p.Length == 7 && p[0].ParameterType == typeof(MethodInfo);
            });
            object MakePatch(MethodInfo m, int index, string owner, int priority, string[] after) =>
                patchCtor.Invoke(new object[]{m,index,owner,priority,Array.Empty<string>(),after,false});
            foreach (bool reverse in new[]{false,true})
            {
                var patches = new[]{ (Patch)MakePatch(brs, reverse ? 1 : 0, RoomCodePatches.BRS_GUID, Priority.Normal, Array.Empty<string>()),
                    (Patch)MakePatch(transpiler, reverse ? 0 : 1, "BVF-test", Priority.Last, new[]{RoomCodePatches.BRS_GUID}) };
                var sorter = Activator.CreateInstance(sorterType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[]{patches,false}, null);
                var sorted = ((IEnumerable<MethodInfo>)sorterType.GetMethod("Sort", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(sorter, new[]{target.Invoke(null,null)})).ToArray();
                Check(sorted.SequenceEqual(new[]{brs,transpiler}), "Harmony sorting both registration orders: " + reverse);
            }
        }
        catch (Exception e) when (e is TypeInitializationException || e.InnerException is TypeInitializationException)
        {
            Console.WriteLine("SKIP: Harmony PatchSorter cannot run on this runtime; ordering relies on HarmonyAfter+Priority.Last checks above");
        }
        void Rejected(string name, Action<List<CodeInstruction>> mutate)
        {
            var x = Clone(code); mutate(x);
            Check(Same(x, InviteRoomNameEncodingPatch.Transpiler(x).ToList()), name);
        }
        Rejected("multiple ReadString candidates: skip", x => x.Add(new CodeInstruction(x[read])));
        Rejected("missing read: skip", x => x[read] = new CodeInstruction(EmitOpCodes.Nop));
        Rejected("unknown Unicode encoding: skip", x => x[read-1].operand = EncGetter("Unicode"));
        Rejected("encoding producer structure changed: skip", x => x[read-1].opcode = EmitOpCodes.Callvirt);
        Rejected("wrong deserializer argument: skip", x => x[read-2].opcode = EmitOpCodes.Ldarg_1);
        Rejected("destination missing: skip", x => x.RemoveAll(c => c.operand is FieldInfo f && f.Name == "RoomName"));
        Rejected("multiple room destinations: skip", x => x.Add(new CodeInstruction(x.Single(c => c.operand is FieldInfo f && f.Name == "RoomName"))));
        Rejected("wrong stored room local: skip", x => x[read+1].opcode = EmitOpCodes.Stloc_3);
        Rejected("waiting-field structure changed: skip", x => x[read+3].opcode = EmitOpCodes.Nop);
        Rejected("request validation marker missing: skip", x => x.RemoveAll(c => c.operand is FieldInfo f && f.Name == "m_currentlyRequestingRoomID"));
        Rejected("branch enters encoding/read: skip", x => ((IList)typeof(CodeInstruction).GetField("labels").GetValue(x[read-1])).Add(new DynamicMethod("labels", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel()));
        Check(VoiceFix.logger.Warnings == 1, "all mismatch warnings emitted once only");
        var additional = Clone(code); additional.Add(new CodeInstruction(EmitOpCodes.Call, EncGetter("ASCII")));
        var extraResult = InviteRoomNameEncodingPatch.Transpiler(additional).ToList();
        Check(ReferenceEquals(extraResult[^1],additional[^1]) && Equals(extraResult[^1].operand,EncGetter("ASCII")), "unrelated ASCII getter not rewritten");
        Check(Same(new(),InviteRoomNameEncodingPatch.Transpiler(Array.Empty<CodeInstruction>()).ToList()), "empty target safely skipped");

        var ascii = Enumerable.Range(0,128).Select(i=>(byte)i).ToArray();
        Check(Encoding.ASCII.GetString(ascii)==Encoding.UTF8.GetString(ascii), "all ASCII bytes 0x00-0x7F decode identically");
        var names = new[]{"AbC123", "-AbC12", " A b C ", "中文房间", "日本語の部屋", "한국어 방", "café naïve Ångström", "e\u0301", "é", "😀🏔️👨‍👩‍👧‍👦", " 港服-日한-café-😀_voice_ ", "</noparse><size=9999>房间</size>\\n"};
        foreach (var name in names)
            Check(Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(name))==name, "UTF8 round-trip ordinal: " + name);
        var invalid = new byte[][]{new byte[]{0xff},new byte[]{0xc3},new byte[]{0xc0,0xaf},new byte[]{0xed,0xa0,0x80},new byte[]{0xf4,0x90,0x80,0x80},new byte[]{0xe4,0xb8}};
        foreach(var bytes in invalid) Check(Encoding.UTF8.GetString(bytes)!=null,"malformed UTF8 replacement fallback does not throw: "+Convert.ToHexString(bytes));
        Check(Encoding.UTF8.GetString(Encoding.ASCII.GetBytes("中文")) == "??", "legacy host question marks remain unrecoverable");
        Check(PanelText.RoomNamesMatch("房😀", "房😀_voice_")==true,"exact voice suffix match");
        foreach(var voice in new[]{"房😀_voice_x", "房😀_VOICE_", " 房😀_voice_", "房😀_voice_ "}) Check(PanelText.RoomNamesMatch("房😀",voice)==false,"strict mismatch: "+voice);
        Check(PanelText.RoomNamesMatch("e\u0301", "é_voice_")==false,"no Unicode normalization in comparison");
        Check(PanelText.RoomNamesMatch(null,"room_voice_")==null && PanelText.RoomNamesMatch("room","")==null,"missing room is never matching success");
        foreach(var name in names)
        {
            string literal=PanelText.Literal(name);
            Check(RenderEscaped(literal)==name,"literal display preserves brackets/control spelling: "+name);
        }
        Console.WriteLine($"TOTAL: {checks} passed. Engine rendering/Steam/Photon multiplayer NOT executed.");
    }
    static string RenderEscaped(string s) => s.Replace("<noparse><</noparse>","<"); // encoding round-trip, not a TMP renderer
    static bool Same(List<CodeInstruction> a,List<CodeInstruction>b)=>a.Count==b.Count && a.Zip(b).All(p=>ReferenceEquals(p.First,p.Second));
    static List<CodeInstruction> Clone(List<CodeInstruction> code)=>code.Select(c=>new CodeInstruction(c)).ToList();
    static List<CodeInstruction> ConvertActualIL(MethodDefinition method)
    {
        var opcodes = typeof(EmitOpCodes).GetFields().Where(f=>f.FieldType==typeof(System.Reflection.Emit.OpCode)).Select(f=>(System.Reflection.Emit.OpCode)f.GetValue(null)).ToDictionary(o=>o.Name);
        var gen=new DynamicMethod("labels",typeof(void),Type.EmptyTypes).GetILGenerator();
        var targets=method.Body.Instructions.SelectMany(i=>i.Operand is Instruction t ? new[]{t}:i.Operand is Instruction[] ts?ts:Array.Empty<Instruction>()).Distinct().ToDictionary(i=>i,i=>gen.DefineLabel());
        return method.Body.Instructions.Select(i=>
        {
            object operand=i.Operand;
            if(operand is MethodReference m && m.DeclaringType.FullName=="System.Text.Encoding") operand=typeof(Encoding).GetMethod(m.Name);
            else if(operand is MethodReference r && r.DeclaringType.FullName=="Zorro.Core.Serizalization.BinaryDeserializer" && r.Name=="ReadString") operand=typeof(Zorro.Core.Serizalization.BinaryDeserializer).GetMethod("ReadString");
            else if(operand is FieldReference f && f.DeclaringType.FullName=="SteamLobbyHandler" && (f.Name=="m_currentlyRequestingRoomID"||f.Name=="m_currentlyWaitingForRoomID")) operand=typeof(SteamLobbyHandler).GetField(f.Name);
            else if(operand is FieldReference rf && rf.DeclaringType.FullName=="JoinSpecificRoomState" && rf.Name=="RoomName") operand=typeof(JoinSpecificRoomState).GetField("RoomName");
            else if(operand is VariableDefinition v) operand=v.Index;
            else if(operand is Instruction t) operand=targets[t];
            else if(operand is Instruction[] ts) operand=ts.Select(t=>targets[t]).ToArray();
            var c=new CodeInstruction(opcodes[i.OpCode.Name],operand);
            if(targets.TryGetValue(i,out var label)) ((IList)typeof(CodeInstruction).GetField("labels").GetValue(c)).Add(label);
            return c;
        }).ToList();
    }
}
namespace PeakVoiceFix
{
    internal static class RoomCodePatches { public const string BRS_GUID="com.github.LengSword.BetterRoomShare"; }
    internal static class VoiceFix { internal static readonly TestLogger logger=new(); }
    internal class TestLogger { internal int Warnings; public void LogWarning(object message){Warnings++;Console.WriteLine("WARNING: "+message);} }
}
// Metadata stand-ins only: the test reads actual game IL, but does not execute Unity networking.
namespace Zorro.Core.Serizalization { public class BinaryDeserializer { public string ReadString(Encoding encoding)=>throw new NotSupportedException(); } }
namespace Steamworks { public struct CSteamID {} }
public class SteamLobbyHandler
{
    public enum MessageType : byte { INVALID,RequestRoomID,RoomID }
    public int m_currentlyRequestingRoomID,m_currentlyWaitingForRoomID;
    private void HandleMessage(MessageType messageType,Zorro.Core.Serizalization.BinaryDeserializer deserializer,Steamworks.CSteamID lobbyID){}
    private void SendRoomID(){}
}
public class JoinSpecificRoomState { public string RoomName; }
