using System.Collections.Generic;
using System.Globalization;

namespace PeakVoiceFix
{
    public static class L
    {
        private static string _lang = "中文";
        private static Dictionary<string, string> _current;

        private static readonly Dictionary<string, string> ZH = new Dictionary<string, string>
        {
            // === UI 标签 ===
            { "ui_title", "语音详细状态" },
            { "ui_debug_title", "语音修复调试控制台 (Alt+J) | EvCode:186" },
            { "btn_copy_all", "复制全部" },
            { "btn_export", "导出文件" },
            { "btn_clear", "清空" },
            { "btn_dump", "打印语音底层名单" },
            { "filter_all", "全部" },
            { "filter_local", "本机" },
            { "client_not_connected", "客户端未连接" },
            { "voice_player_list", "语音底层名单" },
            { "ghost_tag", "[幽灵]" },
            { "exported_to", "已导出" },
            { "export_failed", "失败" },
            { "copied", "已复制" },

            // === UI 状态显示 ===
            { "voice_single_player", "本机语音: 单人游戏" },
            { "voice_local", "本机语音: " },
            { "voice_connected", "已连接" },
            { "voice_count", "语音连接人数：" },
            { "id_mismatch", "人ID错位" },
            { "local_ping", "本机延迟: " },
            { "local_server", "本机已连服务器:" },
            { "host_server", "房主已连服务器:" },
            { "label_local", "本机" },
            { "label_sync", "同步:" },
            { "label_abnormal", "异常:" },
            { "warn_majority", "⚠ [警告] 多数玩家({0}人)在另一频道!" },
            { "virtual_player", "虚拟玩家1" },
            { "sos_snapshot", "[SOS快照]" },
            { "sos_majority", "多数派" },
            { "sos_person", "人" },
            { "sos_detected", "检测到 {0} 掉线" },
            { "sos_target", "目标" },
            { "sos_last", "上次" },
            { "unknown", "未知" },
            { "cache_snapshot", "[缓存快照]" },
            { "seconds_ago", "秒前" },
            { "majority_server", "多数派服务器:" },
            { "not_connected", "未连接" },
            { "history", "[历史]" },
            { "notification_disconnected", "连接断开" },
            { "manual_operation", "[系统] 手动操作..." },

            // === 状态标签 ===
            { "state_synced", "同步" },
            { "state_isolated", "孤立" },
            { "state_connecting", "连接中" },
            { "state_disconnected", "断开" },
            { "state_connected", "已连接" },
            { "state_mismatch", "错位" },
            { "state_abnormal", "异常" },
            { "state_unknown", "未知" },
            { "state_left", "已退" },
            { "state_status", "状态" },

            // === ClientState 本地化 ===
            { "cs_initializing", "初始化中..." },
            { "cs_authenticating", "验证中..." },
            { "cs_authenticated", "已验证" },
            { "cs_joining", "加入中..." },
            { "cs_joined", "已连接" },
            { "cs_disconnecting", "断开中..." },
            { "cs_disconnected", "断开" },
            { "cs_connecting_game", "连接到游戏服务器中..." },
            { "cs_connecting_master", "连接到主服务器中..." },
            { "cs_connecting_name", "连接到名称服务器中..." },

            // === 详细面板 ===
            { "detail_waiting_data", "(等待数据...)" },
            { "detail_fetching", "获取中..." },
            { "detail_connecting_to", "正在连接" },
            { "detail_joined_voice", "已连入语音服" },
            { "detail_connecting_local", "连接中..." },
            { "detail_latency", "延迟" },

            // === GetLocalizedState (简短) ===
            { "ls_joined", "已连接" },
            { "ls_disconnected", "已断开" },

            // === NetworkManager 日志 ===
            { "log_state_change", "状态变更" },
            { "log_host_ip_change", "房主连接服务器变动" },
            { "log_host_disconnected", "[Host] 房主意外断开，正在自动恢复..." },
            { "log_loop_blind", "[循环] 已失败{0}次，暂时切换为盲连..." },
            { "log_wrong_freq", "[异频] 当前:{0} 目标:{1} | 纠正({2}/2)" },
            { "log_compromise", "[妥协] 纠正失败，驻留当前IP: {0}" },
            { "log_reflect_fail", "无法反射设置服务器 IP" },
            { "log_reflect_error", "反射失败" },
            { "log_decision", "[决策] 目标变更" },
            { "log_majority", "多数派({0}人)" },
            { "log_host", "房主" },
            { "log_auto_blind", "自动(盲连)" },
            { "log_sos_send", "[SOS] 发送求救" },
            { "log_sos_target", "目标" },
            { "log_sos_local", "本机" },
            { "log_sos_manual", "手动断开 (Manual)" },
            { "log_sos_received", "收到 {0} SOS (目标:{1})" },
            { "log_alt_k_disconnect", "[系统] Alt+K 手动断开" },
            { "log_alt_k_reconnect", "[系统] Alt+K 强制重连" },
            { "log_cache_snapshot", "[缓存快照] 记录数" },

            // === BepInEx 配置 ===
            { "cfg_cat_ui", "UI设置" },
            { "cfg_cat_net", "网络设置" },
            { "cfg_cat_adv", "高级与调试" },
            { "cfg_ui_position", "选择UI面板显示在屏幕的哪一侧。" },
            { "cfg_toggle_key", "切换语音面板显示的按键。" },
            { "cfg_show_pro", "是否在面板中显示具体的已连接语音服务器IP地址和调试信息。" },
            { "cfg_offset_x_r", "距离屏幕右边缘的水平距离。" },
            { "cfg_offset_y_r", "距离屏幕上边缘的垂直距离。" },
            { "cfg_offset_x_l", "距离屏幕左边缘的水平距离。" },
            { "cfg_offset_y_l", "距离屏幕上边缘的垂直距离。" },
            { "cfg_font_size", "面板文字的基础大小。" },
            { "cfg_host_symbol", "显示在房主名字前的特殊符号。" },
            { "cfg_timeout", "如果连接卡住，超过多少秒判定为断开。" },
            { "cfg_retry_interval", "每次自动重连之间的冷却时间。" },
            { "cfg_manual_reconnect", "允许按 Alt+K 强制断开或重连语音。" },
            { "cfg_ghost_fix", "尝试从场景中搜寻名字以修复 Unknown 问题。" },
            { "cfg_max_name_len", "显示名字的最大字符数。" },
            { "cfg_latency_offset", "Ping值显示的水平像素偏移。" },
            { "cfg_auto_hide", "当所有人连接正常时，自动隐藏简易模式的UI。" },
            { "cfg_show_ping", "在简易模式下方显示本机延迟。" },
            { "cfg_hide_menu", "打开ESC菜单时隐藏UI。" },
            { "cfg_debug_log", "在控制台输出详细的网络日志。" },
            { "cfg_virtual_player", "添加一个假玩家用于测试UI布局。" },
            { "cfg_virtual_name", "假玩家的名字。" },
            { "cfg_language", "语言(重启游戏生效) | Language(Need restart)" },

            // === 配置选项名 ===
            { "cfgn_ui_position", "UI位置" },
            { "cfgn_toggle_key", "开关快捷键" },
            { "cfgn_show_pro", "显示连接到的语音服务器IP和详细信息" },
            { "cfgn_offset_x_r", "右侧边距" },
            { "cfgn_offset_y_r", "顶部边距(右)" },
            { "cfgn_offset_x_l", "左侧边距" },
            { "cfgn_offset_y_l", "顶部边距(左)" },
            { "cfgn_font_size", "字体大小" },
            { "cfgn_host_symbol", "房主标记符号" },
            { "cfgn_timeout", "重连超时时间 (s)" },
            { "cfgn_retry_interval", "重试间隔 (s)" },
            { "cfgn_manual_reconnect", "启用手动重置 (Alt+K)" },
            { "cfgn_ghost_fix", "启用ID漂移修复" },
            { "cfgn_max_name_len", "最大名字长度" },
            { "cfgn_latency_offset", "延迟对齐偏移量" },
            { "cfgn_auto_hide", "自动隐藏简易UI" },
            { "cfgn_show_ping", "简易模式显示Ping" },
            { "cfgn_hide_menu", "ESC菜单界面时隐藏" },
            { "cfgn_debug_log", "启用调试日志" },
            { "cfgn_virtual_player", "启用虚拟玩家" },
            { "cfgn_virtual_name", "虚拟玩家名字" },
            { "cfgn_language", "语言 | Language" },
        };

