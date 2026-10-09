# IsleBar 二次开发 — 项目上下文（交接文件）

> 新会话先读本文件和 TODO.md（进度看 TODO 每条的 [x]/[~] 和注记）。源码已通读，地图见下方。
> **最新交接（10-10 夜，先看这条）**：丙组 ①②③ 提交（`6fec9cb`）；**④ 的卡片玻璃已做完提交**（掀起卡片全程用 Windows 实时亚克力，照抄系统数值，过程和所有坑见 TODO 7.9 丙④）。**下个会话：④ 剩下的部分**——胶囊配色算法 + 设置里三个风格（先和用户定三个风格对卡片玻璃的含义），见 TODO 丙④「还剩」。然后做甲组。
> （以下为更早的交接）**10-10 晚**：F 全部完成并提交（F3 办法 B `6e68728`/`456ae20`、F4 `03bb756`（顺带删了检查更新和崩溃报告，不再联系原作者）、F5 `ec5c89f` 计时数字硬切）。用户 10-10 又给了一批零碎修改，分成**甲乙丙丁四组**，见 TODO 7.9 F 后面「F 之后的零碎修改」：**丁（问答）已答、乙（大胶囊外观）已做完并提交**（关闭按钮镂空、大胶囊图标不变色、掀起卡片时大胶囊也变暗、变暗时静止/边框去色/按钮失效、悬停分开）。**下个会话做丙（掀起卡片：收回时底边线条宽度对不上、卡片高度加 300 档、计时卡片"取消"改"关闭"、给卡片和大小胶囊加 Windows 系统的云母/亚克力——要先实测透明窗口和嵌在任务栏里能不能用），然后甲（卡片规则：Claude 完成弹两次、麦克风/工作中卡片不亮相、右键"固定进小胶囊"）**。每组做完：构建 → 用户试 → 提交。还没打第一版标记（用户要先做完这些零碎修改）。
> （以下为更早的交接）> （以下为上一条交接）**10-09 晚**：F2b 已全部提交（最新提交 `d2aa7e0`）。**F3「⋯」列表**动画已定稿并写了代码（办法 A），已作为半成品提交 `cf5698d`。用户试用后发现截图法（办法 A）两个问题：下面的大胶囊每秒闪、计时胶囊显示不全。**Claude 建议改办法 B（拆出可复用的胶囊部件），用户尚未明确答复，新会话先问用户确认**。现状、原因、B 的两步做法、哪些代码保留/删除，全在 TODO 7.9 F 条「10-09 晚 交接」。F3 定稿参数见同处「F3 动画定稿」（预览 https://claude.ai/artifact/GdAWsJVV8UnrzctjQ98TPm 第 3 版，用 Artifact 工具 `action: "read"` 读回源码；本地副本 `.claude/preview/f3-list.html`，`.claude/` 不进 git 但留在硬盘上。在应用的浏览器面板里看：`preview_start` 启动 `preview-d`，打开 `http://localhost:8765/f3-list.html?v=N`）。之后是 F4（删旧消息卡片 `ExpandedNotice.cs`）→ F5（番茄钟数字）。
> **下一步（10-05）**：新交互设计已定稿，见 TODO.md 第 7 节（小球 + 胶囊、卡片类型、操作、输入框、AI 在胶囊里交互）。定位/风险/代码策略见 7.8，按 **7.9 顺序**做，第 1 步滚动诊断已完成（滚动能收到，结果见 7.9 第 1 条；统计日志用户决定不做），**10-05 路线 A–L 已定稿（见 7.9），A–D、E1、E2 已完成**（D = 预览 https://claude.ai/artifact/PGb2ZCPwCXFwAJnmCDPkDW 第 14 版）。**E1–E5（+E3½）10-07 全部完成，用户看过两轮已按反馈修**（注记见 TODO 7.9 E 条"E3–E5""第一轮""第二轮"）。**10-07 晚又按用户反馈修了五轮（第三~七轮，见 TODO 7.9 E 条），已提交**；第六、七轮定了"亮相"规则和任务规则总表（见下方项目地图第 3 条）。**下个会话先做**：用户继续试用，有问题按反馈修（排查动画看日志 "transition old-card … end …"；AI 卡片看日志 "agent: …"；连拍脚本 `%TEMP%\burst.ps1`，用法见 TODO 第二轮注记；**用户在用电脑时少推测试卡片**，推了要马上删 `%LOCALAPPDATA%\IsleBar\state\demo*.json` / `timer_*.json`）；没问题就进 **F**（掀起卡片 + "⋯"列表）。**10-08 F 的设计讨论已定下**（全部结论见 TODO 7.9 F 条"10-08 设计讨论"，预览 https://claude.ai/artifact/JhFxD4E8rmXgPLrVDUrGTz 第 6 版，其中"新：图像头部"已作废）：弹出卡片宽固定 300、标准 300×200 / 长 300×300、以大胶囊为中心；排版 = **来源行（小图标），不要大图标**（用户 10-08 正式决定）；结构照 Windows 通知、文字不截断（中间滚动）；"⋯" = 竖着摞的胶囊、点一个换到大胶囊；下载胶囊显示来源不显示文件名、去掉速度。**10-08 会话：F1 做完并按用户反馈打磨多轮（掀起窗口、两种动画、回弹/按下效果、平滑圆角、关闭按钮挪右 B5、呼吸），全部提交；细节见 TODO 7.9 F 条"10-08 第 N 轮"。10-08 晚：呼吸全删、互换时掀起的卡片跟着飞进小图标、F2 卡片规范定稿（预览 https://claude.ai/artifact/G4DcS2xPmx8jmgtMscdiMT 第 3 版）、F2a 卡片框架做完并提交（见 TODO F 条"F2 卡片内容"）。**下个会话从 F2b 开始**（顺序和已查到的线索见 TODO 7.9 F 条"F2b"）：先加日志实测 AI 最后一句回答 / 最近一步能否从会话记录读到、"打开对话"能否找到 Claude/ChatGPT 应用里对应窗口，再补下载按来源分行、麦克风按程序分行、音乐来源程序名、通知程序图标。最新提交 `04889e8`**。（以下为 10-08 早先的交接，F1 部分已完成：）**下个会话从 F1 开始**（弹出窗口骨架；第一件事先加日志验证弹出窗口能盖在任务栏上、不抢焦点、圆角平滑）。还待在真机上选：动画变形 / 向上滑出、要不要过渡模糊。预览本地服务 `.claude/launch.json` 的 preview-d 固定指向 `.claude/preview/`（10-09 起；预览 html 复制一份到那里，`http://localhost:8765/文件名`，地址后加 `?v=N` 防缓存；浏览器面板直接打开项目外的本地文件只是静态截图，脚本不跑）。尺寸以 **D 条开头的「最终尺寸表」**为准（冲突时最终尺寸表 > D 条后面的轮次 > 前面的轮次）。不做新旧切换开关，旧代码不需要就直接删。预览清单在 7.9 末尾。

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
- **改文件的 Python 脚本写进 scratchpad 的 .py 文件再跑**（`python -I 文件`）：在 PowerShell 里用 `python -c "…"` 带中文会乱码、反引号会被吃掉（10-10 踩过，TODO 里写进了乱码）；bash 的 heredoc 里脚本太长有时也会解析失败。
- 用 Python 脚本批量改文件时注意：中文路径/反斜杠在 Python 字符串里会出转义错误，失败时改用 Edit 工具；C# 普通字符串里的一个反斜杠要写成两个（`"C:\\Recovery"`），或用 `@"..."`。
- 项目根目录：`C:\Users\Kasumi\Desktop\islebar-0.1.0 (2)\islebar-0.1.0`（比压缩包外层多一层）。
- 用户需求原文档：`C:\Users\Kasumi\Desktop\新建 Microsoft Word 文档.docx`（内容已整理进 TODO.md，另有 2 张截图未看，需要时从 `word/media/` 提取）。

