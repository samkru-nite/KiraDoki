# 用户修改意见（来自用户 Word 文档，原话整理）

状态：[ ] 未开始　[~] 进行中　[x] 完成。
每条后的 → 是读源码后的标注：对应文件/函数 ｜ 难度 ｜ 建议模型。文件路径相对 `src/`，MW = `IsleBar.App/MainWindow.xaml.cs`。

## 1. 下载
- [ ] 1.1 下载总容量未知时，循环进度条卡顿
  → MW `UpdateProgressLine()`。原因：循环条位置每 250ms 才算一次（一秒 4 帧），所以一跳一跳。改成合成器动画（像均衡器那样）即可 ｜ 中 ｜ Sonnet
- [ ] 1.2 总容量已知时仍是循环条
  → `IsleBar.App/Media/DownloadWatcher.cs`：只看临时文件大小，**根本不知道总大小**；且 `Stage="download"` 会强制走循环条。要拿总大小得换数据源（见 4.2a） ｜ 中-难 ｜ Sonnet，调研部分可用 Opus
- [ ] 1.3 多个下载仍只显示循环条
  → 同上 + `IslandSelector` 只显示一个。可合并成"N 个下载 · 合计"一条 ｜ 中 ｜ Sonnet

## 2. Claude / ChatGPT(Codex) 适配
- [ ] 2.1 Claude 和 Codex 启动失败
  → MW `Launch()`；路径查找 `IsleBar.Core/Launch/ClaudeExecutable.cs`、`CodexProfile.cs`、`NpmShim.cs`。需要先拿到实际报错文字（日志在 `%LOCALAPPDATA%\IsleBar\logs`）才能定位 ｜ 未知 ｜ 先 Sonnet 看报错，修不好换 Opus
- [ ] 2.2 启动失败报错自动复制到剪贴板
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
- [ ] 3.4 "Everything 未运行"提示不居中
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
- [ ] 4.4 Edge/B 站视频不显示媒体信息
  → **作者故意排除了浏览器**（`IsleBar.Core/Island/MediaSources.cs` + `MediaWatcher.PickMusicSession()`）。去掉过滤或加开关即可 ｜ 易 ｜ Sonnet
- [ ] 4.5 Everything 搜索界面丑
  → `IsleBar.App/Search/ResultsWindow.cs` `Row()`：只有文件夹/文档两种字体图标。可改用系统真实文件图标、调排版 ｜ 中 ｜ Sonnet
  （用户截图 10-04：所有结果同一个文档图标；结果里大量 AppData/ProgramData 系统缓存文件，排序没价值——可考虑排除系统目录、常用文件优先）
- [ ] 4.6 开启消息通知后显示有 bug
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
