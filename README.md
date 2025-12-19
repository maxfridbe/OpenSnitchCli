# OpenSnitch C# CLI Listener

This is a C# CLI application that acts as a modern OpenSnitch UI. It listens for connections from the OpenSnitch daemon (`opensnitchd`) and displays received messages in a rich, interactive Terminal User Interface (TUI).

![Main UI](screen.png)
![Rule Prompt](prompt.png)

## Feature Parity Checklist (vs Python opensnitch-ui)

- [x] **Live Connection Monitoring:** Real-time streaming of all network activity.
- [x] **Rule Management:** Dedicated tab to list all active daemon rules.
- [x] **Interactive Prompts:** Popup dialogs for new/unknown connections.
- [x] **Custom Rule Creation:** Advanced dialog to match on Path, Comm, Host, IP, Port, or UID.
- [x] **Rule Editing:** Modify Action, Duration, and Data of existing rules on the fly.
- [x] **Rule Deletion:** Remote deletion of rules from the daemon with confirmation.
- [x] **Multi-column Sorting:** Sort by any column (s/S) with secondary "Newest First" logic.
- [x] **Process Details:** Deep inspection of PID, Path, User, and Command Line args.
- [x] **Reverse DNS Resolution:** Background lookups via Cloudflare DoH.
- [x] **User Resolution:** Automatic mapping of UIDs to local system usernames.
- [x] **Container Detection:** Visual identification (📦) of namespaced/containerized processes.
- [x] **Quick Navigation:** Jump directly from a connection event to its applying rule ('j' key).
- [x] **Theme Support:** Multiple color schemes (Dracula, Nord, Monokai, etc.).
- [x] **Notification System:** System beep on prompt to grab attention (rate-limited).
- [x] **Global Search/Filtering:** Ability to filter connection or rule lists.
- [ ] **Daemon Config:** Manage daemon-wide settings (InterceptUnknown, etc.) via UI.
- [ ] **Firewall Viewer:** Display system-level nftables/iptables chains and rules.
- [ ] **Statistics Charts:** Real-time graphing of connections and rule hits.
- [ ] **Rule Export/Import:** Backup and restore rules to/from files.

## Prerequisites

- **.NET 8.0 SDK** (or later)

## Features

- **Dual TUI Modes:** 
    - **Terminal.Gui v2 Mode (Default):** Full interactive grid with tabs, sorting, themes, and rule management.
    - **Spectre.Console Mode (`--tui`):** A high-performance, beautiful streaming view with fixed details pane.
- **Global Hotkeys:** Responsive controls ('q', '0', 's', 'j', 'e', 'd', 'c', 'r') that work regardless of current focus.
- **Smart Data Resolution:** DNS and User mappings handled automatically in the background.

## Setup

1.  Ensure the official OpenSnitch UI is **NOT** running, as this application needs to bind to `/tmp/osui.sock` (Unix socket).
2.  Navigate to the project root or `OpenSnitchCli` directory.

## How to Run

### Default: Terminal.Gui v2 (Full Interactive)
```bash
cd OpenSnitchCli
dotnet run
```

### Mode 2: Spectre.Console (Streaming View)
```bash
cd OpenSnitchCli
dotnet run -- --tui
```

### Mode 3: JSON Raw Output (Dumping Mode)
```bash
cd OpenSnitchCli
dotnet run -- --dump
```

### Help Information
```bash
cd OpenSnitchCli
dotnet run -- --help
```

## Configuration

By default, the application:
1. Starts a gRPC server on `127.0.0.1:50051`.
2. Starts an internal UDS Proxy that bridges `/tmp/osui.sock` to the TCP port, ensuring compatibility with the daemon.

## Runtime Controls

| Key | Action |
| :--- | :--- |
| **Arrow Keys** | Navigate through the lists. |
| **q** | Quit the application. |
| **f** | Focus the Filter bar (Connections: Process, Rules: Name). |
| **0** | Cycle through visual themes. |
| **s** | Cycle sorting column. |
| **S (Shift+S)** | Toggle sort direction. |
| **l** | Cycle event history limit (50 - 1000). |
| **c** | Switch to Connections tab. |
| **r** | Switch to Rules tab. |
| **j** | Jump to the rule applying to selected connection (Connections Tab). |
| **p** | Toggle full process command args in "Program" column (Connections Tab). |
| **t** | Toggle selected rule Enabled/Disabled (Rules Tab). |
| **e** | Edit the selected rule (Rules Tab). |
| **d** | Delete the selected rule (Rules Tab). |
| **?** | Show Help dialog. |

## Troubleshooting

- **Crash Logs:** If the application crashes, check `crash_log.txt` in the working directory for detailed stack traces.
- **No Events:** Ensure `opensnitchd` is running (`sudo systemctl status opensnitch`). Verify no other process is using `/tmp/osui.sock`.
