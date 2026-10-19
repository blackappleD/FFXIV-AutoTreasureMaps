using AutoTreasureMaps.Core;
using AutoTreasureMaps.Core.Cleanup;
using AutoTreasureMaps.Core.Debug;
using AutoTreasureMaps.Core.Dungeon;
using AutoTreasureMaps.Core.Gather;
using AutoTreasureMaps.Core.Game.Watchers;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Market;
using AutoTreasureMaps.Core.Stats;
using AutoTreasureMaps.Core.Supply;
using AutoTreasureMaps.Core.Tasks;
using AutoTreasureMaps.Core.Treasure;
using AutoTreasureMaps.Windows;
using AutoTreasureMaps.Windows.Shell;
using clib;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace AutoTreasureMaps;

public sealed class Plugin : IDalamudPlugin
{
    private const string GotoSubcommand = "goto";
    private const string SpotSubcommand = "spot";
    private const string DecipherSubcommand = "decipher";
    private const string DigSubcommand = "dig";
    private const string BuySubcommand = "buy";
    private const string GatherSubcommand = "gather";
    private const string DungeonSubcommand = "dungeon";
    private const string NavmeshIpcProviderMarker = "Navmesh.IPCProvider";

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    internal static Plugin Instance { get; private set; } = null!;

    internal Configuration Configuration { get; }
    internal WindowSystem WindowSystem { get; } = new("AutoTreasureMaps");
    internal RunHistory History { get; }
    internal TreasureController Controller { get; }
    internal TreasureCatalog Catalog { get; }
    internal TreasureMessageWatcher Messages { get; }

    private readonly DutyWatcher dutyWatcher;
    private readonly GmAlertWatcher gmAlertWatcher;
    private readonly PartyInviteWatcher partyInviteWatcher;
    private readonly TankbusterWatcher tankbusterWatcher;
    private readonly CleanupContextMenu cleanupContextMenu;
    private readonly AppWindow appWindow;
    private readonly CommandInfo primaryCommand;
    private readonly CommandInfo aliasCommand;

    public Plugin()
    {
        Instance = this;

        ECommonsMain.Init(PluginInterface, this);
        CLibMain.Init(PluginInterface, this, CLibModule.Automation);
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? Configuration.CreateFresh();
        if (Configuration.Migrate())
        {
            Configuration.Save();
        }

        History = new RunHistory();
        Catalog = TreasureCatalog.Build();
        Messages = new TreasureMessageWatcher(Catalog);
        Controller = new TreasureController();
        dutyWatcher = new DutyWatcher();
        gmAlertWatcher = new GmAlertWatcher();
        partyInviteWatcher = new PartyInviteWatcher();
        tankbusterWatcher = new TankbusterWatcher();
        cleanupContextMenu = new CleanupContextMenu();

        InitializeLocalization();
        Fonts.Initialize(PluginInterface.UiBuilder, PluginDirectory);
        appWindow = new AppWindow(this);
        WindowSystem.AddWindow(appWindow);

        primaryCommand = new CommandInfo(OnCommand) { HelpMessage = CommandHelp() };
        aliasCommand = new CommandInfo(OnCommand) { HelpMessage = Loc.T(L.Plugin.CommandHelpAlias) };
        CommandManager.AddHandler(AtmConstants.PrimaryCommand, primaryCommand);
        CommandManager.AddHandler(AtmConstants.AliasCommand, aliasCommand);

        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Svc.Framework.Update += OnFrameworkUpdate;
        Svc.ClientState.Login += OnLogin;
        if (Svc.ClientState.IsLoggedIn)
        {
            OnLogin();
        }
    }

    private static string PluginDirectory => PluginInterface.AssemblyLocation.DirectoryName ?? string.Empty;

    public void Dispose()
    {
        Controller.Stop();
        GatherGearsetReturn.AskBeforeUnload();
        MarketHooks.DisposeShared();
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        Svc.Framework.Update -= OnFrameworkUpdate;
        Svc.ClientState.Login -= OnLogin;
        Configuration.SaveIfPending();

        WindowSystem.RemoveAllWindows();
        appWindow.Dispose();
        Fonts.Dispose();

        CommandManager.RemoveHandler(AtmConstants.PrimaryCommand);
        CommandManager.RemoveHandler(AtmConstants.AliasCommand);

        DungeonRecorder.Instance.Dispose();
        dutyWatcher.Dispose();
        gmAlertWatcher.Dispose();
        partyInviteWatcher.Dispose();
        tankbusterWatcher.Dispose();
        cleanupContextMenu.Dispose();
        Messages.Dispose();

        CLibMain.Dispose();
        ECommonsMain.Dispose();
    }

    public void OnLanguageChanged()
    {
        primaryCommand.HelpMessage = CommandHelp();
        aliasCommand.HelpMessage = Loc.T(L.Plugin.CommandHelpAlias);
    }

    public void ToggleMainUi() => appWindow.Toggle();

    public void ToggleConfigUi() => appWindow.TogglePage(AppWindow.Page.Settings);

    public void ToggleAboutUi() => appWindow.TogglePage(AppWindow.Page.About);

    public void ToggleDependenciesUi() => appWindow.TogglePage(AppWindow.Page.Plugins);

    public void ToggleHistoryUi() => appWindow.TogglePage(AppWindow.Page.History);

    public void ToggleLogUi() => appWindow.TogglePage(AppWindow.Page.Log);

