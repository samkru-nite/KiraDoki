using IsleBar.App.Interop;

namespace IsleBar.App.Engine;

/// <summary>Plays the Windows notification sound (falls back to a plain beep if none is registered).</summary>
internal static class Chime
{
    /// <summary>
    /// The chime — not during Do Not Disturb, and not over a full-screen game/video when the person asked for that (10-03).
    /// The pill itself still changes; only the sound is held back.
    /// </summary>
    /// <param name="muteFullscreen">The "quiet over full screen" setting.</param>
    /// <param name="alarm">A timer or pomodoro the user set: it rings through Do Not Disturb and full screen, like Windows' own
    /// alarms — only an agent's completion is held back (review 10-03).</param>
    public static void Play(bool muteFullscreen, bool alarm = false)
    {
        if (!alarm && NativeMethods.SHQueryUserNotificationState(out var quiet) == 0
            && (quiet == NativeMethods.QUNS_QUIET_TIME
                || (muteFullscreen && quiet is NativeMethods.QUNS_BUSY or NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN
                                                 or NativeMethods.QUNS_PRESENTATION_MODE)))
        {
            TaskbarHost.Log($"chime: quiet (notification state {quiet})");
            return;
        }

        if (!alarm && NativeMethods.DoNotDisturbOn())
        {
            TaskbarHost.Log("chime: quiet (Do Not Disturb)");
            return;
        }

        if (!NativeMethods.PlaySound("Notification.Default", IntPtr.Zero, NativeMethods.SND_ASYNC | NativeMethods.SND_ALIAS | NativeMethods.SND_NODEFAULT))
        {
            NativeMethods.MessageBeep(0xFFFFFFFF);
        }
    }
}
