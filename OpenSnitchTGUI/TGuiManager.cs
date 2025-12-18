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
    public class PromptRequest
    {
        public string Description { get; set; } = "";
        public string Process { get; set; } = "";
        public string Destination { get; set; } = "";
    }

    public class PromptResult
    {
        public string Action { get; set; } = "allow";
        public string Duration { get; set; } = "30s";
    }

    public class TGuiManager
    {
        private readonly DnsManager _dnsManager = new();
        private readonly UserManager _userManager = new();
        private TableView? _tableView;
        private TableView? _rulesTableView;
        private TextView? _detailsView;
        private DataTable? _dt;
        private DataTable? _rulesDt;
        private List<TuiEvent> _events = new();
        private List<Protocol.Rule> _rules = new();
        private object _lock = new object();
        private IApplication? _app;
        private Window? _win;
        private Label? _statusLabel;
        private Dictionary<string, object> _rowSchemes = new();

        private bool _themesInitialized = false;
        private List<string> _cycleThemes = new List<string> { "Base", "Matrix", "Red", "SolarizedDark", "SolarizedLight", "Monokai", "Dracula", "Nord" };
        private DateTime _lastBeepTime = DateTime.MinValue;

        public void UpdateRules(IEnumerable<Protocol.Rule> rules)
        {
            lock (_lock)
            {
                _rules = rules.ToList();
            }

            _app?.Invoke(() =>
            {
                RefreshRulesTable();
            });
        }

        private void RefreshRulesTable()
        {
            if (_rulesDt == null || _rulesTableView == null) return;
            _rulesDt.Rows.Clear();
            lock (_lock)
            {
                foreach (var rule in _rules)
                {
                    _rulesDt.Rows.Add(
                        rule.Enabled ? "✓" : " ",
                        rule.Name,
                        rule.Action,
                        rule.Duration,
                        rule.Precedence ? "High" : "Normal",
                        rule.Operator?.Type ?? "",
                        rule.Operator?.Operand ?? "",
                        rule.Operator?.Data ?? ""
                    );
                }
            }
            _rulesTableView.SetNeedsDraw();
        }
        {
            var tcs = new TaskCompletionSource<PromptResult>();

            if (_app == null) 
            {
                tcs.SetResult(new PromptResult { Action = "allow", Duration = "once" });
                return tcs.Task;
            }

            _app.Invoke(() =>
            {
                try 
                {
                    if ((DateTime.Now - _lastBeepTime).TotalSeconds >= 3)
                    {
                        Console.Beep();
                        _lastBeepTime = DateTime.Now;
                    }

                    var text = $"Process: {req.Process}\nDest: {req.Destination}\n\nWhat do you want to do?";
                    
                    var result = MessageBox.Query(_app, "OpenSnitch Request", text, "Allow _Once", "Allow _30s", "Allow _Always", "_Deny Once", "Deny Alwa_ys");
                    
                    string action = "allow";
                    string duration = "30s";

                    switch (result)
                    {
                        case 0: action = "allow"; duration = "once"; break;
                        case 1: action = "allow"; duration = "30s"; break;
                        case 2: action = "allow"; duration = "always"; break;
                        case 3: action = "deny"; duration = "once"; break;
                        case 4: action = "deny"; duration = "always"; break;
                        default: action = "allow"; duration = "once"; break; 
                    }

                    tcs.SetResult(new PromptResult { Action = action, Duration = duration });
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return tcs.Task;
        }

        public void AddEvent(TuiEvent evt)
        {
            if (evt.Type == "Ping") return; 

            lock (_lock)
            {
                _events.Insert(0, evt); 
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
            try 
            {
                _app = Application.Create();
                _app.Init();

                _win = new Window() 
                {
                    Title = "OpenSnitch CLI (Terminal.Gui)",
                    X = 0,
                    Y = 0,
                    Width = Dim.Fill(),
                    Height = Dim.Fill(1) 
                };

                _statusLabel = new Label() 
                {
                    Text = "Status: Online | Events: 0",
                    X = Pos.Right(_win) - 30, 
                    Y = 0,
                    Width = 30,
                    TextAlignment = Alignment.End
                };
                _win.Add(_statusLabel);

                _dt = new DataTable();
                _dt.Columns.Add("Time");
                _dt.Columns.Add("Type");
                _dt.Columns.Add("Protocol");
                _dt.Columns.Add("PID");
                _dt.Columns.Add("User");
                _dt.Columns.Add("Program");
                _dt.Columns.Add("Address");
                _dt.Columns.Add("Port");

                _dt.DefaultView.AllowEdit = false;
                            _dt.DefaultView.AllowNew = false;
                            _dt.DefaultView.AllowDelete = false;
                
                            _rulesDt = new DataTable();
                            _rulesDt.Columns.Add("On");
                            _rulesDt.Columns.Add("Name");
                            _rulesDt.Columns.Add("Action");
                            _rulesDt.Columns.Add("Duration");
                            _rulesDt.Columns.Add("Prec");
                            _rulesDt.Columns.Add("OpType");
                            _rulesDt.Columns.Add("Operand");
                            _rulesDt.Columns.Add("Data");
                
                            var tabView = new TabView()
                            {
                                X = 0,
                                Y = 1,
                                Width = Dim.Fill(),
                                Height = Dim.Percent(70)
                            };
                
                            _tableView = new TableView()
                            {
                                X = 0,
                                Y = 0,
                                Width = Dim.Fill(),
                                Height = Dim.Fill(),
                                Table = new DataTableSource(_dt),
                                FullRowSelect = true,
                                MultiSelect = false
                            };
                            _tableView.SelectedCellChanged += (s, e) => UpdateDetails(e.NewRow);
                
                            _rulesTableView = new TableView()
                            {
                                X = 0,
                                Y = 0,
                                Width = Dim.Fill(),
                                Height = Dim.Fill(),
                                Table = new DataTableSource(_rulesDt),
                                FullRowSelect = true,
                                MultiSelect = false
                            };
                
                            tabView.AddTab(new Tab { Title = "Connections", View = _tableView }, true);
                            tabView.AddTab(new Tab { Title = "Rules", View = _rulesTableView }, false);
                
                            _tableView.Style.GetOrCreateColumnStyle(6).Alignment = Alignment.End;
                            _tableView.Style.GetOrCreateColumnStyle(7).Alignment = Alignment.End;
                            _tableView.Style.GetOrCreateColumnStyle(3).Alignment = Alignment.End;
                
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
                
                            InitCustomThemes();
                            ApplyRowColors();
                
                            var detailsWin = new FrameView() 
                            {
                                Title = "Event Details",
                                X = 0,
                                Y = Pos.Bottom(tabView),
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
                
                            _win.Add(tabView, detailsWin);
                // Use the instance-level Keyboard.KeyDown event for global hotkeys. 
                // This is the correct way to handle keys when using Application.Create() in v2.
                if (_app?.Keyboard != null)
                {
                    _app.Keyboard.KeyDown += (s, e) => {
                        if (e.Handled) return;
                        
                        // Normalize the key by removing modifiers for comparison
                        var baseKey = e.NoAlt.NoCtrl.NoShift;
                        
                        if (baseKey == Key.Q) 
                        { 
                            _app?.RequestStop(); 
                            e.Handled = true; 
                        }
                        else if (baseKey == Key.T) 
                        { 
                            CycleTheme(); 
                            e.Handled = true; 
                        }
                    };
                }

                // Try to use the modern v2 KeyBindings if they work as an extra layer
                try {
                    _win.KeyBindings.Add(Key.Q, Command.Quit);
                } catch {}

                var statusBar = new StatusBar(new Shortcut[] {
                    new Shortcut(Key.Q, "~q~ Quit", () => _app.RequestStop()),
                    new Shortcut(Key.T, "~t~ Theme", () => CycleTheme()),
                    new Shortcut(Key.F1, "~F1~ Help", () => MessageBox.Query(_app, "Help", "Use Arrow Keys to Navigate\nPress 'q' to Quit\nPress 't' to Cycle Themes", "Ok"))
                });
                _win.Add(statusBar);

                _app.Run(_win);
                _app.Dispose();
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText("crash_log.txt", $"CRITICAL ERROR: {ex}\n");
                throw; // Re-throw to be caught by Program.Main
            }
        }

        private void ApplyRowColors()
        {
            try
            {
                if (_tableView == null) return;

                var asm = typeof(Application).Assembly;
                var colStyleType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "ColumnStyle" || t.FullName == "Terminal.Gui.ColumnStyle" || t.Name.EndsWith("ColumnStyle"));

                if (colStyleType == null) return;

                var colorGetterProp = colStyleType.GetProperty("ColorGetter");
                if (colorGetterProp == null) return;
                
                var delegateType = colorGetterProp.PropertyType; 
                var argsType = delegateType.GetGenericArguments()[0];

                var argsParam = System.Linq.Expressions.Expression.Parameter(argsType, "args");
                var getSchemeMethod = this.GetType().GetMethod("GetRowScheme", BindingFlags.Instance | BindingFlags.NonPublic);
                var callGetScheme = System.Linq.Expressions.Expression.Call(System.Linq.Expressions.Expression.Constant(this), getSchemeMethod, argsParam);
                
                var colorSchemeType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("Terminal.Gui.ColorScheme"))
                    .FirstOrDefault(t => t != null);

                var castResult = System.Linq.Expressions.Expression.Convert(callGetScheme, colorSchemeType!); 
                var lambda = System.Linq.Expressions.Expression.Lambda(delegateType, castResult, argsParam);
                var compiledDelegate = lambda.Compile();
                
                foreach (DataColumn col in _dt!.Columns)
                {
                    var style = _tableView.Style.GetOrCreateColumnStyle(col.Ordinal);
                    colorGetterProp.SetValue(style, compiledDelegate);
                }
            }
            catch { }
        }

        private object? GetRowScheme(object args) 
        {
            try 
            {
                var rowIndexProp = args.GetType().GetProperty("RowIndex");
                if (rowIndexProp == null) return null;

                int rowIndex = (int)rowIndexProp.GetValue(args)!;
                if (_dt == null || rowIndex < 0 || rowIndex >= _dt.Rows.Count) return null;
                
                var row = _dt.Rows[rowIndex];
                string type = row["Type"].ToString() ?? "";
                
                if (type.Contains("ALLOW") && _rowSchemes.ContainsKey("RowAllow")) return _rowSchemes["RowAllow"];
                if (type.Contains("DENY") && _rowSchemes.ContainsKey("RowDeny")) return _rowSchemes["RowDeny"];
                if ((type.Contains("ASK") || type.Contains("AskRule")) && _rowSchemes.ContainsKey("RowAsk")) return _rowSchemes["RowAsk"];
                
                return null;
            }
            catch { return null; }
        }

        private void InitCustomThemes()
        {
            if (_themesInitialized) return;

            try 
            {
                var schemeManagerType = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType("Terminal.Gui.Configuration.SchemeManager"))
                        .FirstOrDefault(t => t != null);

                if (schemeManagerType == null) return;

                var addSchemeMethod = schemeManagerType.GetMethods()
                    .FirstOrDefault(m => m.Name == "AddScheme" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(string));

                if (addSchemeMethod == null) return;

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
                        if (name.StartsWith("Row")) _rowSchemes[name] = scheme;
                    } catch { }
                }

                CreateScheme("Matrix", Color.BrightGreen, Color.Black, Color.Black, Color.BrightGreen);
                CreateScheme("Red", Color.Red, Color.Black, Color.White, Color.Red);
                CreateScheme("SolarizedDark", Color.Cyan, Color.Black, Color.White, Color.DarkGray);
                CreateScheme("SolarizedLight", Color.Black, Color.White, Color.Blue, Color.White);
                CreateScheme("Monokai", Color.White, Color.Black, Color.Magenta, Color.Black);
                CreateScheme("Dracula", Color.White, Color.DarkGray, Color.Magenta, Color.DarkGray);
                CreateScheme("Nord", Color.White, Color.Blue, Color.Cyan, Color.Blue);

                CreateScheme("RowAllow", Color.BrightGreen, Color.Black, Color.Black, Color.BrightGreen);
                CreateScheme("RowDeny", Color.BrightRed, Color.Black, Color.White, Color.BrightRed);
                CreateScheme("RowAsk", Color.BrightYellow, Color.Black, Color.Black, Color.BrightYellow);

                _themesInitialized = true;
            }
            catch { }
        }

        private void CycleTheme()
        {
            try 
            {
                InitCustomThemes();
                if (_win == null) return; 
                var current = _win.SchemeName ?? "Base";
                var idx = _cycleThemes.IndexOf(current);
                if (idx == -1) idx = 0;
                var next = _cycleThemes[(idx + 1) % _cycleThemes.Count];
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
                    if (match.Success) user = _userManager.GetUser(match.Groups[1].Value);
                    var address = _dnsManager.GetDisplayName(evt.DestinationIp);
                    string typeStr = evt.Type;
                    if (typeStr == "ALLOW") typeStr = "✓ ALLOW";
                    else if (typeStr == "DENY") typeStr = "✗ DENY";
                    else if (typeStr == "AskRule") typeStr = "? ASK";

                    _dt.Rows.Add(
                        evt.Timestamp.ToString("HH:mm:ss"),
                        typeStr,
                        evt.Protocol,
                        evt.Pid,
                        user,
                        evt.Source,
                        address,
                        evt.DestinationPort
                    );
                }
                if (_statusLabel != null) _statusLabel.Text = $"Events: {_events.Count} | Last: {DateTime.Now:HH:mm:ss}";
            }
            _tableView.SetNeedsDraw();
        }

        private void UpdateDetails(int row)
        {
            if (_detailsView == null || row < 0 || row >= _events.Count) return;
            TuiEvent evt;
            lock (_lock) evt = _events[row];
            var dns = _dnsManager.GetDisplayName(evt.DestinationIp);
            string user = "";
            var match = Regex.Match(evt.Details ?? "", @"U(?:ID|ser):\s*(\d+)");
            if (match.Success) user = _userManager.GetUser(match.Groups[1].Value);

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