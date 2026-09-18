using BepInEx;
using System;
using System.Collections.Generic;
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

    [BepInPlugin("chuxiaaaa.Aiae.BetterPeakVoiceFix", "BetterPeakVoiceFix", PLUGIN_VERSION)]
    public class VoiceFix : BaseUnityPlugin
    {
        private static KeyCode _cachedToggleKey = KeyCode.J;
        public static KeyCode GetToggleKey() => _cachedToggleKey;

        // 把字符串->KeyCode 的解析从每帧调用移到配置加载/变更时一次，Update 里直接读缓存。
        private static void RefreshToggleKey()
        {
            if (ToggleUIKey != null && Enum.TryParse<KeyCode>(ToggleUIKey.Value, true, out var key))
                _cachedToggleKey = key;
            else
                _cachedToggleKey = KeyCode.J;
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
        public static ConfigEntry<bool> EnableVirtualTestPlayer;
        public static ConfigEntry<string> TestPlayerName;

        public static ConfigEntry<string> ForcedRegion;
        public static ConfigEntry<string> UnknownRegionAs;
        public static ConfigEntry<bool> WaitMasterBeforeJoin;
        public static ConfigEntry<bool> EnableInviteRetry;

        public const string PLUGIN_VERSION = "1.1.1";
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

            ToggleUIKey = Config.Bind(catUI, L.Get("cfgn_toggle_key"), KeyCode.J.ToString(), L.Get("cfg_toggle_key_v2"));
            RefreshToggleKey();
            ToggleUIKey.SettingChanged += (s, e) => RefreshToggleKey();

            // 使用英文 Key，中文/英文 Description
            UIPositionSide = Config.Bind(catUI, L.Get("cfgn_ui_position"), UIPositionEnum.Right,
                new ConfigDescription(L.Get("cfg_ui_position")));

            ShowProfessionalInfo = Config.Bind(catUI, L.Get("cfgn_show_pro"), false, L.Get("cfg_show_pro"));

            // 边距预设：左右 30、顶部 80（实机调出来的落点，顶部要躲开本体那排 HUD）。
            // 改默认值不会动已有的 .cfg——BepInEx 只在键不存在时才写默认值，
            // 老用户要用新预设得自己改那四行或把它们删掉。
            OffsetX_Right = Config.Bind(catUI, L.Get("cfgn_offset_x_r"), 30f, L.Get("cfg_offset_x_r"));
            OffsetY_Right = Config.Bind(catUI, L.Get("cfgn_offset_y_r"), 80f, L.Get("cfg_offset_y_r"));
            OffsetX_Left = Config.Bind(catUI, L.Get("cfgn_offset_x_l"), 30f, L.Get("cfg_offset_x_l"));
            OffsetY_Left = Config.Bind(catUI, L.Get("cfgn_offset_y_l"), 80f, L.Get("cfg_offset_y_l"));

            FontSize = Config.Bind(catUI, L.Get("cfgn_font_size"), 21f, L.Get("cfg_font_size"));
            HostSymbol = Config.Bind(catUI, L.Get("cfgn_host_symbol"), "★", L.Get("cfg_host_symbol"));

            // 这五项都是纯显示相关，原来放在"高级与调试"里不合理，1.4.0 起归到 UI 设置。
            // 换 Section 会让旧配置值读不到（BepInEx 按 Section+Key 定位），所以它们会回到默认值。
            MaxTotalLength = Config.Bind(catUI, L.Get("cfgn_max_name_len"), 26, new ConfigDescription(L.Get("cfg_max_name_len"), new AcceptableValueRange<int>(10, 60)));
            LatencyOffset = Config.Bind(catUI, L.Get("cfgn_latency_offset"), 270f, L.Get("cfg_latency_offset"));
            AutoHideNormal = Config.Bind(catUI, L.Get("cfgn_auto_hide"), true, L.Get("cfg_auto_hide"));
            AutoHideNormal.SettingChanged += (s, e) =>
                NetworkManager.DiagLog(L.Get("diag_cfg_change", L.Get("cfgn_auto_hide"), AutoHideNormal.Value));
            ShowPingInNormal = Config.Bind(catUI, L.Get("cfgn_show_ping"), true, L.Get("cfg_show_ping"));
            HideOnMenu = Config.Bind(catUI, L.Get("cfgn_hide_menu"), true, L.Get("cfg_hide_menu"));

            // --- 高级与调试：原"网络设置"和"区服"两个分区合并进来 ---
            string catAdv = L.Get("cfg_cat_adv");
            ConnectTimeout = Config.Bind(catAdv, L.Get("cfgn_timeout"), 25f, L.Get("cfg_timeout"));
            RetryInterval = Config.Bind(catAdv, L.Get("cfgn_retry_interval"), 8f, L.Get("cfg_retry_interval"));
            EnableManualReconnect = Config.Bind(catAdv, L.Get("cfgn_manual_reconnect"), true, L.Get("cfg_manual_reconnect"));

            // 键名不能含 = \n \t \ " ' [ ]，否则 BepInEx 会把之后所有 Bind 一起带走。
            var regionValues = new List<string> { RegionControl.AUTO };
            regionValues.AddRange(RegionControl.KNOWN_REGIONS);
            ForcedRegion = Config.Bind(catAdv, L.Get("cfgn_forced_region"), RegionControl.AUTO,
                new ConfigDescription(L.Get("cfg_forced_region"), new AcceptableValueList<string>(regionValues.ToArray())));
            // 配置变更留痕：排查时经常需要知道"这局到底是什么设置"
            ForcedRegion.SettingChanged += (s, e) =>
                NetworkManager.DiagLog(L.Get("diag_cfg_change", L.Get("cfgn_forced_region"), ForcedRegion.Value));

            EnableVirtualTestPlayer = Config.Bind(catAdv, L.Get("cfgn_virtual_player"), false, L.Get("cfg_virtual_player"));
            TestPlayerName = Config.Bind(catAdv, L.Get("cfgn_virtual_name"), "1234567891012141618202224262830323436", L.Get("cfg_virtual_name"));

            // --- 房间码兜底（本体 bug 的补丁，可单独关掉以防与其他 mod 冲突）---
            var unknownValues = new System.Collections.Generic.List<string> { "" };
            unknownValues.AddRange(RegionControl.KNOWN_REGIONS);
            UnknownRegionAs = Config.Bind(catAdv, L.Get("cfgn_unknown_region"), "hk",
                new ConfigDescription(L.Get("cfg_unknown_region"), new AcceptableValueList<string>(unknownValues.ToArray())));
            WaitMasterBeforeJoin = Config.Bind(catAdv, L.Get("cfgn_wait_master"), true, L.Get("cfg_wait_master"));
            EnableInviteRetry = Config.Bind(catAdv, L.Get("cfgn_invite_retry"), true, L.Get("cfg_invite_retry"));

            Harmony.CreateAndPatchAll(typeof(Patches.LoadBalancingClientPatch));
            Harmony.CreateAndPatchAll(typeof(PhotonRPCFix));
            Harmony.CreateAndPatchAll(typeof(ForceRegionPatch));
            // 房间码兜底：本体那张区服表漏了 hk，解码时会被 Clamp 成 us。补丁做成幂等，与 BetterRoomShare 共存。
            TryPatch(typeof(CodeToRegionFallbackPatch), "CodeToRegion");
            TryPatch(typeof(WaitMasterBeforeJoinPatch), "JoinRoomAndWaitForSpawn");
            TryPatch(typeof(RequestRoomIDPatch), "RequestPhotonRoomID");
            RoomCodePatches.Announce();
            if (!InviteHandshake.Available)
                logger.LogWarning("[邀请] 反射不到握手字段，邀请重试功能停用（本体可能改了字段名）");

            VoiceUIManager.CreateGlobalInstance();

            logger.LogInfo($"Better Voice Fix ({MOD_VERSION}) 已加载。");
        }

        void Update()
        {
            NetworkManager.SystemUpdate();
            // 区服控制不能只在房间内跑：强制区服连不上时人还卡在主菜单，测速也常在主菜单点。
            RegionControl.Update();
            // 邀请握手同理：整个卡死过程都发生在进房之前。
            InviteHandshake.Update();
        }

        /// <summary>
        /// 打本体的补丁一律 fail-open：目标类被改名/搬家（2.4.b 的 SteamManager 就搬过）时
        /// 只记一条日志然后放过，绝不让整个 mod 因为一个附加功能挂掉。
        /// </summary>
        private static void TryPatch(Type patchType, string what)
        {
            try
            {
                Harmony.CreateAndPatchAll(patchType);
            }
            catch (Exception ex)
            {
                if (logger != null) logger.LogWarning($"[补丁] {what} 挂载失败，该功能停用: {ex.Message}");
            }
        }
    }
}
