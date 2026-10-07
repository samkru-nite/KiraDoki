# IsleBar 二次开发 — 项目上下文（交接文件）

> 新会话先读本文件和 TODO.md（进度看 TODO 每条的 [x]/[~] 和注记）。源码已通读，地图见下方。
> **下一步（10-05）**：新交互设计已定稿，见 TODO.md 第 7 节（小球 + 胶囊、卡片类型、操作、输入框、AI 在胶囊里交互）。定位/风险/代码策略见 7.8，按 **7.9 顺序**做，第 1 步滚动诊断已完成（滚动能收到，结果见 7.9 第 1 条；统计日志用户决定不做），**10-05 路线 A–L 已定稿（见 7.9），A–D、E1、E2 已完成**（D = 预览 https://claude.ai/artifact/PGb2ZCPwCXFwAJnmCDPkDW 第 14 版）。**E1–E5（+E3½）10-07 全部完成**（每步一个提交，注记见 TODO 7.9 E 条"E3–E5"）。**下一步：用户一起看 E3–E5 效果**（互换动画做的时候屏幕锁着，没在屏幕上看过，最可能要调；图标是 Fluent 字形占位，用户看完再挑），按反馈修完再进 **F**（掀起卡片 + "⋯"列表）。尺寸以 **D 条开头的「最终尺寸表」**为准（冲突时最终尺寸表 > D 条后面的轮次 > 前面的轮次）。不做新旧切换开关，旧代码不需要就直接删。预览清单在 7.9 末尾。

## 用户与协作方式
- 用户是 vibe coding 新手，只会一点 Python，不会 C#/C++。用户只描述想要的效果和看运行结果，代码由 Claude 写。请用中文、通俗讲解，必要时用 Python 类比。
- 额度有限（Pro）：不要全仓库乱读（跳过 `docs/`、媒体文件）；构建/测试输出只看报错部分；上下文还小时一个会话可以连做几步（10-05 用户），大了再开新会话；不要开子代理做大范围探索。
- **用户常说"先讨论，先别改代码"**：这时只分析、给方案和推荐、等用户拍板。用户学设计，对排版很敏感：间距要按像素量（用户截图是 200% 缩放，px÷2=dip），不要凭感觉。
- **对接系统行为（窗口、焦点、开始菜单等）先加诊断日志验证假设，再改**——10-04 开始菜单凭推测改了 5 轮才成，教训见 `Interop/StartMenu.cs` 注释。
- **10-05 起不再给原作者提 PR，改为自己 fork 维护**（改动太大）。所以：原作者的功能用户不需要就可以直接删（如 ⌄ 面板的新对话/模型/远程控制）；默认值直接按用户习惯定，不必"通用默认 + 个人偏好放设置"；`.claude/pr-notes.md` 停止维护（留作历史）。MIT 要求：`LICENSE` 里原作者版权行和 `THIRD-PARTY-NOTICES.md` 必须保留（可在下面加用户自己的一行）。以后公开发布时建议改名并在 README 注明"基于 ds3owl 的 IsleBar"。CHANGELOG.md 的 "Unreleased" 仍可顺手记。

## 项目概况
- IsleBar 0.1.0（MIT，原作者 ds3owl，github.com/ds3owl/islebar）：Windows 11 任务栏小工具，把搜索框换成"状态胶囊"，显示 Claude Code/Codex 状态、音乐、下载、计时、系统通知，Tab 切换 AI/文件搜索(Everything)/网页搜索。
- 目录：`src/IsleBar.Core`（平台无关逻辑，有测试）、`src/IsleBar.Cli`（push/timer/hook）、`src/IsleBar.App`（WinUI 3，C# 界面）、`native/IsleBarTap`（可选 C++，注入 explorer.exe 把系统搜索框变透明，默认关闭）、`tests/IsleBar.Core.Tests`、`installer/`、`msix/`、`docs/`（多语言网页和视频，勿读）。
- 原仓库没有 CLAUDE.md/AGENTS.md（`.gitignore` 排除了 `.claude/`，作者可能本地有但未公开）；csproj 注释提到的 `docs/PHASE0_CHECKLIST.md` 也不在仓库里。可参考 `CHANGELOG.md` 了解作者踩过的坑。
- **只有 C#**：原来的 C++ 模块 `native/IsleBarTap`（注入 explorer 把系统搜索框变透明）10-05 随路线 C 删除了。

