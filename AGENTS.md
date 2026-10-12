# KiraDoki（基于 IsleBar）— 给 AI 助手的项目介绍

> 任何 AI 接手这个项目，**先读本文件，再读 `TODO-v0.1.1.md`（现在唯一的待办）**。本文件只写长期有效的内容（项目是什么、怎么协作、怎么构建、代码在哪、规矩和踩过的坑）；**最新进度和"下一步做什么"在 `CLAUDE.md` 开头的交接段**。
> 不用读：`docs/`（原作者的多语言网页和视频）、媒体文件、旧待办 `TODO.md` 和 `TODO-history.md`（10-12 起不再参考、不再更新；代码注释里的 "TODO 7.9""TODO 4.5" 指 history 的章节号，真要查来历才翻）。`README.md` 是原作者写的，已经过时（里面的文件搜索、在岛上问 AI 都删了），别按它理解项目。

## 一、项目是什么

- **KiraDoki**：Windows 11 任务栏上的"灵动岛"。在任务栏里放一个状态胶囊，显示后台在发生什么：Claude / ChatGPT 应用的状态（工作中 / 等你 / 完成 / 出错）、音乐、下载、文件复制、计时器 / 番茄钟、日历、系统事件（电量、蓝牙、网络、通知、复制提示……）。点胶囊会"掀起"一张详情卡片。
- 基于 ds3owl 的 **IsleBar 0.1.0**（MIT，github.com/ds3owl/islebar）改的，**自己 fork 维护，不给原作者提 PR**。原作者的功能用户不需要就直接删。`LICENSE` 里原作者的版权行和 `THIRD-PARTY-NOTICES.md` 必须保留。
- 改名只改用户看得见的（安装包、图标、界面里的名字）；**代码、命名空间、数据文件夹 `%LOCALAPPDATA%\IsleBar`、`islebar` 命令都还叫 IsleBar**，不要去改。
- 0.1.0 已打包发给朋友试用，暂时不公开发 GitHub。看板娘头像 `Assets/mascot.png` 是网上找的图，公开发布前要换。
- **现在的方向（10-12 定）：精简、性能、稳定，不再加新功能。**

## 二、用户和协作方式

- 用户是 vibe coding 新手，只会一点 Python，不会 C#。用户描述想要的效果、看运行结果，代码由 AI 写。**一律用中文**，讲解要通俗，必要时用 Python 打比方。
- 额度有限：不要全仓库乱读；构建 / 测试的输出只看报错部分；不要开子代理做大范围探索。
- **用户说"先讨论 / 先别改代码"时，只分析、给方案和一个推荐，等用户拍板。** 用户被多个方案绕晕时，自己理清楚给**一个**方案。
- 用户学设计，对排版很敏感：间距按像素量，不凭感觉。用户屏幕 3072×1920、200% 缩放，截图上的 px ÷ 2 = dip。画方案图按 **1 倍**画（CSS px = dip），浏览器会自己放大。画方案只能用卡片现有的部件和代码里真拿得到的数据。
- **对接系统行为（窗口、焦点、别的软件的界面、文件时间等），先加日志验证假设再改**。凭推测改错过很多次。性能问题也一样：先测量再改。
- 一个任务的流程：改 → 构建 → 跑测试 → 用户真机看效果 → 提交（一个任务一个提交）。用户拍板的决定当场写进 `TODO-v0.1.1.md`（用户说"加入 TODO"就是指它）；做完更新本文件的地图和 `CLAUDE.md` 的交接段。
- 用户说"一起做"就连着做完，不用每步都问。会在用户屏幕上弹窗口、往回收站放东西、模拟点击 / 拖动的测试，先讲清楚再跑；用户正在用电脑时别模拟点击。
- **产品原则**（提新卡片、新按钮、新功能前先对照）：
  - 胶囊是往外送信息的：不切窗口、瞄一眼就知道后台在发生什么；核心 = AI 状态。不做"往里收"的东西（文件搜索、在岛上问 AI 都砍了）；厂商或 Windows 自己会做的不做。
  - 评估新功能问四个问题：用得多不多？别人能不能替代？做和维护要花多少？贴不贴核心？新功能先试用一周确认有用，再抠像素。
  - 电脑要确定、不能是黑箱：同一状态同一动作永远同一结果。
  - "猜下一步"按钮四条都满足才加：①下一步很可能就是它；②用户现在不在那里；③能稳定做到；④这一步不用先看详情。每张卡片最多一个。

