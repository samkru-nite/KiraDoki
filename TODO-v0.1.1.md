# KiraDoki v0.1.1 要修的

> 0.1.0 已打包发给朋友（10-12）。**从 10-12 起所有待办都写在这里**（用户说"加入 TODO"就是指本文件）；`TODO.md`、`TODO-history.md` 是旧的，不再参考也不再更新。

## [ ] 安装版：Claude 完成卡片的胶囊按钮是"关闭"，不是 ↗（10-12 用户报）

**现象**：安装版里，Claude 应用对话的"完成"卡片，胶囊上显示"关闭"；掀起卡片的"打开对话"看起来能用，其实只是把 Claude 应用调到前面（正好是开着的那个对话才像跳过去了）。日志每次都是 `open chat: session … → no desktop session (terminal?) — bring the app up · claude://`。开发版一直正常。

**原因（10-12 查实）**：Claude 桌面应用是 MSIX 安装的。它写到 `%APPDATA%\Claude\…` 的文件，Windows 实际存在它的私有文件夹
`%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude-code-sessions\…\local_*.json`。
- Claude 应用自己、以及**它启动的程序**（Claude Code 里跑的命令、我启动的开发版）看 `%APPDATA%\Claude` 会被悄悄指到私有文件夹 → 找得到。
- 用户自己打开的安装版在外面，`%APPDATA%\Claude\claude-code-sessions` 下没有这些文件 → 永远找不到 → `ChatOpener.CanOpen` 为假 → 胶囊给"关闭"。
- 证据：同一个文件，Claude 里跑的 Python / PowerShell / .NET 8 原样复制 `DesktopSession` 的代码，次次都找到（.NET 8 循环 476 次 0 次失败）；私有文件夹里有全部 43 个 `local_*.json`；安装版的 dll 和源码相同。

**修法**：`src/IsleBar.App/Engine/ChatOpener.cs` 的 `DesktopSession`（约 107 行）同时找两处：原来的 `%APPDATA%\Claude\claude-code-sessions`，和每个 `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude\claude-code-sessions`（包名后缀别写死）。同时改掉第 12 行注释里的路径说明。全项目只有这一处读 `%APPDATA%`（10-12 查过）；`.claude`、`.codex` 在用户文件夹下，不会被重定向。

**怎么验证**：开发版**必须由用户自己在资源管理器里双击启动**（我从 Claude 里启动的会在 Claude 的私有空间里，看不出问题）；或者直接打新安装包装上。看 Claude 完成卡片的胶囊是不是 ↗、点了是不是跳到那个对话、日志是 `→ local_…`。

## [ ] 设置页"Windows 通知"的说明写上 QQ（10-12 用户问"为什么只有微信没有 QQ"）

**原因**：QQ 发 Windows 通知，归在"Windows 通知"开关下（和钉钉、邮件等一样）；微信不发通知、只闪任务栏，所以单独一个开关。现在说明只写"会读取所有通知的内容"，看起来像不支持 QQ。

**改法**：文案 `NotifyToastsDesc` 改成类似"QQ、钉钉等应用发的通知；会读取通知的内容"，`LanguageCatalog.cs` 10 种语言都改（只改文字，文案总数不变）。介绍页 `guide/template.html` 的"Windows 通知"那格和设置页表格顺手对一下，改完跑 `python guide/build.py`。

## [ ] 进行中卡片的"隐藏"按钮改成"关闭卡片"（10-12 用户报：Steam 下载的掀起卡片写"隐藏"，容易误会）

**现状**：`SheetAction.Hide`（文案 `CardHide` = "隐藏"）和 `Close` 点了效果完全一样（`MainWindow.Lift.cs` 约 191 行同一分支，都是关卡片）。所有**状态卡片 = 进行中**的都用"隐藏"：下载、复制 / 解压、Steam、麦克风 / 摄像头、AI 工作中等（`CardSheet.cs` 的 `CloseFor` Status 分支、`Transfer` 约 365 行、`Privacy` 约 539 行）；做完的卡片用"关闭"。

**用户定**：改成 **"关闭卡片"**（用户给了"关闭 / 关闭卡片"两个选，我推荐后者：进行中的下载旁边写"关闭"像"停止下载"，"关闭卡片"说清只关卡片）。做完的卡片仍是"关闭"。

**改法**：只改文案 `CardHide` 的 10 种语言（en "Close card"、zht "關閉卡片"等），按钮动作不动、文案总数不变。介绍页里没写"隐藏"，不用改。

## [ ] 复制提示的开关拆成三项：普通文字 / 文件（含图片）/ 网址（10-12 用户提）

**为什么**：复制普通文字的提示只提供情绪价值，很吵；文件、图片、网址的卡片有用（"+ 加入中转站"、用浏览器打开）。用户要能只关掉普通文字那一种。

**现状**：只有一个开关 `NotifyClipboard`（设置键 `notify_clipboard`，提醒页"系统状态"小节，`SettingsWindow.cs` 约 382 行）→ `SystemWatchers` 整个开 / 关 `ClipboardWatcher`。文字 / 文件 / 图片 / 网址的分辨在 `ClipboardWatcher` + `Core/SystemWatch/ClipboardRules.cs`（`LinkOf` 认网址）。

**做法（待讨论细节）**：
- 设置页照"系统通知 / 只在任务栏显示"的样子：主开关"复制提示"下面三行子开关——普通文字、文件和图片、网址；主开关关时子行变灰。
- 三个都关 = 监视器真停（和其他开关一致）；只关一部分 = 监视器照跑，对应种类不出卡片。
- 剪切（"已剪切"）跟着"文件和图片"走。
- 新设置项按规矩加（`IsleBarSettings` + `Clone()`、`SettingsCodec`、设置页），新界面文案 10 种语言 + 测试总数。介绍页 `guide/` 里设置表格要是写了复制提示，顺手对一下。

**用户定（10-12）**：主开关"复制提示" + 下面三个子开关（方案一，和"系统通知 / 只在任务栏显示"一样）。
**还待定**：普通文字默认开还是关？（我推荐默认关：吵、只有情绪价值；但朋友装了新版会发现"复制文字没反应了"）

## 0.1.1 发布前
- [ ] 重新打安装包（`scripts/build-installer.ps1`，csproj 和 installer 里的版本号改成 0.1.1：`-Version 0.1.1`）
- [ ] 介绍页 `guide/` 里的版本号跟着改