## 环境状态（2026-10-04 已验证）
- Windows 11 x64，Windows PowerShell 5.1（无 `&&`；写文件显式 UTF-8；路径含空格/括号/中文，务必加引号）。
- 已装：.NET SDK 8.0.425、git 2.55、Windows SDK 10.0.26100。**未装 Visual Studio**（仅改 C++ 时才需要，目前不需要）。
- 新终端若找不到 dotnet/git，先刷新 PATH：`$env:Path=[Environment]::GetEnvironmentVariable("Path","Machine")+";"+[Environment]::GetEnvironmentVariable("Path","User")`
- Smart App Control 已关闭。**`run_dev.ps1` 在本机不可用**（要求 `%USERPROFILE%\.dotnet\dotnet.exe`，不存在）；直接运行构建出的 exe 即可（见下方命令）。
- 构建前必须先关掉正在运行的开发版（它锁住 dll，否则 MSB3027）；只关本项目路径下的进程，不动用户装的正式版。
- git 没有配置用户名：提交时用 `git -c user.name="Kasumi" -c user.email="188979868+samkru-nite@users.noreply.github.com" commit ...`。没有 remote（源码是 zip 下载的，不是 clone）。**提交说明里有双引号时 PowerShell 5.1 会把参数拆坏**：把说明写进 scratchpad 的文本文件，用 `commit -F 文件`。
- 日志：`%LOCALAPPDATA%\IsleBar\logs\islebar.log`（文件搜索 >30ms 会记 "file search: ..."）。
- 用 Python 脚本批量改文件时注意：中文路径/反斜杠在 Python 字符串里会出转义错误，失败时改用 Edit 工具；C# 普通字符串里的一个反斜杠要写成两个（`"C:\\Recovery"`），或用 `@"..."`。
- 项目根目录：`C:\Users\Kasumi\Desktop\islebar-0.1.0 (2)\islebar-0.1.0`（比压缩包外层多一层）。
- 用户需求原文档：`C:\Users\Kasumi\Desktop\新建 Microsoft Word 文档.docx`（内容已整理进 TODO.md，另有 2 张截图未看，需要时从 `word/media/` 提取）。

## 基线
- 原始源码：构建 0 警告 0 错误，测试 783 个。**10-07 E5 后：0 警告 0 错误，测试 864 个；界面文案 140。****截至 10-04（下载 1.1/1.3 完成后）：构建 0 警告 0 错误，测试 855 个全过；界面文案 116 条。**
- git：`07041cf` = 原始源码；`10d87c9`/`86c50fc`/`985850d` 只改了 CLAUDE.md/TODO.md；之后每块功能一个提交。
- 已完成（10-04）：4.6 消息卡片默认关闭+开关；4.5/3.4 文件搜索整套重做（见 TODO 4.5 注记）；开始菜单遮挡列表；2.2 启动失败报错复制；4.4 浏览器媒体开关 + 媒体选择规则重写（`9d736c1`）。2.1 不是 bug（用户没装命令行版）。

## 常用命令（在项目根目录）
```
Get-Process IsleBar.App -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*islebar-0.1.0 (2)*' } | Stop-Process -Force
dotnet build src/IsleBar.App -v q -nologo
dotnet test tests/IsleBar.Core.Tests -v q -nologo
Start-Process "src\IsleBar.App\bin\Debug\net8.0-windows10.0.26100.0\IsleBar.App.exe"
```
- 加界面文案：`LanguageStrings.cs` 加属性 + `LanguageCatalog.cs` 10 种语言都要加（文件里顺序：en ko ja zh zht fr de it pt es）+ `tests/.../LocalizationTests.cs` 里的文案总数 +N（10-07 当前 140）。
- 加设置项：`IsleBarSettings`（属性 + `Clone()`）+ `SettingsCodec`（key、读、写）+ `Options/SettingsWindow.cs`。