## 三、环境和常用命令

- Windows 11 x64，Windows PowerShell 5.1（没有 `&&`；路径里有空格、括号、中文，一定加引号）。.NET SDK 8、git、Windows SDK 10.0.26100；没装 Visual Studio（项目只有 C#）。
- 项目根目录：`E:\02-生活爱好\04-Vibcoding\KiraDoki`（10-12 从桌面搬来）。
- 找不到 dotnet / git 时刷新 PATH：`$env:Path=[Environment]::GetEnvironmentVariable("Path","Machine")+";"+[Environment]::GetEnvironmentVariable("Path","User")`

```
# 构建前先关掉正在运行的开发版（只关本项目路径下的，别动用户装的正式版）
Get-Process IsleBar.App -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*04-Vibcoding\KiraDoki*' } | Stop-Process -Force
dotnet build src/IsleBar.App -v q -nologo
dotnet test tests/IsleBar.Core.Tests -v q -nologo
Start-Process "src\IsleBar.App\bin\Debug\net8.0-windows10.0.26100.0\IsleBar.App.exe"
```

- **构建失败就别启动程序**（会启动旧版，还会开出两个实例）。`run_dev.ps1` 在这台电脑上用不了。
- 日志：`%LOCALAPPDATA%\IsleBar\logs\islebar.log`（和 `.log.1`）。排查问题先看这里，每个功能有自己的日志前缀（地图表格里写了）。
- 打安装包：`scripts/build-installer.ps1 -Version x.y.z`（Inno Setup，`installer/IsleBar.iss`）；使用介绍页 `guide/`（改 `template.html` 后跑 `python guide/build.py`）。
- git：remote `origin` = github.com/samkru-nite/KiraDoki（GitHub 上是 ds3owl/islebar 的 Fork；历史开头接着原作者的 `IsleBar 0.1.0` 提交）。仓库里已设好提交用户名和 GitHub 隐私邮箱（`git config user.name / user.email`），**别用用户的 Gmail 提交**。提交说明写进文件再 `git commit -F 说明文件`（PowerShell 5.1 会把带双引号的参数拆坏）。推送 `git push`（只推 main）。提交前先 `git status`，**可能有别的会话在改同一个工作区，只提交自己改的**。
- 改文件：中文 + 反斜杠的长脚本别写在 bash heredoc 或 `python -c` 里（会乱码、转义出错），写成文件再跑，或者直接用编辑工具。PowerShell 的 `Get-Content | Set-Content` 会弄乱中文，别用。写 `.ps1` 时里面别放中文（PowerShell 5.1 按 ANSI 读没有 BOM 的文件）。

## 四、代码结构

- `src/IsleBar.Core`：和界面无关的逻辑（规则、解析、排版计算），**有单元测试**，新规则尽量写在这里。
- `src/IsleBar.App`：WinUI 3 界面和所有对接 Windows 的代码。
- `src/IsleBar.Cli`：`islebar push / timer / hook` 命令（Claude Code 钩子会调它）。
- `tests/IsleBar.Core.Tests`；`installer/`（安装包、`hooks.ps1` 装 Claude 钩子）；`scripts/`（打包、生成图标字体）；`msix/`、`docs/` 是原作者的。
- `.claude/tools/` 里是本机的观察 / 截图 / 模拟点击脚本（git 不管）。

### 核心数据流：一切都是"`ActivityState` 列表 → 选一张显示"
1. **来源**都产出 `ActivityState`（`Core/Island/ActivityState.cs`）：
   - 状态文件 `%LOCALAPPDATA%\IsleBar\state\*.json`，由 `Core/Island/ActivityStore.cs` 读写。写入方：`islebar` 命令、Claude Code 钩子（`Core/Island/AgentHook.cs`）、App 自己。
   - 内存里的来源：音乐 `Media/MediaWatcher.cs`、下载 `Media/DownloadWatcher.cs`、系统事件 `SystemWatch/SystemWatchers.cs` 等。监视器大多跑在后台线程。
2. **汇总**：`Engine/ActivityFeed.cs` 的 `Collect()` 合成列表（顺带推进番茄钟、提示音）；`MainWindow.StartIslandPolling()` 每 250 ms 调一次（在界面线程上）。
3. **挑选**：`Core/Island/CardStack.cs` 决定大胶囊显示哪张（亮相、排队、置顶、收起）；小胶囊的格子 `Core/Island/TaskSlots.cs`。每种任务开头 / 结尾怎么处理看 `Core/Island/TaskRules.cs`。**一件事从开始到结束只用一张卡片（同一个键）**。
4. **绘制**：大胶囊 = `IslandUi/PillView.cs`；换卡动画 `MainWindow.Swap.cs` 的 `Transition()`；掀起卡片内容 `Core/Ui/CardSheet.cs`（放什么）→ `IslandUi/SheetView.cs`（怎么画）；卡片列表 `MainWindow.List.cs` + `IslandUi/ListWindow.cs`。

