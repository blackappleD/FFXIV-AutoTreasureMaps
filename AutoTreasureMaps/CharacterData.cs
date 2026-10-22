using AutoTreasureMaps.Core;
using Newtonsoft.Json;
using System.Globalization;
using System.IO;

namespace AutoTreasureMaps;

// Every character keeps its own settings, run history and map log in a folder of its own, so several game clients on
// one machine, each on its own character, never read or overwrite one another's. Until a character logs in, the files at
// the top of the plugin's folder stand in; a character's first login starts from those settings, and the first character
// to log in takes over the run history and map log kept before they were per character. Read and written only on the
// framework thread.
internal static class CharacterData
{
    private const string CharactersFolder = "characters";
    private const string ConfigFileName = "config.json";
    private static readonly string[] legacyStatsFiles = ["run-history.json", "map-log.json"];

    // Dalamud writes type names into the shared settings file; the per-character files carry none, and lists are
    // replaced on load rather than added to their defaults.
    private static readonly JsonSerializerSettings settings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Formatting = Formatting.Indented,
    };

    // The character the files belong to; 0 before one logs in.
    public static ulong ContentId { get; private set; }

    private static string Root => Plugin.PluginInterface.ConfigDirectory.FullName;

    public static string Folder
        => ContentId == 0 ? Root : Path.Combine(Root, CharactersFolder, ContentId.ToString(CultureInfo.InvariantCulture));

    public static string PathOf(string fileName) => Path.Combine(Folder, fileName);

    public static void EnsureFolder() => Directory.CreateDirectory(Folder);

    // Binds the files to the character that logged in.
    public static void Bind(ulong contentId)
    {
        ContentId = contentId;
        EnsureFolder();
        ClaimLegacyStats();
    }

    // The character's own settings, or, the first time it logs in, a copy of the shared ones, saved as its own.
    public static Configuration LoadConfig(Configuration shared)
    {
        var path = PathOf(ConfigFileName);
        Configuration? loaded = null;
        if (File.Exists(path))
        {
            try
            {
                loaded = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(path), settings);
            }
            catch (Exception exception)
            {
                RunLog.Warning(exception, $"Settings: {path} could not be read; starting from the shared settings and keeping it as {ConfigFileName}.bak.");
                TryCopy(path, path + ".bak");
            }
        }

        var fresh = loaded is null;
        var configuration = loaded ?? JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(shared, settings), settings) ?? Configuration.CreateFresh();
        configuration.OwnerContentId = ContentId;
        if (configuration.Migrate() || fresh)
        {
            configuration.Save();
        }

        return configuration;
    }

    public static void SaveConfig(Configuration configuration)
    {
        try
        {
            EnsureFolder();
            var path = PathOf(ConfigFileName);
            // Written beside the file and moved over it, so a crash mid-write cannot leave half a file behind.
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(configuration, settings));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "Settings: the character's settings could not be saved.");
        }
    }

    private static void ClaimLegacyStats()
    {
        foreach (var fileName in legacyStatsFiles)
        {
            var legacy = Path.Combine(Root, fileName);
            var own = PathOf(fileName);
            if (!File.Exists(legacy) || File.Exists(own))
            {
                continue;
            }

            try
            {
                File.Move(legacy, own);
                RunLog.Info($"Stats: {fileName}, kept before stats were per character, now belongs to character {ContentId}.");
            }
            catch (Exception exception)
            {
                RunLog.Warning(exception, $"Stats: {fileName} could not be moved to the character's folder.");
            }
        }
    }

    private static void TryCopy(string from, string to)
    {
        try
        {
            File.Copy(from, to, overwrite: true);
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, $"Settings: could not keep {from}.");
        }
    }
}