## 基线
- 原始源码：构建 0 警告 0 错误，测试 783 个。**10-10 丙组后：0 警告 0 错误，测试 891 个；界面文案 162。****10-10 乙组后：0 警告 0 错误，测试 890 个；界面文案 163。****10-09 F3（`cf5698d`）：0 警告 0 错误，测试 927 个；界面文案 170。****10-07 第七轮后：0 警告 0 错误，测试 880 个；界面文案 140。****截至 10-04（下载 1.1/1.3 完成后）：构建 0 警告 0 错误，测试 855 个全过；界面文案 116 条。**
- git：`07041cf` = 原始源码；`10d87c9`/`86c50fc`/`985850d` 只改了 CLAUDE.md/TODO.md；之后每块功能一个提交。
- 已完成（10-04）：4.6 消息卡片默认关闭+开关；4.5/3.4 文件搜索整套重做（见 TODO 4.5 注记）；开始菜单遮挡列表；2.2 启动失败报错复制；4.4 浏览器媒体开关 + 媒体选择规则重写（`9d736c1`）。2.1 不是 bug（用户没装命令行版）。

## 常用命令（在项目根目录）
```
Get-Process IsleBar.App -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*islebar-0.1.0 (2)*' } | Stop-Process -Force
dotnet build src/IsleBar.App -v q -nologo
dotnet test tests/IsleBar.Core.Tests -v q -nologo
Start-Process "src\IsleBar.App\bin\Debug\net8.0-windows10.0.26100.0\IsleBar.App.exe"
```
- 加界面文案：`LanguageStrings.cs` 加属性 + `LanguageCatalog.cs` 10 种语言都要加（文件里顺序：en ko ja zh zht fr de it pt es）+ `tests/.../LocalizationTests.cs` 里的文案总数 +N（10-10 后 163）。
- 加设置项：`IsleBarSettings`（属性 + `Clone()`）+ `SettingsCodec`（key、读、写）+ `Options/SettingsWindow.cs`。