## 项目地图（2026-10-04 读源码后补；行号是近似值，以 grep 为准）

### 核心数据流：一切都是「ActivityState 列表 → 选一个最急的显示」
1. **来源**（每个都产出 `ActivityState` 对象，`Core/Island/ActivityState.cs`）：
   - 状态 JSON 文件：`%LOCALAPPDATA%\IsleBar\state\*.json` 和 `%TEMP%\tgprog\tgprog_*.json`，由 `Core/Island/ActivityStore.cs` 读写（含各类过期时长常量）。写入方：`islebar push/timer/hook`（`Cli/CommandRunner.cs`）、App 自己（如启动失败通知 `ShowLaunchFailure`）。
   - Claude/Codex 钩子：Claude Code hook / Codex notify → `islebar hook` → `Core/Island/AgentHook.cs` 翻译成状态文件。钩子安装脚本 `setup/hooks.ps1`，连接逻辑 `App/Interop/HookConnector.cs`。`App/SystemWatch/AgentRecordWatcher.cs` 读会话记录补抓无钩子的情况。
   - 内存来源（不落盘）：音乐 `App/Media/MediaWatcher.cs`、下载 `App/Media/DownloadWatcher.cs`、发手机 `App/IslandUi/PhoneDrop.cs`、系统短通知 `App/SystemWatch/SystemWatchers.cs`（电源/蓝牙/网络/专注/剪贴板/负载/日历/麦克风摄像头/系统通知 ToastWatcher/更新）。
2. **汇总**：`App/Engine/ActivityFeed.cs` 的 `Collect()` 把所有来源合成列表（同时推进番茄钟、响提示音、清除关掉/看过的 AI 状态）；`MainWindow.xaml.cs` 的 `StartIslandPolling()` 每 250ms 调一次。（10-05 路线 A：非界面逻辑都搬到 `App/Engine/`：`ActivityFeed`、`AgentLauncher`、`Chime`、`FileActions`、`FileSearchRequests`。）
3. **挑选**（10-07 E1 起）：`Core/Island/CardStack.cs`（卡片类型、置顶顺序、鼠标在胶囊上不换卡、`Choose` 换卡）决定大胶囊显示哪张；MainWindow 把结果包成 `IslandSnapshot`。小胶囊的格子 `Core/Island/TaskSlots.cs`（顺序、互换、放不下进"⋯"），小图标总表 `Core/Ui/TaskIcons.cs`。媒体卡片规则 `Core/Island/MediaCards.cs`。
4. **绘制**：`RenderIsland()` → `RenderIslandState()` → 文字 `ShowIslandText()` / 计时 `ShowTimerText()`+`ShowClockDigits()` / 进度条 `UpdateProgressLine()` / 边框 `UpdateGlow()` / 前缀文案 `PrefixFor()` / 左图标 `GlyphFor()`。动画在 `App/IslandUi/IslandAnimator.cs`（进出场、呼吸、高亮边框 `UpdateGlow`）。

