# 第三方组件说明 / Third-Party Notices

本仓库分发的文件分三类：**自有代码**、**第三方开源组件的构建产物**、**不分发的专有组件**。

## 自有代码（MIT）

- `wizard.ps1`、`启动向导.bat`、`start-wizard.bat`、`tools/*.py`、`tools/*.ps1`
- `src/MKExtract/*`（`mkextract` 的源码）

## 构建产物：`bin/mkextract/`

`bin/mkextract/` 由 `src/MKExtract` 通过 `dotnet publish` 生成，除自有代码外还包含
CUE4Parse（NuGet）及其依赖的 DLL，主要包括：

| 组件 | 许可 |
| --- | --- |
| [CUE4Parse](https://github.com/FabianFG/CUE4Parse) | Apache-2.0 |
| [Oodle.NET](https://github.com/NotOfficer/Oodle.NET)（托管包装层，**不含** Oodle 本体） | MIT |
| [BouncyCastle.Cryptography](https://www.bouncycastle.org/csharp/) | MIT |
| [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | MIT |
| [Serilog](https://github.com/serilog/serilog) | Apache-2.0 |
| [ZstdSharp.Port](https://github.com/oleg-st/ZstdSharp) | MIT |
| [K4os.Compression.LZ4](https://github.com/MiloszKrajewski/K4os.Compression.LZ4) | MIT |
| Zlib-ng.NET | zlib-ng 许可 |

完整依赖清单见 `src/MKExtract/mkextract.csproj` 与各 NuGet 包内的 `LICENSE`。
再分发时请保留各自的版权与许可声明。

## 不随仓库分发

| 组件 | 原因 |
| --- | --- |
| `oo2core*.dll` / `oodle-data-shared.dll` | Oodle 为 RAD Game Tools / Epic Games **专有库**，不可再分发；由使用者从自己电脑上的游戏目录获取 |
| [vgmstream](https://github.com/vgmstream/vgmstream) 二进制 | 第三方项目，其 Windows 构建含 FFmpeg 组件（LGPL/GPL）；请用 `tools/获取vgmstream.ps1` 获取官方发布版并遵循其许可 |

> 说明：`bin/mkextract/Oodle.NET.dll` 只是调用 Oodle 的**托管接口层**（MIT，随 NuGet 分发）；
> 真正的 Oodle 解密库 `oo2core*.dll` 由使用者在运行时提供，本仓库不包含。

## 游戏资源

本仓库**不包含**任何《真人快打 1》的游戏资源（音频、文本表、模型、贴图等）。
所有导出结果均由使用者在本地对自己合法拥有的游戏副本解析得到，
版权归 Warner Bros. Games / NetherRealm Studios 及各角色权利人所有。
