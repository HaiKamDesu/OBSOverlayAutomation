# OBSOverlayAutomation

A reusable .NET OBS interface layer for OBS Studio (OBS 28+) using **obs-websocket v5** with a native v5 websocket adapter.

## Solution structure

```text
OBSOverlayAutomation/
├─ OBSOverlayAutomation.slnx
├─ ObsInterface/
│  ├─ ObsInterface.csproj
│  ├─ IObsWebsocketAdapter.cs
│  ├─ ObsController.cs
│  ├─ ObsInterfaceOptions.cs
│  ├─ ObsWebsocketAdapter.cs
│  └─ Result.cs
├─ ObsInterface.Demo/
│  ├─ ObsInterface.Demo.csproj
│  └─ Program.cs
└─ ObsInterface.Tests/
   ├─ ObsInterface.Tests.csproj
   └─ ObsControllerTests.cs
```

## What this contains

- `ObsInterface` class library: the only component that talks to OBS.
- `ObsInterface.Demo` console app: simple proof that connection + common operations work.
- `ObsInterface.Tests`: unit tests that validate safe-mode behavior, strict-mode behavior, and settings checks using a mocked adapter.

> Tournament logic is intentionally **not** included. This repository is only for OBS interaction.

## OBS setup

1. Open OBS Studio.
2. Go to **Tools → WebSocket Server Settings**.
3. Enable WebSocket server.
4. Confirm port is `4455`.
5. Set a password.
6. Use these values in the demo through env vars:
   - `OBS_WS_URL` (default `ws://127.0.0.1:4455`)
   - `OBS_WS_PASSWORD`

## Recommended naming conventions

Use stable, explicit names so code does not break when overlays evolve.

- Scenes:
  - `Match`
  - `Intermission`
  - `Results`
- Text sources:
  - `P1 Player Name`
  - `P2 Player Name`
  - `Round Count`
- Image sources:
  - `P1 Character Portrait`
  - `P2 Character Portrait`
- Scene items:
  - `P1 Panel`
  - `P2 Panel`

## Renaming sources/scenes in OBS

- In OBS **Scenes** dock: right-click scene → **Rename**.
- In OBS **Sources** dock: right-click source → **Rename**.
- Keep names synchronized with values passed to `ObsController` methods.

## Running the demo

```bash
# PowerShell example
$env:OBS_WS_URL="ws://127.0.0.1:4455"
$env:OBS_WS_PASSWORD="your-password"
dotnet run --project ObsInterface.Demo
```

Demo operations in `Program.cs`:
- Connect via `ConnectAndWaitAsync`
- Refresh input cache
- Set text (`SetTextAsync`)
- Switch scene (`SwitchSceneAsync`)
- Toggle visibility (`SetVisibilityAsync`)
- Disconnect

## Public API highlights

`ObsController` exposes a small async API surface:
- `ConnectAndWaitAsync` (handles `ConnectAsync` void behavior using event/poll wait)
- `DisconnectAsync`
- `RefreshAsync` and `InvalidateCache`
- `GetInputExistsAsync`
- `GetInputKindAsync`
- `GetInputSettingsAsync`
- `SetInputSettingsAsync`
- `SetTextAsync`
- `SetImageFileAsync`
- `GetSceneItemIdAsync`
- `SetVisibilityAsync` (by ID or name)
- `SwitchSceneAsync`

## Safe mode vs strict mode

- **Safe mode** (`StrictMode = false`): returns `Result<T>` with structured error codes.
- **Strict mode** (`StrictMode = true`): throws on failures for fail-fast workflows.

Standard codes:
- `NotConnected`
- `NotFound`
- `TypeMismatch`
- `InvalidArgument`
- `ObsError`
- `Timeout`

## Notes about scene collection JSON

If your scene collection JSON is in this repository, use it as the source of truth for scene/source naming when wiring higher-level automation.
