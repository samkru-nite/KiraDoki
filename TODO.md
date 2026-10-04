# 用户修改意见（来自用户 Word 文档，原话整理）

状态：[ ] 未开始　[~] 进行中　[x] 完成。
每条后的 → 是读源码后的标注：对应文件/函数 ｜ 难度 ｜ 建议模型。文件路径相对 `src/`，MW = `IsleBar.App/MainWindow.xaml.cs`。

## 1. 下载
> **10-04 讨论结论（下一个会话从这里开始）**：用户主要用 **Edge 和百度网盘** 下载。多个下载时胶囊显示：**下载速度、正在下的文件名、剩余个数**（220px 放不下时可轮流显示，排版先给方案、用户确认再做）。
> 顺序：① 1.1 流动条改合成器动画（独立、小）→ ② 1.3 多下载汇总 + 速度（每秒比较临时文件大小自己算，不依赖总大小）→ ③ 1.2/4.2a 总大小。
> 总大小方案（未验证，先加诊断）：**Edge** 读本机下载历史 SQLite（`%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\History`，`downloads` 表有 total_bytes、current_path = .crdownload 路径）——总大小只需开始时读一次，已下载量继续用临时文件大小。待验证：Edge 运行时能否读（被锁就复制后读）、开始下载时那一行是否立即写入。服务器不给大小时 total=0，继续流动条。**百度网盘**：先诊断它下载时在下载目录/安装目录产生什么文件（边下边长的临时文件？预分配整文件？任务数据库？），再定方案；最坏只有速度+文件名。③ 涉及外部程序，建议 Opus。
- [x] 1.1 下载总容量未知时，循环进度条卡顿（10-04 完成，用户已测顺滑：XAML 新增 `FlowTrack`/`FlowPiece`，MW `StartFlow()/StopFlow()` 用合成器 Translation.X 循环动画，宽度变才重启；打字时照常显示）
  → MW `UpdateProgressLine()`。原因：循环条位置每 250ms 才算一次（一秒 4 帧），所以一跳一跳。改成合成器动画（像均衡器那样）即可 ｜ 中 ｜ Sonnet
- [x] 1.2 总容量已知时仍是循环条（10-04 完成：`Media/BrowserDownloadHistory.cs` 以 immutable 方式读 Edge/Chrome 各 profile 的 History（普通只读会报 locked，已实测），`downloads` 表 state=0 的行按 current_path 或 target_path 匹配临时文件，每 3 秒查一次直到拿到；文字仍显示速度，进度条显示合计百分比（全部已知才显示，否则流动）；暂停时灰色停住。日志 "download total: …" 记录每个文件是否拿到。**待用户实测**：新下载开始后行是否及时写入。SQLite 封装从 ToastWatcher 移到 `Interop/WinSqlite.cs`）
  → `IsleBar.App/Media/DownloadWatcher.cs`：只看临时文件大小，**根本不知道总大小**；且 `Stage="download"` 会强制走循环条。要拿总大小得换数据源（见 4.2a） ｜ 中-难 ｜ Sonnet，调研部分可用 Opus
- [x] 1.3 多个下载仍只显示循环条（10-04 完成：`Core/Island/DownloadTracker.cs` 汇总成一条「合计速度  最早仍在下的文件名  +N」；5 秒不增长=暂停；`IslandSelector.Rank` 让暂停的下载排在音乐/计时之后）
  （10-04 用户追加：**暂停**——临时文件约 5 秒不变大即「已暂停」（分不清手动暂停和网络卡住，1.2 读 Edge 记录后再细分）；样式照音乐暂停：左 ⏸、文字「已暂停 · 文件名」变暗、进度条消失，恢复增长立即复原。用户选 B：暂停的下载**一直显示**直到删除/下完（现在 2 分钟不动就消失，`DownloadWatcher.Stalled`），但优先级排在音乐、计时之后。和速度一起做）
  → 同上 + `IslandSelector` 只显示一个。可合并成"N 个下载 · 合计"一条 ｜ 中 ｜ Sonnet

