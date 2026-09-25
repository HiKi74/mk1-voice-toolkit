# MK1 Voice Toolkit · 《真人快打1》语音导出向导

[![下载最新版](https://img.shields.io/github/v/release/HiKi74/mk1-voice-toolkit?style=for-the-badge&color=2ea44f&logo=github&label=Download)](https://github.com/HiKi74/mk1-voice-toolkit/releases/latest)
&nbsp;
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=for-the-badge)](LICENSE)

**⬇️ [点这里下载最新版](https://github.com/HiKi74/mk1-voice-toolkit/releases/latest)** · 约 3.8 MB · 解压即用（首次运行会自动下载 vgmstream）

一键导出《真人快打 1》(Mortal Kombat 1 / 内部项目名 **MK12**) 里任意角色的**全部语音**：
自动按游戏**官方台词**给文件命名，并生成**官方英文 + 官方简中**字幕、整段合集。

> ⚠️ 需要自备正版游戏。本仓库**不含任何游戏资源文件**，只在本地解析你自己安装的游戏。

---

## ✨ 特性

- **双击向导**：6 步问答走完，不用记命令行参数
- **自动找路**：自动定位 Steam 游戏目录；自动在你电脑上找 Oodle 解压库（`oo2core*.dll`）
- **按角色导出**：对战台词 / 用力声与吼叫 / 招式音效 / 其他角色对该角色的台词
- **文件名就是台词**：例如 `You're a gnat.wav`、`I. Am. The Homelander.wav`
- **官方文本**：字幕取自游戏内置本地化表（`en-US` / `zh-CN` / `zh-HK`），**非机翻、非听写**
- **一键合集**：把台词拼成整段 `wav` + 同步 `srt`/`ass`，可直接拖进剪映 / Premiere 配视频
- **多语言配音**：法语 / 德语 / 意大利语 / 葡语 / 西语（墨西哥、西班牙）等一并支持
- **可静默批处理**：同一套参数支持命令行调用，方便批量导出多个角色

## 📦 环境要求

| 依赖 | 必需性 | 说明 |
| --- | --- | --- |
| Windows 10 / 11 | 必需 | 向导为 PowerShell 脚本 |
| .NET 9 Runtime | 必需 | 运行解包工具 `mkextract`（[下载](https://dotnet.microsoft.com/download/dotnet/9.0)） |
| `oo2core*.dll` | 必需 | Oodle 专有库，**不随仓库分发**；你电脑上任意虚幻引擎游戏目录里都有（向导会自动搜索） |
| Python 3.8+ | 推荐 | 负责按台词命名、生成字幕与合集；没有则只导出原始 wav |
| [vgmstream](https://github.com/vgmstream/vgmstream) | 推荐 | 把 `.wem` 转成 `.wav`；运行 `tools\获取vgmstream.ps1` 自动下载 |

## 🚀 快速开始

1. **下载**：点顶部 **Download** 按钮（或 [Releases 页面](https://github.com/HiKi74/mk1-voice-toolkit/releases/latest)）拿发布包；
   想跟源码就用 Code → Download ZIP 或 `git clone`
2. 双击 **`启动向导.bat`**（英文入口：`start-wizard.bat`）
3. 按提示走 6 步：

```
第 0 步  环境自检        自动检查 .NET / Python / vgmstream / 解包工具
第 1 步  游戏目录        自动在 Steam 库里找，找不到会请你粘贴路径
第 2 步  AES 密钥        默认已填好 MK1 的公开密钥，回车即可
第 3 步  Oodle 解压库    自动搜你电脑上的 oo2core*.dll（多个时取版本最高的）
第 4 步  角色内部名      如 OmniMan / Homelander / Cyrax / Peacemaker
第 5 步  语言 + 输出     语言默认 English(US)，输出默认放桌面
第 6 步  导出            结束会汇总条数，并问你要不要打开输出目录
```

设置会记在 `配置.json`，第二次起基本一路回车。

### 输出结构

```
<输出目录>/
├─ 01_<角色>_对战台词/            台词（文件名 = 官方英文台词）
│                                每个 wav 旁配同名 .srt：英文 + 官方简中
├─ 02_<角色>_用力声_吼叫/          发力、吼叫、笑声
├─ 03_<角色>_招式音效/             技能与动作音效（事件名转写成可读英文）
├─ 05_<角色>_其他角色对X的台词/     其他角色对该角色说的话（附赠）
├─ <角色>_台词合集.wav/.srt/.ass   台词拼成整段，字幕同步，可直接配视频
└─ <角色>_语音对照表.csv           事件ID / 文件名 / 时长 / 官方英文 / 官方简中 / 官方繁中
```

首次运行会自动从游戏里导出三张官方文本表并缓存在 `cache\`（约 18 MB，只需一次）。

## 🖥 命令行用法

向导支持全参数静默运行：

```powershell
pwsh -File wizard.ps1 `
     -Paks "D:\Steam\steamapps\common\Mortal Kombat 1\MK12\Content\Paks" `
     -Character Homelander -Language "English(US)" -OutDir "D:\out" -Yes -Zip
```

| 参数 | 说明 |
| --- | --- |
| `-Paks` | Paks 目录（含 `.pak/.utoc/.ucas`）；省略则自动探测 |
| `-Key` | AES 密钥；省略用内置的 MK1 公开密钥 |
| `-Character` | 角色内部名（英文、无空格） |
| `-Language` | `English(US)` 等，或 `all` 导全部语言 |
| `-OutDir` | 输出目录 |
| `-OodleDll` | 手动指定 `oo2core*.dll` |
| `-NoSrt` / `-NoMerge` | 不生成逐条字幕 / 台词合集 |
| `-WithDescriptive` | 连“无障碍解说”音轨一起导（默认不导） |
| `-Zip` | 导出后自动打 zip |
| `-KeepTemp` | 保留中间文件（排查问题） |
| `-Yes` | 全部使用默认值，不提问 |

底层工具 `mkextract` 也可单独使用：

```
mkextract list    <paks> <key> <out.txt> [关键词]                     列出游戏内文件
mkextract voices  <paks> <key> <wem目录> <wav目录> <语言|all> <关键词>  按角色导出语音
mkextract locres  <paks> <key> <out.tsv> <过滤>                       导出官方文本表
mkextract extract <paks> <key> <out目录> <扩展名|all> <关键词>         按扩展名导出
```

## ❓ FAQ

**Q：提示“没有找到 XX 的音频”？**
角色名要用游戏**内部名**：英文、无空格。例如 `OmniMan`（不是 `Omni-Man`）、`Ghostface`、`T1000`、`NoobSaibot`。

**Q：提示 Oodle 相关错误？**
电脑上没找到 `oo2core*.dll`。几乎所有虚幻引擎游戏都自带，把路径填进去即可；也可从
[WorkingRobot/OodleUE](https://github.com/WorkingRobot/OodleUE/releases) 下载。

**Q：没有 Python / vgmstream 会怎样？**
没有 Python → 仍会导出 wav，但跳过“按台词命名 + 字幕 + 合集”；没有 vgmstream → 只保留 `.wem`。

**Q：中文字幕准确吗？**
来自游戏内置官方本地化表（`zh-CN` / `zh-HK`），已逐字比对，非机翻。个别条目官方译法与英文原意有出入，
属官方文本本身，工具原样保留。

**Q：CSV 在 Excel 里乱码？**
所有 CSV 均为 UTF-8 with BOM，WPS / Excel 2016+ 正常显示。

**Q：支持其他游戏吗？**
目前只针对 MK1（`GAME_MortalKombat1`）。改 `src\MKExtract\Program.cs` 里的 `EGame` 即可尝试别的虚幻引擎游戏。

## ⚖️ 免责声明

- 本项目仅供**个人学习、研究与粉丝创作**使用，**禁止商业用途**。
- 使用前请自行购买正版游戏；工具只解析你本机已安装的游戏文件。
- 本仓库**不包含**任何游戏音频、文本表、模型等版权资源，也不包含 Oodle 专有运行库。
- 导出的音频版权归 Warner Bros. Games / NetherRealm Studios 及各角色权利人所有，
  请勿二次分发原始素材。
- 因使用本工具产生的任何后果由使用者自行承担。

## 🙏 致谢

- [CUE4Parse](https://github.com/FabianFG/CUE4Parse)（Apache-2.0）—— 虚幻引擎资源解析核心
- [FModel](https://github.com/4sval/FModel) —— 同类 GUI 工具，浏览游戏文件很方便
- [vgmstream](https://github.com/vgmstream/vgmstream) —— `.wem` 解码
- [repak](https://github.com/trumank/repak) / [retoc](https://github.com/trumank/retoc) —— UE 包工具
- [WorkingRobot/OodleUE](https://github.com/WorkingRobot/OodleUE) —— Oodle 运行库分发
- MK 模组社区（各类 Wwise 解析与 mod 工具的先行者）

## 📄 License

[MIT](LICENSE) © 2026 MK1 Voice Toolkit contributors

---

## English

**MK1 Voice Toolkit** — a double-click wizard that exports every voice line of any
*Mortal Kombat 1* (internal name **MK12**) character. Filenames come from the game's own
English dialogue text, and subtitles are the game's **official English + official Simplified Chinese**.

- Requirements: Windows 10/11, .NET 9 Runtime, an `oo2core*.dll` from any UE game on your PC,
  Python 3.8+ and [vgmstream](https://github.com/vgmstream/vgmstream) (both recommended).
- Usage: run `启动向导.bat` / `start-wizard.bat`, or call `wizard.ps1` with parameters.
- You must own the game. This repository contains **no game assets** and no proprietary Oodle binary.
- For personal, non-commercial use only. Mortal Kombat 1 © Warner Bros. Games / NetherRealm Studios.
- Licensed under the MIT License.

---

## 作者与版权 / Author & Copyright

© 2026 **HiKi74** · [github.com/HiKi74](https://github.com/HiKi74)

项目仓库：[github.com/HiKi74/mk1-voice-toolkit](https://github.com/HiKi74/mk1-voice-toolkit)

本仓库代码以 [MIT](LICENSE) 协议开源，欢迎二次开发与提交 PR；
游戏音频与各角色的版权归其权利人所有（详见上方免责声明）。
