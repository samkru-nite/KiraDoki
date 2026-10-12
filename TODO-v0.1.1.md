# KiraDoki v0.1.1 要修的

> 0.1.0 已打包发给朋友（10-12）。这里只放 0.1.1 要修的问题；日常待办仍在 `TODO.md`。

## [ ] 安装版：Claude 完成卡片的胶囊按钮是"关闭"，不是 ↗（10-12 用户报）

**现象**：安装版里，Claude 应用对话的"完成"卡片，胶囊上显示"关闭"；掀起卡片的"打开对话"看起来能用，其实只是把 Claude 应用调到前面（正好是开着的那个对话才像跳过去了）。日志每次都是 `open chat: session … → no desktop session (terminal?) — bring the app up · claude://`。开发版一直正常。

**原因（10-12 查实）**：Claude 桌面应用是 MSIX 安装的。它写到 `%APPDATA%\Claude\…` 的文件，Windows 实际存在它的私有文件夹
`%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude-code-sessions\…\local_*.json`。
- Claude 应用自己、以及**它启动的程序**（Claude Code 里跑的命令、我启动的开发版）看 `%APPDATA%\Claude` 会被悄悄指到私有文件夹 → 找得到。
- 用户自己打开的安装版在外面，`%APPDATA%\Claude\claude-code-sessions` 下没有这些文件 → 永远找不到 → `ChatOpener.CanOpen` 为假 → 胶囊给"关闭"。
- 证据：同一个文件，Claude 里跑的 Python / PowerShell / .NET 8 原样复制 `DesktopSession` 的代码，次次都找到（.NET 8 循环 476 次 0 次失败）；私有文件夹里有全部 43 个 `local_*.json`；安装版的 dll 和源码相同。

**修法**：`src/IsleBar.App/Engine/ChatOpener.cs` 的 `DesktopSession`（约 107 行）同时找两处：原来的 `%APPDATA%\Claude\claude-code-sessions`，和每个 `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude\claude-code-sessions`（包名后缀别写死）。同时改掉第 12 行注释里的路径说明。全项目只有这一处读 `%APPDATA%`（10-12 查过）；`.claude`、`.codex` 在用户文件夹下，不会被重定向。

**怎么验证**：开发版**必须由用户自己在资源管理器里双击启动**（我从 Claude 里启动的会在 Claude 的私有空间里，看不出问题）；或者直接打新安装包装上。看 Claude 完成卡片的胶囊是不是 ↗、点了是不是跳到那个对话、日志是 `→ local_…`。

## 0.1.1 发布前
- [ ] 重新打安装包（`scripts/build-installer.ps1`，csproj 和 installer 里的版本号改成 0.1.1：`-Version 0.1.1`）
- [ ] 介绍页 `guide/` 里的版本号跟着改
