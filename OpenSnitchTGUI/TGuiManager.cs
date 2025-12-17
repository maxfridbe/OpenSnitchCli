using Terminal.Gui;
using Terminal.Gui.Views;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Configuration;
using OpenSnitchTUI;
using System.Data;
using System.Text.RegularExpressions;
using System.Linq;

namespace OpenSnitchTGUI
{
    public class TGuiManager
    {
        private readonly DnsManager _dnsManager = new();
        private readonly UserManager _userManager = new();
        private TableView? _tableView;
        private TextView? _detailsView;
        private DataTable? _dt;
        private List<TuiEvent> _events = new();
        private object _lock = new object();
        private IApplication? _app;

        public void AddEvent(TuiEvent evt)
        {
            if (evt.Type == "Ping") return; // Filter pings

            lock (_lock)
            {
                _events.Insert(0, evt); // Newest first
                if (_events.Count > 100) _events.RemoveAt(_events.Count - 1);
            }

            _app?.Invoke(() =>
            {
                RefreshTable();
            });
        }

        public void Stop() { _app?.RequestStop(); }
        public void Run()
        {
            _app = Application.Create();
            _app.Init();

            var win = new Window() 
            {
                Title = "OpenSnitch CLI (Terminal.Gui)",
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(1) // Leave room for status bar
            };

            // Status Bar at Top Right (Manual Label)
            var statusLabel = new Label() 
            {
                Text = "Status: Online",
                X = Pos.Right(win) - 20, 
                Y = 0 
            };
            win.Add(statusLabel);

            // Table
            _dt = new DataTable();
            _dt.Columns.Add("Time");
            _dt.Columns.Add("Type");
            _dt.Columns.Add("Protocol");
            _dt.Columns.Add("PID");
            _dt.Columns.Add("User");
            _dt.Columns.Add("Program");
            _dt.Columns.Add("Address");
            _dt.Columns.Add("Port");

            _tableView = new TableView()
            {
                X = 0,
                Y = 1,
                Width = Dim.Fill(),
                Height = Dim.Percent(70),
                Table = new DataTableSource(_dt)
            };
            _tableView.SelectedCellChanged += (s, e) => UpdateDetails(e.NewRow);

            // Column Styles
            // 6: Address, 7: Port -> Right Aligned
            _tableView.Style.GetOrCreateColumnStyle(6).Alignment = Alignment.End;
            _tableView.Style.GetOrCreateColumnStyle(7).Alignment = Alignment.End;
            // 3: PID -> Right Aligned
            _tableView.Style.GetOrCreateColumnStyle(3).Alignment = Alignment.End;

            // Handle resizing to force Program column to expand
            win.SubViewsLaidOut += (s, e) =>
            {
                if (_tableView!.Viewport.Width <= 0) return;

                int width = _tableView.Viewport.Width;
                int scrollBarWidth = 1; 
                int fixedWidths = 0;

                // Define desired widths for fixed columns
                var colWidths = new Dictionary<string, int>
                {
                    { "Time", 10 },
                    { "Type", 10 },
                    { "Protocol", 8 },
                    { "PID", 8 },
                    { "User", 15 },
                    // Program is variable
                    { "Address", 25 }, // Adjust Address width
                    { "Port", 5 }    // Set Port width to 5
                };

                foreach (var kvp in colWidths) fixedWidths += kvp.Value;

                int programWidth = Math.Max(10, width - fixedWidths - scrollBarWidth - 5); // -5 for padding safety

                foreach (DataColumn col in _dt!.Columns)
                {
                    var style = _tableView.Style.GetOrCreateColumnStyle(col.Ordinal);
                    int w = col.ColumnName == "Program" ? programWidth : (colWidths.ContainsKey(col.ColumnName) ? colWidths[col.ColumnName] : 10);
                    
                    style.MinWidth = w;
                    style.MaxWidth = w;
                }
                
                _tableView.SetNeedsDraw();
            };

            // Details Pane
            var detailsWin = new FrameView() 
            {
                Title = "Event Details",
                X = 0,
                Y = Pos.Bottom(_tableView),
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };
            _detailsView = new TextView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ReadOnly = true
            };
            detailsWin.Add(_detailsView);

            win.Add(_tableView, detailsWin);

            // Global Key Handler for 'q' and 't'
            win.KeyDown += (s, e) =>
            {
                if (e == Key.Q)
                {
                    _app.RequestStop();
                }
                else if (e == Key.T)
                {
                    CycleTheme();
                }
            };

            // Status Bar
            var statusBar = new StatusBar(new Shortcut[] {
                new Shortcut(Key.Q, "~q~ Quit", () => _app.RequestStop()),
                new Shortcut(Key.T, "~t~ Theme", () => CycleTheme()),
                new Shortcut(Key.F1, "~F1~ Help", () => MessageBox.Query(_app, "Help", "Use Arrow Keys to Navigate\nPress 'q' to Quit\nPress 't' to Cycle Themes", "Ok"))
            });
            win.Add(statusBar);

            _app.Run(win);
            _app.Dispose();
        }

        private void CycleTheme()
        {
            if (ThemeManager.Themes == null || !ThemeManager.Themes.Any()) return;
            var themes = ThemeManager.Themes.Keys.ToList();
            var currentTheme = ThemeManager.Theme;
            var currentIndex = themes.IndexOf(currentTheme);
            var nextIndex = (currentIndex + 1) % themes.Count;
            var nextTheme = themes[nextIndex];

            ThemeManager.Theme = nextTheme;
            ConfigurationManager.Apply();
        }

        private void RefreshTable()
        {
            if (_dt == null || _tableView == null) return;

            _dt.Rows.Clear();
            lock (_lock)
            {
                foreach (var evt in _events)
                {
                    string user = "";
                    var match = Regex.Match(evt.Details ?? "", @"U(?:ID|ser):\s*(\d+)");
                    if (match.Success)
                    {
                        user = _userManager.GetUser(match.Groups[1].Value);
                    }
                    
                    var address = _dnsManager.GetDisplayName(evt.DestinationIp);

                    _dt.Rows.Add(
                        evt.Timestamp.ToString("HH:mm:ss"),
                        evt.Type,
                        evt.Protocol,
                        evt.Pid,
                        user,
                        evt.Source,
                        address,
                        evt.DestinationPort
                    );
                }
            }
            _tableView.SetNeedsDraw();
        }

        private void UpdateDetails(int row)
        {
            if (_detailsView == null || row < 0 || row >= _events.Count) return;

            TuiEvent evt;
            lock (_lock)
            {
                evt = _events[row];
            }

            var dns = _dnsManager.GetDisplayName(evt.DestinationIp);
            
            // Resolve user again for details pane
            string user = "";
            var match = Regex.Match(evt.Details ?? "", @"U(?:ID|ser):\s*(\d+)");
            if (match.Success)
            {
                user = _userManager.GetUser(match.Groups[1].Value);
            }

            var text = $"""
Timestamp:   {evt.Timestamp:yyyy-MM-dd HH:mm:ss.fff}
Type:        {evt.Type}
Protocol:    {evt.Protocol}
PID:         {evt.Pid}
User:        {user}
Program:     {evt.Source}
Destination: {evt.DestinationIp} ({dns}) : {evt.DestinationPort}
Details:     {evt.Details}
""";
            _detailsView.Text = text;
        }
    }
}
