using System.Reflection;
using IsleBar.Core.Configuration;
using IsleBar.Core.Localization;
using Xunit;

namespace IsleBar.Core.Tests;

public sealed class LocalizationTests
{
    /// <summary>10 languages = the same list as Slack (owner's decision, 09-29).</summary>
    private static readonly string[] Expected =
        ["en", "zh", "zht", "fr", "de", "it", "ja", "ko", "pt", "es"];

    [Fact]
    public void There_are_ten_languages_with_fixed_order_and_names()
    {
        Assert.Equal(Expected, LanguageCatalog.Languages.Select(l => l.Code));
        Assert.Equal(
            ["English", "简体中文", "繁體中文", "Français", "Deutsch", "Italiano", "日本語", "한국어", "Português", "Español"],
            LanguageCatalog.Languages.Select(l => l.NativeName));
    }

    [Fact]
    public void Every_listed_language_has_strings()
    {
        foreach (var info in LanguageCatalog.Languages)
        {
            Assert.True(LanguageCatalog.IsKnown(info.Code), $"{info.Code} 문구 없음");
        }

        Assert.Equal(Expected.Length, LanguageCatalog.Codes.Count);
    }

    /// <summary>
    /// Every language must have every key. String properties must be non-empty,
    /// and choice options must have a title and the fixed number of choices.
    /// </summary>
    [Fact]
    public void Every_language_has_every_key()
    {
        var textProps = typeof(LanguageStrings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string))
            .ToArray();

        Assert.Equal(234, textProps.Length);     // 10-12 + 26 one switch per feature (nav_files, 5 section titles, switch_* names and grey lines); 10-12 + add_folder, author, based_on, based_on_value − reset_defaults; 10-12 + 25 / − 6 the settings window rewrite (nav, cards, calendar list, Pomodoro rounds); 10-11 + card_calendar; 10-11 + card_more_files, card_remove_from_clipboard (the copy card's file list); 10-11 + 12 card_power, card_bluetooth, card_network, card_focus, card_clipboard, card_performance, card_low_battery, card_cpu_high, card_memory_high, card_open_settings, card_task_manager, card_image (the lifted system cards); 10-11 + 9 card_left, card_took, card_written, card_bytes_of, card_pages_of, card_seconds, card_hours, card_play, card_reveal (the lifted transfer cards); 10-11 + 8 print_* (the printer card); 10-11 + game_installed, game_updated (Steam); 10-11 + file_export, file_exported (Jianying); 10-11 + notice_new_message (WeChat's flash); 10-11 + notice_transit_folders, notice_transit_items (the transit list's drag of several); 10-11 + tuck, untuck (固定 → 收起; pin / unpin = 置顶); 10-11 + notice_cut_file, notice_cut_files (Ctrl+X); 10-11 + card_add_to_transit, transit_picture (the copy card's button); 10-10 + 9 file_* (Explorer's copy card: copying/moving/unzipping/zipping, done ×4, count); 10-10 + notice_transit_cannot (dropped with no file behind it); 10-10 − position_tray (the tray-side position went); 10-10 + card_clear, card_file_gone (the transit list); 10-10 + notice_transit, notice_transit_one, notice_transit_files, notice_transit_drop (the transit station); 10-10 + notice_drive_unsafe, card_drive_busy, card_drive_busy_by, card_drive_unsafe; 10-10 + notice_drive, notice_drive_removed, notice_drive_safe, notice_drive_busy, card_eject (the USB drive card); 10-10 + event_ongoing, event_tomorrow, event_ended (the calendar card); 10-10 − usage_high (the 90% warning); 10-10 + open_with (the open button's tooltip); 10-10 − on_big_pill (the list's label); 10-10 − 23 with the design switches (small pill side included) and the drop command; 10-10 − 7 with launching agents (options, folder, agent, launch failure); 10-10 − 26 with file search, the input box and the Tab order; 10-09 H1b + sort_time, yesterday, mode_ask_tip, mode_files_tip; 10-09 H1 + search_files_hint, ask_agent_hint; 10-09 甲⑤ + pin, unpin; 10-10 丙④ + transparency; 10-10 丙 − card_cancel (timer says Close); 10-10 − crash_reports, check_updates, update_available, update_menu, update_failed (nothing goes to the original author any more); 10-10 F4 − toast_card, − all_notifications; 10-09 F3 +2; 10-09: + card_browser (F2b). 10-08: + 24 lifted card (F2). 10-08: + 3 lift animation (F1).10-07: + agent_interrupted (E3½), + 4 swap animation (E4), − dismiss (E5), + swap_bounce, − web_engine · web_search, + 6 pill corners / small pill side, + close_card, + 4 card text alignment. Notice if the number of string keys drops (includes rc·agent·dismiss·web_engine·tab_order·web_search·timer + 30 system notification keys + advanced_section, 10-01; advanced_section translated in all ten 10-02; + hide_toast_banners 10-01; + replace_timer_ask, needs_answer, agent_done, crash_reports, check_updates, update_available, update_menu, update_failed, connect_agents, connect_agents_card 10-01; + all_notifications, usage_limit, resets_at, usage_high, agent_stopped, stop_login, stop_billing, stop_busy, stop_error, mute_chime_fullscreen 10-03; + toast_card, sort_name, sort_modified, sort_size, open_in_everything, file_results_count, menu_open, menu_open_folder, menu_copy_path, menu_copy_name, file_exclude_label, file_exclude_hint, reset_defaults, no_result_link, es_off_link 10-04; + browser_media 10-04; − native_searchbox, + position ×5, manual_offset, diag ×5 10-05)

