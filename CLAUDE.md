# IsleBar 二次开发 — 项目上下文（交接文件）

> 新会话先读本文件和 **TODO.md（10-09 整理的新版，只放现在要做的事和现行规则）**。旧 TODO 改名为 **`TODO-history.md`**（全部讨论和决定来历；**下文和代码注释里的 "TODO 7.9 …""TODO 4.5" 等都指它的章节号**）。源码已通读，地图见下方。
> **最新交接（10-11 凌晨，先看这条）**：这次会话做了中转站丙（拖进 / 拖出 / 放回动画）、拖出去后的"已复制 / 已移动"提醒、资源管理器复制卡片重做。现状：构建 0 警告 0 错误，测试 708，界面文案 141。
> - **已提交**：`252edd8` 丙第一版；`7ac74a1` 拖出去的提醒（`Interop/DropSpot.cs` 问松手处的文件夹 + `Core/SystemWatch/TransitOut.cs` 看原处还在不在 / 那边有没有刚创建的同名文件）、接收范围改成"到最近的图标为止"（`Core/Ui/DropZone.cs`）、复制卡片只说准的（`Core/SystemWatch/FileOperations.cs`：进行中 `复制中 / 移动中 文件名 +n`，确定顺利做完才 `已复制`，其余不出卡片；推测跳过 / 剩余 / 失败 / 等你处理都做过又删了，原因在 TODO ④ 文件传输条）；最后一个提交 = 放回动画的多轮修正（见下）。
> - **下一步：真机试放回动画最后一版**（TODO ④ 中转站条"丙 10-11 拖出 / 放回现行做法"最后一项列了要看什么）。没问题再做第 3 步：复制文件 / 图片卡片的"加入中转站"按钮（图标先画预览）；TODO 里新记了"复制提示分复制了 / 剪切了"。
> - 这次学到的：①**用户说"先讨论 / 先别改"就只分析**；用户被多方案绕晕时，自己捋清楚给**一个**方案。②**推测系统没明说的结果会错**：读资源管理器窗口猜"跳过 / 取消"两轮全错 → 只说窗口明写的、拿不准就不说；能自己验证的（我们是拖出方，看文件在不在）才做提醒。③**单位坑**：`DragStartingEventArgs.GetPosition` 是屏幕像素、锚点也是像素——先加日志看实际数字再改（这次凭推测改反过一次）。④**闪烁靠慢动作视频定位**（用户手机慢动作），日志看时间差（如"截图 80 ms 后才开始飞"）。⑤嵌在任务栏里的胶囊窗口：每帧 SetWindowRgn 会卡；移窗口左边缘 + 反向挪内容不在同一帧生效会左右闪；窗口变大的新区域第一帧是黑的——要画任意位置的东西用逐像素透明窗口（`SeeThrough`，接收窗口 / 掀起卡片）。⑥bash heredoc 写 Python 又失败了，脚本一律 Write 进 scratchpad。
> **上一条（10-10 最晚）**：这次会话**做了中转站的甲、乙两步并全部提交**，工作区干净。现状：构建 0 警告 0 错误，测试 687，界面文案 131。中转站的全部规则和进度在 TODO ④ 中转站条（"第 1 步 / 第 2 步 / 乙 做完"几段 + "10-10 用户定：拖放区域和动画"）。
> - **已提交**：`c1de3c9` 第 1 步（拖进胶囊 = 放入、拖大胶囊 = 全部拖出、移动走的自动移除、重启后还在）；`e633dab` + `7a19c6b` 第 2 步（掀起卡片 = 文件列表：文件自己的图标、"找不到了"、✕、整行单独拖、"清空"；列表左右伸进留白 8 让图标和来源行对齐）；`48b7128` **亮相规则改了**（鼠标在胶囊上时亮相中的卡片不走、移开后重新停满自己的时长 `CardStack.RestartDebut`；拖文件进胶囊 `ActivityState.ByHand` 鼠标在上面也马上亮相；中转站亮相 10 秒）——修好了用户说的"鼠标移开卡片立刻收走"；`c752e84` **乙 拖放区域**（`IslandUi/DropCatcher.cs` 透明窗口只在拖东西时出现、范围 `Core/Ui/DropZone.cs`、MW `WatchDrags/CheckDrag`；"托盘左边"摆法删了）。
> - **下一步：丙 动画**（规则在 TODO 那条"拖进来 / 拖出去"：拖进来时原卡片收进小胶囊、中转站换上；拖出去时整个大胶囊被拿起跟着鼠标走、原卡片归位；拖回来时退回；复制后中转站图标在小胶囊轻轻弹出）。然后第 3 步：复制文件/图片卡片加"加入中转站"按钮（**图标先画预览给用户挑**；中转站图标现在是占位的盒子 E7B8，也一起挑）。**没真机试过**：只有圆球（没有大胶囊）时往大胶囊位置拖。
> - 这次学到的：①**接拖放的窗口**：任务栏会在东西经过时把自己提到最上面 → 每 50 毫秒重新置顶；WinUI 只在 DragOver 说"可以放"时，刚进入那一下会显示方框 🚫 → DragEnter 也要说；窗口上边缘多伸 2 像素盖缝。②`dragtest` 式模拟：scratchpad 的 `drag.ps1`（绝对坐标 `mouse_event 0x8001`）、`shot.ps1`（先 `SetProcessDPIAware`），但 `CopyFromScreen` 拍不到拖动小图（分层窗口），看效果要靠用户拍照。③scratchpad 的 `patch.py` 以前遇到"换成空"会把 `>>>>` 写进代码（已修，用前检查 `git grep "^>>>>"`）。④用户说"试改、不满意回退"时：先提交当前版本当回退点，再改。
> **上一条（10-10 深夜）**：这次会话做了 TODO ④ 的前两项并讨论了中转站。
> - **已提交**：`60a4c4f` "猜下一步"按钮（MW `StepFor` → Chat = Claude 完成/中断/**等你**、Link = 复制的网址用默认浏览器开 `Interop/DefaultBrowser.cs`、Reveal = 浏览器下载完成在文件夹里选中）+ 复制提示分档（网址/文件/图片 6 秒、其余 2 秒，`ClipboardRules.LinkOf/ShowFor`），真机看过；`9e9c5d7` **U 盘卡片**（`RemovableDrives` / `DriveRules` / `DriveWatcher` / `EjectBlocker`，文字和颜色见 TODO ④ U 盘条），**还没插真 U 盘测**——用户下次插 U 盘时请他留意，日志搜 "drive:"。
> - **中转站**：设计已定（TODO ④ 中转站条 7 点：只记位置、一张状态卡片、拖胶囊 = 全部 / 大卡片里单个拖、**拖出去跟资源管理器规则一样（方案 A：同盘移动跨盘复制，Ctrl/Shift 可改）**、移动走的自动移除、复制卡片加"加入中转站"按钮要先画图标预览、重启后还在）。实验证明从任务栏胶囊能拖出文件，实验代码已撤（`9218613` 里有当时写法）。**下一步：用户说开始再写中转站代码**（用户最后说"别写代码"，只记 TODO）。
> - 这次定下的产品规则（都在 TODO）：第一节加"猜下一步"按钮四条门槛；用户**要公开发布（至少 GitHub）**；TODO 重排成"三 现有功能打磨 / 四 新软件适配"；不做：音量上岛、卡片上允许/拒绝、Office/WPS、Epic、复制图片文件认后缀。
> - 这次学到的：①截图要先 `SetProcessDPIAware`（scratchpad `shot.ps1`，不然坐标按 125% 缩放错位）；模拟点击/拖动 `click.ps1` / `drag.ps1`（拖动要用绝对坐标 `mouse_event 0x8001` 才触发 WinUI 拖动）。②`git commit -am` 会把没打算提交的实验代码一起带上，提交前看 `git status`。③Windows 弹出被拒的程序名在系统日志事件 225（Kernel-PnP），普通用户能读。
> **上一条（10-10 最晚，只讨论、没改代码）**：TODO 重排成"二 试用一周 → 三 第一部分：现有功能打磨（④ 一周后先做 / ⑤ 收尾打磨）→ 四 第二部分：新软件适配"，做完的 ①–②½ 挪到最后"九 已完成"；③④⑤ 编号没变。新定的：用户**要公开发布（至少 GitHub）**；原则加了"猜下一步"按钮四条门槛；④ 加了 Claude 等你 ↗、下载完成在文件夹里显示、U 盘卡片；不做的加了音量上岛、卡片上允许/拒绝、Office/WPS、Epic。
> **上一条交接（10-10 晚）**：这次会话**重做了日历并全部提交**（`0201a9a` 日历、`ab7bf97` 电量 5%、`b689371` 删日历进度条）。工作区干净。现状：构建 0 警告 0 错误，测试 662，界面文案 117。
> - **日历现行规则**全在 TODO ③ 日历（11 条原规则 + 下面几条"10-10 用户改/定"，以后者为准）。要点：平时一张卡、只换样子（提醒 🟠 置顶 → 开始 🟠 亮相 6 秒 → 进行中 ⚪ 小胶囊 → 结束 🟢 亮相 6 秒 → 同一日历 25 分钟内有下一个 = 空档 🟢 置顶，否则消失）；只认日程自己的提醒，"开始时提醒" = 开始卡片橙色置顶等亲手关；**重叠时多一张卡**（用户选 B：主卡片的日程还在进行时开始的日程单独一张，结束后消失；白色通知不要了）；文字**状态词在前（主色）、标题在后（灰）、空一格、不用「·」**：`10 分钟后 高数`（1 小时内每分钟往下数，以上写 `14:00` / `明天 8:00` / `周三 8:00`）、`开始 高数`、`进行中 高数`、`结束 高数`、`8 分钟后 英语`；**日历卡片不放进度条**；掀起卡片 = 标题 + 状态（卡片色）+ `8:00 – 9:35`；关卡片只关"这个样子"（当天有效）；文字在胶囊里居中、离图标远是用户要的，不改。
> - **代码**：规则 `Core/SystemWatch/CalendarLogic.cs`（`Cards` 分主卡片/重叠卡片，`LookIn` 算一张卡的样子，纯按时间算、有测试）；解析 `Core/SystemWatch/IcsCalendar.cs`（DTEND/DURATION/VALARM、`IcsOccurrence` 带 End/Uid/Calendar/Alarms）；监视器 `SystemWatch/CalendarWatcher.cs`（启动即下载、每 10 分钟下载、每秒算样子，`Board.ReplaceAll`，键 `calendar` / `calendar:{事件id}`，日志 "calendar: …"）；`CardStack` 新增**按样子亮相**（`ActivityState.Look` 变了且带 `DebutFor` 才亮相，亮相中样子变了就结束/重新计时）；告知卡片的新 stage `SystemNotice.KeepStage`，橙色 = `State "wait"`、绿 = `"done"`，亮相时边框也上色（MW `UpdateGlow`）；关卡片走 `SystemWatchers.Close` → `CalendarWatcher.Close`；大胶囊日历文字 `PillView` 的 `_join`/`Gap`（一个空格）；文字 `NoticeText.EventWhen/EventSoon`、文案 `NoticeEventNow/Ongoing/Ended/Tomorrow`。
> - **设置**：日历改成多行框、一行一个地址（键 `calendars`，旧 `calendar_ics` 自动搬进来后删）。用户的主日历地址已迁移；**"课程表"日历的地址由用户自己填进设置，别让用户贴进聊天**。测试开关 `%LOCALAPPDATA%\IsleBar\calendar-test.ics`（当成多一个日历读，改了就重读；生成几个从现在起几分钟后的日程来真机看效果，用完删；现在已删）。
> - **电量**：20% / 10% 各弹 6 秒临时卡片，**5% 及以下**橙色置顶直到插电（原来误写成 4%）。重启后会重复弹一次当前档位的提醒，用户说不用管。
> - **TODO 新增**：⑤ 收尾里"全岛排版"一条（所有卡片去掉「·」、前白后灰；灰色和主文字色按胶囊底色算 + 对比度保底，现在灰色是深/浅二选一的固定值）。
> - **下一步：③ 试用一周**（只修 bug；留意日历在真实课表上的表现、中转站需求、"Claude 等你确认"打断次数、复制提示 2 秒够不够；可顺便试竞品 Agent Island）。一周后按 TODO ④。
> - 这次学到的：①bash heredoc 写长 Python 又解析失败，**脚本一律用 Write 工具写进 scratchpad 再 `python -I` 跑**；scratchpad 里有通用替换脚本 `patch.py`（`<<<< 旧 ==== 新 >>>>` 块，保持 BOM/换行），新会话可照样写一个。②PowerShell `WriteAllText` 写文件会丢 BOM——写前先读第一个字节判断，写完 `git diff` 看第一行有没有变。③画排版对比图用 show_widget，按 2 倍、用户任务栏取色（深红棕：任务栏约 `#401A15`、胶囊约 `#482D28`）；量截图间距用 PIL 找墨迹列。
> 更早的交接（10-10 最晚：AI 卡片回到工作中亮相 6 秒、列表按钮能点、排队、通知铃铛图标；更早的删除/后台键等）内容都在 TODO 对应条目和 git 历史（`3c08fa5` 及之前）里。
> 上一次交接（10-10 深夜）：TODO 第二节 **① 三次删除全部完成并提交**：`73c1e08` 文件搜索 + 输入面板（含问题历史、Tab 模式、Ctrl+Alt+C、斜杠命令）· `0534d44` 启动 Claude/Codex（含模型列表、工作文件夹、`islebar args/aliases`）· `3c69c92` 拖放命令 + 死代码 + 设计开关定死 + 小胶囊固定左边。每次和清单不同的地方都记在 TODO ① 各次下面。现状：构建 0 警告 0 错误，测试 628，界面文案 115。
> **10-10 ①½「后台键」已完成**：「⋯」格子删了，小胶囊 = `圆球 │ 图标…`；圆球左键 = 列表（全部卡片），右键 = 工具菜单；全部固定时列表照样开、下面任务栏空着（试过空胶囊，用户不要）。测试 623。
> 10-10 之后（都已提交）：② 系统通知 bug 查明随「⋯」删除消失；挂起式通知（`NoticeBoard.Hold` 打 `HoldStage`）= 状态卡片，电量 20%/10% 各弹 6 秒、<5% 置顶橙色（`CriticalStage` → Pending）；列表的"在大胶囊上"标签删了；**通知保险**（连续 3 次读不到通知 → 还回 Windows 横幅，测试文件 `toastfail`）；**回弹改苹果式**（胶囊互换/列表 bounce 0.15、冲过头时整张卡连内容一起缩放 `SwapMotion.Settle`；掀起卡片不回弹）；**掀起卡片标题**（汉字单独用中文字体 Bold `SheetView.SetWords`，标题到正文 8 `CardSheets.TitleGap`）。日历要重做（规则在 TODO ③ 日历）。
> **10-10 晚：TODO ② 全部完成**（最后一条"关闭→打开"已提交，细节在 TODO ② 该条）：Claude 完成/中断且能找到桌面会话 → 圆形 ↗（`PillButton.Open`，圆 22、图标 E8AD 12、提示 `OpenWith`"用 Claude 打开"），点了跳对话、6 秒后关卡片（`MainWindow.OpenChat`）；其余告知卡片仍文字"关闭"。按钮点击区 = 文字区右边缘往右、胶囊整个高度（`CloseZone`，外观是里面的 chip）。AI 卡片文字：等你/中断 = 会话名，**完成 = 最终回复的第一句**（`PillView.FirstSentence`，读不到退回会话名），**运行中显示 Claude 最新说的话**（`IPillHost.AgentSaid`，不自动滚、每句至少 3 秒、换句淡出淡入 `PillView.Steady/ChangeWords`）。**胶囊排版规范**（TODO ② 下方）：文字夹在两物之间两边各 8、渐隐两头 12、两边有东西时文字在两者之间居中。**教训**：`DispatcherQueue.CreateTimer()` 只存在局部变量里会被回收（字淡出后没回来）——计时器一律存字段。
> 同晚还做了（都已提交）：音乐卡片居中修正（`88b45a2`）；完成卡片显示最终回复第一句（`e9311ff`）；显示时长三处（通知滚完一圈 6–10 秒 `NoticeLengths.ToRead`、断网 = 状态卡片、AI 出错停止等你关，`fd88ddf`）；删"额度 90%"提醒 + 清掉用户 Claude 设置里的 islebar 状态栏（`4538161`）。复制提示分档 + 网址按钮已排进 TODO ④（试用一周后）。
> **下一步：进 ③**：先按 TODO 里的新规则重做日历，再试用一周。
> **10-10 下午会话学到的（以后要记得）**：①**卡片排版规范**（history 10-08 定，改样式前先对照）：字号只有 12/14/40，正文永不加粗、每区最多一处中粗，小节名 12 灰，颜色只有主色/灰/状态色——提方案前先查规范，别提和它矛盾的（这次提过"小节名改 14 粗"被用户指出）。②**中文字重**：卡片字体是"Pretendard + 中文字体"的备选串，汉字靠退字体显示；要中文变粗只能拆段单独指定（`SetWords`），给整行要 600 也到不了退字体。③**动效规范**：小东西可以弹，大面积阅读面（掀起卡片）不弹；回弹要整体缩放，不能只拉外框。④用户要求"过程中的也要改"：改卡片样式时把所有阶段（工作中/等你/完成/中断、Markdown 和普通文字两条路径）都覆盖到，别只改最后的完成卡片。⑤给用户看排版方案先用 show_widget 画对比图（按真实字号/间距）。
> 这次会话定下/改动的、以后要记得的：**计时器可并存**（每个一个状态文件 `timer_{进程号}_{时间}`，`ActivityFeed.StartTimer/RestartTimer`）；**回弹跟随 Windows"动画效果"**（`IslandUi/Animations.cs`）；**玻璃只看 `Frost.Allowed`**；拖文件到胶囊的代码还在但由 `TakesDrops = false` 关着（等中转站）；Everything 只剩给"压缩进度"用的精简版 `Media/EverythingSdk.cs`。旧设置键在 `SettingsCodec.RemovedKeys` 里统一清掉，再删设置项就往里加。改文件的 Python 小工具（读写保持 BOM / 换行）每次写进 scratchpad 再跑。
> 更早的交接（10-05 ~ 10-09：E/F/G/H 各步、预览链接、量间距方法）已删，内容都在 `TODO-history.md` 7.9 和 git 历史（`325efc9` 及之前）里。仍然有效的老规则：尺寸以 history 7.9 D 条开头的「最终尺寸表」为准；不做新旧切换开关，旧代码不需要就直接删；预览/慢放/测试卡片的用法见新 TODO 第七节。

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
- 日志：`%LOCALAPPDATA%\IsleBar\logs\islebar.log`。
- **改文件的 Python 脚本写进 scratchpad 的 .py 文件再跑**（`python -I 文件`）：在 PowerShell 里用 `python -c "…"` 带中文会乱码、反引号会被吃掉（10-10 踩过，TODO 里写进了乱码）；bash 的 heredoc 里脚本太长有时也会解析失败。
- 用 Python 脚本批量改文件时注意：中文路径/反斜杠在 Python 字符串里会出转义错误，失败时改用 Edit 工具；C# 普通字符串里的一个反斜杠要写成两个（`"C:\\Recovery"`），或用 `@"..."`。
- 项目根目录：`C:\Users\Kasumi\Desktop\islebar-0.1.0 (2)\islebar-0.1.0`（比压缩包外层多一层）。
- 用户需求原文档：`C:\Users\Kasumi\Desktop\新建 Microsoft Word 文档.docx`（内容已整理进 TODO.md，另有 2 张截图未看，需要时从 `word/media/` 提取）。

## 基线
- 原始源码：构建 0 警告 0 错误，测试 783 个。**10-11 凌晨（中转站丙、拖出提醒、复制卡片后）：0 警告 0 错误，测试 708 个；界面文案 141。****10-10 最晚（中转站甲乙后）：0 警告 0 错误，测试 687 个；界面文案 131。****10-10 深夜（U 盘卡片后，未插真 U 盘测）：0 警告 0 错误，测试 678 个；界面文案 126。****10-10 夜（复制提示分档 + "猜下一步"按钮后）：0 警告 0 错误，测试 675 个；界面文案 117。****10-10 傍晚（日历重做、电量 5% 后）：0 警告 0 错误，测试 662 个；界面文案 117。****10-10 最晚（排队后）：0 警告 0 错误，测试 639 个；界面文案 114。****10-10 深夜（列表按钮能点后）：0 警告 0 错误，测试 634 个；界面文案 114。****10-10 深夜（AI 卡片回到工作中亮相 6 秒 `CardStack.ResumeDebutLength`，`7662fcc`）：0 警告 0 错误，测试 633 个；界面文案 114。****10-10 深夜（删 90% 提醒后）：0 警告 0 错误，测试 632 个；界面文案 114。****10-10 晚（关闭→打开后）：0 警告 0 错误，测试 628 个；界面文案 115（测试里的数字，之前 CLAUDE.md 写的 115 实为 114）。****10-10 晚（标题粗体后）：0 警告 0 错误，测试 628 个；界面文案 114。****10-10 电量/专注后：0 警告 0 错误，测试 626 个；界面文案 114。****10-10 后台键后：0 警告 0 错误，测试 623 个；界面文案 115。****10-10 第 3 次删除后：0 警告 0 错误，测试 628 个；界面文案 115。****10-10 第 2 次删除后：0 警告 0 错误，测试 634 个；界面文案 138。****10-10 第 1 次删除后：0 警告 0 错误，测试 782 个；界面文案 145。****10-09 H1b 后：1 警告（CS0649 旧打字代码，H1c 删）0 错误，测试 923 个；界面文案 171。****10-09 甲组后：0 警告 0 错误，测试 906 个；界面文案 165。** **10-09 H1a 后：1 警告（CS0649 `_closedByClickAt`，旧打字代码，H1c 删）0 错误，测试 914 个；界面文案 167。****10-10 丙组后：0 警告 0 错误，测试 891 个；界面文案 163（+ 透明模式）。****10-10 乙组后：0 警告 0 错误，测试 890 个；界面文案 163。****10-09 F3（`cf5698d`）：0 警告 0 错误，测试 927 个；界面文案 170。****10-07 第七轮后：0 警告 0 错误，测试 880 个；界面文案 140。****截至 10-04（下载 1.1/1.3 完成后）：构建 0 警告 0 错误，测试 855 个全过；界面文案 116 条。**
- git：`07041cf` = 原始源码；`10d87c9`/`86c50fc`/`985850d` 只改了 CLAUDE.md/TODO.md；之后每块功能一个提交。
- 已完成（10-04）：4.6 消息卡片默认关闭+开关；4.5/3.4 文件搜索整套重做（见 TODO 4.5 注记）；开始菜单遮挡列表；2.2 启动失败报错复制；4.4 浏览器媒体开关 + 媒体选择规则重写（`9d736c1`）。2.1 不是 bug（用户没装命令行版）。

## 常用命令（在项目根目录）
```
Get-Process IsleBar.App -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*islebar-0.1.0 (2)*' } | Stop-Process -Force
dotnet build src/IsleBar.App -v q -nologo
dotnet test tests/IsleBar.Core.Tests -v q -nologo
Start-Process "src\IsleBar.App\bin\Debug\net8.0-windows10.0.26100.0\IsleBar.App.exe"
```
- 加界面文案：`LanguageStrings.cs` 加属性 + `LanguageCatalog.cs` 10 种语言都要加（文件里顺序：en ko ja zh zht fr de it pt es）+ `tests/.../LocalizationTests.cs` 里的文案总数 +N（10-10 现在 114）。
- 加设置项：`IsleBarSettings`（属性 + `Clone()`）+ `SettingsCodec`（key、读、写）+ `Options/SettingsWindow.cs`。删设置项：同样三处删掉，再把旧 key 加进 `SettingsCodec.RemovedKeys`（保存时从 islebar.json 清掉）。删界面文案：先扫"哪些属性代码里没人读"，再两处一起删、改测试总数。

## 项目地图（2026-10-04 读源码后补；行号是近似值，以 grep 为准）

### 核心数据流：一切都是「ActivityState 列表 → 选一个最急的显示」
1. **来源**（每个都产出 `ActivityState` 对象，`Core/Island/ActivityState.cs`）：
   - 状态 JSON 文件：`%LOCALAPPDATA%\IsleBar\state\*.json` 和 `%TEMP%\tgprog\tgprog_*.json`，由 `Core/Island/ActivityStore.cs` 读写（含各类过期时长常量）。写入方：`islebar push/timer/hook`（`Cli/CommandRunner.cs`）、App 自己（如启动失败通知 `ShowLaunchFailure`）。
   - Claude/Codex 钩子：Claude Code hook / Codex notify → `islebar hook` → `Core/Island/AgentHook.cs` 翻译成状态文件。钩子安装脚本 `setup/hooks.ps1`，连接逻辑 `App/Interop/HookConnector.cs`。`App/SystemWatch/AgentRecordWatcher.cs` 读会话记录补抓无钩子的情况。
   - 内存来源（不落盘）：音乐 `App/Media/MediaWatcher.cs`、下载 `App/Media/DownloadWatcher.cs`、系统短通知 `App/SystemWatch/SystemWatchers.cs`（电源/蓝牙/网络/专注/剪贴板/负载/日历/麦克风摄像头/系统通知 ToastWatcher/更新）。
2. **汇总**：`App/Engine/ActivityFeed.cs` 的 `Collect()` 把所有来源合成列表（同时推进番茄钟、响提示音、清除关掉/看过的 AI 状态）；`MainWindow.xaml.cs` 的 `StartIslandPolling()` 每 250ms 调一次。（10-05 路线 A：非界面逻辑都搬到 `App/Engine/`：`ActivityFeed`、`AgentLauncher`、`Chime`、`FileActions`、`FileSearchRequests`。）
3. **挑选**（10-07 E1 起）：`Core/Island/CardStack.cs`（卡片类型、置顶顺序、鼠标在胶囊上不换卡、`Choose` 换卡）决定大胶囊显示哪张；MainWindow 把结果包成 `IslandSnapshot`。小胶囊的格子 `Core/Island/TaskSlots.cs`（顺序、互换；放不下的不显示，只在圆球的列表里），小图标总表 `Core/Ui/TaskIcons.cs`。媒体卡片规则 `Core/Island/MediaCards.cs`。
   - **排队**（10-10 最晚，取代"临时卡片插队 / 新的顶掉旧的"）：亮相和临时卡片共用一条队（`_debutQueue`，大胶囊上的是 `_debutKey`），先来先显示；临时卡片由 `CardStack` 自己存一份（`Waiting`，来源丢了也照样显示），排队时在小胶囊有格子，时长从上大胶囊起算（`ShowFor`），同键合并（系统通知按应用 `system:toast:{AppId}`，旧消息在 `Earlier`，胶囊右边 "+N"），最多 20 秒（`MessageCap`）后缩回格子；鼠标在胶囊上时临时卡片照样排队、正在显示的不走。
   - **亮相**（10-07 第六/七轮）：卡片变成状态卡片时先在大胶囊停 2 秒（有排队 1.5 秒；AI 回到工作中 6 秒）再收进格子，按到达顺序排队；等你/完成/出错的卡片上也亮相，鼠标在胶囊上（含离开后 2 秒）不亮相，启动 2 秒内已有的不亮相（`CardStack.UpdateDebut`，日志 "debut: …" 记每张为什么亮/不亮）。**固定**（10-09，右键"固定/取消固定"）：`CardStack.Tuck/Untuck`，固定的不亮相、不抢大胶囊，全固定时 `Shown` 为空、大胶囊收起。右键菜单的操作要等菜单收尾后再执行（`ShowCardMenu`，否则不出动画）。AI 应用自己的 Windows 通知在卡片刚变状态时丢掉（`ActivityFeed.DropAgentAppToasts`）。**每种任务开头/结尾怎么做看总表 `Core/Island/TaskRules.cs`**（结尾 = 卡片下一个类型：告知=留着、临时=亮一下、删掉=不提醒，由各来源写）。**一件事只用一张卡片**（同一个键从开始到结束；不要"状态卡片 + 另发临时卡片"，动画接不上——麦克风、下载、复制都踩过）。临时卡片时长三档 `Core/SystemWatch/NoticeLengths.cs`（2 / 6 / 10 秒）。
   - 同一聊天换了会话编号（桌面版暂停后继续）：`Core/Island/ChatRoot.cs` 认第一条消息 uuid，`ActivityFeed.DropFinishedSameChat` 删旧的完成/中断卡片；日志 "agent: …"。
4. **绘制**（10-09 晚起大胶囊 = `IslandUi/PillView` 部件 `Big`，绘制方法都在它里面，MW 通过 `MainWindow.Pill.cs` 的别名和 `IPillHost` 接口对接）：`RenderIsland()` → `RenderIslandState()` → `Big.Render()` → 文字 `ShowIslandText()` / 计时 `ShowTimerText()`+`ShowClockDigits()` / 进度条 `UpdateProgressLine()` / 边框 `UpdateGlow()` / 前缀文案 `PrefixFor()` / 左图标 `GlyphFor()`。动画在 `App/IslandUi/IslandAnimator.cs`（进出场、高亮边框 `UpdateGlow`；10-08 起任何东西都不呼吸）。

### 界面结构
- `App/MainWindow.xaml`（10-07 E1）：一个窗口 = `PillRow`（小胶囊 `SmallPill` | 间距 `GapColumn` 8 | 大胶囊 `PillBody` 220×32；小胶囊 10-10 起固定在左边），上下各留 7 dip（E4 弧线）。窗口按两个胶囊的**直角矩形**裁剪（`Interop/PillRegion.cs`，布局算在 `Core/Ui/PillGeometry.cs`），圆角由 `PillSurface`/`SmallSurface` 自己画在任务栏色底（`Root.Background`）上——按圆角裁剪会有锯齿。`MainWindow.LayoutPills()` 管显示/隐藏大胶囊、排列、裁剪、定位（`TaskbarTracker.PlaceNow(宽度)`）。大胶囊内部（E2）：`CardRow` 四列 = 按钮区 `CloseZone`（告知卡片：文字"关闭"或圆形 ↗，`PillView.ShowClose(PillButton)`，MW `ButtonFor/OpensChat`；能否跳到对话 `ChatOpener.CanOpen`，带缓存）| 图标 `LeadSlot` 16 | IslandContent（文字，竖直按墨迹居中 `PlaceText`/`MeasureInkAsync`，水平：文字居中、图标不动，10-10 定死）| 右侧（均衡器、隐私点）；底部进度轨道 `ProgressTrackBar` + `ProgressLine`；边框 `GlowRing` 2 宽按卡片类型（`UpdateGlow`）。小胶囊（E3）：`BallBox`/`BallCell` 圆圈 = 后台键（左键 = 列表，右键 = 工具菜单，10-10 ①½）│ `SmallRule` │ `TaskCells`（代码生成的格子，Tag = 卡片键；「⋯」格子 10-10 删了）。卡片/图标的一切变化（含点任务图标互换）都走 `MainWindow.Swap.cs` 的 `Transition()`（**回弹 10-10 改成苹果式**：`SwapMotion.Bounce` = 0.15，冲过头时 `SwapMotion.Settle` 让整张卡片连内容一起缩放，不再只拉外框；掀起卡片 `LiftMotion.Opening` 不回弹；动画数字在 `Core/Ui/SwapMotion.cs`，飞行层 `FlyLayer`；10-10 起只有线条一种；回弹跟随 Windows"动画效果" `IslandUi/Animations.cs`）。右键分区 `ShowZoneMenu`（大胶囊/任务图标 = 关闭，圆圈 = 工具菜单 `ShowToolMenu`，菜单 `Interop/ContextMenu.cs` 的 `MenuItem` 支持灰字标题和二级菜单）。
- `MainWindow.xaml.cs`（约 1950 行，只剩界面；10-10 输入框、Tab 模式、文件搜索、启动 AI、模型列表更新都删了）：按下/松开分区在 `WireEvents`、关卡片 `CloseCard`、计时器 `StartTimer`（`ActivityFeed.StartTimer`，每个计时器一个文件 = 一张卡片，可并存）。番茄钟/提示音在 `Engine/ActivityFeed.cs`、`Engine/Chime.cs`。
- **掀起卡片（F1，10-08）**：`App/IslandUi/LiftWindow.cs`（透明窗口：`Interop/TransparentBackdrop.cs` + DWM blur-behind 才有逐像素透明；无边框 WS_POPUP；不抢焦点；每帧重新置顶盖住任务栏；点外面关 `Interop/OutsideClick.cs`），MW 侧 `MainWindow.Lift.cs`（松开时掀起/收回、悬停时预先拍快照、收回时大胶囊按下动画时机）。数字全在 `Core/Ui/LiftMotion.cs`（尺寸、位置、时长 = `SwapMotion.Duration`、纸张倾斜、弹簧）。按下效果 `IslandUi/PressFeedback.cs`（和回弹一起跟随 Windows 动画效果，Hold/LetGo/Pulse）。动画帧数日志 `Interop/FrameMeter.cs`（"fps swap/lift …"）。**平滑圆角**：`Core/Ui/SmoothCorner.cs` + `IslandUi/SmoothBorder.cs`（所有圆角形状都用它，不用 Border 的 CornerRadius）。**卡片内部（F2）**：每种卡片放什么 `Core/Ui/CardSheet.cs`（`CardSheets.Build`，尺寸字号常量，测试 `CardSheetTests`）→ 绘制 `IslandUi/SheetView.cs`（四个区：来源行/中间区/进度区/按钮行；会变的部分原地更新，不要整张重建；标题文字一律经 `SetWords`/`Words` 读写——汉字段是单独的 Bold Run，直接改 `.Text` 会丢掉粗体）；`LiftWindow` 打开时量中间区定 3:2 / 3:4，`Refresh` 每 tick 更新、只长不缩；按钮动作 `MainWindow.Lift.cs` `OnSheetAction`。**AI 卡片内容（F2b）**：会话记录解析 `Core/Island/AgentTranscript.cs`（最后回答/最近一步），读文件尾+缓存+按会话编号找记录 `App/Engine/AgentTails.cs`（`ActivityFeed.Tails`，日志 "agent tail: …"）；回答按 Markdown 排 `Core/Ui/CardMarkdown.cs` → `SheetView.AddMarkdown`；"打开对话" `App/Engine/ChatOpener.cs`（`claude://code/continue?session=local_…`，日志 "open chat: …"）。应用名/图标：`Interop/AppNames.cs`（应用编号 → 开始菜单名，exe 编号换算成开始菜单编号）+ `Interop/AppIcon.cs`；下载来源 `Core/Island/DownloadSources.cs`，每个文件 `DownloadLine`（`ActivityState.Downloads`）；麦克风多程序 `ActivityState.Apps`。
- **卡片玻璃**（10-10，丙④）：掀起卡片的底 = Windows 实时亚克力，在窗口的系统背景层里画（`Interop/TransparentBackdrop.cs` `StartGlass/MoveGlass`：host backdrop 画笔 → 再模糊 → WinUI 亚克力配方的亮度层/色调层 → 卡片纯色 "Solid" 叠在上面淡出 → 圆角遮罩，效果用 **Win2D**），遮罩每帧跟着卡片走（`LiftWindow.MoveGlass`：拉伸、倾斜、透视的 3D 矩阵）。**WinUI 层的卡片底色有玻璃时始终全透明**——半透明的 WinUI 底色叠在系统玻璃上会合成错误（比两边都亮）。系统透明效果关、节能模式开（有效电源模式 0/1）时不用玻璃（"透明模式"开关 10-10 删了）（`IslandUi/Frost.cs`，日志 "lift: glass? …"）。滚动文字的渐隐改成合成层遮罩（`SheetView`，CommunityToolkit OpacityMaskView 做法）。卡片按钮是自己画的 Border。**慢放**：建文件 `%LOCALAPPDATA%\IsleBar\slow` = 卡片动画 ×8（调动画时用，用完删）。
- **变暗**（10-10）：列表打开或卡片掀起时大胶囊变暗（`MainWindow.List.cs` `Dim(to, length, cells)`：列表连小图标一起暗，掀起只暗大胶囊；`DimVeil` 盖在 `GlowRing` 上面），变暗时 `Big.Frozen`（停滚动/均衡器/流动条/表盘指针）、边框去色 `PaintGlowDim`、大胶囊按钮失效 `BigDimmed`。大胶囊和掀起卡片的图标一律普通色（只有小图标橙绿红），`BigLook`/`HasBigLook` 管互换时的交叉淡化。
- **列表（F3，10-10 办法 B；10-10 ①½ 起由圆球打开，`ToggleList`，从圆球 `BallOnScreen` 飞出；全部固定时位置用 `BigPillWouldBe`；一张卡片都没有才自动关 `KeepList`）**：窗口 `App/IslandUi/ListWindow.cs`（每张 = 一个真 `PillView`，每 tick `Redraw`），数字 `Core/Ui/ListMotion.cs`，MW 侧 `MainWindow.List.cs`（`PrepareListPill`/`RenderListPill` 画列表胶囊；列表胶囊的按钮能点 = 透明的 `Item.Button` 盖在 `CloseZone` 上 → `OnListButton`（↗ 跳对话 + 收列表，"关闭" 关卡片）；卡片走了（关掉或自己结束）→ `Phase.Shifting` 缩小消失、上面补位 `ListMotion.Leave`，10-10；`OnListTapped` = 互换 + `OpenLift(from:)`，起点图 `TakeListPillShot`；变暗 `DimVeil` + `ApplyCellDim`）。透明窗口公共设置 `IslandUi/SeeThrough.cs`。日志 "list: …"。
- **输入面板（H）、文件搜索**：10-10 第 1 次删除时删了（`InputPanel`、`Search/*`、`ComposeWindow`、`StartMenu`、`ForegroundEvents`、`GlobalHotkey`、`ImeFont`、`Core/Search|History|Commands`）。要查当时的做法看 git `325efc9` 及之前。
- 其他窗口：设置 `App/Options/SettingsWindow.cs`、弹出窗口公用摆放 `Options/PopupPlacement.cs`（⌄ 选项面板 E1 删了）、系统通知横幅 `App/SystemWatch/ToastBanners.cs`。
- 嵌入任务栏：`App/Interop/TaskbarHost.cs`（Attach/Detach，弹右键菜单时会脱离任务栏；嵌入失败 = 置顶浮动在任务栏上方，`LastError` 记错误码）、`App/Tracking/TaskbarTracker.cs`（后台线程用 UIA 读任务栏布局：开始/搜索/程序按钮 = 图标区，WidgetsButton，TrayNotifyWnd = 托盘）→ `Core/Ui/BarSlot.cs`（按设置 `Position` 最左边/正中间/托盘左边/手动 + `ManualOffset` 算位置，放不下就隐藏；有测试）、`PillRegion.cs`（裁剪成胶囊形）。10-05 起不再跟随/盖住系统搜索框（`SearchBoxTracker`、`NativeSearchBox` 已删）。设置窗口最下面有"诊断"小节（MW `Diagnostics()`）。
- 主题色 `App/Interop/ThemeColors.cs`、`Core/Ui/ThemePalette.cs`；字体 `Core/Localization/UiFonts.cs`、`App/IslandUi/AppFonts.cs`；多语言文案 `Core/Localization/LanguageCatalog.cs`。
- 日志：`%LOCALAPPDATA%\IsleBar\logs`（`TaskbarHost.Log`），排查启动失败先看这里。

### 各功能对应文件
| 功能 | 文件 |
|---|---|
| 下载 | `App/Media/DownloadWatcher.cs`（每秒扫"下载"文件夹里的 .crdownload/.part）→ `Core/Island/DownloadTracker.cs`（速度、暂停、多个合成一条 +N）；**拿不到浏览器下载的总大小**（1.2 做过又撤销，见 TODO）；百度网盘按进程写盘量估算；下载文件夹可在设置里改（`DownloadFolders`）；资源管理器复制/解压/压缩 `App/Media/FileOperationWatcher.cs`（读进度窗口文字 → `Core/SystemWatch/FileOperations.cs`，窗口开着时每秒看 4 次；只说窗口明写的：`复制中 / 移动中 文件名 +n`，确定做完才 `已复制`；诊断日志 "fileop: …" 试用期保留） |
| 音乐 | `App/Media/MediaWatcher.cs`（Windows 媒体控制 SMTC；浏览器默认排除，设置 `BrowserMedia` 可开，`Core/Island/MediaSources.cs`；进度来自 app 上报的 Timeline，**网易云不报**（实测 00:00/00:00）。规则（10-07 用户定）在 `Core/Island/MediaCards.cs`：一个播放器一张卡，新标题等 0.5 秒，暂停满 5 分钟或关掉播放器就删，再播是新卡。封面按卡片加载（MW `_covers`）。Edge 全浏览器只有一个 SMTC 会话，切视频时会先报别的标签页的旧标题） |
| Claude/Codex 启动 | 10-10 删了。`Core/Launch` 只剩 `AgentKind`（名字常量）和 `ClaudeExecutable`（找 claude.exe 给火花 logo） |
| Claude/Codex 图标 | **AI 所有阶段都用厂商 logo（10-07 用户定）**。Claude：`App/Interop/ClaudeGlyph.cs` 从命令行 claude.exe 或 Claude 应用（MSIX，`Interop/AgentApps.cs` 读注册表找安装位置）的图标抠火花（`Core/Ui/GlyphMask.cs`，橙色铺满=底板时取浅色火花），涂 Claude 橙；Codex：`Core/Ui/CodexMark.cs`（VS Code 扩展 SVG）或 Codex 应用自带 logo。都找不到才退回字体图标。MW `LoadAgentMarks`，日志 "agent marks: claude ok, codex ok"。**大胶囊用官方原色，小胶囊单色 + 橙/绿/红**（`AgentLogo(agent, ToneColor)`，10-07） |
| 文件搜索 | 10-10 删了。只剩 `App/Media/EverythingSdk.cs`（精简版）给"压缩进度"找正在变大的压缩包（`FileOperationWatcher`） |
| 计时/番茄钟 | `Core/Island/TimerParser.cs`；界面 `ShowTimerText/ShowClockDigits/RollIn/DrawTimerArc`；图标指针实时转 `Core/Ui/TimerDial.cs` + MW `DialContent`（小胶囊 `CellContent`、大胶囊 `ShowLeadDial`） |
| 系统通知 | `App/SystemWatch/ToastWatcher.cs`（读 Windows 通知数据库）→ 普通临时卡片（`Stage = "expand"`，掀起后有"打开"按钮；旧弹出卡片 F4 删了）；"只在任务栏显示"= 关掉 Windows 横幅 `ToastBanners`；**通知保险**：连续 3 次读不到 → `Healthy` false → `SystemWatchers.OnToastHealth` 还回横幅（测试文件 `toastfail`） |
| 复制提示 | `App/SystemWatch/ClipboardWatcher.cs` + `Core/SystemWatch/ClipboardRules.cs`（`LinkOf` 认一整个网址 → 卡片 `Open`；`ShowFor` 网址/文件/图片 6 秒、其余 2 秒）；默认浏览器名 `Interop/DefaultBrowser.cs` |
| U 盘卡片（10-10，**还没插真 U 盘测**） | 发现/弹出 `Interop/RemovableDrives.cs`（可移动盘 + 外部总线固定盘，`CM_Request_Device_Eject`）→ 规则 `Core/SystemWatch/DriveRules.cs` → 监视器 `SystemWatch/DriveWatcher.cs`（键 `system:drive:E`，日志 "drive: …"）；谁挡住弹出 `Interop/EjectBlocker.cs`（`wevtutil` 读事件 225）；掀起卡片 `CardSheets.Drive`、`SheetAction.Eject` → `SystemWatchers.Eject`；自动播放通知 `ActivityFeed.DropDriveToasts`；红边框 MW `UpdateGlow` 的 Status+Error 分支 |
| 中转站（10-10） | 列表规则 `Core/SystemWatch/TransitShelf.cs` → 监视器 `SystemWatch/TransitWatcher.cs`（键 `system:transit`，存 `%LOCALAPPDATA%\IsleBar\transit.txt`，每 2 秒查文件在不在，`Dragged` 按"还在不在"判断移动走的；日志 "transit: …"）；拖进 MW `OnDropOver/OnDropped`，拖出 MW `HookDragOut`（只有中转站卡片 `PillBody.CanDrag`）；掀起卡片 `SheetLayout.Files` → `SheetView.FilesMiddle`（文件图标 `AppIcon.ResolveFile`），清空 `SheetAction.Clear`；拖放区域 `IslandUi/DropCatcher.cs` + `Core/Ui/DropZone.cs`（到最近的图标为止）+ MW `WatchDrags/CheckDrag`（日志 "dropzone: …"）；**拖进 / 拖出 / 放回动画** `MainWindow.Transit.cs`（`Pretend` 决定大胶囊假装显示什么、`TrackHome/InSlot` 按面积判断在不在原位、手里的截图画在 `DropCatcher.ShowHand` 上、`PutBackAsync` 滑回，日志 "transit: …"、"put back: …"）；拖出去后的"已复制 / 已移动" `Interop/DropSpot.cs` + `Core/SystemWatch/TransitOut.cs` + `TransitWatcher.Dragged/Tell` |
| "猜下一步"按钮 | MW `StepFor`（Chat = Claude 完成/中断/等你，Link = 复制的网址，Reveal = 浏览器下载完成）→ `ButtonFor` 给 ↗、`TakeStep` 执行、`OpenTip` 提示；列表 `OnListButton` 同一套。规则（四条门槛）在 TODO 第一节 |

### 脆弱点
- `MainWindow.xaml.cs` 巨大且状态多（_hovering/_swapping…），改一处容易影响别处；tick 里的异常会被吞掉（不报错但效果不出现）。
- 事件只能在 `WireEvents()` 一处绑定（作者踩过重复绑定的坑）。
- 弹右键菜单时窗口会脱离任务栏再回去（`TaskbarHost.Detach/Attach`，嵌在任务栏里的窗口拿不到前台）。
- 单位：WinUI 用 DIP，窗口/区域用物理像素（`DpiSetup.Px`），混用会出 bug。

## 开发约定
- 读改文件用专用工具，PowerShell 只跑 git/dotnet/winget。
- 每个任务：改 → 构建 → 跑测试 → 让用户运行看效果 → 提交（一个任务一个提交）。
- 做完更新本文件的「项目地图」和 TODO.md 进度，便于下个会话接续。
