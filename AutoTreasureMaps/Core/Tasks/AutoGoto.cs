using AutoTreasureMaps.Core.External;
using AutoTreasureMaps.Core.Localization;
using AutoTreasureMaps.Core.Travel;
using AutoTreasureMaps.Core.Treasure;
using ECommons.DalamudServices;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoTreasureMaps.Core.Tasks;

internal sealed class AutoGoto(uint territoryId, Vector3 destination, float arriveWithin, bool landOnArrival) : AutoCommon
{
    private const float ArriveWithinMeters = 3f;
    // Tight enough that the landing after it comes down on the spot's own floor and not on a ledge beside it.
    internal const float DigSpotArriveWithinMeters = 2f;
    private const string StopArgument = "stop";
    private const string LandingScope = "goto-land";
    private const string Usage = AtmConstants.LogPrefix + " Usage: /atm goto <territoryId> <x> <y> <z>, /atm goto spot, or /atm goto stop.";

    private static readonly char[] ArgumentSeparators = [' ', ','];

    public static string? RefusalFor(bool runRunning)
    {
        if (clib.Services.Svc.Automation.CurrentTask is AutoGoto)
        {
            return Loc.T(L.Goto.Busy);
        }

        if (runRunning)
        {
            return Loc.T(L.Goto.StopRunFirst);
        }

        return ExternalPlugins.IsInstalled(ExternalPlugin.Vnavmesh) ? null : Loc.T(L.Goto.NeedsPlugins);
    }

    public static void HandleCommand(string arguments, bool runRunning)
    {
        var automation = clib.Services.Svc.Automation;
        if (arguments.Equals(StopArgument, StringComparison.OrdinalIgnoreCase))
        {
            if (automation.CurrentTask is not AutoGoto)
            {
                Svc.Chat.Print($"{AtmConstants.LogPrefix} No goto is running.");
                return;
            }

            automation.Stop();
            Svc.Chat.Print($"{AtmConstants.LogPrefix} Goto stopped.");
            return;
        }

        if (!TryParse(arguments, out var territoryId, out var destination))
        {
            Svc.Chat.PrintError(Usage);
            return;
        }

        if (Refused(runRunning))
        {
            return;
        }

        Svc.Chat.Print($"{AtmConstants.LogPrefix} Goto: heading to {TerritoryNames.Of(territoryId)} ({territoryId}) at {destination.X:F1}, {destination.Y:F1}, {destination.Z:F1}.");
        automation.Start(new AutoGoto(territoryId, destination, ArriveWithinMeters, landOnArrival: false));
    }

    public static void StartToSpot(in DigSpot spot, bool runRunning)
    {
        if (Refused(runRunning))
        {
            return;
        }

        var x = MapCoordinates.Format(spot.MapCoordinates.X);
        var y = MapCoordinates.Format(spot.MapCoordinates.Y);
        Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Goto.HeadingToSpot, spot.ZoneName, x, y)}");
        clib.Services.Svc.Automation.Start(new AutoGoto(spot.TerritoryId, spot.Position, DigSpotArriveWithinMeters, landOnArrival: true));
    }

    protected override async Task Execute()
    {
        var zoneName = TerritoryNames.Of(territoryId);
        Diag($"Goto: travelling to {zoneName} ({territoryId}) at {destination} within {arriveWithin:F1}m, landing on it: {landOnArrival}.");
        await HoldCombatMovementAndSettle("goto");
        try
        {
            var arrived = await TravelTo(territoryId, destination, arriveWithin);
            var landed = true;
            if (arrived && landOnArrival)
            {
                landed = await LandAndDismount(destination, LandsOnTheSpot, LandingScope);
            }

            if (CancelToken.IsCancellationRequested)
            {
                Diag("Goto: cancelled.");
                return;
            }

            if (!arrived)
            {
                Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Goto.Failed, zoneName)}");
                return;
            }

            if (!landed)
            {
                Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {Loc.T(L.Goto.LandFailed, zoneName)}");
                return;
            }

            Status = Loc.T(L.Status.Arrived);
            Diag($"Goto: arrived {Vector3.Distance(Svc.Objects.LocalPlayer?.Position ?? destination, destination):F1}m from the point.");
            Svc.Chat.Print($"{AtmConstants.LogPrefix} {Loc.T(L.Goto.Arrived, zoneName)}");
        }
        finally
        {
            ReleaseCombatMovement("goto");
        }
    }

    private static bool Refused(bool runRunning)
    {
        if (RefusalFor(runRunning) is not { } refusal)
        {
            return false;
        }

        Svc.Chat.PrintError($"{AtmConstants.LogPrefix} {refusal}");
        return true;
    }

    private static bool TryParse(string arguments, out uint territoryId, out Vector3 destination)
    {
        territoryId = 0;
        destination = default;
        var parts = arguments.Split(ArgumentSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
        {
            return false;
        }

        if (!uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out territoryId))
        {
            return false;
        }

        if (!TryParseCoordinate(parts[1], out var x) || !TryParseCoordinate(parts[2], out var y) || !TryParseCoordinate(parts[3], out var z))
        {
            return false;
        }

        destination = new Vector3(x, y, z);
        return true;
    }

    private static bool TryParseCoordinate(string text, out float value)
        => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
}