### 界面结构
- `App/MainWindow.xaml`（10-07 E1）：一个窗口 = `PillRow`（大胶囊 `PillBody` 220×32 | 间距 `GapColumn` 8 | 小胶囊 `SmallPill`，左右由设置 `BallSide` 决定），上下各留 7 dip（E4 弧线）。窗口按两个胶囊的**直角矩形**裁剪（`Interop/PillRegion.cs`，布局算在 `Core/Ui/PillGeometry.cs`），圆角由 `PillSurface`/`SmallSurface` 自己画在任务栏色底（`Root.Background`）上——按圆角裁剪会有锯齿。`MainWindow.LayoutPills()` 管显示/隐藏大胶囊、排列、裁剪、定位（`TaskbarTracker.PlaceNow(宽度)`）。大胶囊内部（E2）：`CardRow` 四列 = 关闭区 `CloseZone`（告知卡片）| 图标 `LeadSlot` 16 | Entry 或 IslandContent（文字，竖直按墨迹居中 `PlaceText`/`MeasureInkAsync`，水平按设置 `CardTextAlign`）| 右侧（均衡器、Tab 提示、隐私点）；底部进度轨道 `ProgressTrackBar` + `ProgressLine`；边框 `GlowRing` 2 宽按卡片类型（`UpdateGlow`）。小胶囊（E3）：`TaskCells`（代码生成的格子，Tag = 卡片键，"⋯"的 Tag 是 `DotsTag`）│ `SmallRule` │ `BallBox`/`BallCell` 圆圈（点 = 打字开关）；放左边时列对调。卡片/图标的一切变化（含点任务图标互换）都走 `MainWindow.Swap.cs` 的 `Transition()`（动画数字在 `Core/Ui/SwapMotion.cs`，飞行层 `FlyLayer`，设置"互换动画""回弹效果"）。右键分区 `ShowZoneMenu`（大胶囊/任务图标 = 关闭，圆圈 = 工具菜单 `ShowToolMenu`，菜单 `Interop/ContextMenu.cs` 的 `MenuItem` 支持灰字标题和二级菜单）。
- `MainWindow.xaml.cs`（约 3100 行，只剩界面，E 步逐步替换）：输入 `BeginTyping/EndTyping/OnEntryKeyDown`、启动代理 `Launch()`（调 `Engine/AgentLauncher`）/`ShowLaunchFailure()`、模式切换 `ToggleMode/SetMode`（Tab 切换 Claude/文件；网页模式 E1 删了）、文件搜索 `ScheduleSearch/OnSearchResult`、关卡片 `CloseCard`。番茄钟/提示音在 `Engine/ActivityFeed.cs`、`Engine/Chime.cs`。
- 其他窗口：搜索结果 `App/Search/ResultsWindow.cs`、长问题编辑 `ComposeWindow.cs`、消息展开卡片 `App/IslandUi/ExpandedNotice.cs`（760 行）、设置 `App/Options/SettingsWindow.cs`、弹出窗口公用摆放 `Options/PopupPlacement.cs`（⌄ 选项面板 E1 删了）、系统通知横幅 `App/SystemWatch/ToastBanners.cs`。
- 嵌入任务栏：`App/Interop/TaskbarHost.cs`（Attach/Detach，打字时会脱离任务栏；嵌入失败 = 置顶浮动在任务栏上方，`LastError` 记错误码）、`App/Tracking/TaskbarTracker.cs`（后台线程用 UIA 读任务栏布局：开始/搜索/程序按钮 = 图标区，WidgetsButton，TrayNotifyWnd = 托盘）→ `Core/Ui/BarSlot.cs`（按设置 `Position` 最左边/正中间/托盘左边/手动 + `ManualOffset` 算位置，放不下就隐藏；有测试）、`PillRegion.cs`（裁剪成胶囊形）。10-05 起不再跟随/盖住系统搜索框（`SearchBoxTracker`、`NativeSearchBox` 已删）。设置窗口最下面有"诊断"小节（MW `Diagnostics()`）。
- 主题色 `App/Interop/ThemeColors.cs`、`Core/Ui/ThemePalette.cs`；字体 `Core/Localization/UiFonts.cs`、`App/IslandUi/AppFonts.cs`；多语言文案 `Core/Localization/LanguageCatalog.cs`。
- 日志：`%LOCALAPPDATA%\IsleBar\logs`（`TaskbarHost.Log`），排查启动失败先看这里。