## 2. Claude / ChatGPT(Codex) 适配
- [x] 2.1 Claude 和 Codex 启动失败 — **不是 bug**（10-04）：用户没装命令行版（只有桌面 App / ChatGPT 桌面端）。可选改进：未安装时报错改成「没找到 Claude Code 命令行版 — 点此查看安装方法」（适合提 PR）
  （10-04 报错原文：start process 'C:\Users\Kasumi\.local\bin\claude.exe' … 系统找不到指定的文件。即没装命令行版；该路径正是官方安装脚本的默认位置）
  → MW `Launch()`；路径查找 `IsleBar.Core/Launch/ClaudeExecutable.cs`、`CodexProfile.cs`、`NpmShim.cs`。需要先拿到实际报错文字（日志在 `%LOCALAPPDATA%\IsleBar\logs`）才能定位 ｜ 未知 ｜ 先 Sonnet 看报错，修不好换 Opus
- [x] 2.2 启动失败报错自动复制到剪贴板（10-04 完成，用户已测：`ShowLaunchFailure()` 复制「启动失败: …」全文，报错立即显示，2 秒后再复制（原有「已复制」播报约 1.5 秒后回到红色报错））
  → MW `ShowLaunchFailure()` 里加一句复制 ｜ 易 ｜ Sonnet
- [ ] 2.3 中文输入法有时只输入拼音字母（微信输入法）
  → MW `BeginTyping()`（打字时窗口脱离任务栏、`ImeFont.Apply`）、`OnEntryKeyDown()`（Enter/Tab 等在输入法组字时可能被截走）。需要实测复现 ｜ 难（系统接口） ｜ Opus
- [ ] 2.4 Claude/Codex 图标是聊天图标
  → `IsleBar.App/Interop/ClaudeGlyph.cs`（从 claude.exe 图标抠图，npm 装的没有 exe 就失败）、`IsleBar.Core/Ui/CodexMark.cs`（只在装了 VS Code Codex 扩展时有）、MW `LoadAgentMarks()`。方案：自带矢量 logo 作为兜底 ｜ 易-中 ｜ Sonnet

## 3. 文字不居中
（四条大概率同一类问题：文字放在 Canvas 里手动算垂直位置 / 提示文字只设了左边距。需看截图确认是水平还是垂直不居中）
- [ ] 3.1 Claude 启动失败报错字体
- [ ] 3.2 Codex 启动失败报错字体
- [ ] 3.3 音乐歌名和作者名不居中
  → 3.1–3.3 都走 MW `ShowIslandText()` 末尾的 `Canvas.SetTop(...)`；3.1/3.2 报错文字很长会滚动（`StartMarquee/RunMarquee`） ｜ 易-中 ｜ Sonnet
- [x] 3.4 "Everything 未运行"提示不居中（10-04 随 4.5 第 2 步完成）
  → `IsleBar.App/Search/ResultsWindow.cs` 的 `Show()`，提示 TextBlock 没设居中 ｜ 易 ｜ Sonnet

## 4. 其他
- [ ] 4.1 边框高亮太丑
  → XAML `GlowRing/GlassEdge/PillSurface`；MW `UpdateGlow()`、`ApplyStyle()`、`PaintEdge()`；动画 `IslandUi/IslandAnimator.cs` `UpdateGlow()`。先让用户说清/画出想要的样子 ｜ 中 ｜ Sonnet
- [ ] 4.2a 下载只适配浏览器；有无统一下载管线？
  → 结论：**Windows 没有统一下载管线**。现做法是扫"下载"文件夹临时文件。百度网盘用自己的格式和目录。可选方案：监视更多文件夹/后缀、读浏览器下载数据库拿总大小、按进程网速估算。需调研 ｜ 难 ｜ Opus
- [ ] 4.2b 复制文件只有简短提示
  → `IsleBar.App/SystemWatch/ClipboardWatcher.cs` + `IsleBar.Core/SystemWatch/ClipboardRules.cs`。需用户说明想看到什么 ｜ 易-中 ｜ Sonnet