### 界面
- 一个窗口 = 小胶囊（圆球 │ 任务图标格子）+ 大胶囊，嵌在任务栏里（`Interop/TaskbarHost.cs`，位置 `Tracking/TaskbarTracker.cs` + `Core/Ui/BarSlot.cs`，裁剪 `Interop/PillRegion.cs`，尺寸 `Core/Ui/PillGeometry.cs`）。
- `MainWindow.xaml.cs`（约 2500 行，状态字段很多）+ `MainWindow.Swap / Lift / List / Transit / Pill.cs`。圆球左键 = 卡片列表，右键 = 工具菜单。
- 掀起卡片窗口 `IslandUi/LiftWindow.cs`（逐像素透明 + 系统亚克力玻璃 `Interop/TransparentBackdrop.cs`，玻璃开关看 `IslandUi/Frost.cs`）；动画数字 `Core/Ui/LiftMotion.cs`、`SwapMotion.cs`、`ListMotion.cs`；回弹跟随 Windows"动画效果"（`IslandUi/Animations.cs`）。
- 设置窗口 `Options/SettingsWindow.cs`（Windows 11 风格，左栏 6 页）。
- 图标：自带字体 `Assets/Fonts/IsleIcons.ttf`（微软 Fluent UI System Icons），常量 `Core/Ui/Icons.cs`（脚本生成，别手改），加图标见 `scripts/make-icons.py`。
- 颜色 `Interop/ThemeColors.cs`、`Core/Ui/ThemePalette.cs`；文字分段 `Core/Ui/TextGap.cs` + `IslandUi/GapRuns.cs`；多语言 `Core/Localization/LanguageCatalog.cs`。
- 帧率日志 `Interop/FrameMeter.cs`（"fps swap / lift …"）。

### 各功能对应文件（日志前缀写在括号里）
| 功能 | 文件 |
|---|---|
| Claude 应用 / Claude Code | 钩子 → `Core/Island/AgentHook.cs`；无钩子时读会话记录 `SystemWatch/AgentRecordWatcher.cs`；回答内容 `Core/Island/AgentTranscript.cs` + `Engine/AgentTails.cs`；跳到对话 `Engine/ChatOpener.cs`；logo `Interop/ClaudeGlyph.cs`（"agent:"、"open chat:"） |
| ChatGPT 应用（原 Codex） | 读会话记录 `Core/Island/CodexRollout.cs` → `SystemWatch/CodexWatcher.cs`（"codex:"） |
| 合盖不睡眠 | `Core/SystemWatch/LidRules.cs`、`Interop/PowerSettings.cs`、`SystemWatch/LidGuard.cs`（"lid:"） |
| 音乐 | `Media/MediaWatcher.cs`、规则 `Core/Island/MediaCards.cs` |
| 下载（浏览器、迅雷、夸克、百度网盘） | `Media/DownloadWatcher.cs` → `Core/Island/DownloadTracker.cs`、`Core/Island/Downloaders.cs`（"download:"） |
| Steam | `Core/SystemWatch/Steam.cs` → `Media/SteamWatcher.cs`（"steam:"） |
| 资源管理器复制 / 移动 / 解压 | `Media/FileOperationWatcher.cs` → `Core/SystemWatch/FileOperations.cs`（"fileop:"） |
| 压缩软件（WinRAR / 7-Zip / Bandizip） | `Core/SystemWatch/Archivers.cs` → `Media/ArchiverWatcher.cs`（"archiver:"） |
| 剪映导出 | `Media/VideoExportWatcher.cs`（"export:"） |
| 打印机 | `Core/SystemWatch/Printing.cs` → `Media/PrintWatcher.cs`（"print:"） |
| U 盘 | `Interop/RemovableDrives.cs`、`Core/SystemWatch/DriveRules.cs`、`SystemWatch/DriveWatcher.cs`（"drive:"） |
| 中转站（拖文件进胶囊暂存） | `Core/SystemWatch/TransitShelf.cs` → `SystemWatch/TransitWatcher.cs`；拖放 `IslandUi/DropCatcher.cs`、`Core/Ui/DropZone.cs`；动画 `MainWindow.Transit.cs`（"transit:"） |
| 系统通知 / QQ | `SystemWatch/ToastWatcher.cs`，横幅开关 `SystemWatch/ToastBanners.cs`；打开 QQ / 微信 `Interop/ChatWindows.cs` |
| 微信（只闪任务栏） | `SystemWatch/ChatFlashWatcher.cs`（"wechat:"） |
| 复制提示 | `SystemWatch/ClipboardWatcher.cs` + `Core/SystemWatch/ClipboardRules.cs` |
| 日历（ICS 地址） | `Core/SystemWatch/CalendarLogic.cs`、`IcsCalendar.cs` → `SystemWatch/CalendarWatcher.cs`（"calendar:"） |
| 计时器 / 番茄钟 | `Core/Island/TimerParser.cs`、`Engine/ActivityFeed.cs`、表盘 `Core/Ui/TimerDial.cs` |
| "猜下一步"按钮（胶囊上的 ↗） | MW `StepFor / ButtonFor / TakeStep`，规则见第二节"产品原则" |

