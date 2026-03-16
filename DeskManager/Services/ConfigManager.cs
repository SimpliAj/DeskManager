using System.IO;
using System.Text.Json;
using DeskManager.Models;

namespace DeskManager.Services;

public class ConfigManager
{
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskManager");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return CreateDefault();

            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (config == null) return CreateDefault();

            // Migrate old configs that had no spaces
            if (config.Spaces.Count == 0)
            {
                var space = new SpaceData { Name = "Default" };
                config.Spaces.Add(space);
                config.ActiveSpaceId = space.Id;
            }

            return config;
        }
        catch
        {
            return CreateDefault();
        }
    }

    public void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));
        }
        catch { /* silent */ }
    }

    private static AppConfig CreateDefault()
    {
        var space = new SpaceData
        {
            Name = "Default",
            Grids =
            [
                new GridData { Title = "Work",  X = 50,  Y = 50,  Width = 220, Height = 200 },
                new GridData { Title = "Games", X = 300, Y = 50,  Width = 220, Height = 200 },
            ]
        };

        return new AppConfig
        {
            Spaces = [space],
            ActiveSpaceId = space.Id,
            Theme = new ThemeConfig()
        };
    }

    public AppConfig CreateDefaultForFirstLaunch()
    {
        // Ensure config file doesn't exist yet
        if (File.Exists(ConfigPath))
            return Load();

        // Create default with 2 template grids
        var config = CreateDefault();
        Save(config);
        return config;
    }
}