- [ ] 4.3 网易云/QQ 音乐没进度条
  → `IsleBar.App/Media/MediaWatcher.cs` `ReadAsync()`：进度来自 app 向 Windows 上报的时间线，网易云不报就没有。可做"按开始播放时间自己估算"的兜底（拖动进度会不准） ｜ 中 ｜ Sonnet
- [x] 4.4 Edge/B 站视频不显示媒体信息（10-04 完成，用户已简单测过；后台连播 4 秒提示尚未实测：设置「外观」里新开关「也显示浏览器里播放的媒体」`IsleBarSettings.BrowserMedia`，默认关；开启后正在播放的音乐软件仍优先于浏览器，逻辑在 `MediaWatcher.PickMusicSession()`）。**bug（用户 10-04）**：B 站同一标签页先播 A，回首页开 B，再切同合集的 C，胶囊会 A→B→C 跳一遍；后台开网易云也类似。日志证实：Edge 只有一个会话，切视频时会先报约 1 秒旧标题（更早的 A、上一个 B）再报新标题。10-04 讨论后重写 `MediaWatcher`（用户的规则）：在播的显示「最后开始播放的」，都不播显示最后显示过的（暂停样式，30 秒）；换成另一个媒体需连续播放 0.5 秒（所有来源统一）；暂停/继续立即生效；播放器停下时标题与胶囊上的不同＝正在切换，最多 8 秒内仍算它在播。继续播放中换标题：该软件窗口在最前＝你的选择（算新开始）；在后台＝自动连播，不抢胶囊，若不是当前显示的播放器则胶囊临时显示它的新内容 4 秒（用户 10-04 选方案 C + 提示）。诊断日志已删（需要时参考提交历史里的 `LogAsync`）
  → **作者故意排除了浏览器**（`IsleBar.Core/Island/MediaSources.cs` + `MediaWatcher.PickMusicSession()`）。去掉过滤或加开关即可 ｜ 易 ｜ Sonnet
