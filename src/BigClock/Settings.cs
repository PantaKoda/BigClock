using System.IO;
using System.Text.Json;

namespace BigClock;

public sealed class Settings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double FullWidth { get; set; } = 560;
    public double FullHeight { get; set; } = 800;
    public double CompactWidth { get; set; } = 460;
    public double CompactHeight { get; set; } = 250;
    public bool Pinned { get; set; }
    public bool Compact { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BigClock", "settings.json");

    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Settings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the window position is not worth interrupting the user on exit.
        }
    }
}