## 项目地图（2026-10-04 读源码后补；行号是近似值，以 grep 为准）

### 核心数据流：一切都是「ActivityState 列表 → 选一个最急的显示」
1. **来源**（每个都产出 `ActivityState` 对象，`Core/Island/ActivityState.cs`）：
   - 状态 JSON 文件：`%LOCALAPPDATA%\IsleBar\state\*.json` 和 `%TEMP%\tgprog\tgprog_*.json`，由 `Core/Island/ActivityStore.cs` 读写（含各类过期时长常量）。写入方：`islebar push/timer/hook`（`Cli/CommandRunner.cs`）、App 自己（如启动失败通知 `ShowLaunchFailure`）。
   - Claude/Codex 钩子：Claude Code hook / Codex notify → `islebar hook` → `Core/Island/AgentHook.cs` 翻译成状态文件。钩子安装脚本 `setup/hooks.ps1`，连接逻辑 `App/Interop/HookConnector.cs`。`App/SystemWatch/AgentRecordWatcher.cs` 读会话记录补抓无钩子的情况。
   - 内存来源（不落盘）：音乐 `App/Media/MediaWatcher.cs`、下载 `App/Media/DownloadWatcher.cs`、发手机 `App/IslandUi/PhoneDrop.cs`、系统短通知 `App/SystemWatch/SystemWatchers.cs`（电源/蓝牙/网络/专注/剪贴板/负载/日历/麦克风摄像头/系统通知 ToastWatcher/更新）。