- [x] 4.5 Everything 搜索界面丑（10-04 用户确认满意）
  → `IsleBar.App/Search/ResultsWindow.cs` `Row()`：只有文件夹/文档两种字体图标。可改用系统真实文件图标、调排版 ｜ 中 ｜ Sonnet
  （用户截图 10-04：所有结果同一个文档图标；结果里大量 AppData/ProgramData 系统缓存文件，排序没价值）
  [~] 第 1 步已做（10-04，讨论后改定）：尽量贴近 Everything 自己的窗口——默认按名称排序（文件夹在前）；只隐藏 `C:\Windows`（AppData 不能藏：软件装在 AppData\Local\Programs，开始菜单快捷方式也在 AppData；回收站按用户要求不藏）；搜索词含 \ 或 / 时不过滤。设置里可改显示条数（3–50，默认 8，列表可滚动）；结果列表底部有排序按钮（名称/修改时间/大小）+「在 Everything 中打开」按钮。逻辑 `IsleBar.Core/Search/FileSearchQuery.cs`（有测试），设置项 `FileResults`/`FileSort`。
  （智能排序——按文件类型/位置/最近使用打分——用户决定先不做）
  [~] 第 2 步第一轮修正（10-04，待用户看效果）：宽度固定 480、去掉悬停文件夹按钮、文件名中间省略保留扩展名、日期/大小右对齐+等间距、表头 32 高、图标改 SHGetFileInfo 后台线程（`Search/ShellIcons.cs`）、右键菜单共用一个、测量缓存、亚克力改开始菜单参数、列表窗口在前台时不结束输入（修"切换排序 3 秒后列表消失"）、慢于 30ms 的搜索/绘制写日志。
  第一轮补充（10-04，待用户看）：表头与各列全部左对齐（方案 A）、表头按钮撑满高度以垂直居中；隐藏目录改为设置里可编辑（`IsleBarSettings.FileExclude`，null=内置默认，规则见 `Core/Search/FileExcludes.cs`，支持 * 通配和环境变量）。**隐藏改在 IsleBar 本地过滤**：实测 Everything 里写 32 条 `!path:` 每次查询 ~820ms，不排除只要 ~25ms，所以按页（每页 1000 条，最多 5 页）取回后本地过滤。搜索规则（如 Apple.jpg）不改，用户用 Everything 语法（`apple .jpg`、`folder:`）。
  默认隐藏列表已改为通用精简版（PR 用）；用户个人列表存在 `.claude/my-file-excludes.txt`（git 忽略），粘贴进设置即可。
  [~] 第二轮（10-04 已写，待用户看）：空输入显示最近文件——Windows"显示最近打开的项目"开着时读 Recent 快捷方式（`Search/RecentFiles.cs`），关着时（用户电脑即如此，Start_TrackDocs=0）用 Everything `file: dm:last7days`，另隐藏 AppData/ProgramData/Program Files/点开头文件夹（`FileSearchQuery.RecentRules`）；最近文件与搜索共用排序设置；表头始终显示；无结果/未运行显示一行居中链接（未运行→后台启动 Everything 再重搜，未安装→打开官网下载页）；所有排序箭头朝下；进入输入时若开始菜单开着先关掉（`Interop/StartMenu.cs`，检测 StartMenuExperienceHost 窗口未隐藏后模拟 Win 键）。Win+S 搜索面板未处理。
  第二轮修正（10-04，待用户看）：开始菜单检测改为看前台窗口——实测用户的 Win11 开始菜单由 **SearchHost** 显示（CoreWindow，标题"搜索"），EnumWindows 列不出它；改用 Esc 关闭（也关 Win+S 面板），并记录"开始打字时前台是谁"的诊断日志。列表关不掉：改用 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`（`Interop/ForegroundEvents.cs`）+ 每 tick 后备 `CheckForeground()`：前台不是 IsleBar 自己的窗口就结束输入；前台是结果列表时把键盘还给输入框。不再自动选中第一行（↓ 选第一行，Enter 无选中时打开第一行，Esc 关闭）；输入框为空时再点胶囊关闭列表。间距统一约 14（估算值，待截图核对）；表头悬停背景上下各 4 贴住文字。最近文件（B 方案）额外隐藏回收站。
  第二轮修正 2（10-04）：日志显示点胶囊时前台已是任务栏（Shell_TrayWnd）而开始菜单仍显示——改为在前台通知里记住开始菜单窗口（`StartMenu.Notice`），打字开始时若它未隐藏（关着时 DWM cloaked=2，开着为 0），先切到前台再发 Esc。最近文件改为与搜索**完全相同**的隐藏规则（用户要求统一管理，去掉 RecentRules）。修"Esc/点空白关闭后最近文件又弹出且关不掉"：清空输入框触发的搜索在非输入状态下不再显示列表。表头到第一行间距 +3；列表只显示整行。
  开始菜单最终方案（10-04，待测）：试过 5 种都失败（找错进程 / 看前台时机不对 / 打开动画时 cloaked=2 被过滤 / 激活自己的窗口它不收起 / 系统不让把它切到前台所以 Esc 无效），结论写在 `Interop/StartMenu.cs` 注释里。现用：前台通知里记住 SearchHost 窗口，**仅在点击胶囊时**若它未隐藏就模拟按 Win 键（Win+S 面板会盖住胶囊，点不到，所以不会误把它切成开始菜单）。Win+S 若想停用：注册表 `Explorer\Advanced\DisabledHotkeys="S"`（用户自行操作）。
- [ ] 4.8（新，10-04 发现）胶囊时不时变回 Windows 原生搜索框：日志为 `attach failed ... err=87` 连续失败后的自我保护（"showing the real search box until it works"）。**原版代码就有**（最早 10-03 23:59，改代码之前）。涉及 `App/Interop/TaskbarHost.cs` 的 Attach，建议单独会话用 Opus 排查。
  [~] 第 2 步初版（10-04）：`ResultsWindow.cs` 重写——顶部资源管理器式表头（名称/修改日期/大小，点击排序+箭头）、原生 ListView、系统真实图标（StorageFile 缩略图，按扩展名缓存）、路径中间省略（`Core/Search/FileListText.cs`）、右键菜单（打开/打开所在位置/复制路径/复制文件名/在 Everything 中打开）、悬停行显示"打开所在位置"按钮、列表限高 534dip 后滚动、无结果提示居中+Everything 链接（即 3.4）、常亮亚克力 `Interop/ActiveAcrylic.cs`（原来的亚克力因窗口不激活实际显示为纯色）
- [~] 4.6 开启消息通知后显示有 bug（10-04 已做：卡片默认关闭+设置开关、卡片限制在屏幕内，提交 a131152，待用户实测；卡片美化留到 5.1/5.2 统一设计）
  → `IsleBar.App/SystemWatch/ToastWatcher.cs`、`IsleBar.App/IslandUi/ExpandedNotice.cs`（760 行）、`ToastBanners.cs`。需用户描述/截图具体现象 ｜ 未知 ｜ Sonnet，复杂再 Opus
  （用户截图 10-04：卡片宽于胶囊，`ExpandedNotice.Present()` 以胶囊为中心摆放但没限制在屏幕内，左边伸出屏幕；用户觉得卡片丑。用户可接受的方案：不要卡片 / 把系统原生通知移到胶囊上方 / 重新设计卡片。原生通知位置 Windows 不提供接口，基本不可行。卡片入口：MW `ExpandIfMessage()`）
- [ ] 4.7 番茄钟数字跳动太大（**暂缓**：用户打算以后把番茄钟放进 5.1 的小圆圈，届时一起做）
  → MW `RollIn()`（每个变化的数字从上方 7px 滑下 + 淡入）和 `ShowClockDigits()`。把动画减弱或改成纯淡入 ｜ 易 ｜ Sonnet

## 5. 大想法（需先设计再做）
- [ ] 5.1 「一个圆圈 + 一个胶囊」同时显示多种常驻信息
  → 要改根本结构：`IsleBar.Core/Island/IslandSelector.cs`（现在只选一个）+ MainWindow.xaml 布局 + 窗口宽度/裁剪（`PillRegion`、`TaskbarHost`，胶囊只有 220px 宽） ｜ 难 ｜ Opus
- [ ] 5.2 排版规范由谁出？（待定）
- [ ] 5.3 胶囊上滚轮/双指滑动切换模式
  → MW `WireEvents()` 加 PointerWheelChanged → `ToggleMode()`；注意胶囊嵌在任务栏里，滚轮事件能否收到需实测 ｜ 中 ｜ Sonnet
- [x] 5.4 目前支持哪些常驻信息、哪些短时通知？（已回答，见下）

### 5.4 答案
优先级从高到低（`ActivityKind.cs`）：
- **常驻**（事情没结束就一直在）：Claude/Codex 请求权限（等你回答）→ 下载/传输中 → 计时器/番茄钟 → 音乐播放（暂停后短暂保留）→ Claude/Codex 任务完成（点掉才消失，最长 12 小时）→ Claude/Codex 工作中（最长 6 小时）。另有右侧橙色小点 = 麦克风/摄像头正在用。
- **短时**（几秒后消失）：错误（12 秒）、下载完成（6 秒）、计时结束（10 秒）、系统短通知（充电/电量低、蓝牙连接、网络断开/恢复、专注模式、复制、CPU/内存负载高、日历提醒、更新可用、拖文件发手机）；Windows 应用消息（微信、邮件等）会从胶囊长出一张展开卡片。
- 同时有多件事时**只显示最急的一件**，末尾一个小圆点表示"还有别的"。

## 建议排期
见会话回复；概要：A 小修（4.7、3.4、2.2、4.4、2.4）→ B 中等（3.1–3.3、1.1、4.5、4.3、5.3）→ C 需信息（2.1、2.3、4.6、4.1、4.2b）→ D 大改（1.2/1.3/4.2a 下载数据源、5.1）。
