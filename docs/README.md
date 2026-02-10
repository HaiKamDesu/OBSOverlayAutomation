# OBSOverlayAutomation Docs

This folder is the single entry point for agents and contributors. It captures what this repo is, how it is structured, and where to find build/test/run commands.

## Project scope (short)

OBSOverlayAutomation provides a small, reusable .NET interface layer for OBS Studio (OBS 28+) using obs-websocket v5 via `obs-websocket-dotnet`. It includes:

- `ObsInterface` class library: the only component that talks to OBS.
- `ObsInterface.Demo` console app: example usage against a live OBS instance.
- `ObsInterface.Tests`: unit tests for strict/safe mode behavior.

This repository is only for OBS interaction. Tournament logic is intentionally not included.

## Repo layout

```
OBSOverlayAutomation/
├─ OBSOverlayAutomation.slnx
├─ ObsInterface/
├─ ObsInterface.Demo/
├─ ObsInterface.Tests/
├─ README.md
└─ docs/
```

## Quick start for agents

1. Read `README.md` for user-facing overview and OBS setup basics.
2. Use `docs/commands.md` for restore/build/test/run commands.
3. If you need to validate OBS behavior, you must have OBS running with WebSocket enabled.

## Key constraints and assumptions

- .NET SDK 8.x is the baseline for building and testing in this repo.
- The demo requires OBS Studio + obs-websocket v5 configured and running.
- The solution file is `OBSOverlayAutomation.slnx` (new Visual Studio format). If your toolchain cannot load `.slnx`, use project-level commands from `docs/commands.md`.

## Where to go next

- Command reference: `docs/commands.md`
- Public API entry point: `ObsInterface/ObsController.cs`
- Tests: `ObsInterface.Tests/ObsControllerTests.cs`