        foreach (var code in Expected)
        {
            var strings = LanguageCatalog.For(code);
            foreach (var prop in textProps)
            {
                var value = (string?)prop.GetValue(strings);
                Assert.False(string.IsNullOrWhiteSpace(value), $"{code}.{prop.Name} 이(가) 비었다");
            }
        }
    }

    [Fact]
    public void Every_language_duration_string_has_placeholders()
    {
        foreach (var code in Expected)
        {
            var strings = LanguageCatalog.For(code);
            Assert.Contains("{m}", strings.MinSec);
            Assert.Contains("{s:02d}", strings.MinSec);
            Assert.Contains("{s}", strings.Sec);
        }
    }

    [Fact]
    public void Unknown_language_code_falls_back_to_english()
    {
        Assert.Same(LanguageCatalog.For("en"), LanguageCatalog.For("sv"));
        Assert.Same(LanguageCatalog.For("en"), LanguageCatalog.For(null));
        Assert.Same(LanguageCatalog.For("en"), LanguageCatalog.For("auto"));
    }

    [Fact]
    public void Finishing_string_exists_in_all_ten_languages()
    {
        // A key singled out in HANDOFF — easy to miss, so checked separately
        foreach (var code in Expected)
        {
            Assert.False(string.IsNullOrWhiteSpace(LanguageCatalog.For(code).Finishing));
        }

        Assert.Equal("마무리 중", LanguageCatalog.For("ko").Finishing);
        Assert.Equal("Finishing", LanguageCatalog.For("en").Finishing);
        Assert.Equal("仕上げ中", LanguageCatalog.For("ja").Finishing);
        Assert.Equal("收尾中", LanguageCatalog.For("zh").Finishing);
        Assert.Equal("收尾中", LanguageCatalog.For("zht").Finishing);
    }

    [Theory]
    [InlineData("ko", 0, "0초")]
    [InlineData("ko", 59, "59초")]
    [InlineData("ko", 60, "1분 00초")]
    [InlineData("ko", 125, "2분 05초")]
    [InlineData("ko", 3661, "61분 01초")]
    [InlineData("en", 5, "5s")]
    [InlineData("en", 65, "1m 05s")]
    [InlineData("ja", 90, "1分30秒")]
    [InlineData("fr", 90, "1 min 30 s")]
    [InlineData("de", 605, "10 Min. 05 s")]
    public void Duration_string_format(string code, int seconds, string expected)
        => Assert.Equal(expected, LanguageCatalog.For(code).FormatDuration(seconds));

    [Theory]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    public void Invalid_duration_becomes_zero_seconds(double seconds)
        => Assert.Equal("0초", LanguageCatalog.For("ko").FormatDuration(seconds));

    // ---------------- Automatic language selection ----------------

    [Theory]
    [InlineData(0x0412, "ko")]   // Korean
    [InlineData(0x0411, "ja")]   // Japanese
    [InlineData(0x040C, "fr")]   // French (France)
    [InlineData(0x0C0C, "fr")]   // French (Canada)
    [InlineData(0x0407, "de")]
    [InlineData(0x0410, "it")]
    [InlineData(0x0416, "pt")]   // Portuguese (Brazil)
    [InlineData(0x0816, "pt")]   // Portuguese (Portugal)
    [InlineData(0x0C0A, "es")]
    [InlineData(0x0409, "en")]   // English (US)
    [InlineData(0x0809, "en")]   // English (UK)
    [InlineData(0x041D, "en")]   // Swedish → unsupported → English
    [InlineData(0x0000, "en")]
    public void Language_from_LANGID(int langId, string expected)
        => Assert.Equal(expected, LanguageResolver.FromLangId(langId));

    [Theory]
    [InlineData(0x0404, "zht")]  // Taiwan
    [InlineData(0x0C04, "zht")]  // Hong Kong
    [InlineData(0x1404, "zht")]  // Macau
    [InlineData(0x0804, "zh")]   // Mainland China
    [InlineData(0x1004, "zh")]   // Singapore
    public void Chinese_is_zht_only_for_traditional_regions(int langId, string expected)
        => Assert.Equal(expected, LanguageResolver.FromLangId(langId));

    [Fact]
    public void Language_chosen_in_settings_wins_over_LANGID()
    {
        Assert.Equal("de", LanguageResolver.Resolve("de", 0x0412));
        Assert.Equal("ko", LanguageResolver.Resolve("auto", 0x0412));
        Assert.Equal("ko", LanguageResolver.Resolve(null, 0x0412));
        Assert.Equal("ko", LanguageResolver.Resolve("스웨덴어", 0x0412));   // unknown value → automatic
    }

    // ---------------- Fonts ----------------

    [Theory]
    [InlineData("ko", "Malgun Gothic")]
    [InlineData("ja", "Yu Gothic UI")]
    [InlineData("zh", "Microsoft YaHei UI")]
    [InlineData("zht", "Microsoft JhengHei UI")]
    [InlineData("en", "Segoe UI")]
    [InlineData("fr", "Segoe UI")]
    [InlineData("de", "Segoe UI")]
    [InlineData("it", "Segoe UI")]
    [InlineData("pt", "Segoe UI")]
    [InlineData("es", "Segoe UI")]
    public void UI_font_per_language(string code, string expected)
        => Assert.Equal(expected, UiFonts.Text(code));

    [Theory]
    [InlineData("ja", "Yu Gothic UI")]
    [InlineData("zh", "Microsoft YaHei UI")]
    [InlineData("zht", "Microsoft JhengHei UI")]
    [InlineData("ko", "Malgun Gothic")]
    [InlineData("en", "Malgun Gothic")]   // input can be in any language → a font that covers Hangul
    public void Input_box_font(string code, string expected)
        => Assert.Equal(expected, UiFonts.Entry(code));

    [Fact]
    public void Text_fonts_are_not_apple_fonts()
    {
        // no SF fonts: only Windows fonts and the bundled ones (Pretendard, the icon font)
        Assert.All(Expected, code => Assert.DoesNotContain("SF ", UiFonts.Text(code)));
    }

    [Fact]
    public void Stack_puts_primary_first_and_picks_cjk_by_script()
    {
        Assert.StartsWith("Pretendard Variable, Malgun Gothic", UiFonts.Stack("Save Your Tears", "ko"));
        Assert.StartsWith("Pretendard Variable, Yu Gothic UI", UiFonts.Stack("ただ君に晴れ", "ko"));
        Assert.StartsWith("Pretendard Variable, Microsoft YaHei UI", UiFonts.Stack("烏", "zh"));
        Assert.StartsWith("x.ttf#P, ", UiFonts.Stack("a", "ko", "x.ttf#P"));
        var fonts = UiFonts.Stack("a", "en").Split(", ");
        Assert.Equal(fonts.Length, fonts.Distinct().Count());
    }
}
