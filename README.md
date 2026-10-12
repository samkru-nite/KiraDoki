<h1 align="center">KiraDoki</h1>

<p align="center">
  <b>Windows 11 任务栏上的灵动岛</b><br>
  AI 写完代码、下载好了、歌换了、日程快开始了、有人给你发消息……不用切窗口，瞄一眼任务栏就知道。
</p>

<p align="center">
  <a href="https://samkru-nite.github.io/KiraDoki/guide/KiraDoki-%E4%BD%BF%E7%94%A8%E4%BB%8B%E7%BB%8D.html"><b>📖 使用介绍（能点的演示）</b></a>
  &nbsp;·&nbsp; <a href="../../releases"><b>⬇ 下载</b></a>
</p>

<p align="center">
  <img alt="Windows 11" src="https://img.shields.io/badge/Windows-11%20x64-0078D4">
  <img alt="License MIT" src="https://img.shields.io/badge/license-MIT-blue">
  <img alt="Based on IsleBar" src="https://img.shields.io/badge/based%20on-IsleBar-555">
</p>

> KiraDoki 基于 [ds3owl](https://github.com/ds3owl) 的 [IsleBar](https://github.com/ds3owl/islebar) 0.1.0 改写：去掉了在任务栏里提问、文件搜索，重做了卡片、动画和设置页，加了很多软件的适配。

## 它是什么

任务栏最左边（也可以放正中间）住着两颗胶囊：

- **大胶囊**：显示一件现在最值得你看的事。点一下，它会像一张纸一样**掀起来**，变成一张有进度、按钮的完整卡片。
- **小胶囊**：每个小图标是另一件正在发生的事，点一下就和大胶囊互换。左边的圆球：左键列出所有卡片，右键是计时器、番茄钟和设置。

颜色只有三种意思：**橙色** = 需要你处理，**绿色** = 做完了，**红色** = 出错了。

所有内容都在你自己的电脑上处理，不上传（日历除外：它要去下载你填的日历地址）。

## 能显示什么

| | |
|---|---|
| **AI** | Claude Code / Claude 应用、ChatGPT 应用的 工作中 / 等你 / 完成 / 出错；掀起卡片看它最后的回答；↗ 一键跳回那个对话；可以让 AI 在合盖时继续工作 |
| **中转站** | 把文件拖到小岛上先放着，想用时再拖出去（只记位置，不复制、不占空间） |
| **下载和文件** | 浏览器、迅雷、夸克、百度网盘、Steam 下载；资源管理器复制 / 移动 / 解压；WinRAR、7-Zip、Bandizip；剪映导出；打印机；U 盘 |
| **音乐** | 歌名、封面、跟着声音跳的音乐条；掀起后上一首 / 暂停 / 下一首 |
| **计时器** | 快速计时器、番茄钟、秒表，可以同时开好几个 |
| **日历** | 填 Google 日历（或任何 iCal 地址），日程快开始时倒数，开始、进行中、结束都会告诉你 |
| **消息和系统** | QQ 等应用的 Windows 通知、微信新消息、充电与电量、蓝牙、网络、专注时段、复制提示、CPU 与内存、麦克风与摄像头 |

每一项都能在设置里单独关掉。界面支持 10 种语言（简体中文、繁体中文、英语、日语、韩语、法语、德语、意大利语、葡萄牙语、西班牙语）。

## 安装

- 系统要求：Windows 11，64 位（x64）。不需要管理员权限，也不用另外装 .NET。
- 在 [Releases](../../releases) 页面下载 `KiraDoki-Setup-x.y.z.exe`，双击安装。
- 出现蓝色的"Windows 已保护你的电脑"时，点 **更多信息 → 仍要运行**（程序没有数字签名证书，不是病毒）。
- 安装时建议勾上"登录时启动 KiraDoki"；用 Claude Code 的话再勾上"连接 Claude Code 通知"。

## 从源码构建

需要 .NET SDK 8 和 Windows SDK 10.0.26100。

```powershell
dotnet build src/IsleBar.App -v q -nologo
dotnet test tests/IsleBar.Core.Tests -v q -nologo
```

打安装包：`scripts/build-installer.ps1 -Version x.y.z`（需要 Inno Setup）。使用介绍页的源文件是 `guide/template.html`，改完跑 `python guide/build.py`。

代码里的名字、命名空间和数据文件夹（`%LOCALAPPDATA%\IsleBar`）仍然叫 IsleBar，只有用户看得见的地方叫 KiraDoki。代码结构和开发说明见 [AGENTS.md](AGENTS.md)。

`docs/` 是原作者 IsleBar 的宣传网页和视频，和 KiraDoki 现在的样子不一样。

## 作者和许可

- KiraDoki：无敌企鹅大王、Claude
- 基于 [IsleBar](https://github.com/ds3owl/islebar)（原作者 ds3owl），MIT 许可，见 [LICENSE](LICENSE)
- 图标：微软 [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons)（MIT）；其他第三方组件见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)

与 Anthropic、OpenAI、腾讯等公司无关；文中提到的软件名称属于各自的所有者。
