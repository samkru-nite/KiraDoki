# IsleBar 二次开发 — 项目上下文（交接文件）

> 新会话先读本文件和 TODO.md。**源码尚未通读**：第一个任务是读 `src/` 后补全下方「项目地图」。

## 用户与协作方式
- 用户是 vibe coding 新手，只会一点 Python，不会 C#/C++。用户只描述想要的效果和看运行结果，代码由 Claude 写。请用中文、通俗讲解，必要时用 Python 类比。
- 用户想控制节奏：**没让读/做的不要先做**；一次只做一小块，改完先构建、让用户看效果再继续。
- 额度有限（Pro）：不要全仓库乱读（跳过 `docs/`、媒体文件）；构建/测试输出只看报错部分；一个任务一个会话；不要开子代理做大范围探索。
- 模型策略：默认 Sonnet 5.5；同一问题 Sonnet 两次修不好，或做大架构改动（如 TODO 5.1）、对接系统接口时，建议用户切 Opus 5.5（用户在应用界面切换）。

## 项目概况
- IsleBar 0.1.0（MIT，原作者 ds3owl，github.com/ds3owl/islebar）：Windows 11 任务栏小工具，把搜索框换成"状态胶囊"，显示 Claude Code/Codex 状态、音乐、下载、计时、系统通知，Tab 切换 AI/文件搜索(Everything)/网页搜索。
- 目录：`src/IsleBar.Core`（平台无关逻辑，有测试）、`src/IsleBar.Cli`（push/timer/hook）、`src/IsleBar.App`（WinUI 3，C# 界面）、`native/IsleBarTap`（可选 C++，注入 explorer.exe 把系统搜索框变透明，默认关闭）、`tests/IsleBar.Core.Tests`、`installer/`、`msix/`、`docs/`（多语言网页和视频，勿读）。
- 原作者的构建检查清单：`docs/PHASE0_CHECKLIST.md`。
- **几乎所有需求只改 C#，不碰 C++**；`dist/IsleBarTap.dll` 是预编译的，不要改它。

## 环境状态（2026-10-04 已验证）
- Windows 11 x64，Windows PowerShell 5.1（无 `&&`；写文件显式 UTF-8；路径含空格/括号/中文，务必加引号）。
- 已装：.NET SDK 8.0.425、git 2.55、Windows SDK 10.0.26100。**未装 Visual Studio**（仅改 C++ 时才需要，目前不需要）。
- 新终端若找不到 dotnet/git，先刷新 PATH：`$env:Path=[Environment]::GetEnvironmentVariable("Path","Machine")+";"+[Environment]::GetEnvironmentVariable("Path","User")`
- Smart App Control 已关闭（注册表 VerifiedAndReputablePolicyState=0）。若要启动开发版，作者用 `run_dev.ps1`（经签名的 dotnet.exe 启动）。
- 项目根目录：`C:\Users\Kasumi\Desktop\islebar-0.1.0 (2)\islebar-0.1.0`（比压缩包外层多一层）。
- 用户需求原文档：`C:\Users\Kasumi\Desktop\新建 Microsoft Word 文档.docx`（内容已整理进 TODO.md，另有 2 张截图未看，需要时从 `word/media/` 提取）。

## 基线（未改任何代码时）
- `dotnet build src/IsleBar.App` 成功，0 警告 0 错误。
- `dotnet test tests/IsleBar.Core.Tests` 783 个全过。
- git 已初始化，首个提交 `07041cf` = 原始源码；之后每块修改单独提交，方便回退。

## 常用命令（在项目根目录）
```
dotnet build src/IsleBar.App -v q -nologo
dotnet test tests/IsleBar.Core.Tests -v q -nologo
```

## 项目地图（待补全）
读完源码后在此补：模块职责、状态流转（hook → CLI → state 文件 → UI）、胶囊各"状态/槽位"的实现位置、下载/媒体/通知/搜索各自的文件、脆弱点、TODO 每条对应的文件和函数。

## 开发约定
- 读改文件用专用工具，PowerShell 只跑 git/dotnet/winget。
- 每个任务：改 → 构建 → 跑测试 → 让用户运行看效果 → 提交（一个任务一个提交）。
- 做完更新本文件的「项目地图」和 TODO.md 进度，便于下个会话接续。
