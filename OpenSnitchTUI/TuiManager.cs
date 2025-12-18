using Spectre.Console;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace OpenSnitchTUI
{
    public class TuiManager
    {
        private readonly ConcurrentQueue<TuiEvent> _events = new();
        private DateTime _lastPingTime = DateTime.MinValue;
        private readonly DnsManager _dnsManager = new();
        private readonly UserManager _userManager = new();
        
        // Selection state
        private int _selectedIndex = 0;

        public void AddEvent(TuiEvent evt)
        {
            if (evt.Type == "Ping" || evt.Details == "Ping")
            {
                _lastPingTime = DateTime.Now;
                return; 
            }
            
            _events.Enqueue(evt);
            // Limit history
            if (_events.Count > 100)
            {
                _events.TryDequeue(out _);
            }
        }

        public async Task RunAsync(CancellationToken token, Action? onQuit = null)
        {
            // Create Layout with Footer for details
            var layout = new Layout("Root")
                .SplitRows(
                    new Layout("Header").Size(3),
                    new Layout("Content"),
                    new Layout("Footer").Size(10) // Fixed size for details pane
                );

            // Run Live Loop
            await AnsiConsole.Live(layout)
                .AutoClear(false)
                .Overflow(VerticalOverflow.Ellipsis)
                .Cropping(VerticalOverflowCropping.Bottom)
                .StartAsync(async ctx =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        // Snapshot events for stable rendering and selection
                        // Reverse so index 0 is newest (top)
                        var snapshot = _events.ToArray().Reverse().Take(20).ToList();
                        
                        // Check for Input
                        while (AnsiConsole.Console.Input.IsKeyAvailable())
                        {
                            var key = AnsiConsole.Console.Input.ReadKey(true);
                            if (key != null)
                            {
                                if (key.Value.Key == ConsoleKey.Q)
                                {
                                    onQuit?.Invoke();
                                }
                                else if (key.Value.Key == ConsoleKey.UpArrow)
                                {
                                    _selectedIndex = Math.Max(0, _selectedIndex - 1);
                                }
                                else if (key.Value.Key == ConsoleKey.DownArrow)
                                {
                                    // Limit selection to visible items
                                    _selectedIndex = Math.Min(snapshot.Count - 1, _selectedIndex + 1);
                                }
                            }
                        }
                        
                        // Clamp selection in case list shrank
                        if (snapshot.Count > 0)
                            _selectedIndex = Math.Clamp(_selectedIndex, 0, snapshot.Count - 1);
                        else 
                            _selectedIndex = 0;

                        // Update Header
                        layout["Header"].Update(CreateHeader());
                        
                        // Update Content (Table)
                        layout["Content"].Update(CreateTable(snapshot));
                        
                        // Update Footer (Details)
                        var selectedEvent = (snapshot.Count > 0 && _selectedIndex < snapshot.Count) 
                            ? snapshot[_selectedIndex] 
                            : null;
                        layout["Footer"].Update(CreateDetailsPanel(selectedEvent));
                        
                        ctx.Refresh();
                        await Task.Delay(150, token); // Reduced refresh rate to prevent flickering
                    }
                });
        }

        private Panel CreateHeader()
        {
            bool isAlive = (DateTime.Now - _lastPingTime).TotalSeconds < 5;
            var statusColor = isAlive ? "green" : "red";
            var statusIcon = isAlive ? "●" : "○";
            var statusText = isAlive ? "Online" : "No Signal";

            var grid = new Grid().Expand();
            grid.AddColumn(new GridColumn().NoWrap());
            grid.AddColumn(new GridColumn().PadLeft(2));
            grid.AddColumn(new GridColumn().RightAligned());

            grid.AddRow(
                new Text("OpenSnitch CLI", new Style(Color.Blue, decoration: Decoration.Bold)),
                new Text("v1.0", new Style(Color.Grey)),
                new Markup($"[{statusColor}]{statusIcon} {statusText}[/]")
            );

            return new Panel(grid).Expand().Border(BoxBorder.None);
        }

        private Table CreateTable(List<TuiEvent> snapshot)
        {
            var table = new Table().Expand().Border(TableBorder.Rounded);
            table.AddColumn(new TableColumn("Time").Width(8).NoWrap());
            table.AddColumn(new TableColumn("Type").Width(10));
            table.AddColumn(new TableColumn("Protocol").Width(8));
            table.AddColumn(new TableColumn("PID").Width(6));
            table.AddColumn("Program");
            table.AddColumn("Address");
            table.AddColumn(new TableColumn("Port").Width(6));
            table.AddColumn("Details");

            for (int i = 0; i < snapshot.Count; i++)
            {
                var evt = snapshot[i];
                var isSelected = (i == _selectedIndex);
                
                // Style for selected row
                var style = isSelected ? new Style(foreground: Color.Black, background: Color.White) : Style.Plain;

                // Resolve UID in details
                var details = evt.Details ?? "";
                details = Regex.Replace(details, @"U(?:ID|ser):\s*(\d+)", m => 
                {
                    var uid = m.Groups[1].Value;
                    return _userManager.GetUser(uid);
                });

                // Helper to apply style to text
                // Spectre Table AddRow accepts Renderables or strings. 
                // We must wrap strings in Text/Markup with style if selected.
                // Actually, AddRow() doesn't take a row style easily for the whole row unless we wrap each cell.
                
                table.AddRow(
                    new Text(evt.Timestamp.ToString("HH:mm:ss"), style),
                    new Text(FormatType(evt.Type) ?? "", style),
                    new Text(evt.Protocol ?? "", style),
                    new Text(evt.Pid ?? "", style),
                    new Text(evt.Source ?? "", style),
                    new Text(_dnsManager.GetDisplayName(evt.DestinationIp) ?? "", style),
                    new Text(evt.DestinationPort ?? "", style),
                    new Text(details ?? "", style)
                );
            }

            return table;
        }

        private Panel CreateDetailsPanel(TuiEvent? evt)
        {
            if (evt == null)
            {
                return new Panel(new Text("No event selected")).Expand().Border(BoxBorder.Rounded).Header("Details");
            }

            var grid = new Grid().Expand();
            grid.AddColumn(new GridColumn().NoWrap().PadRight(2));
            grid.AddColumn();

            grid.AddRow("Timestamp:", evt.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            grid.AddRow("Type:", evt.Type);
            grid.AddRow("Protocol:", evt.Protocol ?? "");
            grid.AddRow("PID:", evt.Pid ?? "");
            grid.AddRow("Program:", evt.Source ?? "");
            
            if (evt.IsFlatpak)
            {
                grid.AddRow("Origin:", "[cyan]📦 Flatpak (Sandboxed)[/]");
            }
            else if (evt.IsInNamespace)
            {
                grid.AddRow("Origin:", "[magenta]📦 Container/Namespace[/]");
            }

            var description = DescriptionManager.Instance.GetDescription(evt.Source ?? "");
            if (!string.IsNullOrEmpty(description))
            {
                grid.AddRow("About:", $"[yellow]{description}[/]");
            }
            
            var dnsName = _dnsManager.GetDisplayName(evt.DestinationIp);
            grid.AddRow("Destination:", $"{evt.DestinationIp} ({dnsName}) : {evt.DestinationPort}");
            
            var details = evt.Details ?? "";
            details = Regex.Replace(details, @"U(?:ID|ser):\s*(\d+)", m => 
            {
                var uid = m.Groups[1].Value;
                return $"{_userManager.GetUser(uid)} (UID:{uid})";
            });
            grid.AddRow("Details:", details);

            return new Panel(grid)
                .Expand()
                .Border(BoxBorder.Rounded)
                .Header("Event Details");
        }

        private string FormatType(string type)
        {
            // Note: If row is selected, we override style, so this method just returns text content 
            // effectively if we use Text(..., style). 
            // If we want to keep colors AND highlight, it's tricky.
            // For now, simpler to just lose color on selection (invert).
            return type;
        }
    }
}