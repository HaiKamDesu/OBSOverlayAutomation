# TournamentAutomation

Command-driven orchestration layer for OBS tournament overlays. This project uses `ObsInterface` for OBS communication and focuses on hotkeys, match state, and undo/redo.

## Quick start
1. Edit `TournamentAutomation/ConfigScript.cs` to match your OBS setup and preferred hotkeys.
2. Run the app:

```bash
DOTNET_CLI_HOME=/tmp/dotnet dotnet run --project TournamentAutomation
```

## Hotkey model
- Press `Ctrl+K` to enter hotkey mode (configurable in `ConfigScript.Build()`).
- Press a configured key within the timeout to trigger the action.

## Hotkey examples
```csharp
Bind(ConsoleKey.F1, () => new SwitchSceneCommand(config.Scenes.InMatch));

Bind(ConsoleKey.F5, "Force Refresh Overlay",
    async (ctx, ct) =>
    {
        var ok = await ctx.Overlay.ApplyMatchAsync(ctx.State.CurrentMatch, ct);
        return ok ? CommandResult.Success("Overlay refreshed.") : CommandResult.Fail("Overlay refresh failed.");
    });
```

## Player profiles
Define profiles in `ConfigScript.Build()` under `PlayerProfiles`, then bind them:
```csharp
BindKey(ConsoleKey.F6, () => new SetPlayerProfileCommand(true, "P1_Default"));
BindKey(ConsoleKey.F7, () => new SetPlayerProfileCommand(false, "P2_Default"));
BindKey(ConsoleKey.F8, () => new EditPlayerCommand(true));
BindKey(ConsoleKey.F9, () => new EditPlayerCommand(false));
```

## Match queue setup
Edit `TournamentAutomation/ConfigScript.cs` and add your matches in `SeedQueue`:
```csharp
public static void SeedQueue(TournamentState state)
{
    state.Queue.Enqueue(CreateMatch(
        roundLabel: "Pools",
        format: BestOfFormat.BO5,
        p1: CreatePlayer("PlayerOne", "TeamA", CountryId.ARG, FGCharacterId.Ragna),
        p2: CreatePlayer("PlayerTwo", "TeamB", CountryId.USA, FGCharacterId.Jin)));
}
```

## Action IDs
- `scene.inmatch`, `scene.desk`, `scene.break`, `scene.results`
- `score.p1+1`, `score.p1-1`, `score.p2+1`, `score.p2-1`
- `players.swap`, `match.reset`, `match.next`
- `undo`, `redo`

## Notes
- The console hotkey listener is a development-only fallback. Replace with a global hotkey listener for production use.
- OBS connectivity failures are logged but do not stop the hotkey loop.
