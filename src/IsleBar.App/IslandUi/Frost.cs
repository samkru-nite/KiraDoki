namespace IsleBar.App.IslandUi;

/// <summary>The lifted card's glass (TODO 7.9 丙④): whether to show it.</summary>
internal static class Frost
{
    /// <summary>Glass only with Windows' transparency effects on and energy saver off (it's off there in Windows' own menus too).</summary>
    public static bool Allowed()
    {
        try
        {
            return new Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled
                && Windows.System.Power.PowerManager.EnergySaverStatus != Windows.System.Power.EnergySaverStatus.On;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
