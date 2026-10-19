using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;

namespace AutoTreasureMaps.Core.Ipc;

// Both editions of the combat plugin register the same call gates, so either one answers here.
internal sealed class BossModIPC
{
    private const string SetActiveFailed = AtmConstants.LogPrefix + " BossMod SetActive failed";
    private const string ClearActiveFailed = AtmConstants.LogPrefix + " BossMod ClearActive failed";
    private const string GetActiveFailed = AtmConstants.LogPrefix + " BossMod GetActive failed";
    private const string GetPresetFailed = AtmConstants.LogPrefix + " BossMod GetPreset failed";
    private const string AddTransientFailed = AtmConstants.LogPrefix + " BossMod AddTransientStrategy failed";
    private const string ClearTransientFailed = AtmConstants.LogPrefix + " BossMod ClearTransientStrategy failed";
    private const string CreatePresetFailed = AtmConstants.LogPrefix + " BossMod CreatePreset failed";
    private const string PauseMovementFailed = AtmConstants.LogPrefix + " BossMod AI.PauseMovement failed";
    private const string IsNavigatingFailed = AtmConstants.LogPrefix + " BossMod AI.IsNavigating failed";
    private const string IsMovingFailed = AtmConstants.LogPrefix + " BossMod Movement.IsMoving failed";
    private const string ConfigurationFailed = AtmConstants.LogPrefix + " BossMod Configuration failed";
    private const string HasModuleFailed = AtmConstants.LogPrefix + " BossMod HasModuleByDataId failed";
    private const string MovementConfigType = "AIConfig";
    private const string MovementForbiddenField = "ForbidMovement";

    private static BossModIPC? instance;

    private readonly ICallGateSubscriber<string, bool> setActive;
    private readonly ICallGateSubscriber<bool> clearActive;
    private readonly ICallGateSubscriber<string> getActive;
    private readonly ICallGateSubscriber<string, string?> getPreset;
    private readonly ICallGateSubscriber<string, string, string, string, bool> addTransient;
    private readonly ICallGateSubscriber<string, string, string, bool> clearTransient;
    private readonly ICallGateSubscriber<string, bool, bool> createPreset;
    // The far side answers the pause with the value it stored, so the gate is a function, not an action.
    private readonly ICallGateSubscriber<bool, bool> pauseMovement;
    private readonly ICallGateSubscriber<bool> isNavigating;
    private readonly ICallGateSubscriber<bool> isMoving;
    private readonly ICallGateSubscriber<List<string>, bool, List<string>> configuration;
    private readonly ICallGateSubscriber<uint, bool> hasModuleByDataId;
    private readonly List<string> movementForbiddenQuery = [MovementConfigType, MovementForbiddenField];

    // Cached once so the fight loop's per-tick checks do not allocate a delegate on every call.
    private readonly Func<bool> clearActiveCall;
    private readonly Func<string?> getActiveCall;
    private readonly Func<bool> isNavigatingCall;
    private readonly Func<bool> isMovingCall;
    private readonly Action forbidMovementCall;
    private readonly Action allowMovementCall;

    private bool movementHeld;
    private bool movementForbiddenByUser;

    private BossModIPC()
    {
        var pluginInterface = Svc.PluginInterface;
        setActive = pluginInterface.GetIpcSubscriber<string, bool>("BossMod.Presets.SetActive");
        clearActive = pluginInterface.GetIpcSubscriber<bool>("BossMod.Presets.ClearActive");
        getActive = pluginInterface.GetIpcSubscriber<string>("BossMod.Presets.GetActive");
        getPreset = pluginInterface.GetIpcSubscriber<string, string?>("BossMod.Presets.Get");
        addTransient = pluginInterface.GetIpcSubscriber<string, string, string, string, bool>("BossMod.Presets.AddTransientStrategy");
        clearTransient = pluginInterface.GetIpcSubscriber<string, string, string, bool>("BossMod.Presets.ClearTransientStrategy");
        createPreset = pluginInterface.GetIpcSubscriber<string, bool, bool>("BossMod.Presets.Create");
        pauseMovement = pluginInterface.GetIpcSubscriber<bool, bool>("BossMod.AI.PauseMovement");
        isNavigating = pluginInterface.GetIpcSubscriber<bool>("BossMod.AI.IsNavigating");
        isMoving = pluginInterface.GetIpcSubscriber<bool>("BossMod.Movement.IsMoving");
        configuration = pluginInterface.GetIpcSubscriber<List<string>, bool, List<string>>("BossMod.Configuration");
        hasModuleByDataId = pluginInterface.GetIpcSubscriber<uint, bool>("BossMod.HasModuleByDataId");

        clearActiveCall = clearActive.InvokeFunc;
        getActiveCall = getActive.InvokeFunc;
        isNavigatingCall = isNavigating.InvokeFunc;
        isMovingCall = isMoving.InvokeFunc;
        forbidMovementCall = () => pauseMovement.InvokeFunc(true);
        allowMovementCall = () => pauseMovement.InvokeFunc(false);
    }

