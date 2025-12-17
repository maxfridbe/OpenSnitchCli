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
using System.Reflection;

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
        private Window? _win;

        private bool _themesInitialized = false;
        private List<string> _cycleThemes = new List<string> { "Base", "Matrix", "Red", "SolarizedDark", "SolarizedLight", "Monokai", "Dracula", "Nord" };

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

            _win = new Window() 
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
                X = Pos.Right(_win) - 20, 
                Y = 0 
            };
            _win.Add(statusLabel);

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
            _tableView.Style.GetOrCreateColumnStyle(6).Alignment = Alignment.End;
            _tableView.Style.GetOrCreateColumnStyle(7).Alignment = Alignment.End;
            _tableView.Style.GetOrCreateColumnStyle(3).Alignment = Alignment.End;

            // Handle resizing
            _win.SubViewsLaidOut += (s, e) =>
            {
                if (_tableView!.Viewport.Width <= 0) return;

                int width = _tableView.Viewport.Width;
                int scrollBarWidth = 1; 
                int fixedWidths = 0;

                var colWidths = new Dictionary<string, int>
                {
                    { "Time", 10 },
                    { "Type", 10 },
                    { "Protocol", 8 },
                    { "PID", 8 },
                    { "User", 15 },
                    { "Address", 25 }, 
                    { "Port", 5 }
                };

                foreach (var kvp in colWidths) fixedWidths += kvp.Value;

                int programWidth = Math.Max(10, width - fixedWidths - scrollBarWidth - 5);

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

            _win.Add(_tableView, detailsWin);

            // Global Key Handler
            _win.KeyDown += (s, e) =>
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
            _win.Add(statusBar);

            try 
            {
                _app.Run(_win);
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText("crash_log.txt", $"MAIN LOOP CRASH: {ex}\n");
            }
            _app.Dispose();
        }

        private void InitCustomThemes()
        {
            if (_themesInitialized) return;

            try 
            {
                System.IO.File.AppendAllText("theme_init.log", "InitCustomThemes: Started dynamic discovery\n");
                
                var schemeManagerType = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType("Terminal.Gui.Configuration.SchemeManager"))
                        .FirstOrDefault(t => t != null);

                if (schemeManagerType == null)
                {
                    System.IO.File.AppendAllText("theme_init.log", "InitCustomThemes: SchemeManager not found\n");
                    return;
                }

                var addSchemeMethod = schemeManagerType.GetMethods()
                    .FirstOrDefault(m => m.Name == "AddScheme" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(string));

                if (addSchemeMethod == null)
                {
                    System.IO.File.AppendAllText("theme_init.log", "InitCustomThemes: AddScheme method not found\n");
                    return;
                }

                var schemeType = addSchemeMethod.GetParameters()[1].ParameterType;
                var normalProp = schemeType.GetProperty("Normal");
                if (normalProp == null) return;
                var attrType = normalProp.PropertyType;

                object CreateAttr(Color fg, Color bg)
                {
                    return Activator.CreateInstance(attrType, fg, bg)!;
                }

                void CreateScheme(string name, Color fg, Color bg, Color focusFg, Color focusBg)
                {
                    try {
                        var scheme = Activator.CreateInstance(schemeType);
                        
                        schemeType.GetProperty("Normal")?.SetValue(scheme, CreateAttr(fg, bg));
                        schemeType.GetProperty("Focus")?.SetValue(scheme, CreateAttr(focusFg, focusBg));
                        schemeType.GetProperty("HotNormal")?.SetValue(scheme, CreateAttr(fg, bg));
                        schemeType.GetProperty("HotFocus")?.SetValue(scheme, CreateAttr(focusFg, focusBg));
                        
                        addSchemeMethod.Invoke(null, new object[] { name, scheme });
                        System.IO.File.AppendAllText("theme_init.log", $"InitCustomThemes: Registered {name}\n");
                    } catch (Exception ex) {
                        System.IO.File.AppendAllText("theme_init.log", $"InitCustomThemes: Failed to register {name}: {ex}\n");
                    }
                }

                // Matrix Theme
                CreateScheme("Matrix", Color.BrightGreen, Color.Black, Color.Black, Color.BrightGreen);
                
                // Red Theme
                CreateScheme("Red", Color.Red, Color.Black, Color.White, Color.Red);

                // Solarized Dark (Teal/Gray)
                CreateScheme("SolarizedDark", Color.Cyan, Color.Black, Color.White, Color.DarkGray);

                // Solarized Light (Cream/Gray) - Using White/Black approximation
                CreateScheme("SolarizedLight", Color.Black, Color.White, Color.Blue, Color.White);

                // Monokai (Dark/Pink)
                CreateScheme("Monokai", Color.White, Color.Black, Color.Magenta, Color.Black);

                // Dracula (Dark/Purple)
                CreateScheme("Dracula", Color.White, Color.DarkGray, Color.Magenta, Color.DarkGray);

                // Nord (Blueish)
                CreateScheme("Nord", Color.White, Color.Blue, Color.Cyan, Color.Blue);

                _themesInitialized = true;
                System.IO.File.AppendAllText("theme_init.log", "InitCustomThemes: Finished\n");
            }
            catch (Exception ex)
            {
                 System.IO.File.AppendAllText("theme_init.log", $"InitCustomThemes EXCEPTION: {ex}\n");
                 MessageBox.ErrorQuery(_app, "Init Error", ex.Message, "Ok");
            }
        }

        private void CycleTheme()
        {
            try 
            {
                System.IO.File.AppendAllText("cycle_debug.log", "CycleTheme: Start\n");
                InitCustomThemes();

                if (_win == null) return; 
                
                var current = _win.SchemeName ?? "Base";
                System.IO.File.AppendAllText("cycle_debug.log", $"CycleTheme: Current={current}\n");
                
                var idx = _cycleThemes.IndexOf(current);
                if (idx == -1) idx = 0;
                
                var next = _cycleThemes[(idx + 1) % _cycleThemes.Count];
                System.IO.File.AppendAllText("cycle_debug.log", $"CycleTheme: Next={next}\n");
                
                _win.SchemeName = next;
                _win.SetNeedsDraw();
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery(_app, "Error", $"Failed to cycle: {ex.Message}", "Ok");
            }
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

            var text = $"Timestamp:   {evt.Timestamp:yyyy-MM-dd HH:mm:ss.fff}\n" +
                       $"Type:        {evt.Type}\n" +
                       $"Protocol:    {evt.Protocol}\n" +
                       $"PID:         {evt.Pid}\n" +
                       $"User:        {user}\n" +
                       $"Program:     {evt.Source}\n" +
                       $"Destination: {evt.DestinationIp} ({dns}) : {evt.DestinationPort}\n" +
                       $"Details:     {evt.Details}";
            _detailsView.Text = text;
        }
    }
}
