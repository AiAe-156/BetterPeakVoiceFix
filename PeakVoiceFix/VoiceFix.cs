using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using PeakVoiceFix.Patches;

namespace PeakVoiceFix
{
    // [版本] v0.3.2 - Smart Ghost Detection
    [BepInPlugin("chuxiaaaa.Aiae.BetterVoiceFix", "BetterVoiceFix", "0.3.2")]
    public class VoiceFix : BaseUnityPlugin
    {
        public static VoiceFix Instance;
        public static ManualLogSource logger;
        public static ManualLogSource debugLogger;

        public static ConfigEntry<bool> UIPositionSide;
        public static ConfigEntry<KeyCode> ToggleUIKey;
        public static ConfigEntry<bool> ShowProfessionalInfo;
        public static ConfigEntry<float> OffsetX_Right, OffsetY_Right, OffsetX_Left, OffsetY_Left;
        public static ConfigEntry<float> FontSize;
        public static ConfigEntry<string> HostSymbol;

        public static ConfigEntry<float> ConnectTimeout;
        public static ConfigEntry<float> RetryInterval;
        public static ConfigEntry<bool> EnableManualReconnect;
        public static ConfigEntry<bool> EnableGhostFix;

        public static ConfigEntry<int> MaxTotalLength;
        public static ConfigEntry<float> LatencyOffset;
        public static ConfigEntry<bool> AutoHideNormal;
        public static ConfigEntry<bool> ShowPingInNormal;
        public static ConfigEntry<bool> HideOnMenu;
        public static ConfigEntry<bool> EnableDebugLogs;
        public static ConfigEntry<bool> EnableVirtualTestPlayer;
        public static ConfigEntry<string> TestPlayerName;

        public const string MOD_VERSION = "v0.3.2"; // [新增] 版本常量供广播使用

        void Awake()
        {
            Instance = this;
            logger = Logger;
            debugLogger = new ManualLogSource("VoiceFixDebug");
            BepInEx.Logging.Logger.Sources.Add(debugLogger);

            // --- UI Settings ---
            string catUI = "UI Settings";
            UIPositionSide = Config.Bind(catUI, "Position Side (Right)", true, "If true, UI aligns to the top-right. If false, top-left.");
            ToggleUIKey = Config.Bind(catUI, "Toggle Key", KeyCode.J, "Key to toggle the voice status panel.");
            ShowProfessionalInfo = Config.Bind(catUI, "Show Detailed IP Info", true, "Show IP addresses and debug info in the expanded panel.");

            OffsetX_Right = Config.Bind(catUI, "Right Offset X", 20f, "Horizontal margin from right edge.");
            OffsetY_Right = Config.Bind(catUI, "Right Offset Y", 20f, "Vertical margin from top edge.");
            OffsetX_Left = Config.Bind(catUI, "Left Offset X", 20f, "Horizontal margin from left edge.");
            OffsetY_Left = Config.Bind(catUI, "Left Offset Y", 20f, "Vertical margin from top edge.");

            FontSize = Config.Bind(catUI, "Font Size", 18f, "Base font size for the panel.");
            HostSymbol = Config.Bind(catUI, "Host Symbol", "★", "Symbol displayed next to the room host's name.");

            // --- Network Settings ---
            string catNet = "Network Settings";
            ConnectTimeout = Config.Bind(catNet, "Reconnect Timeout (s)", 25f, "Threshold to force a retry if connection is stuck.");
            RetryInterval = Config.Bind(catNet, "Retry Interval (s)", 8f, "Cooldown between reconnection attempts.");
            EnableManualReconnect = Config.Bind(catNet, "Enable Manual Reset (Alt+K)", true, "Allow pressing Alt+K to force disconnect/reconnect.");
            EnableGhostFix = Config.Bind(catNet, "Enable ID Drift Fix", true, "Prevents 'Unknown' names by scavenging data from the scene.");

            // --- Advanced ---
            string catAdv = "Advanced & Debug";
            MaxTotalLength = Config.Bind(catAdv, "Max Name Length", 26, new ConfigDescription("Max characters for name display.", new AcceptableValueRange<int>(10, 60)));
            LatencyOffset = Config.Bind(catAdv, "Latency Alignment Offset", 350f, "Pixel offset for the Ping column.");
            AutoHideNormal = Config.Bind(catAdv, "Auto Hide Normal UI", true, "Hide the simple UI when everyone is connected.");
            ShowPingInNormal = Config.Bind(catAdv, "Show Ping in Normal UI", true, "Show local ping at the bottom of the simple UI.");
            HideOnMenu = Config.Bind(catAdv, "Hide on Menu", true, "Hide UI when ESC menu is open.");
            EnableDebugLogs = Config.Bind(catAdv, "Enable Debug Logs", false, "Print verbose network logs to console.");

            // Test
            EnableVirtualTestPlayer = Config.Bind("Testing", "Enable Virtual Player", false, "Add a fake player to UI for testing layout.");
            TestPlayerName = Config.Bind("Testing", "Virtual Player Name", "TestBot", "Name of the fake player.");

            Harmony.CreateAndPatchAll(typeof(Patches.LoadBalancingClientPatch));
            Harmony.CreateAndPatchAll(typeof(PhotonRPCFix));

            VoiceUIManager.CreateGlobalInstance();

            logger.LogInfo($"Better Voice Fix ({MOD_VERSION}) Loaded.");
        }

        void Update()
        {
            NetworkManager.SystemUpdate();
        }
    }
}