    public static BossModIPC Instance => instance ??= new BossModIPC();

    public bool IsAvailable => setActive.HasFunction;

    public bool CanClearTransientStrategy => clearTransient.HasFunction;

    public bool MovementHeld => movementHeld;

    public bool SetActive(string presetName)
        => IpcGate.Invoke(setActive.HasFunction, () => setActive.InvokeFunc(presetName), false, SetActiveFailed);

    public bool ClearActive()
        => IpcGate.Invoke(clearActive.HasFunction, clearActiveCall, false, ClearActiveFailed);

    public string? GetActive()
        => IpcGate.Invoke(getActive.HasFunction, getActiveCall, null, GetActiveFailed);

    public string? GetPreset(string name)
        => IpcGate.Invoke(getPreset.HasFunction, () => getPreset.InvokeFunc(name), null, GetPresetFailed);

    public bool AddTransientStrategy(string preset, string module, string track, string option)
        => IpcGate.Invoke(addTransient.HasFunction, () => addTransient.InvokeFunc(preset, module, track, option), false, AddTransientFailed);

    public bool ClearTransientStrategy(string preset, string module, string track)
        => IpcGate.Invoke(clearTransient.HasFunction, () => clearTransient.InvokeFunc(preset, module, track), false, ClearTransientFailed);

    public bool CreatePreset(string serialized, bool overwrite)
        => IpcGate.Invoke(createPreset.HasFunction, () => createPreset.InvokeFunc(serialized, overwrite), false, CreatePresetFailed);

    // The combat plugin's automatic movement keeps steering toward its last target whenever the pathfinder is idle, and
    // while the character flies it never recomputes, so a stale target survives take-off and drags the mount off every
    // path this plugin queues. Held while this plugin drives the character, released for the fights that need it, and
    // put back to the user's own setting afterwards. True when this call is the one that took the hold.
    public bool HoldMovement()
    {
        if (movementHeld || !pauseMovement.HasFunction)
        {
            return false;
        }

        movementForbiddenByUser = ReadMovementForbidden();
        IpcGate.Run(pauseMovement.HasFunction, forbidMovementCall, PauseMovementFailed);
        movementHeld = true;
        return true;
    }

    public bool ReleaseMovement()
    {
        if (!movementHeld)
        {
            return false;
        }

        movementHeld = false;
        IpcGate.Run(pauseMovement.HasFunction, movementForbiddenByUser ? forbidMovementCall : allowMovementCall, PauseMovementFailed);
        return true;
    }

    public bool IsNavigating()
        => IpcGate.Invoke(isNavigating.HasFunction, isNavigatingCall, false, IsNavigatingFailed);

    // True when the loaded edition has an encounter module that starts on an actor of this data id. The editions differ
    // in which encounters they cover, so a mechanic is only left to the combat plugin where it has the module.
    public bool HasModuleFor(uint dataId)
        => IpcGate.Invoke(hasModuleByDataId.HasFunction, () => hasModuleByDataId.InvokeFunc(dataId), false, HasModuleFailed);

    public bool IsForcingMovement()
        => IpcGate.Invoke(isMoving.HasFunction, isMovingCall, false, IsMovingFailed);

    // The console gate answers a field query with that field's current value as its only line.
    private bool ReadMovementForbidden()
    {
        var answer = IpcGate.Invoke<List<string>?>(configuration.HasFunction, () => configuration.InvokeFunc(movementForbiddenQuery, false), null, ConfigurationFailed);
        return answer is { Count: 1 } && bool.TryParse(answer[0], out var forbidden) && forbidden;
    }
}