    public void ToggleChangelogUi() => appWindow.TogglePage(AppWindow.Page.Changelog);

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();
        if (trimmed.Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            ToggleConfigUi();
        }
        else if (trimmed.Equals("about", StringComparison.OrdinalIgnoreCase))
        {
            ToggleAboutUi();
        }
        else if (trimmed.Equals("deps", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("dependencies", StringComparison.OrdinalIgnoreCase))
        {
            ToggleDependenciesUi();
        }
        else if (trimmed.Equals("stats", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("history", StringComparison.OrdinalIgnoreCase))
        {
            ToggleHistoryUi();
        }
        else if (trimmed.Equals("log", StringComparison.OrdinalIgnoreCase))
        {
            ToggleLogUi();
        }
        else if (trimmed.Equals("changelog", StringComparison.OrdinalIgnoreCase))
        {
            ToggleChangelogUi();
        }
        else if (trimmed.Equals("start", StringComparison.OrdinalIgnoreCase))
        {
            Controller.Start();
        }
        else if (trimmed.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            StopFromCommand();
        }
        else if (trimmed.Equals("pause", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("resume", StringComparison.OrdinalIgnoreCase))
        {
            Controller.TogglePause();
        }
        else if (trimmed.Equals("target", StringComparison.OrdinalIgnoreCase))
        {
            TargetDumper.Dump();
        }
        else if (trimmed.Equals(SpotSubcommand, StringComparison.OrdinalIgnoreCase))
        {
            SpotActions.PrintHeld(Catalog);
        }
        else if (trimmed.Equals("flag", StringComparison.OrdinalIgnoreCase))
        {
            SpotActions.Flag(Catalog);
        }
        else if (trimmed.Equals("tiers", StringComparison.OrdinalIgnoreCase))
        {
            SpotActions.ListTiers(Catalog);
        }
        else if (trimmed.Equals(DigSubcommand, StringComparison.OrdinalIgnoreCase))
        {
            AutoMapRun.Start(this);
        }
        else if (CommandWords.StartsWith(trimmed, DungeonSubcommand))
        {
            DungeonCommand.Handle(CommandWords.After(trimmed, DungeonSubcommand), this);
        }
        else if (CommandWords.StartsWith(trimmed, GatherSubcommand))
        {
            GatherMapTask.HandleCommand(CommandWords.After(trimmed, GatherSubcommand), this);
        }
        else if (CommandWords.StartsWith(trimmed, DecipherSubcommand))
        {
            AutoDecipher.HandleCommand(CommandWords.After(trimmed, DecipherSubcommand), this);
        }
        else if (CommandWords.StartsWith(trimmed, BuySubcommand))
        {
            AutoBuyMap.HandleCommand(CommandWords.After(trimmed, BuySubcommand), this);
        }
        else if (IsGotoCommand(trimmed))
        {
            HandleGoto(trimmed[GotoSubcommand.Length..].Trim());
        }
        else
        {
            ToggleMainUi();
        }
    }

    private void StopFromCommand()
    {
        if (!Controller.Running)
        {
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Run.ChatNothingToStop)}");
            return;
        }

        Controller.Stop();
        Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Run.ChatStopped)}");
    }

    private void OnDraw()
    {
        WindowSystem.Draw();
        Configuration.FlushPendingSave();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        Controller.Tick();
        MapAllowance.Tick(Configuration);
        GatherGearsetReturn.Tick();
        LootPrices.Tick();
        LootCleanup.Tick();
    }

    private static string CommandHelp() => string.Join(" ", Loc.T(L.Plugin.CommandHelp), Loc.T(L.Gather.CommandHelp), Loc.T(L.Dungeon.CommandHelp));

    private void HandleGoto(string arguments)
    {
        if (arguments.Equals(SpotSubcommand, StringComparison.OrdinalIgnoreCase))
        {
            SpotActions.GoTo(Catalog, Controller.Running);
            return;
        }

        AutoGoto.HandleCommand(arguments, Controller.Running);
    }

    private static bool IsGotoCommand(string arguments)
        => arguments.StartsWith(GotoSubcommand, StringComparison.OrdinalIgnoreCase)
        && (arguments.Length == GotoSubcommand.Length || char.IsWhiteSpace(arguments[GotoSubcommand.Length]));

    // The navmesh plugin answers pathfind IPC on fire-and-forget tasks this plugin never gets a handle to. When one
    // faults, typically a query issued while the zone mesh is still building, the finalizer would rethrow it as noise.
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        if (eventArgs.Observed)
        {
            return;
        }

        if (!eventArgs.Exception.ToString().Contains(NavmeshIpcProviderMarker, StringComparison.Ordinal))
        {
            return;
        }

        eventArgs.SetObserved();
        RunLog.Debug($"Observed a navmesh IPC task fault: {eventArgs.Exception.GetBaseException().Message}");
    }

    private void InitializeLocalization()
    {
        var directory = Path.Combine(PluginDirectory, "Localization");
        if (string.IsNullOrEmpty(Configuration.Language))
        {
            Configuration.Language = DetectLanguage();
            Configuration.Save();
        }

        Loc.Initialize(Configuration.Language, directory);
    }

    private static string DetectLanguage()
    {
        var dalamudLanguage = PluginInterface.UiLanguage;
        if (Languages.IsKnown(dalamudLanguage))
        {
            return Languages.Resolve(dalamudLanguage).Code;
        }

        switch (Svc.ClientState.ClientLanguage)
        {
            case Dalamud.Game.ClientLanguage.German: return Languages.German.Code;
            case Dalamud.Game.ClientLanguage.French: return Languages.French.Code;
            case Dalamud.Game.ClientLanguage.Japanese: return Languages.Japanese.Code;
        }

        var osLanguage = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
        return Languages.IsKnown(osLanguage) ? Languages.Resolve(osLanguage).Code : Languages.English.Code;
    }

    private void OnLogin()
    {
        if (!Configuration.AutoShowOnLogin)
        {
            return;
        }

        appWindow.Show(AppWindow.Page.Treasure);
    }
}
