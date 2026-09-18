# BetterPeakVoiceFix

PEAK 联机语音诊断与修复模组，基于 `PEAK VOICE FIX` 继续开发。

主要用于检测和处理语音连接异常、玩家进入不同语音服务器、重连后的 Actor ID 错位，以及本机远端玩家语音状态引用失效等问题。

> 面板中显示的 IP 均为 **Photon 语音服务器地址**，不是玩家真实 IP。

---

## 主要功能

| 功能 | 说明 |
|---|---|
| 语音状态监控 | 实时显示房间内每名玩家的语音连接状态 |
| 游戏 / 语音延迟 | 分别显示游戏房间 Ping 与 Photon Voice Ping |
| 跨服检测 | 检测玩家是否进入了不同的语音服务器 |
| 语音状态恢复 | 自动恢复本机失效的远端玩家 Voice State 引用 |
| 自动 / 手动重连 | 语音连接异常时尝试重新建立连接 |
| Actor 错位识别 | 识别玩家重连后游戏与语音 Actor ID 不一致的情况 |
| 区服诊断 | 显示游戏区服、语音区服及各区域延迟 |
| Photon AppId 守卫 | 防止其他模组错误修改 PEAK 的 Photon AppId |
| 房间码修复 | 修复部分 HK 房间码解析与切区加入问题 |
| Steam 邀请修复 | 增加邀请重试，并支持 UTF-8 中文房名 |

---

## 界面预览

![BetterPeakVoiceFix UI](https://raw.githubusercontent.com/AiAe-156/BetterPeakVoiceFix/master/%E5%9B%BE%E7%89%87/icon0.png)

![BetterPeakVoiceFix UI](https://raw.githubusercontent.com/AiAe-156/BetterPeakVoiceFix/master/%E5%9B%BE%E7%89%87/icon2.png)

<details>
<summary>旧版界面演示</summary>

<br>

![旧版中文界面](https://raw.githubusercontent.com/AiAe-156/BetterPeakVoiceFix/master/%E5%9B%BE%E7%89%87/%E4%B8%AD%E6%96%87%E7%89%880.3.4%E6%BC%94%E7%A4%BA%E5%9B%BE%E7%89%87.png)

</details>

---

## 状态面板

按 `J` 循环切换：

```text
关闭 → 简易 → 详细 → 关闭
```

详细面板会显示每名玩家的状态，以及：

```text
房间 - 语音
182ms - 179ms
```

左侧为 **游戏房间延迟**，右侧为 **Photon Voice 延迟**。

| 状态 | 含义 |
|---|---|
| **[本机]** | 当前玩家自己 |
| **[同步]** | 对方安装了 BVF，并上报与本机相同的语音服务器 |
| **[已连接]** | 对方未上报 BVF 数据，但已确认存在于当前语音房 |
| **[错位]** | 玩家仍在语音房，但游戏与语音 Actor 无法正常对应，通常不影响语音 |
| **[连接中]** | 正在连接 / 验证语音服务，或仍处于进房宽限期 |
| **[跨服]** | 对方与本机不在同一个语音服务器，无法正常互通语音 |
| **[断开]** | 当前未检测到该玩家进入语音房 |

> **跨服** 比旧的 **跨区** 更准确：不同区服只是其中一种情况，同一区服内也可能因为异常分叉进入不同语音服务器。

整个房间**不需要所有人都安装 BetterPeakVoiceFix**。安装 BVF 的玩家能上报更完整的语音信息；未安装的玩家则通过本机能够观察到的 PEAK / Photon Voice 数据进行判断。

---

## 语音状态自动恢复

有时玩家已经正常进入语音房，但本机保存的远端玩家语音状态引用失效，仍可能导致听不到对方。

BetterPeakVoiceFix 会自动检测并尝试恢复这类异常。

- 面板关闭时仍然运行
- 不修改静音、屏蔽或通信权限
- 不要求房主或队友安装 BVF
- 不依赖 CrossplayStutterFix

恢复成功后，面板会短暂显示 **“语音状态已恢复”**。

---

## `Alt + K` 手动重连

`Alt + K` 用于手动重置 Photon Voice 连接。

它主要适合处理偶发网络波动、Voice Client 卡死或某一次语音错连。简单来说，就是让 Photon Voice **多一次重新连接、重新进入正确语音房的机会**。

例如：

```text
HK：400ms
EU：200ms
```

如果当前游戏房位于 HK，正确的语音连接仍然需要进入 HK。`Alt + K` 不会因为 EU 延迟更低就切到 EU，也不会把本身 400ms 的网络线路优化成 200ms。

---

## 快捷键

| 按键 | 功能 |
|---|---|
| `J` | 关闭 → 简易 → 详细 → 关闭 |
| `Alt + J` | 打开 / 关闭调试控制台 |
| `Alt + K` | 手动重置语音连接 |

`J` 可以在配置文件中修改。

---

## 调试控制台

`Alt + J` 可以查看：

- 游戏 / 语音区服
- Voice 连接与服务器信息
- Photon AppId 状态
- 各区延迟测试
- 语音房玩家列表
- 诊断日志

日志支持直接复制或导出，方便排查多人房间中的语音问题。

---

## 其他修复

| 修复 | 说明 |
|---|---|
| Photon AppId 守卫 | 其他模组改写 Photon AppId 时恢复 PEAK 原本的设置 |
| HK 房间码 | 修复港服房间码被解析到错误区服的情况 |
| 切区加入 | 等待目标 Master Server 就绪后再尝试加入房间 |
| Steam 邀请重试 | 房间信息没有返回时自动重试，避免静默卡住 |
| UTF-8 房名 | 正确接收 BetterRoomShare 等模组使用的中文 / 非 ASCII 房名 |

---

## 兼容性

| 模组 / 情况 | 说明 |
|---|---|
| 其他玩家未安装 BVF | 可以正常联机，本机仍可判断其基本语音状态，但信息较少 |
| CrossplayStutterFix | 可共存，BVF 的语音状态恢复独立运行 |
| BetterRoomShare | 兼容相关房间码与 UTF-8 邀请房名处理 |
| LocalMultiplayer | Photon AppId 守卫可避免意外改写 PEAK 的语音 AppId |

---

## 配置

```text
BepInEx/config/chuxiaaaa.Aiae.BetterPeakVoiceFix.cfg
```

大多数玩家保持默认设置即可。

版本更新记录见 [CHANGELOG.md](https://github.com/AiAe-156/BetterPeakVoiceFix/blob/master/CHANGELOG.md)。

---

## FAQ

### `[错位]` 需要重连吗？

一般不需要。它表示游戏玩家和语音 Actor 无法精确对应，并不等于语音已经断开。

### `[跨服]` 是什么意思？

说明对方和你没有进入同一个 Photon Voice Server / Voice Room。可能是连接到了不同区服，也可能是在同一区服内发生异常分叉。

### `Alt + K` 能降低延迟吗？

不能。它只是重新建立一次语音连接，给 Photon 多一次连接 / 分配机会。

---

## Credits

基于 `PEAK VOICE FIX` by **@chuxia** 继续开发。

BetterPeakVoiceFix 在此基础上增加了新的状态面板、玩家状态识别、语音状态恢复、区服诊断、Photon AppId 保护，以及房间码 / Steam 邀请相关修复。
