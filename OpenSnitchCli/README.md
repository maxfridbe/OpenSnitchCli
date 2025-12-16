# OpenSnitch C# CLI Listener

This is a C# CLI application that acts as a mock OpenSnitch UI. It listens for connections from the OpenSnitch daemon (`opensnitchd`) and prints received messages to the console in JSON format.

## Prerequisites

- **.NET 8.0 SDK** (or later)

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

## output

All gRPC messages received from the daemon (Ping, AskRule, Subscribe, Alerts) will be output to the console as JSON.
