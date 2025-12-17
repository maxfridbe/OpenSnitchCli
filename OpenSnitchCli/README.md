# OpenSnitch C# CLI Listener

This is a C# CLI application that acts as a mock OpenSnitch UI. It listens for connections from the OpenSnitch daemon (`opensnitchd`) and displays received messages in a rich, interactive Terminal User Interface (TUI).

## Prerequisites

- **.NET 8.0 SDK** (or later)

## Features

- **Live Event Monitoring:** Displays real-time events (AskRule, Alerts) from the OpenSnitch daemon.
- **Interactive UI:** Built with `Terminal.Gui` for a responsive console experience.
- **Event Details:** Select any event row to view full details in a dedicated pane.
- **Theme Support:** Includes multiple visual themes (Base, Matrix, Red, Solarized, Monokai, Dracula, Nord).
- **Resizing:** Automatically adapts to terminal window resize.

## Setup

1.  Ensure the official OpenSnitch UI is **NOT** running, as this application needs to bind to `/tmp/osui.sock` (Unix socket) or port 50051 (TCP).
2.  Navigate to the `OpenSnitchCli` directory.

## How to Run

```bash
cd OpenSnitchCli
dotnet run
```

## Configuration

By default, the application listens on:
- **Unix Socket:** `/tmp/osui.sock`
- **TCP:** `0.0.0.0:50051`

Make sure your `opensnitchd` is configured to connect to one of these addresses (default behavior).

## Runtime Info / Controls

Once the application is running, you can interact with it using the following keys:

| Key | Action |
| :--- | :--- |
| **Arrow Keys** | Navigate up/down through the event list. |
| **q** | Quit the application. |
| **t** | Cycle through available visual themes. |
| **F1** | Show Help dialog. |

### Available Themes

You can cycle through these themes using the **'t'** key:
1.  **Base** (Default Blue/Gray)
2.  **Matrix** (High-Contrast Green/Black)
3.  **Red** (Red Alert Style)
4.  **SolarizedDark**
5.  **SolarizedLight**
6.  **Monokai**
7.  **Dracula**
8.  **Nord**

## Troubleshooting

- **Logs:** Debug logs for theme initialization (`theme_init.log`) or main loop crashes (`crash_log.txt`) may be generated in the working directory if issues occur.
- **Connection:** If no events appear, verify `opensnitchd` is running and the official UI is stopped.