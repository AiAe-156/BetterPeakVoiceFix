using BepInEx;
using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using PeakVoiceFix.Patches;

namespace PeakVoiceFix
{
    public enum UIPositionEnum
    {
        Left,
        Right
    }

    [BepInPlugin("chuxiaaaa.Aiae.BetterPeakVoiceFix", "BetterVoiceFixCN", PLUGIN_VERSION)]
    public class VoiceFix : BaseUnityPlugin
    {
        public static KeyCode GetToggleKey()
        {
            if (Enum.TryParse<KeyCode>(ToggleUIKey.Value, true, out var key)) return key;
            return KeyCode.J;
        }

        public static VoiceFix Instance;
        public static ManualLogSource logger;
        public static ManualLogSource debugLogger;

        public static ConfigEntry<string> Language;
        public static ConfigEntry<UIPositionEnum> UIPositionSide;
        public static ConfigEntry<string> ToggleUIKey;
        public static ConfigEntry<bool> ShowProfessionalInfo;
        public static ConfigEntry<float> OffsetX_Right, OffsetY_Right, OffsetX_Left, OffsetY_Left;
        public static ConfigEntry<float> FontSize;
        public static ConfigEntry<string> HostSymbol;

        public static ConfigEntry<float> ConnectTimeout;
        public static ConfigEntry<float> RetryInterval;
        public static ConfigEntry<bool> EnableManualReconnect;

        public static ConfigEntry<int> MaxTotalLength;
        public static ConfigEntry<float> LatencyOffset;
        public static ConfigEntry<bool> AutoHideNormal;
        public static ConfigEntry<bool> ShowPingInNormal;
        public static ConfigEntry<bool> HideOnMenu;
        public static ConfigEntry<bool> EnableDebugLogs;
        public static ConfigEntry<bool> EnableVirtualTestPlayer;
        public static ConfigEntry<string> TestPlayerName;

        public const string PLUGIN_VERSION = "1.0.0";
        public const string MOD_VERSION = "v" + PLUGIN_VERSION;

        void Awake()
        {
            Instance = this;
            logger = Logger;
            debugLogger = new ManualLogSource("VoiceFixDebug");
            BepInEx.Logging.Logger.Sources.Add(debugLogger);

            // 先使用系统语言初始化一次，确保语言配置描述文字与系统环境一致。
            string detectedLanguage = L.DetectDefault();
            L.Init(detectedLanguage);

            // 语言配置使用固定英文 Section，避免后续切换语言后出现 Section 混杂。
            Language = Config.Bind("Language", "语言-重启生效 | Language - need restart", detectedLanguage,
                new ConfigDescription(L.Get("cfg_language"), new AcceptableValueList<string>("中文", "English")));
            L.Init(Language.Value);

            // --- UI Settings (Section: UI) ---
            string catUI = L.Get("cfg_cat_ui");

            ToggleUIKey = Config.Bind(catUI, L.Get("cfgn_toggle_key"), KeyCode.J.ToString(), L.Get("cfg_toggle_key"));

            // 使用英文 Key，中文/英文 Description
            UIPositionSide = Config.Bind(catUI, L.Get("cfgn_ui_position"), UIPositionEnum.Right,
                new ConfigDescription(L.Get("cfg_ui_position")));

            ShowProfessionalInfo = Config.Bind(catUI, L.Get("cfgn_show_pro"), false, L.Get("cfg_show_pro"));

            OffsetX_Right = Config.Bind(catUI, L.Get("cfgn_offset_x_r"), 20f, L.Get("cfg_offset_x_r"));
            OffsetY_Right = Config.Bind(catUI, L.Get("cfgn_offset_y_r"), 20f, L.Get("cfg_offset_y_r"));
            OffsetX_Left = Config.Bind(catUI, L.Get("cfgn_offset_x_l"), 20f, L.Get("cfg_offset_x_l"));
            OffsetY_Left = Config.Bind(catUI, L.Get("cfgn_offset_y_l"), 20f, L.Get("cfg_offset_y_l"));

            FontSize = Config.Bind(catUI, L.Get("cfgn_font_size"), 21f, L.Get("cfg_font_size"));
            HostSymbol = Config.Bind(catUI, L.Get("cfgn_host_symbol"), "★", L.Get("cfg_host_symbol"));

            // --- Network Settings (Section: Network) ---
            string catNet = L.Get("cfg_cat_net");
            ConnectTimeout = Config.Bind(catNet, L.Get("cfgn_timeout"), 25f, L.Get("cfg_timeout"));
            RetryInterval = Config.Bind(catNet, L.Get("cfgn_retry_interval"), 8f, L.Get("cfg_retry_interval"));
            EnableManualReconnect = Config.Bind(catNet, L.Get("cfgn_manual_reconnect"), true, L.Get("cfg_manual_reconnect"));

            // --- Advanced Settings (Section: Advanced) ---
            string catAdv = L.Get("cfg_cat_adv");
            MaxTotalLength = Config.Bind(catAdv, L.Get("cfgn_max_name_len"), 26, new ConfigDescription(L.Get("cfg_max_name_len"), new AcceptableValueRange<int>(10, 60)));
            LatencyOffset = Config.Bind(catAdv, L.Get("cfgn_latency_offset"), 350f, L.Get("cfg_latency_offset"));
            AutoHideNormal = Config.Bind(catAdv, L.Get("cfgn_auto_hide"), true, L.Get("cfg_auto_hide"));
            ShowPingInNormal = Config.Bind(catAdv, L.Get("cfgn_show_ping"), true, L.Get("cfg_show_ping"));
            HideOnMenu = Config.Bind(catAdv, L.Get("cfgn_hide_menu"), true, L.Get("cfg_hide_menu"));
            EnableDebugLogs = Config.Bind(catAdv, L.Get("cfgn_debug_log"), false, L.Get("cfg_debug_log"));

            EnableVirtualTestPlayer = Config.Bind(catAdv, L.Get("cfgn_virtual_player"), false, L.Get("cfg_virtual_player"));
            TestPlayerName = Config.Bind(catAdv, L.Get("cfgn_virtual_name"), "1234567891012141618202224262830323436", L.Get("cfg_virtual_name"));

            Harmony.CreateAndPatchAll(typeof(Patches.LoadBalancingClientPatch));
            Harmony.CreateAndPatchAll(typeof(PhotonRPCFix));

            VoiceUIManager.CreateGlobalInstance();

            logger.LogInfo($"Better Voice Fix ({MOD_VERSION}) 已加载。");
        }

        void Update()
        {
            NetworkManager.SystemUpdate();
        }
    }
}
