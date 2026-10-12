# KiraDoki（IsleBar 二次开发）— Claude 交接文件

> **项目介绍、协作方式、命令、代码地图、规范都在 `AGENTS.md`，新会话先读它，再读 `TODO-v0.1.1.md`（现在唯一的待办；用户说"加入 TODO"就是指它，旧的 `TODO.md` / `TODO-history.md` 不再参考）。** 本文件只放最新进度（下一个会话做什么）。
> 更早的交接记录（10-04 ~ 10-12 每次会话做了什么、学到了什么）和旧版详细地图都在 git 历史里：`git show d06012e:CLAUDE.md`。

> **最新交接（10-12 晚，下一个会话 = 优化主窗口的掉帧，先看这条）**
> - **用户要做什么**：新会话专门优化界面主文件（`MainWindow.xaml.cs` 2364 行、约 66 个状态字段，加 `MainWindow.Swap / Lift / List / Transit / Pill.cs`）的性能。用户笔记本 U5 338H、屏幕 165 Hz（一帧 6 ms）、200% 缩放，**时常掉帧**。方向（10-12 复盘定）：精简、性能、稳定性，不再加新功能。
> - **规矩：先测量，再改**（用户老规矩"对接系统行为先加日志验证"，这次 10-12 违反过一次：黑边凭推测改，没修好还回退了）。第一步只加计时日志、不改行为，让用户正常用半天到一天，看日志找出最慢的，再一处一处改；每改一处用户真机看过再提交。
> - **已有的数据**（`%LOCALAPPDATA%\IsleBar\logs\islebar.log` + `.log.1`，`Interop/FrameMeter.cs` 记的 "fps …"，10-12 统计）：**换卡 swap 最差**——1202 次里平均 120 帧/秒，133 次（约 11%）出现 ≥ 33 ms 的卡顿，最长 468 ms，28 次低于 60 帧；列表收起平均 89 帧/秒、最长 468 ms；掀起卡片打开 / 收回平均 149 / 133 帧/秒、最长约 50 ms。日志里 "transition old-card … new-card … fly … end …" 是换卡各阶段的毫秒数。
> - **嫌疑（都没验证，按把握排）**：
>   1. **读屏幕像素 `GetPixel`**（MW `SampleBeside`）：10-12 实测 **9 ms**（`beside: reading the pixel took …`，≥ 8 ms 每分钟记一次），在界面线程上。调用处：`RefreshBesideColor` 每 3 秒一次；**`FollowTaskbar` 每次胶囊挪位置 / 改宽度（= 每次换卡，日志 "follow: visible=…" 就在 transition 中间）**；`ApplyTheme`。最可能是换卡卡顿的原因之一。注意它和**黑边**是同一段代码（下一条），改之前先和用户说好；那里有诊断日志 `beside paint [follow|refresh|theme]`，别删。
>   2. **每 250 ms 的主循环 `StartIslandPolling` 全在界面线程**：`RetryAttachIfNeeded → FollowTaskbar → RefreshBesideColor → _feed.Collect`（**`ActivityStore.Read` 列文件夹 + 读每个状态 JSON，磁盘读在界面线程**；还有番茄钟、提示音、`Tails.Note` 读会话记录尾巴？要查）`→ _cards.Update → Pretend → KeepLift / KeepList → LoadAlbumArt → Classify → ComputeCells → RenderTaskCells → LayoutPills → 渲染`。换卡飞行中（`_flying`）只做到 `LoadAlbumArt` 就 return，但前面那些仍在跑。
>   3. 音乐条跟随声音（设置 `AudioBars`）：`PillView.StartMeterBars` 每 33 ms 一次，只在显示音乐条时跑，很轻。
>   4. 后台监视器（22 个）大多在**后台线程**，结果存好、界面只取列表 → 对掉帧影响小（10-12 查过，之前说"监视器多 = 掉帧"是说重了）。
> - **建议第一步**：在主循环每一步、`Transition()` 各阶段、`SampleBeside`、`_feed.Collect` 内部各步加 `Stopwatch` 计时，**只在一轮超过 4 ms 时记一行**（写清每步各多少 ms），避免刷屏；`FrameMeter` 卡顿时也顺带记当时界面线程在做什么。日志前缀建议 "slow tick: …"。
> - **别碰 / 小心**：`FollowTaskbar` 里 `ShowWindow` 和 `PlaceAt` 之间不要加耗时的事；tick 里的异常会被吞（效果不出现但不报错）；事件只在 `WireEvents()` 绑；**另一个会话在做打包 / 改名 KiraDoki**（工作区里它的文件：installer、csproj、`MainWindow.Swap.cs` 的圆球修复、图标等），动代码前先 `git status`，提交只提交自己的（共用文件用 `git diff 文件 > p; 删掉别人的块; git apply --cached p`，已暂存的别人的删除先 `git restore --staged` 再提交后 `git rm --cached` 还原）。

## 基线
- 10-12 晚（每个功能一个开关后）：构建 0 警告 0 错误，测试 774 个，界面文案 234。之后的合盖不睡眠、打包、介绍页提交没有更新这里的数字，以实际构建为准。

## 只和 Claude Code 有关的
- 改文件的小工具：scratchpad 里的 `patch.py`（`@@@ 路径` / `<<<<` 旧 `====` 新 `>>>>`，一处对不上整个文件都不写；用前 `git grep "^>>>>"` 查有没有误写进代码）。新会话的 scratchpad 是空的，要用就照这个格式重写一个。
- 上个会话的聊天记录在 `~/.claude/projects/<项目>/*.jsonl`，要查"哪一版"时可以读（Python 输出写进 utf-8 文件再读）。
- 做完一件事：更新本文件的交接段，长期有效的东西（新文件、新规矩、新坑）写进 `AGENTS.md`。