### 各功能对应文件
| 功能 | 文件 |
|---|---|
| 下载 | `App/Media/DownloadWatcher.cs`（每秒扫"下载"文件夹里的 .crdownload/.part）→ `Core/Island/DownloadTracker.cs`（速度、暂停、多个合成一条 +N）；**拿不到浏览器下载的总大小**（1.2 做过又撤销，见 TODO）；百度网盘按进程写盘量估算；下载文件夹可在设置里改（`DownloadFolders`）；资源管理器复制/解压/压缩 `App/Media/FileOperationWatcher.cs` |
| 音乐 | `App/Media/MediaWatcher.cs`（Windows 媒体控制 SMTC；浏览器默认排除，设置 `BrowserMedia` 可开，`Core/Island/MediaSources.cs`；进度来自 app 上报的 Timeline，**网易云不报**（实测 00:00/00:00）。规则（10-07 用户定）在 `Core/Island/MediaCards.cs`：一个播放器一张卡，新标题等 0.5 秒，暂停满 5 分钟或关掉播放器就删，再播是新卡。封面按卡片加载（MW `_covers`）。Edge 全浏览器只有一个 SMTC 会话，切视频时会先报别的标签页的旧标题） |
| Claude/Codex 启动 | `App/Engine/AgentLauncher.cs`；路径查找 `Core/Launch/ClaudeExecutable.cs`、`ClaudeProfile.cs`、`CodexProfile.cs`、`NpmShim.cs`、参数 `ClaudeArguments.cs` |
| Claude/Codex 图标 | `App/Interop/ClaudeGlyph.cs`（从本机 claude.exe 图标里抠火花）、`Core/Ui/CodexMark.cs`（从 VS Code 的 Codex 扩展里找 SVG）；都找不到就退回字体图标（就是"聊天图标"） |
| 文件搜索 | `App/Search/EverythingSdk.cs`（分页取回+本地过滤隐藏目录）、`FileSearchWorker.cs`（空输入=最近文件）、`ResultsWindow.cs`（ListView、表头排序、右键菜单、间距常量）、`ShellIcons.cs`（后台取系统图标）、`RecentFiles.cs`（Windows 最近打开）；Core：`Search/FileSearchQuery.cs`、`FileExcludes.cs`（隐藏规则）、`FileListText.cs`（大小格式、中间省略）。焦点/关闭：`Interop/ForegroundEvents.cs` + MainWindow `CheckForeground()`；开始菜单 `Interop/StartMenu.cs`；常亮亚克力 `Interop/ActiveAcrylic.cs` |
| 网页搜索 | `Core/Search/WebSearch.cs` |
| 计时/番茄钟 | `Core/Island/TimerParser.cs`；界面 `ShowTimerText/ShowClockDigits/RollIn/DrawTimerArc` |
| 系统通知 | `App/SystemWatch/ToastWatcher.cs`（读 Windows 通知数据库）→ `ExpandedNotice.cs` 展开卡片 |
| 复制提示 | `App/SystemWatch/ClipboardWatcher.cs` + `Core/SystemWatch/ClipboardRules.cs` |

### 脆弱点
- `MainWindow.xaml.cs` 巨大且状态多（_typing/_hovering/_swapping…），改一处容易影响别处；tick 里的异常会被吞掉（不报错但效果不出现）。
- 事件只能在 `WireEvents()` 一处绑定（作者踩过重复绑定的坑）。
- 打字时窗口会脱离任务栏再回去（`TaskbarHost.Detach/Attach`），输入法问题可能跟这个有关。
- 单位：WinUI 用 DIP，窗口/区域用物理像素（`DpiSetup.Px`），混用会出 bug。

## 开发约定
- 读改文件用专用工具，PowerShell 只跑 git/dotnet/winget。
- 每个任务：改 → 构建 → 跑测试 → 让用户运行看效果 → 提交（一个任务一个提交）。
- 做完更新本文件的「项目地图」和 TODO.md 进度，便于下个会话接续。