2. **汇总**：`App/Engine/ActivityFeed.cs` 的 `Collect()` 把所有来源合成列表（同时推进番茄钟、响提示音、清除关掉/看过的 AI 状态）；`MainWindow.xaml.cs` 的 `StartIslandPolling()` 每 250ms 调一次。（10-05 路线 A：非界面逻辑都搬到 `App/Engine/`：`ActivityFeed`、`AgentLauncher`、`Chime`、`FileActions`、`FileSearchRequests`。）
3. **挑选**（10-07 E1 起）：`Core/Island/CardStack.cs`（卡片类型、置顶顺序、鼠标在胶囊上不换卡、`Choose` 换卡）决定大胶囊显示哪张；MainWindow 把结果包成 `IslandSnapshot`。小胶囊的格子 `Core/Island/TaskSlots.cs`（顺序、互换、放不下进"⋯"），小图标总表 `Core/Ui/TaskIcons.cs`。媒体卡片规则 `Core/Island/MediaCards.cs`。
   - **亮相**（10-07 第六/七轮）：卡片变成状态卡片时先在大胶囊停 2 秒（有排队 1.5 秒）再收进格子，按到达顺序排队；等你/完成/出错的卡片上也亮相，临时卡片在时排队，鼠标在胶囊上（含离开后 2 秒）不亮相，启动 2 秒内已有的不亮相（`CardStack.UpdateDebut`）。**每种任务开头/结尾怎么做看总表 `Core/Island/TaskRules.cs`**（结尾 = 卡片下一个类型：告知=留着、临时=亮一下、删掉=不提醒，由各来源写）。**一件事只用一张卡片**（同一个键从开始到结束；不要"状态卡片 + 另发临时卡片"，动画接不上——麦克风、下载、复制都踩过）。临时卡片时长三档 `Core/SystemWatch/NoticeLengths.cs`（2 / 6 / 10 秒）。
   - 同一聊天换了会话编号（桌面版暂停后继续）：`Core/Island/ChatRoot.cs` 认第一条消息 uuid，`ActivityFeed.DropFinishedSameChat` 删旧的完成/中断卡片；日志 "agent: …"。
4. **绘制**（10-09 晚起大胶囊 = `IslandUi/PillView` 部件 `Big`，绘制方法都在它里面，MW 通过 `MainWindow.Pill.cs` 的别名和 `IPillHost` 接口对接）：`RenderIsland()` → `RenderIslandState()` → `Big.Render()` → 文字 `ShowIslandText()` / 计时 `ShowTimerText()`+`ShowClockDigits()` / 进度条 `UpdateProgressLine()` / 边框 `UpdateGlow()` / 前缀文案 `PrefixFor()` / 左图标 `GlyphFor()`。动画在 `App/IslandUi/IslandAnimator.cs`（进出场、高亮边框 `UpdateGlow`；10-08 起任何东西都不呼吸）。

