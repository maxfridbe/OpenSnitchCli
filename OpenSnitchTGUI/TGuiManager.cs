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
        public string Protocol { get; set; } = "";
        public string DestHost { get; set; } = "";
        public string DestIp { get; set; } = "";
        public string DestPort { get; set; } = "";
        public string UserId { get; set; } = "";
    }

    public class PromptResult
    {
        public string Action { get; set; } = "allow";
        public string Duration { get; set; } = "30s";
        public bool IsCustom { get; set; } = false;
        public string CustomName { get; set; } = "";
        public string CustomOperand { get; set; } = "process.path";
        public string CustomData { get; set; } = "";
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
        private TabView? _tabView;
        private List<TuiEvent> _events = new();
        private List<object> _rules = new(); 
        private object _lock = new object();
        private IApplication? _app;
        private Window? _win;
        private Label? _statusLabel;
        private Dictionary<string, object> _rowSchemes = new();

        public event Action<object>? OnRuleDeleted;
        public event Action<object>? OnRuleChanged;

        private bool _themesInitialized = false;
        private List<string> _cycleThemes = new List<string> { "Base", "Matrix", "Red", "SolarizedDark", "SolarizedLight", "Monokai", "Dracula", "Nord" };
        private DateTime _lastBeepTime = DateTime.MinValue;

        // Base column names for dynamic updates
        private static readonly string[] ConnColNames = { "Time", "Type", "PID", "User", "Program", "Address", "Port", "Protocol" };
        private static readonly string[] RuleColNames = { "On", "Name", "Action", "Duration", "Prec", "OpType", "Operand", "Data" };

        // Sorting state
        private int _connSortCol = 0;
        private bool _connSortAsc = false;
        private int _ruleSortCol = 1;
        private bool _ruleSortAsc = true;

        public void UpdateRules(IEnumerable<object> rules)
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

        public void AddRule(object rule)
        {
            lock (_lock)
            {
                _rules.Insert(0, rule);
            }

            _app?.Invoke(() =>
            {
                RefreshRulesTable();
            });
        }

        private void ShowEditRuleDialog(object ruleObj)
        {
            dynamic rule = ruleObj;
            var dialog = new Dialog() { Title = "Edit Rule", Width = 80, Height = 22 };

            var nameLabel = new Label() { Text = "Name:", X = 1, Y = 1 };
            var nameEdit = new TextField() { Text = rule.Name, X = 15, Y = 1, Width = Dim.Fill(1), ReadOnly = true };

            var actionLabel = new Label() { Text = "Action:", X = 1, Y = 3 };
            var actions = new string[] { "Allow", "Deny" };
            var actionList = new ListView() { Source = new ListWrapper<string>(new System.Collections.ObjectModel.ObservableCollection<string>(actions)), X = 15, Y = 3, Width = 10, Height = 2 };
            string currentAction = rule.Action;
            actionList.SelectedItem = currentAction.ToLower() == "allow" ? 0 : 1;

            var durationLabel = new Label() { Text = "Duration:", X = 30, Y = 3 };
            var durations = new string[] { "once", "30s", "5m", "1h", "always" };
            var durationList = new ListView() { Source = new ListWrapper<string>(new System.Collections.ObjectModel.ObservableCollection<string>(durations)), X = 45, Y = 3, Width = 10, Height = 5 };
            string currentDuration = rule.Duration;
            int durIdx = Array.IndexOf(durations, currentDuration);
            if (durIdx != -1) durationList.SelectedItem = durIdx;

            var opLabel = new Label() { Text = "Operator:", X = 1, Y = 9 };
            var opTypeLabel = new Label() { Text = $"Type: {rule.Operator?.Type}", X = 15, Y = 9 };
            var opOperandLabel = new Label() { Text = $"Operand: {rule.Operator?.Operand}", X = 15, Y = 10 };
            var dataLabel = new Label() { Text = "Data:", X = 1, Y = 12 };
            var dataEdit = new TextField() { Text = rule.Operator?.Data ?? "", X = 15, Y = 12, Width = Dim.Fill(1) };

            var okBtn = new Button() { Text = "Save", IsDefault = true };
            var cancelBtn = new Button() { Text = "Cancel" };

            okBtn.Accepted += (s, e) => {
                rule.Action = actions[actionList.SelectedItem ?? 0].ToLower();
                rule.Duration = durations[durationList.SelectedItem ?? 0];
                if (rule.Operator != null) rule.Operator.Data = dataEdit.Text;
                
                OnRuleChanged?.Invoke(rule);
                _app?.RequestStop(dialog);
                e.Handled = true;
            };

            cancelBtn.Accepted += (s, e) => {
                _app?.RequestStop(dialog);
                e.Handled = true;
            };

            dialog.Add(nameLabel, nameEdit, actionLabel, actionList, durationLabel, durationList, opLabel, opTypeLabel, opOperandLabel, dataLabel, dataEdit);
            dialog.AddButton(okBtn);
            dialog.AddButton(cancelBtn);

            _app?.Run(dialog);
        }

        public void JumpToRule(string ruleName)
        {
            if (string.IsNullOrEmpty(ruleName) || _tabView == null || _rulesTableView == null) return;

            lock (_lock)
            {
                var sortedRules = GetSortedRules();
                for (int i = 0; i < sortedRules.Count; i++)
                {
                    dynamic rule = sortedRules[i];
                    if (rule.Name == ruleName)
                    {
                        _tabView.SelectedTab = _tabView.Tabs.ElementAt(1);
                        _rulesTableView.SetSelection(0, i, false);
                        _rulesTableView.SetFocus();
                        return;
                    }
                }
            }
        }

        private List<object> GetSortedRules()
        {
            IOrderedEnumerable<object> query;
            bool asc = _ruleSortAsc;
            switch (_ruleSortCol)
            {
                case 0: query = asc ? _rules.OrderBy(r => ((dynamic)r).Enabled) : _rules.OrderByDescending(r => ((dynamic)r).Enabled); break;
                case 1: query = asc ? _rules.OrderBy(r => ((dynamic)r).Name) : _rules.OrderByDescending(r => ((dynamic)r).Name); break;
                case 2: query = asc ? _rules.OrderBy(r => ((dynamic)r).Action) : _rules.OrderByDescending(r => ((dynamic)r).Action); break;
                case 3: query = asc ? _rules.OrderBy(r => ((dynamic)r).Duration) : _rules.OrderByDescending(r => ((dynamic)r).Duration); break;
                case 4: query = asc ? _rules.OrderBy(r => ((dynamic)r).Precedence) : _rules.OrderByDescending(r => ((dynamic)r).Precedence); break;
                case 5: query = asc ? _rules.OrderBy(r => ((dynamic)r).Operator?.Type) : _rules.OrderByDescending(r => ((dynamic)r).Operator?.Type); break;
                case 6: query = asc ? _rules.OrderBy(r => ((dynamic)r).Operator?.Operand) : _rules.OrderByDescending(r => ((dynamic)r).Operator?.Operand); break;
                case 7: query = asc ? _rules.OrderBy(r => ((dynamic)r).Operator?.Data) : _rules.OrderByDescending(r => ((dynamic)r).Operator?.Data); break;
                default: query = _rules.OrderByDescending(r => ((dynamic)r).Created); break;
            }
            return query.ThenByDescending(r => ((dynamic)r).Created).ToList();
        }

        private void RefreshRulesTable()
        {
            if (_rulesDt == null || _rulesTableView == null) return;
            
            // Update column headers with sort indicator
            for (int i = 0; i < RuleColNames.Length; i++)
            {
                string header = RuleColNames[i];
                if (i == _ruleSortCol) header += _ruleSortAsc ? " ▲" : " ▼";
                _rulesDt.Columns[i].ColumnName = header;
            }

            _rulesDt.Rows.Clear();
            lock (_lock)
            {
                var sorted = GetSortedRules();
                foreach (dynamic rule in sorted)
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

        private void CycleSort()
        {
            if (_tabView == null) return;
            if (_tabView.SelectedTab == _tabView.Tabs.ElementAt(0))
            {
                _connSortCol = (_connSortCol + 1) % ConnColNames.Length;
                RefreshTable();
            }
            else
            {
                _ruleSortCol = (_ruleSortCol + 1) % RuleColNames.Length;
                RefreshRulesTable();
            }
        }

        private void ToggleSortOrder()
        {
            if (_tabView == null) return;
            if (_tabView.SelectedTab == _tabView.Tabs.ElementAt(0))
            {
                _connSortAsc = !_connSortAsc;
                RefreshTable();
            }
            else
            {
                _ruleSortAsc = !_ruleSortAsc;
                RefreshRulesTable();
            }
        }

        public Task<PromptResult> PromptForRule(PromptRequest req)
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
                    
                    var result = MessageBox.Query(_app, "OpenSnitch Request", text, "Allow _Once", "Allow _30s", "Allow _Always", "_Deny Once", "Deny Alwa_ys", "New _Rule");
                    
                    if (result == 5) // New Rule
                    {
                        ShowCustomRuleDialog(req, (customRes) => {
                            tcs.SetResult(customRes);
                        });
                        return;
                    }

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

        private void ShowCustomRuleDialog(PromptRequest req, Action<PromptResult> onComplete)
        {
            var dialog = new Dialog() { Title = "New Rule", Width = 80, Height = 22 };

            var nameLabel = new Label() { Text = "Name:", X = 1, Y = 1 };
            var nameEdit = new TextField() { Text = $"Rule for {System.IO.Path.GetFileName(req.Process)}", X = 15, Y = 1, Width = Dim.Fill(1) };

            var actionLabel = new Label() { Text = "Action:", X = 1, Y = 3 };
            var actions = new string[] { "Allow", "Deny" };
            var actionList = new ListView() { Source = new ListWrapper<string>(new System.Collections.ObjectModel.ObservableCollection<string>(actions)), X = 15, Y = 3, Width = 10, Height = 2 };

            var durationLabel = new Label() { Text = "Duration:", X = 30, Y = 3 };
            var durations = new string[] { "once", "30s", "5m", "1h", "always" };
            var durationList = new ListView() { Source = new ListWrapper<string>(new System.Collections.ObjectModel.ObservableCollection<string>(durations)), X = 45, Y = 3, Width = 10, Height = 5 };

            // Properties with individual text boxes
            var props = new (string Label, string Operand, string Value)[] {
                ("Process Path:", "process.path", req.Process),
                ("Process Comm:", "process.comm", System.IO.Path.GetFileName(req.Process)),
                ("Dest Host:",    "dest.host",    req.DestHost),
                ("Dest IP:",      "dest.ip",      req.DestIp),
                ("Dest Port:",    "dest.port",    req.DestPort),
                ("User ID:",      "user.id",      req.UserId)
            };

            var radios = new CheckBox[props.Length];
            var edits = new TextField[props.Length];

            for (int i = 0; i < props.Length; i++) {
                int row = 9 + i;
                var cb = new CheckBox() { Text = props[i].Label, X = 1, Y = row, RadioStyle = true };
                var tf = new TextField() { Text = props[i].Value, X = 20, Y = row, Width = Dim.Fill(1) };
                
                radios[i] = cb;
                edits[i] = tf;
                
                int index = i;
                cb.CheckedStateChanged += (s, e) => {
                    if (cb.CheckedState == CheckState.Checked) {
                        for (int j = 0; j < radios.Length; j++) {
                            if (j != index) radios[j].CheckedState = CheckState.UnChecked;
                        }
                    }
                };
                dialog.Add(cb, tf);
            }
            radios[0].CheckedState = CheckState.Checked;

            var okBtn = new Button() { Text = "Ok", IsDefault = true };
            var cancelBtn = new Button() { Text = "Cancel" };

            okBtn.Accepted += (s, e) => {
                int selected = -1;
                for (int i = 0; i < radios.Length; i++) if (radios[i].CheckedState == CheckState.Checked) selected = i;
                if (selected == -1) selected = 0;

                var res = new PromptResult {
                    IsCustom = true,
                    Action = actions[actionList.SelectedItem ?? 0].ToLower(),
                    Duration = durations[durationList.SelectedItem ?? 0],
                    CustomName = nameEdit.Text,
                    CustomOperand = props[selected].Operand,
                    CustomData = edits[selected].Text
                };
                onComplete(res);
                _app?.RequestStop(dialog);
                e.Handled = true; 
            };

            cancelBtn.Accepted += (s, e) => {
                onComplete(new PromptResult { Action = "allow", Duration = "once" });
                _app?.RequestStop(dialog);
                e.Handled = true;
            };

            dialog.Add(nameLabel, nameEdit, actionLabel, actionList, durationLabel, durationList);
            dialog.AddButton(okBtn);
            dialog.AddButton(cancelBtn);

            _app?.Run(dialog);
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
                foreach (var col in ConnColNames) _dt.Columns.Add(col);

                _dt.DefaultView.AllowEdit = false;
                _dt.DefaultView.AllowNew = false;
                _dt.DefaultView.AllowDelete = false;

                _rulesDt = new DataTable();
                foreach (var col in RuleColNames) _rulesDt.Columns.Add(col);

                _tabView = new TabView()
                {
                    X = 0,
                    Y = 1,
                    Width = Dim.Fill(),
                    Height = Dim.Percent(70),
                    MaxTabTextWidth = 50 // Allow wider tab titles
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

                _tabView.AddTab(new Tab { Title = "   Connections   ", View = _tableView }, true);
                _tabView.AddTab(new Tab { Title = "   Rules   ", View = _rulesTableView }, false);

                _tableView.Style.GetOrCreateColumnStyle(6).Alignment = Alignment.Start; // Port Left Aligned
                _tableView.Style.GetOrCreateColumnStyle(7).Alignment = Alignment.Start; // Protocol
                _tableView.Style.GetOrCreateColumnStyle(2).Alignment = Alignment.End; // PID

                _win.SubViewsLaidOut += (s, e) =>
                {
                    if (_tableView!.Viewport.Width <= 0) return;

                    int width = _tableView.Viewport.Width;
                    int scrollBarWidth = 1; 
                    int fixedWidths = 0;

                                    var colWidths = new Dictionary<string, int>
                                    {
                                        { "Time", 12 },
                                        { "Type", 10 },
                                        { "PID", 8 },
                                        { "User", 15 },
                                        { "Address", 25 }, 
                                        { "Port", 10 },
                                        { "Protocol", 8 }
                                    };
                    foreach (var kvp in colWidths) fixedWidths += kvp.Value;

                    int programWidth = Math.Max(10, width - fixedWidths - scrollBarWidth - 5);

                    for (int i = 0; i < _dt.Columns.Count; i++)
                    {
                        var col = _dt.Columns[i];
                        var style = _tableView.Style.GetOrCreateColumnStyle(i);
                        string baseName = ConnColNames[i];
                        int w = baseName == "Program" ? programWidth : (colWidths.ContainsKey(baseName) ? colWidths[baseName] : 10);
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
                    Y = Pos.Bottom(_tabView),
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

                _win.Add(_tabView, detailsWin);

                // Use the instance-level Keyboard.KeyDown event for global hotkeys. 
                // This is the correct way to handle keys when using Application.Create() in v2.
                if (_app?.Keyboard != null)
                {
                    _app.Keyboard.KeyDown += (s, e) => {
                        if (e.Handled) return;
                        
                        // Normalize the key by removing modifiers for comparison
                        var baseKey = e.NoAlt.NoCtrl.NoShift;
                        var keyCode = e.KeyCode;
                        
                        if (baseKey == Key.Q || keyCode == Key.Q.KeyCode) 
                        {
                            _app?.RequestStop(); 
                            e.Handled = true; 
                        }
                        else if (baseKey == Key.T || keyCode == Key.T.KeyCode) 
                        {
                            CycleTheme(); 
                            e.Handled = true; 
                        }
                        else if (baseKey == Key.S || keyCode == Key.S.KeyCode || keyCode == (Key.S.KeyCode | Terminal.Gui.Drivers.KeyCode.ShiftMask))
                        {
                            if (e.IsShift || (keyCode & Terminal.Gui.Drivers.KeyCode.ShiftMask) != 0) ToggleSortOrder();
                            else CycleSort();
                            e.Handled = true;
                        }
                        else if (baseKey == Key.R || keyCode == Key.R.KeyCode)
                        {
                            // Only jump if we are on the connections tab
                            if (_tabView != null && _tabView.SelectedTab == _tabView.Tabs.ElementAt(0) && _tableView != null)
                            {
                                int row = _tableView.SelectedRow;
                                lock (_lock)
                                {
                                    var sortedEvents = GetSortedEvents();
                                    if (row >= 0 && row < sortedEvents.Count)
                                    {
                                        var evt = sortedEvents[row];
                                        var match = Regex.Match(evt.Details ?? "", @"\[Rule: (.*?)\]");
                                        if (match.Success)
                                        {
                                            JumpToRule(match.Groups[1].Value);
                                            e.Handled = true;
                                        }
                                        else if (evt.Details != null && evt.Details.StartsWith("Rule Hit: "))
                                        {
                                            JumpToRule(evt.Details.Replace("Rule Hit: ", ""));
                                            e.Handled = true;
                                        }
                                    }
                                }
                            }
                        }
                        else if (baseKey == Key.E || keyCode == Key.E.KeyCode)
                        {
                            if (_tabView != null && _tabView.SelectedTab == _tabView.Tabs.ElementAt(1) && _rulesTableView != null)
                            {
                                int row = _rulesTableView.SelectedRow;
                                var sortedRules = GetSortedRules();
                                if (row >= 0 && row < sortedRules.Count)
                                {
                                    ShowEditRuleDialog(sortedRules[row]);
                                    e.Handled = true;
                                }
                            }
                        }
                        else if (baseKey == Key.D || keyCode == Key.D.KeyCode)
                        {
                            if (_tabView != null && _tabView.SelectedTab == _tabView.Tabs.ElementAt(1) && _rulesTableView != null)
                            {
                                int row = _rulesTableView.SelectedRow;
                                var sortedRules = GetSortedRules();
                                if (row >= 0 && row < sortedRules.Count)
                                {
                                    dynamic rule = sortedRules[row];
                                    if (MessageBox.Query(_app, "Delete Rule", $"Are you sure you want to delete rule: {rule.Name}?", "Yes", "No") == 0)
                                    {
                                        OnRuleDeleted?.Invoke(rule);
                                        lock(_lock) _rules.Remove(sortedRules[row]);
                                        RefreshRulesTable();
                                        e.Handled = true;
                                    }
                                }
                            }
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
                    new Shortcut(Key.S, "~s/S~ Sort", () => { 
                        // Handled by global KeyDown
                    }),
                    new Shortcut(Key.R, "~r~ Jump to Rule", () => {
                        // Handled by global KeyDown
                    }),
                    new Shortcut(Key.E, "~e~ Edit Rule", () => {
                        // Handled by global KeyDown
                    }),
                    new Shortcut(Key.D, "~d~ Delete Rule", () => {
                        // Handled by global KeyDown
                    }),
                    new Shortcut(Key.F1, "~F1~ Help", () => MessageBox.Query(_app, "Help", "Use Arrow Keys to Navigate\nPress 'q' to Quit\nPress 't' to Cycle Themes\nPress 's' to Cycle Sort Column\nPress 'S' to Toggle Sort Order\nPress 'r' to Jump to Rule\nPress 'e' to Edit Rule\nPress 'd' to Delete Rule", "Ok"))
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

        private List<TuiEvent> GetSortedEvents()
        {
            IOrderedEnumerable<TuiEvent> query;
            bool asc = _connSortAsc;
            switch (_connSortCol)
            {
                case 0: query = asc ? _events.OrderBy(e => e.Timestamp) : _events.OrderByDescending(e => e.Timestamp); break;
                case 1: query = asc ? _events.OrderBy(e => e.Type) : _events.OrderByDescending(e => e.Type); break;
                case 2: query = asc ? _events.OrderBy(e => e.Pid) : _events.OrderByDescending(e => e.Pid); break;
                case 3: query = asc ? _events.OrderBy(e => e.Details) : _events.OrderByDescending(e => e.Details); break; // User is in details currently
                case 4: query = asc ? _events.OrderBy(e => e.Source) : _events.OrderByDescending(e => e.Source); break;
                case 5: query = asc ? _events.OrderBy(e => e.DestinationIp) : _events.OrderByDescending(e => e.DestinationIp); break;
                case 6: query = asc ? _events.OrderBy(e => e.DestinationPort) : _events.OrderByDescending(e => e.DestinationPort); break;
                case 7: query = asc ? _events.OrderBy(e => e.Protocol) : _events.OrderByDescending(e => e.Protocol); break;
                default: query = _events.OrderByDescending(e => e.Timestamp); break;
            }
            return query.ThenByDescending(e => e.Timestamp).ToList();
        }

        private void RefreshTable()
        {
            if (_dt == null || _tableView == null) return;

            // Update column headers with sort indicator
            for (int i = 0; i < ConnColNames.Length; i++)
            {
                string header = ConnColNames[i];
                if (i == _connSortCol) header += _connSortAsc ? " ▲" : " ▼";
                _dt.Columns[i].ColumnName = header;
            }

            _dt.Rows.Clear();
            lock (_lock)
            {
                var sorted = GetSortedEvents();
                foreach (var evt in sorted)
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
                        evt.Timestamp.ToString("HH:mm:ss.fff"),
                        typeStr,
                        evt.Pid,
                        user,
                        (evt.IsInNamespace ? "📦 " : "") + evt.Source,
                        address,
                        evt.DestinationPort,
                        evt.Protocol
                    );
                }
            }
            _tableView.SetNeedsDraw();
        }

        private void UpdateDetails(int row)
        {
            if (_detailsView == null || row < 0) return;
            TuiEvent evt;
            lock (_lock) 
            {
                var sorted = GetSortedEvents();
                if (row >= sorted.Count) return;
                evt = sorted[row];
            }
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
