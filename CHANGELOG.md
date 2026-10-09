# Changelog

## Unreleased

### Removed: file search and typing on the bar (fork, 10-10)
- The bar only shows what happens in the background now. File search (Everything), the input box and its panel, question
  history, the Tab modes, Ctrl+Alt+C and `islebar commands` are gone; their settings are dropped from islebar.json.
- A click on the circle opens the tools menu (the same as a right click).
- Timers run side by side, each on its own card, instead of asking to replace the running one.
- Files can be dropped on the bar only when a drop command is set.
- Everything is still used, if it runs, to follow an archive Explorer is compressing.
- The design switches are gone: the swap is the straight morph, the card lifts by morphing, the ⋯ list staggers 20 ms, card
  text is centred, corners are round, and the small pill sits left of the big one. The bounce follows Windows' animation
  effects; the card's glass follows Windows' transparency effects and energy saver. The drop command and its phone transfer
  went too (dropping files on the bar is off until something takes them).
- Launching Claude Code / Codex from the bar is gone too: the agent, launch options, working folder and model-list settings,
  the model list check at startup, and `islebar args` / `islebar aliases`.

### Small pill and card rules (fork, 10-07)
- The small pill shows one icon for every card behind the big pill (temporary ones excepted), as many as fit before the
  taskbar's app icons; the rest wait behind "⋯". Icons are colourless unless something needs you: orange waits for you,
  green is done, red failed. Music shows its album cover; paused things get two bars.
- Click an icon to swap that card onto the big pill (morph, morph with arcs, or flip — a setting).
- Right click depends on where: the big pill closes its card, an icon shows its name and Close, the circle opens the tools
  (timers in a submenu).
- One card per media player; paused for 5 minutes it goes. A finished agent card stays until closed; a session that ends
  mid-turn leaves "Claude interrupted". Mic / camera in use is a quiet card instead of an orange dot.

### File search
- The results list is rebuilt on the system list control: Explorer-style column headers (name · date modified · size)
  that sort when clicked, the shell's own file icons, long names and paths shortened in the middle (the extension and the
  last folders stay), a fixed width, and Start-menu acrylic that stays on. Past about a dozen rows it scrolls.
- Default order is Everything's own — by name, folders first. Newest-first used to fill the list with logs Windows rewrites
  every minute. The order chosen in the header is remembered.
- Right-click a result: open · open file location · copy path · copy name · open in Everything.
- With nothing typed, the list shows recent files: Windows' "recently opened" list, or files changed this week when that
  list is turned off.
- Nothing is selected until <kbd>↓</kbd>; <kbd>Enter</kbd> opens the top result; <kbd>Esc</kbd> — or a second click on the
  empty pill — closes the list.
