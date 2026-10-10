using IsleBar.Core.Configuration;
using IsleBar.Core.Island;
using IsleBar.Core.Localization;
using IsleBar.Core.SystemWatch;
using Xunit;

namespace IsleBar.Core.Tests;

public sealed class SystemWatchTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly LanguageStrings Ko = LanguageCatalog.For("ko");
    private static readonly LanguageStrings En = LanguageCatalog.For("en");

    // ---------------- Notice board ----------------

    [Fact]
    public void Flash_notices_expire_while_held_notices_remain()
    {
        var board = new NoticeBoard();
        board.Flash("a", SystemNotice.Make("잠깐", NoticeGlyphs.Copy, T0), TimeSpan.FromSeconds(2), T0);
        board.Hold("b", SystemNotice.Make("고정", NoticeGlyphs.Wifi, T0, urgent: true));

        Assert.Equal(2, board.Snapshot(T0.AddSeconds(1)).Count);
        var later = board.Snapshot(T0.AddSeconds(2));
        Assert.Equal(["고정"], later.Select(n => n.Title));

        board.Clear("b");
        Assert.Empty(board.Snapshot(T0.AddSeconds(3)));
    }

    [Fact]
    public void Posting_again_with_same_key_replaces()
    {
        var board = new NoticeBoard();
        board.Flash("power", SystemNotice.Make("첫째", NoticeGlyphs.Charging, T0), TimeSpan.FromSeconds(2), T0);
        board.Flash("power", SystemNotice.Make("둘째", NoticeGlyphs.Charging, T0), TimeSpan.FromSeconds(2), T0);

        var items = board.Snapshot(T0);
        Assert.Single(items);
        Assert.Equal("둘째", items[0].Title);
        Assert.Equal("system:power", items[0].SourcePath);
        Assert.True(board.Has("power", T0));
        Assert.False(board.Has("power", T0.AddSeconds(5)));
    }

    [Fact]
    public void Replace_all_at_once()
    {
        var board = new NoticeBoard();
        board.Flash("old", SystemNotice.Make("옛것", NoticeGlyphs.Calendar, T0), TimeSpan.FromMinutes(1), T0);
        board.ReplaceAll(
        [
            new("cal:1", SystemNotice.Make("하나", NoticeGlyphs.Calendar, T0)),
            new("cal:2", SystemNotice.Make("둘", NoticeGlyphs.Calendar, T0)),
        ]);

        Assert.Equal(["하나", "둘"], board.Snapshot(T0.AddHours(1)).Select(n => n.Title));
        board.ReplaceAll([]);
        Assert.Empty(board.Snapshot(T0));
    }

    [Fact]
    public void Notice_shape_is_notice_kind_and_error_when_urgent()
    {
        var calm = SystemNotice.Make("충전 중 · 78%", NoticeGlyphs.Charging, T0, done: 78, total: 100, open: "ms-settings:batterysaver");
        Assert.Equal(ActivityKind.Notice, calm.Kind);
        Assert.Equal("run", calm.State);
        Assert.Equal(0.78, calm.Fraction!.Value, 3);
        Assert.Equal(T0.ToUnixTimeSeconds(), calm.T0);
        Assert.Null(calm.Msg);

        var urgent = SystemNotice.Make("인터넷 끊김", NoticeGlyphs.NoInternet, T0, msg: " ", urgent: true);
        Assert.Equal(ActivityRunState.Error, urgent.RunState);
        Assert.Null(urgent.Msg);   // a blank message counts as none
    }

    [Theory]
    [InlineData(0, '\uEBA0')]
    [InlineData(4, '\uEBA0')]
    [InlineData(78, '\uEBA8')]
    [InlineData(100, '\uEBAA')]
    [InlineData(150, '\uEBAA')]
    public void Battery_icon_steps_by_10_percent(int percent, char glyph)
        => Assert.Equal(glyph.ToString(), NoticeGlyphs.Battery(percent));

    // ---------------- Text ----------------

    [Fact]
    public void Korean_text()
    {
        Assert.Equal("충전 중 · 78%", NoticeText.Charging(Ko, 78));
        Assert.Equal("배터리 · 76%", NoticeText.OnBattery(Ko, 76));
        Assert.Equal("배터리 20% · 약 45분", NoticeText.BatteryLow(Ko, 20, TimeSpan.FromMinutes(45)));
        Assert.Equal("배터리 10% · 약 1시간 20분", NoticeText.BatteryLow(Ko, 10, TimeSpan.FromMinutes(80.5)));
        Assert.Equal("에어팟 · 연결됨", NoticeText.Connected(Ko, "에어팟"));
        Assert.Equal("에어팟 · 연결 끊김", NoticeText.Disconnected(Ko, "에어팟"));
        Assert.Equal("복사됨 · 안녕", NoticeText.Copied(Ko, "안녕"));
        Assert.Equal("파일 복사됨", NoticeText.CopiedFiles(Ko, 1));
        Assert.Equal("파일 3개 복사됨", NoticeText.CopiedFiles(Ko, 3));
        Assert.Equal("파일 잘라냄", NoticeText.CopiedFiles(Ko, 1, cut: true));
        Assert.Equal("파일 3개 잘라냄", NoticeText.CopiedFiles(Ko, 3, cut: true));
        Assert.Equal("CPU 96% · chrome", NoticeText.Cpu(Ko, 96, "chrome"));
        Assert.Equal("CPU 96%", NoticeText.Cpu(Ko, 96, null));
        Assert.Equal("메모리 93%", NoticeText.Memory(Ko, 93));
        Assert.Equal("10분 후", NoticeText.EventSoon(Ko, 10));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.5)]            // under 1 minute
    [InlineData(60 * 24 + 1.0)]  // over a day = Windows doesn't know yet
    public void Unknown_remaining_time_shows_only_the_prefix(double? minutes)
    {
        TimeSpan? remaining = minutes is { } m ? TimeSpan.FromMinutes(m) : null;
        Assert.Equal("Battery 20%", NoticeText.BatteryLow(En, 20, remaining));
    }

    [Fact]
    public void Notice_text_in_every_language_has_placeholders()
    {
        foreach (var info in LanguageCatalog.Languages)
        {
            var s = LanguageCatalog.For(info.Code);
            Assert.Contains("{p}", s.NoticeCharging);
            Assert.Contains("{p}", s.NoticeOnBattery);
            Assert.Contains("{p}", s.NoticeBatteryLevel);
            Assert.Contains("{p}", s.NoticeCpu);
            Assert.Contains("{p}", s.NoticeMemory);
            Assert.Contains("{m}", s.NoticeAboutMin);
            Assert.Contains("{h}", s.NoticeAboutHourMin);
            Assert.Contains("{m}", s.NoticeAboutHourMin);
            Assert.Contains("{}", s.NoticeConnected);
            Assert.Contains("{}", s.NoticeDisconnected);
            Assert.Contains("{}", s.NoticeCopied);
            Assert.Contains("{n}", s.NoticeCopiedFiles);
            Assert.Contains("{n}", s.NoticeCutFiles);
            Assert.Contains("{m}", s.NoticeEventSoon);

            // No placeholders remain after filling
            Assert.DoesNotContain("{", NoticeText.BatteryLow(s, 20, TimeSpan.FromMinutes(95)));
            Assert.DoesNotContain("{", NoticeText.CopiedFiles(s, 4));
            Assert.DoesNotContain("{", NoticeText.CopiedFiles(s, 4, cut: true));
            Assert.DoesNotContain("{", NoticeText.EventSoon(s, 3));
            Assert.Contains("{t}", s.NoticeEventTomorrow);
        }
    }

    // ---------------- Power ----------------

    [Fact]
    public void Nothing_on_startup_only_plug_and_unplug_are_announced()
    {
        var logic = new PowerLogic();
        Assert.Equal(PowerChange.None, logic.Observe(new(true, false, 80)));
        Assert.Equal(PowerChange.None, logic.Observe(new(true, false, 79)));     // only the percentage changed
        Assert.Equal(PowerChange.PluggedIn, logic.Observe(new(true, true, 79)));
        Assert.Equal(PowerChange.None, logic.Observe(new(true, true, 80)));      // duplicate event
        Assert.Equal(PowerChange.Unplugged, logic.Observe(new(true, false, 80)));
    }

    [Fact]
    public void Without_battery_nothing_is_announced()
    {
        var logic = new PowerLogic();
        Assert.Equal(PowerChange.None, logic.Observe(new(false, true, 100)));
        Assert.Equal(PowerChange.None, logic.Observe(new(false, false, 100)));
        Assert.Null(PowerLogic.LowLevel(new(false, false, 5)));
    }

    [Theory]
    [InlineData(false, 25, null)]
    [InlineData(false, 21, null)]
    [InlineData(false, 20, 20)]
    [InlineData(false, 15, 20)]
    [InlineData(false, 10, 10)]
    [InlineData(false, 6, 10)]
    [InlineData(false, 5, PowerLogic.Critical)]   // 5% or less
    [InlineData(false, 4, PowerLogic.Critical)]
    [InlineData(false, 3, PowerLogic.Critical)]
    [InlineData(true, 3, null)]      // no warning while charging
    public void Low_battery_thresholds(bool plugged, int percent, int? expected)
        => Assert.Equal(expected, PowerLogic.LowLevel(new(true, plugged, percent)));

    // ---------------- Internet ----------------

    [Fact]
    public void Internet_startup_value_is_baseline_and_changes_confirm_after_3_seconds()
    {
        var logic = new ConnectivityLogic();
        logic.Report(true, T0);
        Assert.Equal(ConnectivityChange.None, logic.Tick(T0.AddSeconds(3)));   // baseline

        logic.Report(false, T0.AddSeconds(10));
        Assert.Equal(ConnectivityChange.None, logic.Tick(T0.AddSeconds(11)));
        logic.Report(true, T0.AddSeconds(12));                                  // back after 2 seconds = a blip
        Assert.Equal(ConnectivityChange.None, logic.Tick(T0.AddSeconds(16)));

        logic.Report(false, T0.AddSeconds(20));
        Assert.Equal(ConnectivityChange.Lost, logic.Tick(T0.AddSeconds(23)));
        Assert.True(logic.LostShown);
        Assert.Equal(ConnectivityChange.None, logic.Tick(T0.AddSeconds(30)));

        logic.Report(true, T0.AddSeconds(40));
        Assert.Equal(ConnectivityChange.Restored, logic.Tick(T0.AddSeconds(43)));
        Assert.False(logic.LostShown);
    }

    [Fact]
    public void Offline_at_startup_then_connecting_passes_silently()
    {
        var logic = new ConnectivityLogic();
        logic.Report(false, T0);
        Assert.Equal(ConnectivityChange.None, logic.Tick(T0.AddSeconds(3)));
        logic.Report(true, T0.AddSeconds(5));
        Assert.Equal(ConnectivityChange.None, logic.Tick(T0.AddSeconds(8)));
    }

    // ---------------- Bluetooth ----------------

    [Fact]
    public void Initial_list_is_not_announced_only_later_changes()
    {
        var tracker = new ConnectionTracker();
        Assert.Null(tracker.Update("mouse", "MX Master", true, T0));   // already connected at startup
        tracker.CompleteEnumeration();

        var connected = tracker.Update("buds", "Galaxy Buds", true, T0.AddSeconds(1));
        Assert.Equal(new ConnectionChange("buds", "Galaxy Buds", true), connected);

        Assert.Null(tracker.Update("buds", "Galaxy Buds", true, T0.AddSeconds(2)));   // unchanged
        Assert.Null(tracker.Update("buds", null, null, T0.AddSeconds(3)));             // unknown values keep the previous state

        var gone = tracker.Update("buds", null, false, T0.AddSeconds(10));
        Assert.Equal(new ConnectionChange("buds", "Galaxy Buds", false), gone);
        Assert.Equal(1, tracker.ConnectedCount);
    }

    [Fact]
    public void Classic_and_LE_twins_are_announced_once()
    {
        var tracker = new ConnectionTracker();
        tracker.CompleteEnumeration();
        Assert.NotNull(tracker.Update("classic", "WH-1000XM5", true, T0));
        Assert.Null(tracker.Update("le", "WH-1000XM5", true, T0.AddSeconds(1)));
        Assert.NotNull(tracker.Update("classic", "WH-1000XM5", true, T0.AddSeconds(1)) ?? tracker.Update("classic", null, false, T0.AddSeconds(2)));
        Assert.Null(tracker.Update("le", null, false, T0.AddSeconds(2)));
    }

    [Fact]
    public void Connected_device_removed_from_list_means_disconnected()
    {
        var tracker = new ConnectionTracker();
        tracker.CompleteEnumeration();
        tracker.Update("kb", "Keyboard", true, T0);
        Assert.Equal(new ConnectionChange("kb", "Keyboard", false), tracker.Remove("kb", T0.AddSeconds(30)));
        Assert.Null(tracker.Remove("kb", T0.AddSeconds(31)));
    }

    // ---------------- Clipboard ----------------

    [Theory]
    [InlineData("Tr0ub4dor&3", true)]
    [InlineData("hunter2Hunter", true)]          // lower, upper, digits
    [InlineData("k9$mq!zp", true)]               // lower, digits, symbols, 8 chars
    [InlineData("password", false)]              // one class only
    [InlineData("Abc12!", false)]                // fewer than 8 chars
    [InlineData("안녕하세요 반갑습니다", false)]
    [InlineData("hello world 123 ABC!", false)]  // contains spaces
    [InlineData("https://example.com/A1?b=2", false)]
    [InlineData(@"C:\Users\Me\Doc1.txt", false)]
    [InlineData("John.Doe1@example.com", false)]
    [InlineData("ghp_aB3dE5fG7hI9jK1lM3nO5pQ7rS9tU1vW3xY5", true)]   // token
    // review 10-03: these used to show on the pill
    [InlineData("sk-ant-api03-AbCdEfGhIjKlMnOpQrStUvWxYz0123456789AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-AbCdEfGh", true)]   // > 64 chars
    [InlineData("3f2a9c4e8b7d6a5f4e3d2c1b0a9f8e7d", true)]          // hex token: lowercase + digits
    [InlineData("hunter2024", true)]                                 // letters + digits, 10
    [InlineData("API_KEY=sk-abc123", true)]
    [InlineData("password: Xy9 secret phrase", true)]
    [InlineData("비밀번호: 1234abcd", true)]
    [InlineData("4111111111111111", true)]                           // card number
    [InlineData("P@ssw0rd.1", true)]                                 // not an email address
    [InlineData("C:\\Users\\me\\OneDrive - Personal\\Documents\\report2026.pdf", false)]   // a path with spaces
    [InlineData("see github.com/ds3owl/islebar/blob/main/x2.cs", false)]
    [InlineData("Kim@1990.06", true)]
    [InlineData("me.kim+news@mail.example.co.kr", false)]            // an email address
    [InlineData("postgres://admin:hunter2@db.local/app", true)]      // credentials in a URL
    [InlineData("https://github.com/ds3owl/islebar", false)]
    [InlineData("sk-proj-AbCdEf1234567890XyZ (expires 30d)", true)]   // a key followed by words
    [InlineData("Windows 11 build 26200 is out", false)]             // an ordinary sentence
    [InlineData("see C:\\Users\\me\\Downloads\\report2026.pdf now", false)]
    [InlineData("4111-1111-1111-1111", true)]
    [InlineData("4111 1111 1111 1111", true)]
    [InlineData("1791014422000", false)]                             // a millisecond timestamp (fails Luhn)
    [InlineData("Hunter2.pw", true)]                                 // not a known file extension
    [InlineData("/home/me/projects/app", false)]                     // a Unix path
    [InlineData("150000", false)]                                    // a price
    [InlineData("20261003", false)]                                  // a date
    [InlineData("IMG_20261003.jpg", false)]                          // a file name
    [InlineData("compass:north", false)]
    [InlineData("AKIAIOSFODNN7EXAMPLE", true)]                       // AWS access key
    [InlineData("\"api_key\": \"abc\"", true)]
    [InlineData("01012345678", false)]                               // a phone number stays readable
    [InlineData("2026", false)]
    [InlineData("report_final", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Looks_like_a_password(string? text, bool expected)
        => Assert.Equal(expected, ClipboardRules.LooksSecret(text));

    [Fact]
    public void Preview_collapses_to_one_line_and_masks_passwords()
    {
        Assert.Equal("첫 줄 둘째 줄", ClipboardRules.Preview("  첫 줄\r\n\t둘째 줄  "));
        Assert.Equal("abcdefghijklmnopqrstuvwx…", ClipboardRules.Preview("abcdefghijklmnopqrstuvwxyz and more"));
        Assert.Equal("••••••", ClipboardRules.Preview("Tr0ub4dor&3"));
        Assert.Equal("••••••", ClipboardRules.Preview("그냥 글", fromPasswordManager: true));
        Assert.Equal(string.Empty, ClipboardRules.Preview("   \n "));
        Assert.Equal(string.Empty, ClipboardRules.Preview(" , "));   // only punctuation: "Copied" alone (user 10-07)
        Assert.Equal("Copied", NoticeText.Copied(LanguageCatalog.For("en"), string.Empty));
        Assert.Equal("已复制", NoticeText.Copied(LanguageCatalog.For("zh"), string.Empty));
        Assert.Equal("已复制 · a,b", NoticeText.Copied(LanguageCatalog.For("zh"), ClipboardRules.Preview("a,b")));

        // Don't cut an emoji (surrogate pair) in half
        var emoji = new string('a', 23) + "😀😀";
        var preview = ClipboardRules.Preview(emoji);
        Assert.EndsWith("…", preview);
        Assert.False(char.IsHighSurrogate(preview[^2]));
    }

    [Theory]
    [InlineData("KeePass.exe", true)]
    [InlineData("KeePassXC", true)]
    [InlineData("1Password.exe", true)]
    [InlineData("Bitwarden.exe", true)]
    [InlineData("chrome.exe", false)]
    [InlineData("keyboard.exe", false)]
    [InlineData(null, false)]
    public void Detects_password_manager(string? process, bool expected)
        => Assert.Equal(expected, ClipboardRules.IsPasswordManager(process));

    [Theory]
    [InlineData("https://github.com/ds3owl/islebar", "https://github.com/ds3owl/islebar")]
    [InlineData("  http://localhost:3000/x \r\n", "http://localhost:3000/x")]
    [InlineData("HTTPS://Example.com", "HTTPS://Example.com")]
    [InlineData("www.bilibili.com/video/BV1xx", "https://www.bilibili.com/video/BV1xx")]
    [InlineData("www.", null)]
    [InlineData("bilibili.com", null)]                                    // bare domain: too easily ordinary text
    [InlineData("see https://github.com", null)]                          // inside a sentence
    [InlineData("https://a.com\nhttps://b.com", null)]                    // two lines
    [InlineData("ftp://files.example.com", null)]
    [InlineData("https://me:ghp_abcdefghijklmnop1234@github.com/x", null)] // a password in it: hidden, no button
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Opens_a_copied_address_only_when_the_copy_is_one(string? text, string? expected)
        => Assert.Equal(expected, ClipboardRules.LinkOf(text));

    [Fact]
    public void Copy_cards_with_something_to_do_stay_longer()
    {
        Assert.Equal(NoticeLengths.Read, ClipboardRules.ShowFor(linkFilesOrImage: true));
        Assert.Equal(NoticeLengths.Glance, ClipboardRules.ShowFor(linkFilesOrImage: false));
        Assert.Null(ClipboardRules.LinkOf(new string('a', 10) + "https://x.com/" + new string('b', ClipboardRules.LinkLength)));
        Assert.Null(ClipboardRules.LinkOf("https://x.com/" + new string('b', ClipboardRules.LinkLength)));
    }

    // ---------------- USB drive ----------------

    [Fact]
    public void Drive_card_is_named_by_its_label_or_the_word_for_a_usb_drive()
    {
        var zh = LanguageCatalog.For("zh");
        Assert.Equal("KINGSTON", DriveRules.Name(new PluggedDrive(@"E:\", " KINGSTON ", 1, 1), zh.NoticeDrive));
        Assert.Equal("U盘", DriveRules.Name(new PluggedDrive(@"E:\", "", 1, 1), zh.NoticeDrive));
        Assert.Equal("drive:E", DriveRules.KeyOf(new PluggedDrive(@"e:\", null, 1, 1)));
        Assert.Equal("E:", new PluggedDrive(@"e:\", null, 1, 1).Letter);
    }

    [Fact]
    public void Drive_is_called_like_file_explorer_and_counts_as_written_to_for_5_seconds()
    {
        var zh = LanguageCatalog.For("zh");
        Assert.Equal("KINGSTON (E:)", DriveRules.FullName(new PluggedDrive(@"E:\", "KINGSTON", 1, 1), zh.NoticeDrive));
        Assert.Equal("U盘 (F:)", DriveRules.FullName(new PluggedDrive(@"F:\", null, 1, 1), zh.NoticeDrive));
        Assert.Equal("(F:)", DriveRules.Letter(new PluggedDrive(@"F:\", null, 1, 1)));

        var now = DateTimeOffset.UtcNow;
        Assert.True(DriveRules.WasWriting(now.AddSeconds(-5), now));
        Assert.False(DriveRules.WasWriting(now.AddSeconds(-6), now));
        Assert.False(DriveRules.WasWriting(null, now));   // never seen moving
    }

    [Fact]
    public void Drive_diff_sees_drives_come_go_and_swap_on_one_letter()
    {
        var e = new PluggedDrive(@"E:\", "A", 100, 50, 0x1111);
        var f = new PluggedDrive(@"F:\", "B", 100, 50, 0x2222);
        var (came, went) = DriveRules.Diff([e], [e, f with { Free = 10 }]);   // free space changing isn't a new drive
        Assert.Equal([@"F:\"], came.Select(d => d.Root));
        Assert.Empty(went);

        (came, went) = DriveRules.Diff([e, f], [f]);
        Assert.Empty(came);
        Assert.Equal([@"E:\"], went.Select(d => d.Root));

        var other = new PluggedDrive(@"E:\", "A", 100, 50, 0x3333);   // another stick on E: between two looks
        (came, went) = DriveRules.Diff([e], [other]);
        Assert.Equal([other], came);
        Assert.Equal([e], went);

        // serials unknown: the label tells them apart
        (came, went) = DriveRules.Diff([e with { Serial = 0 }], [e with { Serial = 0, Label = "C" }]);
        Assert.Single(came);
        Assert.Single(went);
    }

    // ---------------- CPU / memory ----------------

    [Fact]
    public void Turns_on_after_30_seconds_sustained_and_off_after_10_seconds_low()
    {
        var rule = SustainedThreshold.Cpu();
        Assert.False(rule.Update(95, T0));
        Assert.True(rule.Elevated);
        Assert.False(rule.Update(96, T0.AddSeconds(28)));
        Assert.True(rule.Update(97, T0.AddSeconds(30)));

        Assert.True(rule.Update(70, T0.AddSeconds(32)));     // dips briefly
        Assert.True(rule.Update(80, T0.AddSeconds(36)));     // between 75 and 90 = hold, low timer resets
        Assert.True(rule.Update(70, T0.AddSeconds(38)));
        Assert.False(rule.Update(60, T0.AddSeconds(48)));
        Assert.False(rule.Elevated);
    }

    [Fact]
    public void Ignores_brief_spikes()
    {
        var rule = SustainedThreshold.Memory();
        rule.Update(95, T0);
        rule.Update(80, T0.AddSeconds(20));                   // interrupted → start over
        Assert.False(rule.Update(95, T0.AddSeconds(40)));
        Assert.False(rule.Update(95, T0.AddSeconds(60)));
        Assert.True(rule.Update(95, T0.AddSeconds(70)));
    }

    [Fact]
    public void Cpu_usage_calculation()
    {
        // Kernel time includes idle time: kernel 600 (of which idle 500) + user 400 = total 1000, busy 500
        Assert.Equal(50, CpuMath.TotalPercent(500, 600, 400), 3);
        Assert.Equal(0, CpuMath.TotalPercent(0, 0, 0));
        Assert.Equal(25, CpuMath.ProcessPercent(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2), 8), 3);
        Assert.Equal(0, CpuMath.ProcessPercent(TimeSpan.FromSeconds(1), TimeSpan.Zero, 8));
    }

    // ---------------- Microphone / camera ----------------

    [Theory]
    [InlineData(133000000000000000L, 0L, true)]
    [InlineData(133000000000000000L, 133000000000000100L, false)]
    [InlineData(0L, 0L, false)]
    [InlineData(null, null, false)]
    public void Detects_in_use(long? start, long? stop, bool expected)
        => Assert.Equal(expected, PrivacyRules.IsInUse(start, stop));

    [Fact]
    public void Builds_app_names()
    {
        Assert.Equal("Zoom", PrivacyRules.FriendlyName("C:#Program Files#Zoom#bin#Zoom.exe", packaged: false));
        Assert.Equal(@"C:\Program Files\Zoom\bin\Zoom.exe", PrivacyRules.PathFromNonPackagedKey("C:#Program Files#Zoom#bin#Zoom.exe"));
        Assert.Equal("WindowsCamera", PrivacyRules.FriendlyName("Microsoft.WindowsCamera_8wekyb3d8bbwe", packaged: true));
        Assert.Equal("WhatsAppDesktop", PrivacyRules.FriendlyName("5319275A.WhatsAppDesktop_cv1g1gvanyjgm", packaged: true));
        Assert.True(PrivacyRules.IsSelf("C:#Tools#IsleBar#IsleBar.App.exe", @"c:\tools\islebar\IsleBar.App.exe"));
        Assert.False(PrivacyRules.IsSelf("C:#Tools#IsleBar#IsleBar.App.exe", null));
    }

    // ---------------- ICS ----------------

    private static readonly TimeZoneInfo Seoul = IcsParser.ResolveZone("Asia/Seoul")!;

    private static string Calendar(params string[] events)
        => "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + string.Join("\r\n", events) + "\r\nEND:VCALENDAR\r\n";

    [Fact]
    public void Time_zone_names_accept_both_IANA_and_Windows()
    {
        Assert.NotNull(Seoul);
        Assert.Equal(TimeSpan.FromHours(9), Seoul.BaseUtcOffset);
        Assert.Equal(TimeSpan.FromHours(9), IcsParser.ResolveZone("Korea Standard Time")!.BaseUtcOffset);
        Assert.Equal(TimeSpan.FromHours(-5), IcsParser.ResolveZone("\"/mozilla.org/20050126_1/America/New_York\"")!.BaseUtcOffset);
        Assert.Null(IcsParser.ResolveZone("Nowhere/Atlantis"));
        Assert.Null(IcsParser.ResolveZone(""));
    }

    [Fact]
    public void Reads_each_time_format()
    {
        var ics = Calendar(
            "BEGIN:VEVENT", "UID:1", "SUMMARY:UTC 일정", "DTSTART:20260930T050000Z", "END:VEVENT",
            "BEGIN:VEVENT", "UID:2", "SUMMARY:서울 일정", "DTSTART;TZID=Asia/Seoul:20260930T140000", "END:VEVENT",
            "BEGIN:VEVENT", "UID:3", "SUMMARY:떠 있는 시각", "DTSTART:20260930T140000", "END:VEVENT",
            "BEGIN:VEVENT", "UID:4", "SUMMARY:종일", "DTSTART;VALUE=DATE:20260930", "END:VEVENT",
            "BEGIN:VEVENT", "UID:5", "SUMMARY:시작 없음", "END:VEVENT");

        var events = IcsParser.Parse(ics, Seoul);
        Assert.Equal(4, events.Count);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 5, 0, 0, TimeSpan.Zero), events[0].StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 5, 0, 0, TimeSpan.Zero), events[1].StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 5, 0, 0, TimeSpan.Zero), events[2].StartUtc);   // interpreted in Seoul time
        Assert.True(events[3].AllDay);
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 15, 0, 0, TimeSpan.Zero), events[3].StartUtc);
    }

    [Fact]
    public void Folded_lines_escapes_and_nested_blocks()
    {
        var ics = Calendar(
            "BEGIN:VEVENT",
            "UID:x",
            "SUMMARY:아주 긴 회의\\, 그리고",
            " 이어지는 제목\\; 끝",
            "DTSTART:20260930T050000Z",
            "BEGIN:VALARM",
            "SUMMARY:알람 제목은 무시",
            "END:VALARM",
            "END:VEVENT");

        var e = Assert.Single(IcsParser.Parse(ics, Seoul));
        Assert.Equal("아주 긴 회의, 그리고이어지는 제목; 끝", e.Summary);
    }

    [Fact]
    public void Weekly_recurrence_follows_BYDAY_and_EXDATE()
    {
        // From 2026-09-28 (Mon), Mon and Wed at 10:00 (Seoul), excluding Wed 9/30
        var ics = Calendar(
            "BEGIN:VEVENT", "UID:w", "SUMMARY:스탠드업",
            "DTSTART;TZID=Asia/Seoul:20260928T100000",
            "RRULE:FREQ=WEEKLY;BYDAY=MO,WE",
            "EXDATE;TZID=Asia/Seoul:20260930T100000",
            "END:VEVENT");

        var from = new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.FromHours(9));
        var to = from.AddDays(14);
        var starts = IcsSchedule.Occurrences(IcsParser.Parse(ics, Seoul), from, to)
            .Select(o => o.Start.ToOffset(TimeSpan.FromHours(9)).ToString("MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal(["09-28 10:00", "10-05 10:00", "10-07 10:00"], starts);
    }

    [Fact]
    public void Daily_recurrence_COUNT_and_UNTIL()
    {
        var counted = Calendar("BEGIN:VEVENT", "UID:c", "SUMMARY:c", "DTSTART:20260901T000000Z", "RRULE:FREQ=DAILY;COUNT=3", "END:VEVENT");
        var until = Calendar("BEGIN:VEVENT", "UID:u", "SUMMARY:u", "DTSTART:20260901T000000Z", "RRULE:FREQ=DAILY;INTERVAL=2;UNTIL=20260907T000000Z", "END:VEVENT");
        var from = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(90);

        Assert.Equal(3, IcsSchedule.Occurrences(IcsParser.Parse(counted, Seoul), from, to).Count);
        Assert.Equal([1, 3, 5, 7], IcsSchedule.Occurrences(IcsParser.Parse(until, Seoul), from, to).Select(o => o.Start.Day));
    }

    [Fact]
    public void Weekday_recurrence_DAILY_BYDAY()
    {
        var ics = Calendar("BEGIN:VEVENT", "UID:d", "SUMMARY:d", "DTSTART:20260928T000000Z", "RRULE:FREQ=DAILY;BYDAY=MO,TU,WE,TH,FR", "END:VEVENT");
        var from = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
        var days = IcsSchedule.Occurrences(IcsParser.Parse(ics, Seoul), from, from.AddDays(7).AddSeconds(-1)).Select(o => o.Start.DayOfWeek).ToArray();
        Assert.Equal(5, days.Length);
        Assert.DoesNotContain(DayOfWeek.Saturday, days);
    }

    [Fact]
    public void Daily_event_started_long_ago_expands_quickly()
    {
        var ics = Calendar("BEGIN:VEVENT", "UID:old", "SUMMARY:old", "DTSTART:20100101T090000Z", "RRULE:FREQ=DAILY", "END:VEVENT");
        var from = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var o = Assert.Single(IcsSchedule.Occurrences(IcsParser.Parse(ics, Seoul), from, from.AddDays(1).AddSeconds(-1)));
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.Zero), o.Start);
    }

    [Fact]
    public void Keeps_wall_clock_time_across_DST()
    {
        // New York every Monday 09:00 — stays 09:00 after DST ends on the first Sunday of November (UTC goes 13 → 14)
        var ics = Calendar("BEGIN:VEVENT", "UID:ny", "SUMMARY:ny", "DTSTART;TZID=America/New_York:20261026T090000", "RRULE:FREQ=WEEKLY", "END:VEVENT");
        var from = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
        var hours = IcsSchedule.Occurrences(IcsParser.Parse(ics, Seoul), from, from.AddDays(14)).Select(o => o.Start.UtcDateTime.Hour).ToArray();
        Assert.Equal([13, 14], hours);
    }

    [Fact]
    public void Modified_occurrence_and_cancelled_event()
    {
        var ics = Calendar(
            "BEGIN:VEVENT", "UID:r", "SUMMARY:주간회의", "DTSTART:20261001T010000Z", "RRULE:FREQ=WEEKLY;COUNT=3", "END:VEVENT",
            "BEGIN:VEVENT", "UID:r", "SUMMARY:주간회의(옮김)", "RECURRENCE-ID:20261008T010000Z", "DTSTART:20261008T050000Z", "END:VEVENT",
            "BEGIN:VEVENT", "UID:gone", "SUMMARY:취소됨", "STATUS:CANCELLED", "DTSTART:20261002T010000Z", "END:VEVENT");

        var from = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero);
        var list = IcsSchedule.Occurrences(IcsParser.Parse(ics, Seoul), from, from.AddDays(30));
        Assert.Equal(["주간회의", "주간회의(옮김)", "주간회의"], list.Select(o => o.Summary));
        Assert.Equal(5, list[1].Start.Hour);
    }

    [Fact]
    public void Unsupported_rule_yields_first_occurrence_only()
    {
        var ics = Calendar("BEGIN:VEVENT", "UID:m", "SUMMARY:m", "DTSTART:20260901T000000Z", "RRULE:FREQ=MONTHLY;BYDAY=1MO", "END:VEVENT");
        var e = Assert.Single(IcsParser.Parse(ics, Seoul));
        Assert.True(e.Rule!.Unsupported);
        var from = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Single(IcsSchedule.Occurrences([e], from, from.AddDays(365)));
    }

    [Fact]
    public void Broken_ICS_does_not_throw()
    {
        Assert.Empty(IcsParser.Parse(null));
        Assert.Empty(IcsParser.Parse("<html>로그인이 필요합니다</html>"));
        Assert.Empty(IcsParser.Parse("BEGIN:VEVENT\nDTSTART:어제\nEND:VEVENT"));
    }

    [Fact]
    public void Reads_end_length_and_reminders()
    {
        var ics = Calendar(
            "BEGIN:VEVENT", "UID:a", "SUMMARY:고등수학", "DTSTART:20261012T000000Z", "DTEND:20261012T013500Z",
            "BEGIN:VALARM", "ACTION:DISPLAY", "TRIGGER:-PT15M", "END:VALARM",
            "BEGIN:VALARM", "ACTION:DISPLAY", "TRIGGER:P0D", "END:VALARM",
            "BEGIN:VALARM", "ACTION:NONE", "TRIGGER:-PT30M", "END:VALARM",
            "BEGIN:VALARM", "ACTION:DISPLAY", "TRIGGER;RELATED=END:-PT5M", "END:VALARM",
            "BEGIN:VALARM", "ACTION:DISPLAY", "TRIGGER;VALUE=DATE-TIME:20261011T000000Z", "END:VALARM",
            "BEGIN:VALARM", "ACTION:DISPLAY", "TRIGGER:PT10M", "END:VALARM",
            "END:VEVENT",
            "BEGIN:VEVENT", "UID:b", "SUMMARY:b", "DTSTART:20261012T020000Z", "DURATION:PT1H30M", "END:VEVENT",
            "BEGIN:VEVENT", "UID:c", "SUMMARY:c", "DTSTART:20261012T040000Z", "END:VEVENT",
            "BEGIN:VEVENT", "UID:d", "SUMMARY:d", "DTSTART;VALUE=DATE:20261012", "END:VEVENT");

        var events = IcsParser.Parse(ics, Seoul, calendar: 1);
        Assert.Equal(TimeSpan.FromMinutes(95), events[0].Length);
        Assert.Equal([TimeSpan.Zero, TimeSpan.FromMinutes(15)], events[0].Alarms);   // off, after the start, absolute, from the end: left out
        Assert.Equal(TimeSpan.FromMinutes(90), events[1].Length);
        Assert.Equal(TimeSpan.Zero, events[2].Length);   // neither end nor duration: a moment
        Assert.Equal(TimeSpan.FromDays(1), events[3].Length);
        Assert.All(events, e => Assert.Equal(1, e.Calendar));

        var o = IcsSchedule.Occurrences(events, new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero), new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero))
            .Single(o => o.Summary == "고등수학");
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 1, 35, 0, TimeSpan.Zero), o.End);
        Assert.Equal(1, o.Calendar);
        Assert.Equal(2, o.Alarms!.Count);
    }

    [Theory]
    [InlineData("PT15M", 15)]
    [InlineData("-PT15M", -15)]
    [InlineData("P0D", 0)]
    [InlineData("-P1DT2H", -1560)]
    [InlineData("P1W", 10080)]
    [InlineData("+PT90S", 1.5)]
    public void Reads_durations(string text, double minutes)
        => Assert.Equal(TimeSpan.FromMinutes(minutes), IcsParser.ParseDuration(text));

    [Theory]
    [InlineData("")]
    [InlineData("15M")]
    [InlineData("PT")]
    [InlineData("P15M")]
    [InlineData("PTM")]
    [InlineData("PT15")]
    public void Rejects_broken_durations(string text) => Assert.Null(IcsParser.ParseDuration(text));

    // ---------------- Calendar card (TODO ③, user 10-10) ----------------

    private static readonly DateTimeOffset Morning = new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);

    /// <summary>An event at <paramref name="at"/> (h.mm, 8.00 = 8:00), <paramref name="minutes"/> long, with reminders that many minutes before.</summary>
    private static IcsOccurrence Ev(string name, double at, int minutes, int calendar = 0, params int[] alarms)
    {
        var start = At(at);
        return new(name, start, false, start.AddMinutes(minutes), name, calendar, [.. alarms.Select(a => TimeSpan.FromMinutes(a))]);
    }

    private static DateTimeOffset At(double hm, int seconds = 0)
    {
        var hours = (int)Math.Floor(hm + 0.0001);
        var minutes = (int)Math.Round((hm - hours) * 100);
        return Morning.Date.AddHours(hours).AddMinutes(minutes).AddSeconds(seconds);
    }

    private static string Shows(IEnumerable<IcsOccurrence> events, DateTimeOffset now, params string[] closed)
        => CalendarLogic.Look(events, now, closed.ToHashSet()) is { } look ? look.Kind + " " + look.Event.Summary : "-";

    private static string TagAt(IEnumerable<IcsOccurrence> events, DateTimeOffset now) => CalendarLogic.Look(events, now)!.Tag;

    /// <summary>Every card, main first: "Now 수업 | Start 회의".</summary>
    private static string ShowsAll(IEnumerable<IcsOccurrence> events, DateTimeOffset now, params string[] closed)
    {
        var cards = CalendarLogic.Cards(events, now, closed.ToHashSet());
        return cards.Count == 0 ? "-" : string.Join(" | ", cards.Select(c => c.Look.Kind + " " + c.Look.Event.Summary));
    }

    [Fact]
    public void A_reminder_says_minutes_within_the_hour_then_the_time()
    {
        var zh = LanguageCatalog.For("zh");
        var culture = System.Globalization.CultureInfo.GetCultureInfo("zh-CN");
        DateTimeOffset noon = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Local);   // a Monday
        string Clock(int hour) => new DateTime(2026, 10, 12, hour, 0, 0).ToString("t", culture);   // "08:00" in zh-CN

        Assert.Equal("10 分钟后", NoticeText.EventWhen(zh, noon.AddMinutes(10), noon, culture));
        Assert.Equal("60 分钟后", NoticeText.EventWhen(zh, noon.AddHours(1), noon, culture));
        Assert.Equal(Clock(14), NoticeText.EventWhen(zh, noon.AddHours(2), noon, culture));
        Assert.Equal("明天 " + Clock(8), NoticeText.EventWhen(zh, noon.AddHours(20), noon, culture));
        Assert.Equal("周三 " + Clock(8), NoticeText.EventWhen(zh, noon.AddHours(44), noon, culture));
    }

    [Fact]
    public void A_class_day_goes_reminder_start_under_way_end_gap()
    {
        IcsOccurrence[] day = [Ev("수학", 8.00, 95, 0, 5), Ev("영어", 9.45, 95)];

        Assert.Equal("-", Shows(day, At(7.54)));
        Assert.Equal("Remind 수학", Shows(day, At(7.55)));   // its own reminder, 5 min before
        Assert.Equal(At(7.55), CalendarLogic.Look(day, At(7.57))!.From);
        Assert.Equal("Start 수학", Shows(day, At(8.00)));
        Assert.True(CalendarLogic.Look(day, At(8.00))!.Debuts);
        Assert.Equal("Start 수학", Shows(day, At(8.00, 5)));
        Assert.Equal("Now 수학", Shows(day, At(8.00, 6)));   // 6 s, then in its cell
        Assert.Equal("End 수학", Shows(day, At(9.35)));
        Assert.Equal("Gap 영어", Shows(day, At(9.35, 6)));   // the same calendar's next within 25 min
        Assert.Equal(At(9.35), CalendarLogic.Look(day, At(9.40))!.From);
        Assert.Equal(5, CalendarLogic.MinutesLeft(At(9.45), At(9.40)));
        Assert.Equal(1, CalendarLogic.MinutesLeft(At(9.45), At(9.44, 50)));   // rounded up
        Assert.Equal("Start 영어", Shows(day, At(9.45)));   // no reminder: no remind look, straight to the start
        Assert.Equal("Now 영어", Shows(day, At(10.00)));
        Assert.Equal("End 영어", Shows(day, At(11.20, 3)));
        Assert.Equal("-", Shows(day, At(11.20, 6)));   // nothing next: gone
    }

    [Fact]
    public void Back_to_back_changes_each_get_their_six_seconds()
    {
        IcsOccurrence[] day = [Ev("수학", 8.00, 60), Ev("영어", 9.00, 60)];

        Assert.Equal("End 수학", Shows(day, At(9.00)));
        Assert.Equal("End 수학", Shows(day, At(9.00, 5)));
        Assert.Equal("Start 영어", Shows(day, At(9.00, 6)));
        Assert.Equal("Start 영어", Shows(day, At(9.00, 11)));
        Assert.Equal("Now 영어", Shows(day, At(9.00, 12)));

        // a change closed by hand gives its time to the next
        Assert.Equal("Start 영어", Shows(day, At(9.00, 1), "end:" + day[0].Id));
    }

    [Fact]
    public void A_gap_only_counts_within_one_calendar_and_up_to_25_minutes()
    {
        Assert.Equal("-", Shows([Ev("수업", 8.00, 60, 0), Ev("택배", 9.10, 10, 1)], At(9.00, 6)));
        Assert.Equal("-", Shows([Ev("수업", 8.00, 60), Ev("다음", 9.26, 60)], At(9.00, 6)));
        Assert.Equal("Gap 다음", Shows([Ev("수업", 8.00, 60), Ev("다음", 9.25, 60)], At(9.00, 6)));

        // closed: until the next one starts
        IcsOccurrence[] day = [Ev("수업", 8.00, 60), Ev("다음", 9.10, 60)];
        var gap = TagAt(day, At(9.05));
        Assert.Equal("-", Shows(day, At(9.05), gap));
        Assert.Equal("Start 다음", Shows(day, At(9.10), gap));
    }

    [Fact]
    public void A_reminder_at_the_start_holds_until_closed()
    {
        IcsOccurrence[] day = [Ev("시험", 8.00, 60, 0, 0)];

        Assert.Equal("-", Shows(day, At(7.59)));
        Assert.Equal("Held 시험", Shows(day, At(8.00)));
        Assert.False(CalendarLogic.Look(day, At(8.00))!.Debuts);
        Assert.Equal("Held 시험", Shows(day, At(8.30)));
        Assert.Equal("Now 시험", Shows(day, At(8.30), "held:" + day[0].Id));
        Assert.Equal("End 시험", Shows(day, At(9.00)));
    }

    [Fact]
    public void Each_reminder_comes_back_after_the_last_was_closed()
    {
        IcsOccurrence[] day = [Ev("회의", 9.00, 60, 0, 15, 5)];

        var first = TagAt(day, At(8.45));
        Assert.Equal("Remind 회의", Shows(day, At(8.45)));
        Assert.Equal("-", Shows(day, At(8.50), first));
        Assert.Equal("Remind 회의", Shows(day, At(8.55), first));
        Assert.NotEqual(first, TagAt(day, At(8.55)));
    }

    [Fact]
    public void An_event_starting_while_the_main_one_runs_gets_a_card_of_its_own()
    {
        // a short one inside a long one: its own card from start to end, the main card stays on the long one
        IcsOccurrence[] inside = [Ev("긴 일정", 8.00, 120), Ev("짧은 일정", 9.00, 30)];
        Assert.Equal("Now 긴 일정", ShowsAll(inside, At(8.59)));
        Assert.Equal("Now 긴 일정 | Start 짧은 일정", ShowsAll(inside, At(9.00)));
        Assert.Equal("Now 긴 일정 | Now 짧은 일정", ShowsAll(inside, At(9.10)));
        Assert.Equal("Now 긴 일정 | End 짧은 일정", ShowsAll(inside, At(9.30)));
        Assert.Equal("Now 긴 일정", ShowsAll(inside, At(9.30, 6)));   // its card gone
        Assert.Equal("End 긴 일정", ShowsAll(inside, At(10.00)));

        // one that ends later: the same — and once the main one ends, the main card is free (no gap: it's another card's event)
        IcsOccurrence[] later = [Ev("수업", 8.00, 120), Ev("회의", 9.00, 120)];
        Assert.Equal("Now 수업 | Start 회의", ShowsAll(later, At(9.00)));
        Assert.Equal("End 수업 | Now 회의", ShowsAll(later, At(10.00)));
        Assert.Equal("Now 회의", ShowsAll(later, At(10.00, 6)));
        Assert.Equal("End 회의", ShowsAll(later, At(11.00)));

        // two starting at once: the first in order on the main card
        IcsOccurrence[] same = [Ev("가", 9.00, 60), Ev("나", 9.00, 30)];
        Assert.Equal("Start 가 | Start 나", ShowsAll(same, At(9.00)));

        // back to back is not overlapping: one card
        Assert.Equal("End 수학", ShowsAll([Ev("수학", 8.00, 60), Ev("영어", 9.00, 60)], At(9.00)));
    }

    [Fact]
    public void An_overlapping_events_reminder_is_on_its_own_card_and_all_day_events_are_left_out()
    {
        IcsOccurrence[] day = [Ev("수업", 8.00, 120), Ev("면담", 9.30, 30, 1, 10), new("휴가", Morning.Date, true, Morning.Date.AddDays(1))];

        Assert.Equal("Now 수업", ShowsAll(day, At(9.00)));
        Assert.Equal("Now 수업 | Remind 면담", ShowsAll(day, At(9.20)));   // the class stays on its card
        var remind = CalendarLogic.Cards(day, At(9.20))[1].Look.Tag;
        Assert.Equal("Now 수업", ShowsAll(day, At(9.25), remind));
        Assert.Equal("Now 수업 | Start 면담", ShowsAll(day, At(9.30), remind));
        Assert.Equal("-", ShowsAll([new("휴가", Morning.Date, true, Morning.Date.AddDays(1))], At(9.00)));
    }

    [Fact]
    public void A_debut_stays_while_the_mouse_is_on_it_and_gets_its_whole_time_after()
    {
        var stack = new CardStack();
        static ActivityState Card(string look, TimeSpan? debut) => new()
        {
            RawKind = ActivityState.KindNotice, Title = look, Stage = SystemNotice.HoldStage, SourcePath = "system:calendar", Look = look,
            DebutFor = debut, UpdatedAt = T0,
        };

        var six = TimeSpan.FromSeconds(6);
        stack.Update([], T0);
        stack.Update([Card("now:a", null)], T0.AddSeconds(3));
        stack.Update([Card("end:a", six)], T0.AddSeconds(4));
        stack.SetHover(true, T0.AddSeconds(5));
        stack.Update([Card("end:a", six)], T0.AddSeconds(30));
        Assert.Equal("system:calendar", stack.DebutKey);   // the mouse came: it stays (it used to be dropped)
        stack.SetHover(false, T0.AddSeconds(30));
        stack.Update([Card("end:a", six)], T0.AddSeconds(35));
        Assert.Equal("system:calendar", stack.DebutKey);   // its whole 6 s again from the mouse leaving
        stack.Update([Card("end:a", six)], T0.AddSeconds(36.5));
        Assert.Null(stack.DebutKey);
    }

    [Fact]
    public void Files_dropped_on_the_pill_debut_with_the_mouse_still_there()
    {
        var stack = new CardStack();
        var ten = TimeSpan.FromSeconds(10);
        ActivityState Transit(string look, TimeSpan? debut, string? title = null) => new()
        {
            RawKind = ActivityState.KindNotice, Title = title ?? "中转站", Stage = SystemNotice.HoldStage, SourcePath = "system:transit",
            Look = look, DebutFor = debut, ByHand = debut is not null, UpdatedAt = T0,
        };
        var music = new ActivityState { RawKind = ActivityState.KindMusic, SourcePath = "m", UpdatedAt = T0 };

        stack.Update([music], T0);
        stack.SetHover(true, T0.AddSeconds(3));
        stack.Update([music], T0.AddSeconds(3));
        Assert.Equal("m", stack.Shown!.Key);
        stack.Update([music, Transit("in1", ten)], T0.AddSeconds(4));
        Assert.Equal("system:transit", stack.DebutKey);   // dropped there: up at once
        Assert.Equal("system:transit", stack.Shown!.Key);
        stack.SetHover(false, T0.AddSeconds(8));
        stack.Update([music, Transit("in1", ten)], T0.AddSeconds(17));
        Assert.Equal("system:transit", stack.Shown!.Key);   // 10 s from the mouse leaving
        stack.Update([music, Transit("in1", ten)], T0.AddSeconds(18.5));
        Assert.Null(stack.DebutKey);
    }

    [Fact]
    public void Calendar_card_new_looks_debut_their_own_length()
    {
        var stack = new CardStack();
        static ActivityState Card(string look, string stage, TimeSpan? debut) => new()
        {
            RawKind = ActivityState.KindNotice, Title = look, Stage = stage, SourcePath = "system:calendar", Look = look, DebutFor = debut,
            UpdatedAt = T0,
        };

        stack.Update([], T0);
        stack.Update([Card("now:a", SystemNotice.HoldStage, null)], T0.AddSeconds(3));
        Assert.Null(stack.DebutKey);   // under way: straight to its cell

        var six = TimeSpan.FromSeconds(6);
        stack.Update([Card("end:a", SystemNotice.HoldStage, six)], T0.AddSeconds(4));
        Assert.Equal("system:calendar", stack.DebutKey);   // same card, same type, a new look: debuts
        stack.Update([Card("end:a", SystemNotice.HoldStage, six)], T0.AddSeconds(9));
        Assert.Equal("system:calendar", stack.DebutKey);   // its own 6 s, not the usual 2
        stack.Update([Card("start:b", SystemNotice.HoldStage, six)], T0.AddSeconds(9.5));
        Assert.Equal("system:calendar", stack.DebutKey);   // the next look: its full time again
        stack.Update([Card("start:b", SystemNotice.HoldStage, six)], T0.AddSeconds(15));
        Assert.Equal("system:calendar", stack.DebutKey);
        stack.Update([Card("start:b", SystemNotice.HoldStage, six)], T0.AddSeconds(15.5));
        Assert.Null(stack.DebutKey);

        // a pinned reminder closed into an under-way look changes type but asks for no debut
        stack.Update([Card("held:c", SystemNotice.KeepStage, null)], T0.AddSeconds(20));
        Assert.Equal(CardType.Inform, stack.Shown!.Type);
        stack.Update([Card("now:c", SystemNotice.HoldStage, null)], T0.AddSeconds(21));
        Assert.Null(stack.DebutKey);

        // a look that moved on ends its debut
        stack.Update([Card("end:c", SystemNotice.HoldStage, six)], T0.AddSeconds(22));
        Assert.Equal("system:calendar", stack.DebutKey);
        stack.Update([Card("gap:d", SystemNotice.KeepStage, null)], T0.AddSeconds(23));
        Assert.Null(stack.DebutKey);
    }

    // ---------------- Settings ----------------

    [Fact]
    public void Notice_settings_default_to_all_on_and_calendar_blank()
    {
        using var dir = new TempDir();
        var settings = new ConfigStore(dir.File("islebar.json")).Load();
        Assert.True(settings.NotifyPower);
        Assert.True(settings.NotifyBluetooth);
        Assert.True(settings.NotifyNetwork);
        Assert.True(settings.NotifyFocus);
        Assert.True(settings.NotifyClipboard);
        Assert.True(settings.NotifyLoad);
        Assert.True(settings.NotifyPrivacy);
        Assert.Empty(settings.Calendars);

        var options = SystemWatchOptions.From(settings);
        Assert.Equal(new SystemWatchOptions(), options);
        Assert.False(options.Calendar);
    }

    [Fact]
    public void Notice_settings_save_and_read_back()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.File("islebar.json"));
        var settings = store.Load();
        settings.NotifyClipboard = false;
        settings.NotifyLoad = false;
        settings.Calendars = ["https://calendar.example.com/private/basic.ics", "https://calendar.example.com/private/class.ics"];
        Assert.True(store.Save(settings));

        var text = File.ReadAllText(store.Path);
        Assert.Contains("\"notify_clipboard\": false", text);
        Assert.Contains("\"calendars\"", text);

        var loaded = store.Load();
        Assert.False(loaded.NotifyClipboard);
        Assert.False(loaded.NotifyLoad);
        Assert.True(loaded.NotifyPower);
        Assert.Equal(settings.Calendars, loaded.Calendars);
        Assert.True(SystemWatchOptions.From(loaded).Calendar);
        Assert.Equal(string.Join('\n', settings.Calendars), SystemWatchOptions.From(loaded).Calendars);

        var clone = loaded.Clone();
        Assert.False(clone.NotifyClipboard);
        Assert.Equal(loaded.Calendars, clone.Calendars);
        Assert.NotSame(loaded.Calendars, clone.Calendars);
    }

    [Fact]
    public void The_one_calendar_address_of_before_becomes_the_first_of_the_list()
    {
        using var dir = new TempDir();
        var path = dir.File("islebar.json");
        File.WriteAllText(path, """{ "calendar_ics": " https://example.com/old.ics " }""");
        var store = new ConfigStore(path);
        var settings = store.Load();
        Assert.Equal(["https://example.com/old.ics"], settings.Calendars);

        Assert.True(store.Save(settings));
        var text = File.ReadAllText(path);
        Assert.DoesNotContain("calendar_ics", text);
        Assert.Equal(["https://example.com/old.ics"], store.Load().Calendars);
    }

    [Fact]
    public void Notice_settings_with_wrong_type_fall_back_to_default()
    {
        using var dir = new TempDir();
        var path = dir.File("islebar.json");
        File.WriteAllText(path, """{ "notify_power": "no", "notify_focus": false, "calendars": 5 }""");
        var settings = new ConfigStore(path).Load();
        Assert.True(settings.NotifyPower);
        Assert.False(settings.NotifyFocus);
        Assert.Empty(settings.Calendars);
    }
}
