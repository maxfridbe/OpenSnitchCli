# OpenSnitch C# CLI Listener

This is a C# CLI application that acts as a modern OpenSnitch UI. It listens for connections from the OpenSnitch daemon (`opensnitchd`) and displays received messages in a rich, interactive Terminal User Interface (TUI).

## Prerequisites

- **.NET 8.0 SDK** (or later)
- **socat** (used for Unix socket to TCP proxying)

## Features

- **Dual TUI Modes:** 
    - **Spectre.Console Mode (`--tui`):** A high-performance, beautiful streaming view with fixed details pane.
    - **Terminal.Gui v2 Mode (`--tui2`):** A full interactive grid with sorting, multiple themes, and rule prompting.
- **Live Event Monitoring:** Displays real-time events (AskRule, Alerts, Monitor events) from the OpenSnitch daemon.
- **Global Hotkeys:** Responsive controls that work regardless of current focus.
- **Smart Data Resolution:** 
    - **DNS Resolution:** Automatically resolves destination IPs to hostnames in the background.
    - **User Resolution:** Maps UIDs to local system usernames.
- **Rule Prompting (TGUI):** Visual dialogs to Allow/Deny new connections directly from the CLI.
- **Notification System:** System beep on new connection prompts (rate-limited to 3s).
- **Theme Support (TGUI):** Includes multiple visual themes (Base, Matrix, Red, Solarized, Monokai, Dracula, Nord).

## Setup

1.  Ensure the official OpenSnitch UI is **NOT** running, as this application needs to bind to `/tmp/osui.sock` (Unix socket) or port 50051 (TCP).
2.  Navigate to the project root or `OpenSnitchCli` directory.

## How to Run

### Mode 1: Terminal.Gui v2 (Full Interactive)
```bash
cd OpenSnitchCli
dotnet run -- --tui2
```

### Mode 2: Spectre.Console (Streaming View)
```bash
cd OpenSnitchCli
dotnet run -- --tui
```

### Mode 3: JSON Raw Output
```bash
cd OpenSnitchCli
dotnet run
```

## Configuration

By default, the application:
1. Starts a gRPC server on `0.0.0.0:50051`.
2. Automatically manages a `socat` process to proxy `/tmp/osui.sock` to the gRPC port.

## Runtime Controls

| Key | Action |
| :--- | :--- |
| **Arrow Keys** | Navigate through the event list. |
| **q** | Quit the application. |
| **t** | Cycle through visual themes (TGUI Mode). |
| **F1** | Show Help dialog (TGUI Mode). |

## Troubleshooting

- **Crash Logs:** If the application crashes, check `crash_log.txt` in the working directory for detailed stack traces.
- **Debug Logs:** TUI debug logs are temporarily created in your system temp folder and printed to console on exit if an issue is detected.
- **No Events:** Ensure `opensnitchd` is running (`sudo systemctl status opensnitch`). Verify no other process is using `/tmp/osui.sock`.
