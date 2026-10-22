using AutoTreasureMaps.Core.Changelog;
using Dalamud.Configuration;
using ECommons.Throttlers;
using Newtonsoft.Json;

namespace AutoTreasureMaps;

[Serializable]
public sealed partial class Configuration : IPluginConfiguration
{
    // Bumped with a step in Migrate whenever a saved setting changes meaning.
    public const int CurrentVersion = 1;

    [JsonIgnore]
    private bool savePending;

    // The character these settings belong to, saved in its own folder; 0 for the shared settings used before one logs in.
    [JsonIgnore]
    internal ulong OwnerContentId { get; set; }

    public int Version { get; set; }

    public bool AutoShowOnLogin { get; set; } = false;

    public bool DragWindowFromAnywhere { get; set; } = false;

    public string Language { get; set; } = "";

    // The bundled combat preset is rewritten in the combat plugin only while this trails its revision, so edits to it survive otherwise.
    public int BundledCombatPresetRevision { get; set; }

    // Never fires on a manual Stop or a fault.
    public AfterRunAction AfterRun { get; set; } = AfterRunAction.StayLoggedIn;

    // One chat command per line, sent before the after-run action under the same rules, e.g. "/ays m" for AutoRetainer.
    public string PostRunCommands { get; set; } = string.Empty;

    public bool AutoPauseInContent { get; set; } = true;

    public string LastSeenChangelogVersion { get; set; } = string.Empty;

    [JsonIgnore]
    public bool HasUnseenChangelog => !string.Equals(LastSeenChangelogVersion, ChangelogData.LatestVersion, StringComparison.Ordinal);

    public static Configuration CreateFresh() => new() { Version = CurrentVersion };

    // True when the loaded config was older and changed, so the caller saves it once.
    public bool Migrate()
    {
        if (Version >= CurrentVersion)
        {
            return false;
        }

        Version = CurrentVersion;
        return true;
    }

    public void MarkChangelogSeen()
    {
        if (!HasUnseenChangelog)
        {
            return;
        }

        LastSeenChangelogVersion = ChangelogData.LatestVersion;
        Save();
    }

    public void Save()
    {
        savePending = false;
        if (OwnerContentId != 0)
        {
            CharacterData.SaveConfig(this);
            return;
        }

        Plugin.PluginInterface.SavePluginConfig(this);
    }

    // A slider or a text box changes on every frame it is edited, so the throttle drops most calls; the change stays
    // pending, and FlushPendingSave writes the last one once the throttle window has passed.
    public void SaveDebounced()
    {
        savePending = true;
        FlushPendingSave();
    }

    public void FlushPendingSave()
    {
        if (savePending && EzThrottler.Throttle(Core.AtmConstants.ThrottleKeys.Save, Core.AtmConstants.SaveThrottleMs))
        {
            Save();
        }
    }

    public void SaveIfPending()
    {
        if (savePending)
        {
            Save();
        }
    }
}
