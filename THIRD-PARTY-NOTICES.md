# Third-party notices

KiraDoki itself is released under the MIT License (see `LICENSE`). It ships the third-party components below, each under
its own licence. The full licence and notice texts are in the `licenses` folder (installed next to KiraDoki).

| Component | Used for | Licence | Text |
|---|---|---|---|
| [IsleBar](https://github.com/ds3owl/islebar) by ds3owl | KiraDoki is built on IsleBar's source | MIT | `LICENSE` (ds3owl's copyright line) |
| [Windows App SDK](https://github.com/microsoft/WindowsAppSDK) 1.6 / WinUI 3 | App framework (bundled) | Microsoft Software License Terms (redistributable) | `licenses/WindowsAppSDK-LICENSE.txt`, `WindowsAppSDK-NOTICE.txt` |
| [Microsoft Edge WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2) (`Microsoft.Web.WebView2.Core.dll`, `WebView2Loader.dll`) | Pulled in by the Windows App SDK | BSD-style Microsoft licence | `licenses/WebView2-LICENSE.txt`, `WebView2-NOTICE.txt` |
| [.NET 8 runtime](https://github.com/dotnet/runtime) | Runtime (bundled, self-contained) | MIT, with the runtime's own third-party notices | `licenses/dotnet-LICENSE.txt`, `dotnet-THIRD-PARTY-NOTICES.txt` |
| [C#/WinRT](https://github.com/microsoft/CsWinRT) (`WinRT.Runtime.dll`, `Microsoft.Windows.SDK.NET.dll` — the .NET projection of the Windows SDK, from Microsoft.Windows.SDK.NET.Ref) | Windows Runtime projections | MIT | `licenses/CsWinRT-LICENSE.txt`, `CsWinRT-NOTICE.txt` |
| [C++/WinRT](https://github.com/microsoft/cppwinrt) headers | Compiled into `IsleBarTap.dll` (native mode) | MIT | `licenses/CppWinRT-LICENSE.txt` |
| [Win2D](https://github.com/microsoft/Win2D) 1.2 (`Microsoft.Graphics.Canvas.dll`, NuGet Microsoft.Graphics.Win2D) | The lifted card's glass (its acrylic layers and rounded cut) | Microsoft Software License Terms for the Win2D package ([licence](http://www.microsoft.com/web/webpi/eula/eula_win2d_10012014.htm)); Win2D's source is MIT | `licenses/Win2D-LICENSE.txt` (to add before release) |
| [Interop.UIAutomationClient](https://www.nuget.org/packages/Interop.UIAutomationClient) | Finding the taskbar search box via UI Automation | MIT, Copyright (c) 2019 Roman | `licenses/Interop.UIAutomationClient-LICENSE.txt` |
| [Everything SDK](https://www.voidtools.com/support/everything/sdk/) (`Everything64.dll`) | File search (talks to Everything, which you install yourself) | MIT, Copyright (C) 2022 David Carpenter | below |
| [Pretendard](https://github.com/orioncactus/pretendard) (Regular, Medium) | UI font for Latin and Korean text | SIL Open Font License 1.1 | `licenses/Pretendard-OFL.txt` |
| [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) (Regular; only the icons used, as `Assets/Fonts/IsleIcons.ttf`, built by `scripts/make-icons.py`) | Every icon on the island | MIT, Copyright (c) 2020 Microsoft Corporation | `licenses/FluentSystemIcons-LICENSE.txt` |

The Microsoft components are licensed to you by Microsoft under the terms in their licence files; installing IsleBar
means accepting those terms for those components. Segoe UI is part of Windows and is not redistributed.

"Claude" and "Claude Code" are trademarks of Anthropic PBC; "OpenAI" and "Codex" are trademarks of OpenAI;
"Everything" is a product of voidtools; "Dynamic Island" is a trademark of Apple Inc. IsleBar is not affiliated with
or endorsed by any of them. The Claude and Codex marks shown in the bar are read at run time from software already
installed on your PC; IsleBar does not ship them.

## Everything SDK

```
Copyright (C) 2022 David Carpenter

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```