        private static readonly Dictionary<string, string> EN = new Dictionary<string, string>
        {
            // === UI Labels ===
            { "ui_title", "Voice Detail Status" },
            { "ui_debug_title", "Voice Fix Debug Console (Alt+J) | EvCode:186" },
            { "btn_copy_all", "Copy All" },
            { "btn_export", "Export" },
            { "btn_clear", "Clear" },
            { "btn_dump", "Dump Photon Player List" },
            { "filter_all", "All" },
            { "filter_local", "Local" },
            { "client_not_connected", "Client not connected" },
            { "voice_player_list", "Photon Player List" },
            { "ghost_tag", "[Ghost]" },
            { "exported_to", "Exported to" },
            { "export_failed", "Failed" },
            { "copied", "Copied" },

            // === UI Status ===
            { "voice_single_player", "Local Voice: Single Player" },
            { "voice_local", "Local Voice: " },
            { "voice_connected", "Connected" },
            { "voice_count", "Voice Connected: " },
            { "id_mismatch", "ID Mismatched" },
            { "local_ping", "Local Ping: " },
            { "local_server", "Local Server:" },
            { "host_server", "Host Server:" },
            { "label_local", "Local" },
            { "label_sync", "Sync:" },
            { "label_abnormal", "Abnormal:" },
            { "warn_majority", "⚠ [WARN] Majority ({0}) on another channel!" },
            { "virtual_player", "Virtual Player 1" },
            { "sos_snapshot", "[SOS Snapshot]" },
            { "sos_majority", "Majority" },
            { "sos_person", "" },
            { "sos_detected", "Detected {0} disconnected" },
            { "sos_target", "Target" },
            { "sos_last", "Last" },
            { "unknown", "Unknown" },
            { "cache_snapshot", "[Cache Snapshot]" },
            { "seconds_ago", "sec ago" },
            { "majority_server", "Majority Server:" },
            { "not_connected", "Not Connected" },
            { "history", "[History]" },
            { "notification_disconnected", "Disconnected" },
            { "manual_operation", "[System] Manual operation..." },

            // === Status Tags ===
            { "state_synced", "Synced" },
            { "state_isolated", "Isolated" },
            { "state_connecting", "Connecting" },
            { "state_disconnected", "Disconnected" },
            { "state_connected", "Connected" },
            { "state_mismatch", "Mismatched" },
            { "state_abnormal", "Abnormal" },
            { "state_unknown", "Unknown" },
            { "state_left", "Left" },
            { "state_status", "Status" },

            // === ClientState Localized ===
            { "cs_initializing", "Initializing..." },
            { "cs_authenticating", "Authenticating..." },
            { "cs_authenticated", "Authenticated" },
            { "cs_joining", "Joining..." },
            { "cs_joined", "Joined" },
            { "cs_disconnecting", "Disconnecting..." },
            { "cs_disconnected", "Disconnected" },
            { "cs_connecting_game", "Connecting to game server..." },
            { "cs_connecting_master", "Connecting to master server..." },
            { "cs_connecting_name", "Connecting to name server..." },

            // === Detail Panel ===
            { "detail_waiting_data", "(Waiting for data...)" },
            { "detail_fetching", "Fetching..." },
            { "detail_connecting_to", "Connecting to" },
            { "detail_joined_voice", "Joined voice server" },
            { "detail_connecting_local", "Connecting..." },
            { "detail_latency", "Latency" },

            // === GetLocalizedState (short) ===
            { "ls_joined", "Connected" },
            { "ls_disconnected", "Disconnected" },

            // === NetworkManager Logs ===
            { "log_state_change", "State change" },
            { "log_host_ip_change", "Host server connection change" },
            { "log_host_disconnected", "[Host] Unexpected disconnect, recovering..." },
            { "log_loop_blind", "[Loop] Failed {0} times, switching to blind..." },
            { "log_wrong_freq", "[WrongIP] Current:{0} Target:{1} | Fix({2}/2)" },
            { "log_compromise", "[Compromise] Fix failed, staying on: {0}" },
            { "log_reflect_fail", "Cannot set Server IP via reflection" },
            { "log_reflect_error", "Reflection failed" },
            { "log_decision", "[Decision] Target changed" },
            { "log_majority", "Majority ({0})" },
            { "log_host", "Host" },
            { "log_auto_blind", "Auto (Blind)" },
            { "log_sos_send", "[SOS] Sending SOS" },
            { "log_sos_target", "Target" },
            { "log_sos_local", "Local" },
            { "log_sos_manual", "Manual Disconnect" },
            { "log_sos_received", "Received {0} SOS (Target:{1})" },
            { "log_alt_k_disconnect", "[System] Alt+K Manual disconnect" },
            { "log_alt_k_reconnect", "[System] Alt+K Force reconnect" },
            { "log_cache_snapshot", "[Cache Snapshot] Count" },

            // === BepInEx Config Descriptions ===
            { "cfg_cat_ui", "UI Settings" },
            { "cfg_cat_net", "Network Settings" },
            { "cfg_cat_adv", "Advanced & Debug" },
            { "cfg_ui_position", "Which side of the screen to display the UI panel." },
            { "cfg_toggle_key", "Key to toggle voice panel display." },
            { "cfg_show_pro", "Show connected voice server IP and debug info in the panel." },
            { "cfg_offset_x_r", "Horizontal offset from right edge." },
            { "cfg_offset_y_r", "Vertical offset from top edge." },
            { "cfg_offset_x_l", "Horizontal offset from left edge." },
            { "cfg_offset_y_l", "Vertical offset from top edge." },
            { "cfg_font_size", "Base font size of the panel." },
            { "cfg_host_symbol", "Symbol displayed before host's name." },
            { "cfg_timeout", "Seconds before connection is deemed disconnected." },
            { "cfg_retry_interval", "Cooldown between auto-reconnect attempts." },
            { "cfg_manual_reconnect", "Allow Alt+K to force disconnect/reconnect voice." },
            { "cfg_ghost_fix", "Try to find names from scene to fix Unknown issue." },
            { "cfg_max_name_len", "Max display characters for names." },
            { "cfg_latency_offset", "Horizontal pixel offset for ping display." },
            { "cfg_auto_hide", "Auto-hide simple UI when all connections are normal." },
            { "cfg_show_ping", "Show local ping below simple mode UI." },
            { "cfg_hide_menu", "Hide UI when ESC menu is open." },
            { "cfg_debug_log", "Output detailed network logs to console." },
            { "cfg_virtual_player", "Add a fake player for UI layout testing." },
            { "cfg_virtual_name", "Name of the fake player." },
            { "cfg_language", "语言(重启游戏生效) | Language(Need restart)" },

            // === Config Option Names ===
            { "cfgn_ui_position", "UI Position" },
            { "cfgn_toggle_key", "Toggle Key" },
            { "cfgn_show_pro", "Show Server IP & Details" },
            { "cfgn_offset_x_r", "Right Margin" },
            { "cfgn_offset_y_r", "Top Margin (Right)" },
            { "cfgn_offset_x_l", "Left Margin" },
            { "cfgn_offset_y_l", "Top Margin (Left)" },
            { "cfgn_font_size", "Font Size" },
            { "cfgn_host_symbol", "Host Symbol" },
            { "cfgn_timeout", "Reconnect Timeout (s)" },
            { "cfgn_retry_interval", "Retry Interval (s)" },
            { "cfgn_manual_reconnect", "Enable Manual Reset (Alt+K)" },
            { "cfgn_ghost_fix", "Enable ID Drift Fix" },
            { "cfgn_max_name_len", "Max Name Length" },
            { "cfgn_latency_offset", "Latency Alignment Offset" },
            { "cfgn_auto_hide", "Auto-Hide Simple UI" },
            { "cfgn_show_ping", "Show Ping in Simple Mode" },
            { "cfgn_hide_menu", "Hide on ESC Menu" },
            { "cfgn_debug_log", "Enable Debug Logs" },
            { "cfgn_virtual_player", "Enable Virtual Player" },
            { "cfgn_virtual_name", "Virtual Player Name" },
            { "cfgn_language", "语言 | Language" },
        };

        public static string DetectDefault()
        {
            var culture = CultureInfo.CurrentUICulture;
            return culture.Name.StartsWith("zh") ? "中文" : "English";
        }

        public static void Init(string lang)
        {
            _lang = lang;
            _current = (_lang == "中文") ? ZH : EN;
        }

        public static string Get(string key)
        {
            if (_current != null && _current.TryGetValue(key, out var v)) return v;
            if (ZH.TryGetValue(key, out var fallback)) return fallback;
            return key;
        }

        public static string Get(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }
    }
}
