using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using PeakVoiceFix.Patches;

namespace PeakVoiceFix
{
    // [版本] 
    [BepInPlugin("chuxiaaaa.Aiae.BetterVoiceFix", "BetterVoiceFix", "0.3.0")]
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

        // [变更] EventCode 已移除，强制硬编码为 186
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

        private void Awake()
        {
            Instance = this;
            logger = Logger;
            debugLogger = BepInEx.Logging.Logger.CreateLogSource("BetterVoiceFixDebug");

            try
            {
                InitConfig();

                Harmony.CreateAndPatchAll(typeof(LoadBalancingClientPatch));
                Harmony.CreateAndPatchAll(typeof(PhotonRPCFix));

                logger.LogInfo($"模组加载成功: Peak Voice Fix v0.3.0");
            }
            catch (System.Exception ex)
            {
                logger.LogError($"初始化失败: {ex}");
            }
        }

        private void InitConfig()
        {
            var fontDesc = new ConfigDescription("调整文本的字号大小。", new AcceptableValueRange<float>(10f, 60f));
            FontSize = Config.Bind("界面布局", "字体大小", 22f, fontDesc);

            UIPositionSide = Config.Bind("界面布局", "UI显示在屏幕哪侧", true, "true = 右侧(默认), false = 左侧。");
            ToggleUIKey = Config.Bind("界面布局", "切换显示语音详细状态UI按键", KeyCode.J, "切换显示语音面板。");
            ShowProfessionalInfo = Config.Bind("界面布局", "显示专业信息", false, "开启后显示IP、Ping、缓存历史及详细调试信息。");
            HostSymbol = Config.Bind("界面布局", "房主标识符号", "★", "显示在房主名字前的符号。");
            OffsetX_Right = Config.Bind("界面布局", "右-水平边距 (X)", 10f, "距离右边缘的距离。");
            OffsetY_Right = Config.Bind("界面布局", "右-垂直边距 (Y)", 40f, "距离顶部的距离。");
            OffsetX_Left = Config.Bind("界面布局", "左-水平边距 (X)", 10f, "距离左边缘的距离。");
            OffsetY_Left = Config.Bind("界面布局", "左-垂直边距 (Y)", 40f, "距离顶部的距离 (Top-Left)。");

            // EventCode 移除

            ConnectTimeout = Config.Bind("网络设置", "重连超时时间(秒)", 8f, "连接卡住时的强制重试阈值。");
            RetryInterval = Config.Bind("网络设置", "重连间隔时间(秒)", 8f, "两次重连尝试之间的冷却时间。");
            EnableManualReconnect = Config.Bind("网络设置", "启用手动重连 (Alt+K)", true, "允许使用 Alt+K 组合键强制重连/断开。");
            EnableGhostFix = Config.Bind("网络设置", "启用ID漂移修复", true, "拦截 RPCA_PlayRemove 的缓冲发送，防止 Unknown Name 问题。");

            string cat = "进阶控制与调试";
            MaxTotalLength = Config.Bind(cat, "“状态+玩家名” 最大字符长度", 26, new ConfigDescription("最大长度", new AcceptableValueRange<int>(10, 60)));
            LatencyOffset = Config.Bind(cat, "延迟对齐偏移量", 350f, "延迟数值距离行首的像素距离。");
            AutoHideNormal = Config.Bind(cat, "正常状态自动隐藏", true, "全员连接正常时自动隐藏面板。");
            ShowPingInNormal = Config.Bind(cat, "常态界面显示延迟", true, "在常态UI底部显示本机延迟。");
            HideOnMenu = Config.Bind(cat, "ESC界面自动隐藏", true, "当呼出ESC菜单(或鼠标光标可见)时，自动隐藏UI。");
            EnableDebugLogs = Config.Bind(cat, "启用调试日志", false, "输出详细日志到控制台。");
            EnableVirtualTestPlayer = Config.Bind(cat, "启用虚拟测试玩家", false, "生成虚拟玩家数据用于测试UI布局。");
            TestPlayerName = Config.Bind(cat, "测试玩家名字", "测试玩家91012141618202224262830", "虚拟测试模式下显示的名字。");
        }

        private void Update()
        {
            try
            {
                if (VoiceUIManager.Instance == null) VoiceUIManager.CreateGlobalInstance();
                NetworkManager.SystemUpdate();
            }
            catch (System.Exception) { }
        }
    }
}