### 界面结构
- `App/MainWindow.xaml`（10-07 E1）：一个窗口 = `PillRow`（大胶囊 `PillBody` 220×32 | 间距 `GapColumn` 8 | 小胶囊 `SmallPill`，左右由设置 `BallSide` 决定），上下各留 7 dip（E4 弧线）。窗口按两个胶囊的**直角矩形**裁剪（`Interop/PillRegion.cs`，布局算在 `Core/Ui/PillGeometry.cs`），圆角由 `PillSurface`/`SmallSurface` 自己画在任务栏色底（`Root.Background`）上——按圆角裁剪会有锯齿。`MainWindow.LayoutPills()` 管显示/隐藏大胶囊、排列、裁剪、定位（`TaskbarTracker.PlaceNow(宽度)`）。大胶囊内部（E2）：`CardRow` 四列 = 关闭区 `CloseZone`（告知卡片）| 图标 `LeadSlot` 16 | Entry 或 IslandContent（文字，竖直按墨迹居中 `PlaceText`/`MeasureInkAsync`，水平按设置 `CardTextAlign`）| 右侧（均衡器、Tab 提示、隐私点）；底部进度轨道 `ProgressTrackBar` + `ProgressLine`；边框 `GlowRing` 2 宽按卡片类型（`UpdateGlow`）。小胶囊（E3）：`TaskCells`（代码生成的格子，Tag = 卡片键，"⋯"的 Tag 是 `DotsTag`）│ `SmallRule` │ `BallBox`/`BallCell` 圆圈（点 = 打字开关）；放左边时列对调。卡片/图标的一切变化（含点任务图标互换）都走 `MainWindow.Swap.cs` 的 `Transition()`（动画数字在 `Core/Ui/SwapMotion.cs`，飞行层 `FlyLayer`，设置"互换动画""回弹效果"）。右键分区 `ShowZoneMenu`（大胶囊/任务图标 = 关闭，圆圈 = 工具菜单 `ShowToolMenu`，菜单 `Interop/ContextMenu.cs` 的 `MenuItem` 支持灰字标题和二级菜单）。
- `MainWindow.xaml.cs`（约 3100 行，只剩界面，E 步逐步替换）：输入 `BeginTyping/EndTyping/OnEntryKeyDown`、启动代理 `Launch()`（调 `Engine/AgentLauncher`）/`ShowLaunchFailure()`、模式切换 `ToggleMode/SetMode`（Tab 切换 Claude/文件；网页模式 E1 删了）、文件搜索 `ScheduleSearch/OnSearchResult`、关卡片 `CloseCard`。番茄钟/提示音在 `Engine/ActivityFeed.cs`、`Engine/Chime.cs`。
- **掀起卡片（F1，10-08）**：`App/IslandUi/LiftWindow.cs`（透明窗口：`Interop/TransparentBackdrop.cs` + DWM blur-behind 才有逐像素透明；无边框 WS_POPUP；不抢焦点；每帧重新置顶盖住任务栏；点外面关 `Interop/OutsideClick.cs`），MW 侧 `MainWindow.Lift.cs`（松开时掀起/收回、悬停时预先拍快照、收回时大胶囊按下动画时机）。数字全在 `Core/Ui/LiftMotion.cs`（尺寸、位置、时长 = `SwapMotion.Duration`、纸张倾斜、弹簧）。按下效果 `IslandUi/PressFeedback.cs`（和回弹开关绑定，Hold/LetGo/Pulse）。动画帧数日志 `Interop/FrameMeter.cs`（"fps swap/lift …"）。**平滑圆角**：`Core/Ui/SmoothCorner.cs` + `IslandUi/SmoothBorder.cs`（所有圆角形状都用它，不用 Border 的 CornerRadius）。**卡片内部（F2）**：每种卡片放什么 `Core/Ui/CardSheet.cs`（`CardSheets.Build`，尺寸字号常量，测试 `CardSheetTests`）→ 绘制 `IslandUi/SheetView.cs`（四个区：来源行/中间区/进度区/按钮行；会变的部分原地更新，不要整张重建）；`LiftWindow` 打开时量中间区定 3:2 / 3:4，`Refresh` 每 tick 更新、只长不缩；按钮动作 `MainWindow.Lift.cs` `OnSheetAction`。**AI 卡片内容（F2b）**：会话记录解析 `Core/Island/AgentTranscript.cs`（最后回答/最近一步），读文件尾+缓存+按会话编号找记录 `App/Engine/AgentTails.cs`（`ActivityFeed.Tails`，日志 "agent tail: …"）；回答按 Markdown 排 `Core/Ui/CardMarkdown.cs` → `SheetView.AddMarkdown`；"打开对话" `App/Engine/ChatOpener.cs`（`claude://code/continue?session=local_…`，日志 "open chat: …"）。应用名/图标：`Interop/AppNames.cs`（应用编号 → 开始菜单名，exe 编号换算成开始菜单编号）+ `Interop/AppIcon.cs`；下载来源 `Core/Island/DownloadSources.cs`，每个文件 `DownloadLine`（`ActivityState.Downloads`）；麦克风多程序 `ActivityState.Apps`。
- **卡片玻璃**（10-10，丙④）：掀起卡片的底 = Windows 实时亚克力，在窗口的系统背景层里画（`Interop/TransparentBackdrop.cs` `StartGlass/MoveGlass`：host backdrop 画笔 → 再模糊 → WinUI 亚克力配方的亮度层/色调层 → 卡片纯色 "Solid" 叠在上面淡出 → 圆角遮罩，效果用 **Win2D**），遮罩每帧跟着卡片走（`LiftWindow.MoveGlass`：拉伸、倾斜、透视的 3D 矩阵）。**WinUI 层的卡片底色有玻璃时始终全透明**——半透明的 WinUI 底色叠在系统玻璃上会合成错误（比两边都亮）。透明效果关/省电时不用玻璃（`IslandUi/Frost.cs`）。滚动文字的渐隐改成合成层遮罩（`SheetView`，CommunityToolkit OpacityMaskView 做法）。卡片按钮是自己画的 Border。**慢放**：建文件 `%LOCALAPPDATA%\IsleBar\slow` = 卡片动画 ×8（调动画时用，用完删）。
- **变暗**（10-10）：「⋯」列表打开或卡片掀起时大胶囊变暗（`MainWindow.List.cs` `Dim(to, length, cells)`：列表连小图标一起暗，掀起只暗大胶囊；`DimVeil` 盖在 `GlowRing` 上面），变暗时 `Big.Frozen`（停滚动/均衡器/流动条/表盘指针）、边框去色 `PaintGlowDim`、大胶囊按钮失效 `BigDimmed`。大胶囊和掀起卡片的图标一律普通色（只有小图标橙绿红），`BigLook`/`HasBigLook` 管互换时的交叉淡化。
- **「⋯」列表（F3，10-10 办法 B）**：窗口 `App/IslandUi/ListWindow.cs`（每张 = 一个真 `PillView`，每 tick `Redraw`），数字 `Core/Ui/ListMotion.cs`，MW 侧 `MainWindow.List.cs`（`PrepareListPill`/`RenderListPill` 画列表胶囊；`OnListTapped` = 互换 + `OpenLift(from:)`，起点图 `TakeListPillShot`；变暗 `DimVeil` + `ApplyCellDim`）。透明窗口公共设置 `IslandUi/SeeThrough.cs`。日志 "list: …"。
- 其他窗口：搜索结果 `App/Search/ResultsWindow.cs`、长问题编辑 `ComposeWindow.cs`、设置 `App/Options/SettingsWindow.cs`、弹出窗口公用摆放 `Options/PopupPlacement.cs`（⌄ 选项面板 E1 删了）、系统通知横幅 `App/SystemWatch/ToastBanners.cs`。
- 嵌入任务栏：`App/Interop/TaskbarHost.cs`（Attach/Detach，打字时会脱离任务栏；嵌入失败 = 置顶浮动在任务栏上方，`LastError` 记错误码）、`App/Tracking/TaskbarTracker.cs`（后台线程用 UIA 读任务栏布局：开始/搜索/程序按钮 = 图标区，WidgetsButton，TrayNotifyWnd = 托盘）→ `Core/Ui/BarSlot.cs`（按设置 `Position` 最左边/正中间/托盘左边/手动 + `ManualOffset` 算位置，放不下就隐藏；有测试）、`PillRegion.cs`（裁剪成胶囊形）。10-05 起不再跟随/盖住系统搜索框（`SearchBoxTracker`、`NativeSearchBox` 已删）。设置窗口最下面有"诊断"小节（MW `Diagnostics()`）。
- 主题色 `App/Interop/ThemeColors.cs`、`Core/Ui/ThemePalette.cs`；字体 `Core/Localization/UiFonts.cs`、`App/IslandUi/AppFonts.cs`；多语言文案 `Core/Localization/LanguageCatalog.cs`。
- 日志：`%LOCALAPPDATA%\IsleBar\logs`（`TaskbarHost.Log`），排查启动失败先看这里。

