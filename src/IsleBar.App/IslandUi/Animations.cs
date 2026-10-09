namespace IsleBar.App.IslandUi;

/// <summary>
/// Windows' "Animation effects" (Settings › Accessibility › Visual effects) decides the bounce (user 10-10; it was a setting of our
/// own): on = the swap, the lifted card and the list spring a little past their place and the pills sink under a press; off = the
/// plain 0.28 s ease, no press, no jump. Read on every use, so turning it off in Windows applies at once.
/// </summary>
internal static class Animations
{
    private static Windows.UI.ViewManagement.UISettings? _ui;

    public static bool On
    {
        get
        {
            try
            {
                _ui ??= new Windows.UI.ViewManagement.UISettings();
                return _ui.AnimationsEnabled;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
            {
                return true;   // can't tell: as before
            }
        }
    }
}
