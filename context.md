# OpenSnitch C# CLI Listener (Context)

This project is a high-performance, modern replacement for the official Python OpenSnitch UI. It is written in C# and targets .NET 8.

## Project Architecture

- **OpenSnitchCli (Main Project):**
  - Acts as a gRPC server that the `opensnitchd` daemon connects to.
  - Implements an internal Unix Domain Socket (UDS) proxy to bridge `/tmp/osui.sock` to the internal gRPC server (replacing `socat`).
  - Orchestrates the different UI modes.
  - `Program.cs`: Main entry point and service wiring.
  - `Services/UiService.cs`: Implements the `UI.UIBase` gRPC service defined in `ui.proto`.

- **OpenSnitchTGUI:**
  - The primary interactive UI built using **Terminal.Gui (v2)**.
  - `TGuiManager.cs`: Contains the core UI logic, including the multi-tab interface, DataTable-backed grids, filtering logic, and custom color schemes.
  - Supports real-time filtering, sorting, rule editing, and deletion.

- **OpenSnitchTUI:**
  - Contains shared logic and the legacy/streaming UI mode using **Spectre.Console**.
  - `DnsManager.cs`: Handles background DNS resolution (DoH).
  - `UserManager.cs`: Resolves UIDs to local usernames.

- **Protos:**
  - `ui.proto`: The gRPC service definition for communication with the OpenSnitch daemon.

## Tech Stack
- **Framework:** .NET 8
- **UI:** Terminal.Gui 2.0 (develop branch), Spectre.Console
- **Communication:** gRPC (Google.Protobuf, Grpc.Core), System.Net.Sockets (UDS)

## Development Patterns
- **Events:** The `UiService` exposes events (`OnMessageReceived`, `OnRulesReceived`, `OnDaemonConnected`) that the UI managers subscribe to.
- **Filtering/Sorting:** UI grids are filtered locally in `TGuiManager` using LINQ against the event/rule collections.
- **Thread Safety:** Most UI updates are performed via `_app.Invoke` to ensure thread safety on the main Terminal.Gui loop.

## Key Files for Agents
- `OpenSnitchCli/Program.cs`: Startup logic.
- `OpenSnitchTGUI/TGuiManager.cs`: Interactive UI and hotkey management.
- `OpenSnitchCli/Services/UiService.cs`: Protocol implementation.
- `publish.sh`: Build and packaging configuration.