### 各功能对应文件
| 功能 | 文件 |
|---|---|
| 下载 | `App/Media/DownloadWatcher.cs`（每秒扫"下载"文件夹里的 .crdownload/.part）→ `Core/Island/DownloadTracker.cs`（速度、暂停、多个合成一条 +N）；**拿不到浏览器下载的总大小**（1.2 做过又撤销，见 TODO）；百度网盘按进程写盘量估算；下载文件夹可在设置里改（`DownloadFolders`）；资源管理器复制/解压/压缩 `App/Media/FileOperationWatcher.cs` |
| 音乐 | `App/Media/MediaWatcher.cs`（Windows 媒体控制 SMTC；浏览器默认排除，设置 `BrowserMedia` 可开，`Core/Island/MediaSources.cs`；进度来自 app 上报的 Timeline，**网易云不报**（实测 00:00/00:00）。规则（10-07 用户定）在 `Core/Island/MediaCards.cs`：一个播放器一张卡，新标题等 0.5 秒，暂停满 5 分钟或关掉播放器就删，再播是新卡。封面按卡片加载（MW `_covers`）。Edge 全浏览器只有一个 SMTC 会话，切视频时会先报别的标签页的旧标题） |
| Claude/Codex 启动 | `App/Engine/AgentLauncher.cs`；路径查找 `Core/Launch/ClaudeExecutable.cs`、`ClaudeProfile.cs`、`CodexProfile.cs`、`NpmShim.cs`、参数 `ClaudeArguments.cs` |
| Claude/Codex 图标 | **AI 所有阶段都用厂商 logo（10-07 用户定）**。Claude：`App/Interop/ClaudeGlyph.cs` 从命令行 claude.exe 或 Claude 应用（MSIX，`Interop/AgentApps.cs` 读注册表找安装位置）的图标抠火花（`Core/Ui/GlyphMask.cs`，橙色铺满=底板时取浅色火花），涂 Claude 橙；Codex：`Core/Ui/CodexMark.cs`（VS Code 扩展 SVG）或 Codex 应用自带 logo。都找不到才退回字体图标。MW `LoadAgentMarks`，日志 "agent marks: claude ok, codex ok"。**大胶囊用官方原色，小胶囊单色 + 橙/绿/红**（`AgentLogo(agent, ToneColor)`，10-07） |
| 文件搜索 | `App/Search/EverythingSdk.cs`（分页取回+本地过滤隐藏目录）、`FileSearchWorker.cs`（空输入=最近文件）、`ResultsWindow.cs`（ListView、表头排序、右键菜单、间距常量）、`ShellIcons.cs`（后台取系统图标）、`RecentFiles.cs`（Windows 最近打开）；Core：`Search/FileSearchQuery.cs`、`FileExcludes.cs`（隐藏规则）、`FileListText.cs`（大小格式、中间省略）。焦点/关闭：`Interop/ForegroundEvents.cs` + MainWindow `CheckForeground()`；开始菜单 `Interop/StartMenu.cs`；常亮亚克力 `Interop/ActiveAcrylic.cs` |
| 网页搜索 | `Core/Search/WebSearch.cs` |
| 计时/番茄钟 | `Core/Island/TimerParser.cs`；界面 `ShowTimerText/ShowClockDigits/RollIn/DrawTimerArc`；图标指针实时转 `Core/Ui/TimerDial.cs` + MW `DialContent`（小胶囊 `CellContent`、大胶囊 `ShowLeadDial`） |
| 系统通知 | `App/SystemWatch/ToastWatcher.cs`（读 Windows 通知数据库）→ 普通临时卡片（`Stage = "expand"`，掀起后有"打开"按钮；旧弹出卡片 F4 删了） |
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