- One clickable line when there's nothing to list: "No results — search in Everything", "Everything isn't running — start it"
  (starts it in the background and searches again; opens the download page if it isn't installed).
- Hidden folders are a list in settings: full paths, folder names or `*` patterns, one per line. The default hides only
  Windows, restore/upgrade folders, Temp and caches. They're filtered by the bar rather than in the Everything query —
  32 path exclusions made each search take ~820 ms instead of ~60.
- How many results to show is a setting (3–50, default 8).

### Other
- When Claude Code or Codex can't be started, the error is also copied to the clipboard, two seconds later (so the "Copied" notice follows the error instead of flipping with it) — long
  errors only scroll past on the pill.
- New setting (off by default): also show media playing in a browser — YouTube, Bilibili and other videos — on the music isle.
  The isle shows the player you started last while it plays, and the one shown last (paused) when nothing plays — so music comes
  back once the video stops. Switching videos no longer flashes older videos' titles or lets background music cut in while the
  next video loads; a different track or video takes over after half a second of playing. Picking another song or video in a
  player's window counts as starting it; a player in the background moving on by itself (next track, next episode) doesn't take
  the isle — its new item peeks in for four seconds instead.

### Downloads
- Browser downloads show their speed instead of the amount received ("2.4 MB/s  report.pdf"). Several at once are one item:
  the summed speed, the earliest one still moving, and how many more ("+2").
- A download whose file stops growing for 5 seconds shows as paused (⏸, dimmed, no bar) and stays until it finishes or is
  removed — it used to vanish after two minutes. It waits behind music and timers. (A paused download and a stalled
  connection look the same from the file.)
- Download folders are a setting (one per line), for browsers set to save elsewhere. The box starts with your own Downloads
  folder as Windows reports it (no user name is stored; it follows the folder if moved), with a reset link. The hints under
  this box and the hidden-folders box now spell out every form a line can take.
- Baidu Netdisk downloads show too (".baiduyun.p.downloading"). Baidu reserves the whole file up front, so its size says
  nothing: the speed is what Baidu's helper process writes, and the bar is an estimate from that against the reserved size.
- Chrome/Edge keep a download as "Unconfirmed 123.crdownload" until it ends: the bar says "Downloading" instead of the number,
  and the "done" notice finds the renamed file.
- Explorer's file operations show anywhere on disk as icon · speed · file name, the icon telling copy, unzip and compress
  apart. Copy, move and unzip follow the percentage in Explorer's progress window, and the file name is read from that window
  (unzip is a "copy" from an archive there). Compressing (no progress window) shows the archive being written, found through
  Everything, with a flowing bar. Copied files are created at full size up front, so their size says nothing.

### Fixes
- The flowing bar under a download of unknown size moved in jumps — it was redrawn four times a second. It now runs on the
  compositor at the screen's frame rate (same speed and length).
- The results list could stay open after clicking elsewhere (once the list itself had been clicked). Typing now ends as soon
  as another app's window comes to the front.
- With the Start menu open, a click on the pill closes it first — it used to cover the results list.
- The Windows-notification card that grows out of the pill is off by default (new setting) and no longer runs off the screen
  when the pill sits near its edge.

## 0.1.0 — first public build (2026-10-03)

### The bar
- A pill in place of the Windows 11 taskbar search box: ask Claude Code or Codex (new terminal, pinned launch options),
  search files (Everything) or the web with <kbd>Tab</kbd>, prompt history with <kbd>↑</kbd>/<kbd>↓</kbd>, wrapped preview
  and a multi-line compose box for long prompts.
- The isle: music with album art and bars, timers / pomodoro / stopwatch, transfers and downloads, agent states
  (working · needs you · done) with green/orange borders and a chime, and short system notices (charging, Bluetooth,
  internet, focus, clipboard, CPU/memory, calendar, mic/camera dot). Windows notifications on the isle (optional).
- Drop files on the pill to hand them to a command — folders are zipped first — or attach them to the prompt.
- Ten UI languages, light and dark themes, native mode that hides the real search box from inside Explorer.

### Licence
- MIT License: use, change, build on and share freely; keep the copyright notice.

### Agents
- Claude Code and **Codex** light the pill the same way: working, needs you (orange, stays until answered or the terminal
  closes), done (green until you look). Codex uses its lifecycle hooks (Codex 0.15x+); the prompt names the session.
- Fresh installs start on **auto** permissions.
- No third-party logos ship: the Codex mark is read at runtime from the user's own Codex extension, like the Claude icon (Windows glyphs otherwise), and the test copy of
  Claude Code's docs page was replaced by a hand-written sample.
- The default work folder is pre-trusted for both Claude Code and Codex so the first question isn't stopped by a trust prompt.

### Updates
- A daily check for a newer release on GitHub; when one is out, a card on the bar (once per version) and an
  "Update now" item in the right-click menu download the new setup, check its SHA-256 and install it silently, then
  IsleBar starts again. Can be turned off in settings (the Microsoft Store build leaves updates to the Store).

### Privacy
- Opt-in crash reports (setup checkbox, unticked by default, and a settings switch): only the error type, where in IsleBar's
  code it happened, app/Windows versions and basic device info (processor, memory, language/region format), sent to Sentry's EU region. Paths and quoted text are stripped on the PC;
  no titles, files, prompts, logs, names or IP addresses. See `docs/privacy.html`.

### Installer
- Per-user Inno Setup installer (`scripts\build-installer.ps1`), bundling .NET and the Windows App SDK; optional sign-in
  start and hook setup; uninstall restores the search box, notification banners and removes only IsleBar's hooks.

### Fixed during pre-release testing (10-01)
- Quitting or self-restarting crashed with 0xC000027B (closing a window re-parented into the taskbar).
- The pill kept its old size after a display-scale change and covered the Start button.
- If attaching to the taskbar failed, the search slot stayed empty — the real search box now comes back until it works.
- Release builds bundled an old Windows App SDK that gave popups a title bar.
- "Working" lingered for 6 h after a terminal was killed; "needs you" vanished after 2 minutes while still waiting.
- A corrupt settings file was overwritten with defaults — it's now kept as `islebar.json.broken-*`.
- Popups (options, settings, results, preview, compose) are centred on the pill; ⌄ toggles the options window.
- Starting a timer while one runs asks first instead of replacing it silently.
- Titles no longer scroll under the music bars right after sending a prompt; accent colour readable on the dark pill;
  "needs you" has a word, not just a colour; settings switches are named for screen readers.
- "Claude done" / "Codex done" no longer capitalised mid-phrase ("Claude Done") — agent words now have their own string in all ten languages.
- CPU / memory overload notices show for ten seconds when a spike starts, without the orange border (orange now always
  means "an agent needs your answer"); they used to sit on the pill for as long as the load lasted. The same for "internet lost": a ten-second heads-up
  instead of an orange border for the whole outage. Orange now means only "an agent needs your answer"; a low battery gets a red border.
- Switching Windows between light and dark recolours the bar in place — it used to restart, so the pill vanished and the
  real search box flashed for a second.
- Code review round: Korean Codex prompts/folders no longer break the hook (stdin read as UTF-8); reinstalling updates
  IsleBar's existing hooks instead of keeping an older command; only Claude notifications that need an answer turn
  orange; the timer "replace?" text is cleared after <kbd>Esc</kbd>; same-name folders dropped together no longer collide;
  setup stops a running bar before replacing its files; uninstall gives back only banners still switched off.

### Fixed during pre-release testing (10-02)
- The bar could crash while showing album art (0xC000027B in WinUI): the player's thumbnail stream was closed before WinUI
  had decoded it. IsleBar now decodes from its own copy.
- Native mode stopped hiding the search box after many IsleBar restarts in one Explorer session (each run left a
  visual-tree listener behind); it now unsubscribes when it gives the box back, and retries while Explorer is still
  starting instead of giving up until the next IsleBar start.
- Level gauges removed from battery, Bluetooth battery and CPU / memory notices — the number is already in the text;
  bars now only mean progress (downloads, transfers, music).
- With no drop command set, dropping files never runs anything — it attaches them to the prompt.
- Right-clicking the idle pill opened the input box's "Paste" menu instead of IsleBar's menu (timers, pomodoro, stopwatch, close).
- Crash reports: an update no longer switches them back on after you turned them off (nor reconnects hooks you disconnected);
  turning them off now also stops the background supervisor from reporting and deletes reports still waiting to be sent;
  module and source file names are sent without their folders, so no Windows user name can travel with a report.
- Right-click menu: "All notifications" opens the Windows notification centre.
- Usage limits: when Claude Code (StopFailure hook) or Codex (its session record — Codex has no hook for this) hits the plan's
  usage limit, a red notice with the reset time stays until then; other failures (sign-in, billing, busy servers) say why the
  turn stopped; a heads-up the first time a usage window passes 90% (Claude via a status line IsleBar adds only if you have none).
- The chime respects Do Not Disturb (including Windows 11's, which the standard notification-state check doesn't report — tested on PC);
  a new setting mutes it over full-screen games and videos (off by default).
- Clicking a file search result opened nothing: the click made the bar end typing, which closed the list before the click arrived (the keyboard worked).
- Clicking the long-question preview ended typing and wiped the question; it now opens the multi-line compose box with it
  (it opens once the click is over — opened on the press, it closed again at once).
- Stopping the terminal-input watch right after starting it could lose the stop and leave its hooks running.
- A UI crash was reported twice (by the bar and again by the supervisor seeing the fail-fast exit code).
- Switching displays (laptop screen on/off) could leave a black ring around the pill until a restart: the taskbar colour
  beside it, read mid-switch, was kept. It is now read again every few seconds, and a reading that can't be the taskbar
  (dark on a light taskbar while a UAC prompt dims the screen) is ignored.
- Settings: long labels (crash reports, Claude Code / Codex connection — in Korean) ran under their switches; they now wrap.
- "Hide the real search box": attaching the Explorer module again to the same Explorer (after an update, a reinstall or a
  restart of IsleBar) crashed Explorer about 1 time in 10. The module now stays for Explorer's lifetime and a new IsleBar run
  takes it over instead of attaching again (15 forced restarts in a row: no crash).
- Stopping Claude Code with Esc (or answering a permission prompt with Esc) left "working" / "needs you" up for hours —
  Claude fires no hook then. IsleBar now spots the interruption in the session's transcript and clears it.
- Claude Code installed with npm (node.exe) was never linked to its terminal, so closing it didn't clear the pill.
- Codex: after a failed turn, the next prompt could show the old failure again; lines appended mid-read could be skipped.
- Typing could stay on (preview floating over other windows) after Alt+Tab with the mouse resting over the preview, or a
  click on a file result that couldn't open.
- Timers and pomodoros ring through Do Not Disturb and full screen again (like Windows' own alarms); only an agent's
  completion chime is held back.
- Hooks: a 10-second limit on IsleBar's Claude Code hooks; disconnecting no longer fails (and leaves Codex hooks behind) when
  Claude's settings have no hooks; a reinstall no longer reconnects hooks you disconnected in settings; a hook never shows an
  error for an unexpected input (e.g. reset times in milliseconds).
- Rare: a state written by a hook while the bar read it could be lost; quickly switching "hide the real search box" off and
  on could leave it in the wrong state.
- If the taskbar keeps refusing the bar for 30 seconds (it happened twice, with Windows' search box shown meanwhile), the bar
  restarts itself — a fresh start always attached. At most once every 5 minutes.
- Codex questions are no longer passed through cmd.exe (npm installs Codex as codex.cmd): `&` or `|` in a question could run
  another command, `%NAME%` was replaced, and only the first line of a long question arrived. IsleBar now starts Codex's own
  script with node directly. A one-word question that is a command name (`update`, `logout` …) stays a question; with Codex's
  "Resume" picker the question is no longer taken for a session id.
- A folder you picked is no longer marked trusted for the agent automatically — only IsleBar's own folder is.
- If the agent can't be started, your question stays (it used to be replaced by the error).
- Copied API keys, tokens, long passwords, one-time codes, card numbers and `KEY=value` lines are hidden on the pill.
- Windows notifications aren't shown on the bar during Do Not Disturb or while presenting.
- Search set to an icon (or icon + label): the pill no longer covers the Start button — it moves to the free space on the left
  (after the Widgets button), and the real search icon stays. Hidden pill: typing with the hotkey and message cards appear
  above the middle of the taskbar instead of the top-left corner.
- Settings switched in an open settings window are saved even after asking something from the bar.
- Crashes fixed: a huge timer ("99999999999m"), a settings file with a key written twice, some album art, a rare one when
  turning Windows notifications off; the music and taskbar-tracking loops recover from unexpected errors.
- A paused Firefox download no longer shows as finished; a pomodoro can't ring every tick when its file is locked;
  the "working" breathing animation plays again; quitting no longer waits 4 seconds.
- Settings hides "Hide the real search box" when the Explorer module isn't shipped (build-msix.ps1 -NoNativeMode, a fallback for Store certification).
