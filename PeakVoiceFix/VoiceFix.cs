using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using PeakVoiceFix.Patches;

namespace PeakVoiceFix
{
    // [版本] v0.3.6 - Config UI Update
    [BepInPlugin("chuxiaaaa.Aiae.BetterVoiceFix", "BetterVoiceFixCN", "0.3.6")]
    public class VoiceFix : BaseUnityPlugin
    {
        public static VoiceFix Instance;
        public static ManualLogSource logger;
        public static ManualLogSource debugLogger;

        // [修改] 改为 string 类型
        public static ConfigEntry<string> UIPositionSide;
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

        public const string MOD_VERSION = "v0.3.6";

        void Awake()
        {
            Instance = this;
            logger = Logger;
            debugLogger = new ManualLogSource("VoiceFixDebug");
            BepInEx.Logging.Logger.Sources.Add(debugLogger);

            // --- UI 设置 ---
            string catUI = "UI设置";

            // [修改] 下拉菜单选择：左侧 / 右侧
            UIPositionSide = Config.Bind(catUI, "UI位置", "右侧",
                new ConfigDescription("选择UI面板显示在屏幕的哪一侧。", new AcceptableValueList<string>("左侧", "右侧")));

            ToggleUIKey = Config.Bind(catUI, "开关快捷键", KeyCode.J, "切换语音面板显示的按键。");
            ShowProfessionalInfo = Config.Bind(catUI, "显示连接到的语音服务器IP和详细信息", false, "是否在面板中显示具体的已连接语音服务器IP地址和调试信息。");

            OffsetX_Right = Config.Bind(catUI, "右侧边距", 20f, "距离屏幕右边缘的水平距离。");
            OffsetY_Right = Config.Bind(catUI, "顶部边距(右)", 20f, "距离屏幕上边缘的垂直距离。");
            OffsetX_Left = Config.Bind(catUI, "左侧边距", 20f, "距离屏幕左边缘的水平距离。");
            OffsetY_Left = Config.Bind(catUI, "顶部边距(左)", 20f, "距离屏幕上边缘的垂直距离。");

            FontSize = Config.Bind(catUI, "字体大小", 21f, "面板文字的基础大小。");
            HostSymbol = Config.Bind(catUI, "房主标记符号", "★", "显示在房主名字前的特殊符号。");

            // --- 网络设置 ---
            string catNet = "网络设置";
            ConnectTimeout = Config.Bind(catNet, "重连超时时间 (s)", 25f, "如果连接卡住，超过多少秒判定为断开。");
            RetryInterval = Config.Bind(catNet, "重试间隔 (s)", 8f, "每次自动重连之间的冷却时间。");
            EnableManualReconnect = Config.Bind(catNet, "启用手动重置 (Alt+K)", true, "允许按 Alt+K 强制断开或重连语音。");
            EnableGhostFix = Config.Bind(catNet, "启用ID漂移修复", true, "尝试从场景中搜寻名字以修复 Unknown 问题。");

            // --- 高级设置 ---
            string catAdv = "高级与调试";
            MaxTotalLength = Config.Bind(catAdv, "最大名字长度", 26, new ConfigDescription("显示名字的最大字符数。", new AcceptableValueRange<int>(10, 60)));
            LatencyOffset = Config.Bind(catAdv, "延迟对齐偏移量", 350f, "Ping值显示的水平像素偏移。");
            AutoHideNormal = Config.Bind(catAdv, "自动隐藏简易UI", true, "当所有人连接正常时，自动隐藏简易模式的UI。");
            ShowPingInNormal = Config.Bind(catAdv, "简易模式显示Ping", true, "在简易模式下方显示本机延迟。");
            HideOnMenu = Config.Bind(catAdv, "ESC菜单界面时隐藏", true, "打开ESC菜单时隐藏UI。");
            EnableDebugLogs = Config.Bind(catAdv, "启用调试日志", false, "在控制台输出详细的网络日志。");

            EnableVirtualTestPlayer = Config.Bind(catAdv, "启用虚拟玩家", false, "添加一个假玩家用于测试UI布局。");
            TestPlayerName = Config.Bind(catAdv, "虚拟玩家名字", "1234567891012141618202224262830323436", "假玩家的名字。");

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