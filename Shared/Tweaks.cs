namespace KioskShared;

public record TweakInfo(string Id, string Title, string Description, bool DefaultOn);
public record TweaksRequest(List<string> Ids);

/// <summary>The tweaks the admin app can send. The agent applies them by id. Add new ones here and in TweakApplier.</summary>
public static class TweakCatalog
{
    public static readonly TweakInfo[] All =
    {
        new("power-high", "High performance power plan", "Switches to the High performance plan (creates it if missing).", true),
        new("screen-never", "Screen never turns off", "On mains and battery.", true),
        new("sleep-never", "PC never goes to sleep or hibernates", "On mains and battery.", true),
        new("fast-startup-off", "Turn off Fast Startup", "Shut down is then a real shutdown. Takes effect after a restart.", true),
        new("nic-nosleep", "Network card may not be switched off to save power", "Keeps the PC reachable from the admin app. Takes effect after a restart.", true),
        new("usb-suspend-off", "Turn off USB selective suspend", "Stops USB mice, keyboards and gamepads from dropping out.", true),
        new("nic-wol", "Turn on Wake-on-LAN on the network card", "If the card supports it. Needed for waking a PC remotely.", false),
        new("wake-timers-off", "Disable wake timers", "The PC never wakes itself up for scheduled tasks.", false),
        new("cpu-min-100", "Processor minimum speed 100%", "No clocking down. Uses more power and runs warmer; not for laptops.", false),
    };

    public static List<string> Defaults => All.Where(t => t.DefaultOn).Select(t => t.Id).ToList();
}
