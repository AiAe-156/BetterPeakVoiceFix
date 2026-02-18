0.3.6
	- 修复：修复了游戏启动/重连期间语音客户端尚未加入房间时，UI 每帧抛出 NullReferenceException 的崩溃问题。
	- 修复：修复了"孤立"状态判断中 PhotonNetwork.CurrentRoom 可能为 null 导致崩溃的问题。
	- 修复：修复了 SOS 列表管理中 PhotonNetwork.CurrentRoom 可能为 null 导致崩溃的问题。
	- 新增：补全了"自动隐藏简易UI"配置项的实际功能（之前该配置已存在但未生效）。
	- 新增：joinTimes 字典现在每60秒自动清理已离开房间的玩家记录，防止长时间游戏积累无用数据。
	- 优化：格式化了部分压缩代码，提升可读性。
0.3.5

	- "显示详细IP选项"改名为"显示连接到的语音服务器IP和详细信息"
	- 优化IP显示文本，之前的"本机IP"之类的表达确实会让人误以为会暴露自己的IP.
	- 由于变动了选项名，建议旧版本玩家清除之前的配置文件(···\PEAK\BepInEx\Config\chuxiaaaa.Aiae.BetterVoiceFix.cfg)。
	- 更新了README.md描述文档。
0.3.4 发布