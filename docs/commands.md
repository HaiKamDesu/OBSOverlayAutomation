# Build, Test, and Restore Commands

This file is the command reference for this repository. Commands are written for bash on Linux.

## Baseline requirements

- .NET SDK 8.x
- Network access to `https://api.nuget.org/v3/index.json` for restore

## Environment helper (recommended in constrained shells)

Some environments block writes to the default dotnet home. Use:

```bash
export DOTNET_CLI_HOME=/tmp
```

## Restore

```bash
DOTNET_CLI_HOME=/tmp dotnet restore ObsInterface/ObsInterface.csproj
DOTNET_CLI_HOME=/tmp dotnet restore ObsInterface.Demo/ObsInterface.Demo.csproj
DOTNET_CLI_HOME=/tmp dotnet restore ObsInterface.Tests/ObsInterface.Tests.csproj
```

## Build

```bash
DOTNET_CLI_HOME=/tmp dotnet build ObsInterface/ObsInterface.csproj -c Release
DOTNET_CLI_HOME=/tmp dotnet build ObsInterface.Demo/ObsInterface.Demo.csproj -c Release
```

## Test

```bash
DOTNET_CLI_HOME=/tmp dotnet test ObsInterface.Tests/ObsInterface.Tests.csproj
```

## Run the demo

```bash
export OBS_WS_URL="ws://127.0.0.1:4455"
export OBS_WS_PASSWORD="your-password"
DOTNET_CLI_HOME=/tmp dotnet run --project ObsInterface.Demo/ObsInterface.Demo.csproj
```

## Solution file note (.slnx)

The repository uses `OBSOverlayAutomation.slnx`. Some toolchains cannot load `.slnx` and will fail with an MSBuild error. If that happens, use the project-level commands above or open the solution in a toolchain that supports `.slnx`.
