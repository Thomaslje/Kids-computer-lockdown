using System.Text.Json;

namespace KioskShared;

public enum TileType { Steam, Exe, Chrome }

/// <summary>Where the logo sits on the kids' screen.</summary>
public enum LogoCorner { BottomRight, BottomLeft, TopRight, TopLeft }

public class Tile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public TileType Type { get; set; }
    public string? SteamAppId { get; set; }
    public string? ExePath { get; set; }
    public string? Arguments { get; set; }
    public string? ImageBase64 { get; set; }
    /// <summary>Program names (without .exe, comma separated) that are closed before this tile starts. Empty = off.</summary>
    public string? CloseFirst { get; set; }

    public override string ToString() => Type switch
    {
        TileType.Steam => $"{Name}   (Steam)",
        TileType.Chrome => $"{Name}   (Chrome)",
        _ => $"{Name}   (Program)"
    };
}

/// <summary>A piece of text placed on the kiosk screen. X and Y are the top-left corner as a fraction (0..1) of the screen;
/// Size is the font size in points on a 1080 pixel high screen (scaled to the real screen height).</summary>
public class ScreenText
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public double X { get; set; } = 0.3;
    public double Y { get; set; } = 0.45;
    public double Size { get; set; } = 40;
    public string Color { get; set; } = "#3C3C50";
    public bool Bold { get; set; } = true;
    public ScreenText Clone() => (ScreenText)MemberwiseClone();
}

public class KioskConfig
{
    public int Version { get; set; }
    public string Title { get; set; } = "";
    public string? ExitPasswordHash { get; set; }
    public List<Tile> Tiles { get; set; } = new();
    public List<ScreenText> Texts { get; set; } = new();
    /// <summary>The PC checks GitHub by itself and updates the kiosk software when it is idle.</summary>
    public bool AutoUpdate { get; set; }
    /// <summary>Your own logo (PNG, base64). Empty = no logo.</summary>
    public string? LogoBase64 { get; set; }
    public LogoCorner LogoCorner { get; set; } = LogoCorner.BottomRight;
}

public record SteamGame(string AppId, string Name);
public record PingInfo(string PcName, int ConfigVersion, string Revision = "", string TeamViewerId = "");
/// <summary>Password = null or empty means "leave the account password alone".</summary>
public record KioskOptions(string? Password, bool BlockCtrlAltDel);
public record UninstallRequest(bool RemoveAccount);
public record OptionsResult(bool RestartNeeded, List<string> Notes);
/// <summary>Written by the agent, picked up by the launcher in the kids' session.</summary>
public record KioskCommand(string Id, string Cmd, long Ts, string? Path = null, string? Args = null);

public static class Json
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    public static string To<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T? From<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}

public static class Paths
{
    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KioskSuite");
    public static string ConfigFile => Path.Combine(DataDir, "config.json");
    public static string CommandFile => Path.Combine(DataDir, "command.json");
    public static string KioskUserFile => Path.Combine(DataDir, "kiosk.user");
    public static string PrivateDir => Path.Combine(DataDir, "Private");
    public static string KeyFile => Path.Combine(PrivateDir, "agent.key");
}