## 五、规范

- **排版**：字号只有 12 / 14 / 40；正文不加粗，每个区最多一处中粗；小节名 12 号灰色；颜色只有主色 / 灰 / 状态色（橙 = 等你 / 提醒，绿 = 完成，红 = 出错）。卡片文字不用「·」：前一段主色、后一段灰色，两段之间固定 4 dip（`TextGap`）；汉字和数字之间不加空格（`1个文件`、`10分钟后`）。掀起卡片只有四个区：顶栏 / 中间 / 进度区 / 按钮行。
- **动效**：小东西可以回弹，大面积阅读面（掀起卡片）不弹；回弹要整体缩放。
- **按钮常驻可见**，不做鼠标悬停才出现。每张卡片最多一个"猜下一步"按钮。
- **读别人软件界面的适配一定加保底**：认不出界面时退回用稳定信号（进程读写量等）显示一张简单卡片，并在日志里记下认不出的原文。
- **加界面文案**：`LanguageStrings.cs` 加属性 + `LanguageCatalog.cs` 10 种语言都加（文件里的顺序：en ko ja zh zht fr de it pt es）+ `tests/.../LocalizationTests.cs` 的文案总数 +N。
- **加设置项**：`IsleBarSettings`（属性 + `Clone()`）+ `SettingsCodec`（key、读、写）+ `Options/SettingsWindow.cs`。删设置项就三处一起删，并把旧 key 加进 `SettingsCodec.RemovedKeys`。
- 新界面不做"新旧切换开关"，旧代码不需要就直接删。没让改的尺寸不要顺手改。修完一个 bug，主动排查全项目有没有同类问题。

## 六、脆弱点和踩过的坑

- `MainWindow.xaml.cs` 又大状态又多，改一处容易影响别处。**每 250 ms 的 tick 里的异常会被吞掉**（效果不出现，也不报错）。事件只能在 `WireEvents()` 一处绑定。
- 单位：WinUI 用 dip，窗口 / 区域 / 鼠标坐标用物理像素（`DpiSetup.Px`），混用就出 bug。不确定时先打日志看实际数字。
- 弹右键菜单时窗口会暂时脱离任务栏再回去（`TaskbarHost.Detach / Attach`）。`FollowTaskbar` 里 `ShowWindow` 和 `PlaceAt` 之间别加耗时的操作。
- 计时器（`DispatcherQueue.CreateTimer()`）一定要存进字段，只放在局部变量里会被回收。
- 异步加载图片会先画一帧替身，导致闪烁 → 应用图标一律 `AppIcon.Now` 当场画。
- Claude 桌面应用是 MSIX 安装的：它的 `%APPDATA%` 被重定向到 `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming`。**从 Claude 里启动的程序和用户自己双击打开的程序看到的文件不一样**，要验证这类问题，必须让用户自己双击启动。
- WinUI 的文件接口会拒绝 / 藏起 `.lnk` 快捷方式；拖动快捷方式要交 Shell IDList（`Interop/DragFiles.cs`）。
- 截图和模拟点击的脚本要先 `SetProcessDPIAware`，不然坐标会错位。模拟拖动前先确认目标窗口在最前面。
- 推测"系统没明说的结果"（跳过了没有、文件是复制的还是新建的）经常出错：只显示能确定的，拿不准就不显示。
