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
        private FrameView? _detailsFrame;
        private DataTable? _dt;
        private DataTable? _rulesDt;
        private TabView? _tabView;
        private StatusBar? _statusBar;
        private List<TuiEvent> _events = new();
        private List<object> _rules = new(); 
        private object _lock = new object();
        private IApplication? _app;
        private Window? _win;
        private Label? _statusLabel; // Top-right status label
        private Dictionary<string, object> _rowSchemes = new();
        private TextField? _filterField;
        private string _appVersion = "1.0.0";
        private string _daemonVersion = "Unknown";
        private DateTime _lastPingTime = DateTime.MinValue; // Tracks daemon connectivity
        private bool _showFullProcessCommand = false;

        public event Action<object>? OnRuleDeleted;
        public event Action<object>? OnRuleChanged;

        public void SetVersions(string appVersion, string daemonVersion)
        {
            _appVersion = appVersion;
            _daemonVersion = daemonVersion;
            UpdateTitle();
        }

        private void UpdateTitle()
        {
            if (_win != null)
            {
                _win.Title = $"OpenSnitch CLI v{_appVersion} | Daemon v{_daemonVersion}";
                _win.SetNeedsDraw();
            }
        }

        private bool _themesInitialized = false;
        private List<string> _cycleThemes = new List<string> { 
            "Base", "Matrix", "Red", "SolarizedDark", "SolarizedLight", "Monokai", "Dracula", "Nord",
            "Catppuccin", "AyuMirage", "Everforest", "Gruvbox" 
        };
        private DateTime _lastBeepTime = DateTime.MinValue;

        // History limits
        private int[] _historyLimits = { 1000, 800, 500, 200, 100, 50 };
        private int _limitIndex = 0;
        private int _maxEvents => _historyLimits[_limitIndex];

        private void UpdateStatusBar()
        {
            if (_statusBar == null || _tabView == null || _win == null) return;

            var currentTheme = _win.SchemeName ?? "Base";
            
            string sortInfo = "";
            if (_tabView.SelectedTab == _tabView.Tabs.ElementAt(0))
                sortInfo = $"{ConnColNames[_connSortCol]} {(_connSortAsc ? "ASC" : "DESC")}";
            else
                sortInfo = $"{RuleColNames[_ruleSortCol]} {(_ruleSortAsc ? "ASC" : "DESC")}";

            var shortcuts = new List<Shortcut>();
            shortcuts.Add(new Shortcut(Key.Q, "~q~ Quit", () => _app?.RequestStop()));
            shortcuts.Add(new Shortcut(Key.F, "~f~ Filter", () => _filterField?.SetFocus()));
            shortcuts.Add(new Shortcut(Key.D0, $"~0~ Theme: {currentTheme}", () => CycleTheme()));
            shortcuts.Add(new Shortcut(Key.S, "~s/S~ Sort: {sortInfo}", () => {{ /* Handled in KeyDown */ }} ));
            shortcuts.Add(new Shortcut(Key.L, "~l~ Limit", () => CycleLimit()));

            if (_tabView.SelectedTab == _tabView.Tabs.ElementAt(0)) // Connections
            {
                shortcuts.Add(new Shortcut(Key.P, "~p~ Toggle Process", () => {{ /* Handled in KeyDown */ }} ));
                shortcuts.Add(new Shortcut(Key.J, "~j~ Jump to Rule", () => HandleJumpToRule()));
            }
            else // Rules
            {
                shortcuts.Add(new Shortcut(Key.T, "~t~ Toggle", () => {{ /* Handled in KeyDown */ }} ));
                shortcuts.Add(new Shortcut(Key.E, "~e~ Edit Rule", () => {{ /* Handled in KeyDown */ }} ));
                shortcuts.Add(new Shortcut(Key.D, "~d~ Delete Rule", () => {{ /* Handled in KeyDown */ }} ));
            }

            shortcuts.Add(new Shortcut((Key)'?', "~?~ Help", () => ShowHelp()));

            _statusBar.RemoveAll();
            foreach (var s in shortcuts) _statusBar.Add(s); 
            
            // Sync status bar theme with window
            _statusBar.SchemeName = _win.SchemeName;

            // Update the top-right status label with connection status and event count
            UpdateConnectionStatusLabel();
        }

        private void UpdateConnectionStatusLabel()
        {
            if (_statusLabel != null)
            {
                bool isAlive = (DateTime.Now - _lastPingTime).TotalSeconds < 5; // Daemon is considered alive if pinged recently
                var statusIcon = isAlive ? "●" : "○";
                var statusText = isAlive ? "Online" : "No Signal";
                _statusLabel.Text = $"{statusIcon} {statusText} | Events: {_events.Count}/{_maxEvents}";
            }
        }

        private void CycleLimit()
        {
            _limitIndex = (_limitIndex + 1) % _historyLimits.Length;
            RefreshTable();
        }

        private void ShowHelp()
        {
            MessageBox.Query(_app, "Help", 
                "Use Arrow Keys to Navigate\n" +
                "Press 'q' to Quit\n" +
                "Press 'f' to Filter\n" +
                "Press '0' to Cycle Themes\n" +
                "Press 's' to Cycle Sort Column\n" +
                "Press 'S' to Toggle Sort Order\n" +
                "Press 'c' for Connections tab\n" +
                "Press 'r' for Rules tab\n" +
                "Press 'j' to Jump to Rule (Connections tab)\n" +
                "Press 't' to Toggle Rule (Rules tab)\n" +
                "Press 'e' to Edit Rule (Rules tab)\n" +
                "Press 'd' to Delete Rule (Rules tab)", "Ok");
        }

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
            var dialog = new Dialog() { 
                Title = "Edit Rule", 
                Width = 80, 
                Height = 22,
                SchemeName = _win?.SchemeName // Inherit current theme
            };

            var nameLabel = new Label() { Text = "Name:", X = 1, Y = 1 };
            var nameEdit = new TextField() { Text = rule.Name, X = 15, Y = 1, Width = Dim.Fill(1) };

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

            okBtn.Accepting += (s, e) => {
                rule.Name = nameEdit.Text;
                rule.Action = actions[actionList.SelectedItem ?? 0].ToLower();
                rule.Duration = durations[durationList.SelectedItem ?? 0];
                if (rule.Operator != null) rule.Operator.Data = dataEdit.Text;
                
                OnRuleChanged?.Invoke(rule);
                _app?.RequestStop(dialog);
                e.Handled = true;
            };

            cancelBtn.Accepting += (s, e) => {
                _app?.RequestStop(dialog);
                e.Handled = true;
            };

            dialog.Add(nameLabel, nameEdit, actionLabel, actionList, durationLabel, durationList, opLabel, opTypeLabel, opOperandLabel, dataLabel, dataEdit);
            dialog.AddButton(okBtn);
            dialog.AddButton(cancelBtn);

            _app?.Run(dialog);
        }

        private void HandleJumpToRule()
        {
            if (_tableView == null || _tabView == null || _tabView.SelectedTab != _tabView.Tabs.ElementAt(0)) return;

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
                    }
                    else if (evt.Details != null && evt.Details.StartsWith("Rule Hit: "))
                    {
                        JumpToRule(evt.Details.Replace("Rule Hit: ", ""));
                    }
                }
            }
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
                        _rulesTableView.EnsureSelectedCellIsVisible();
                        _rulesTableView.SetFocus();
                        return;
                    }
                }
            }
        }

        private List<object> GetSortedRules()
        {
            IEnumerable<object> filtered = _rules;
            if (_filterField != null && _filterField.Text.Length > 0)
            {
                var term = _filterField.Text.ToString()!.ToLower();
                filtered = filtered.Where(r => ((dynamic)r).Name != null && ((string)((dynamic)r).Name).ToLower().Contains(term));
            }

            IOrderedEnumerable<object> query;
            bool asc = _ruleSortAsc;
            switch (_ruleSortCol)
            {
                case 0: query = asc ? filtered.OrderBy(r => ((dynamic)r).Enabled) : filtered.OrderByDescending(r => ((dynamic)r).Enabled); break;
                case 1: query = asc ? filtered.OrderBy(r => ((dynamic)r).Name) : filtered.OrderByDescending(r => ((dynamic)r).Name); break;
                case 2: query = asc ? filtered.OrderBy(r => ((dynamic)r).Action) : filtered.OrderByDescending(r => ((dynamic)r).Action); break;
                case 3: query = asc ? filtered.OrderBy(r => ((dynamic)r).Duration) : filtered.OrderByDescending(r => ((dynamic)r).Duration); break;
                case 4: query = asc ? filtered.OrderBy(r => ((dynamic)r).Precedence) : filtered.OrderByDescending(r => ((dynamic)r).Precedence); break;
                case 5: query = asc ? filtered.OrderBy(r => ((dynamic)r).Operator?.Type) : filtered.OrderByDescending(r => ((dynamic)r).Operator?.Type); break;
                case 6: query = asc ? filtered.OrderBy(r => ((dynamic)r).Operator?.Operand) : filtered.OrderByDescending(r => ((dynamic)r).Operator?.Operand); break;
                case 7: query = asc ? filtered.OrderBy(r => ((dynamic)r).Operator?.Data) : filtered.OrderByDescending(r => ((dynamic)r).Operator?.Data); break;
                default: query = filtered.OrderByDescending(r => ((dynamic)r).Created); break;
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
            UpdateStatusBar();
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
            UpdateStatusBar();
        }

        public Task<PromptResult> PromptForRule(PromptRequest req)
        {
            var tcs = new TaskCompletionSource<PromptResult>();

            if (_app == null) 
            {
                tcs.SetResult(new PromptResult { Action = "allow", Duration = "once" });
                return tcs.Task;
            }

            _app?.Invoke(() =>
            {
                try 
                {
                    try {{ Console.Title = "OpenSnitchCLI **PROMPT**"; }} catch {{}}

                    if ((DateTime.Now - _lastBeepTime).TotalSeconds >= 3)
                    {
                        Console.Beep();
                        _lastBeepTime = DateTime.Now;
                    }

                    var dnsName = _dnsManager.GetDisplayName(req.DestIp, req.DestHost);
                    var destDisplay = (string.IsNullOrEmpty(dnsName) || dnsName == req.DestIp)
                        ? req.Destination
                        : $"{dnsName} ({req.DestIp}) : {req.DestPort}";

                    var description = DescriptionManager.Instance.GetDescription(req.Process);
                    var aboutText = !string.IsNullOrEmpty(description) ? $"\nAbout: {description}" : "";
                    var text = $"Process: {req.Process}\n{req.Description}{aboutText}\nDest: {destDisplay}\n\nWhat do you want to do?";
                    
                    var dialog = new Dialog() { 
                        Title = "OpenSnitch Request", 
                        Width = 75, 
                        Height = 20,
                        SchemeName = _win?.SchemeName
                    };
                    var lbl = new Label() { Text = text, X = 1, Y = 1 };
                    dialog.Add(lbl);

                    int result = -1;
                    
                    // Row 1: Allows
                    var btnAllowOnce = new Button() { Text = "Allow _Once", X = Pos.Center() - 22, Y = 6 };
                    var btnAllow30s = new Button() { Text = "Allow _30s", X = Pos.Center() - 5, Y = 6 };
                    var btnAllowAlways = new Button() { Text = "Allow _Always", X = Pos.Center() + 12, Y = 6 };

                    // Row 2: Denies
                    var btnDenyOnce = new Button() { Text = "_Deny Once", X = Pos.Center() - 12, Y = 8 };
                    var btnDenyAlways = new Button() { Text = "Deny Alwa_ys", X = Pos.Center() + 5, Y = 8 };

                    // Row 3: Advanced
                    var btnNewRule = new Button() { Text = "Make a New _Rule", X = Pos.Center(), Y = 10 };

                    btnAllowOnce.Accepting += (s, e) => {{ result = 0; _app?.RequestStop(dialog); e.Handled = true; }};
                    btnAllow30s.Accepting += (s, e) => {{ result = 1; _app?.RequestStop(dialog); e.Handled = true; }};
                    btnAllowAlways.Accepting += (s, e) => {{ result = 2; _app?.RequestStop(dialog); e.Handled = true; }};
                    btnDenyOnce.Accepting += (s, e) => {{ result = 3; _app?.RequestStop(dialog); e.Handled = true; }};
                    btnDenyAlways.Accepting += (s, e) => {{ result = 4; _app?.RequestStop(dialog); e.Handled = true; }};
                    btnNewRule.Accepting += (s, e) => {{ result = 5; _app?.RequestStop(dialog); e.Handled = true; }};

                    dialog.Add(btnAllowOnce, btnAllow30s, btnAllowAlways, btnDenyOnce, btnDenyAlways, btnNewRule);

                    _app?.Run(dialog);
                    try {{ Console.Title = $"OpenSnitch CLI v{_appVersion}"; }} catch {{}}
                    
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
            var dialog = new Dialog() { 
                Title = "New Rule", 
                Width = 80, 
                Height = 22,
                SchemeName = _win?.SchemeName // Inherit current theme
            };

            var nameLabel = new Label() { Text = "Name:", X = 1, Y = 1 };
            var nameEdit = new TextField() { Text = $"Rule for {System.IO.Path.GetFileName(req.Process)}", X = 15, Y = 1, Width = Dim.Fill(1) };

            var actionLabel = new Label() { Text = "Action:", X = 1, Y = 3 };
            var actions = new string[] { "Allow", "Deny" };
            var actionList = new ListView() { Source = new ListWrapper<string>(new System.Collections.ObjectModel.ObservableCollection<string>(actions)), X = 15, Y = 3, Width = 10, Height = 2 };

            var durationLabel = new Label() { Text = "Duration:", X = 30, Y = 3 };
            var durations = new string[] { "once", "30s", "5m", "1h", "always" };
            var durationList = new ListView() { Source = new ListWrapper<string>(new System.Collections.ObjectModel.ObservableCollection<string>(durations)), X = 45, Y = 3, Width = 10, Height = 5 };

            // Properties with individual text boxes
            var dnsName = _dnsManager.GetDisplayName(req.DestIp, req.DestHost);
            var initialDestHost = (string.IsNullOrEmpty(req.DestHost) || req.DestHost == req.DestIp) ? dnsName : req.DestHost;

            var props = new (string Label, string Operand, string Value)[] {
                ("Process Path:", "process.path", req.Process),
                ("Process Comm:", "process.comm", System.IO.Path.GetFileName(req.Process)),
                ("Dest Host:",    "dest.host",    initialDestHost),
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

            okBtn.Accepting += (s, e) => {
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

            cancelBtn.Accepting += (s, e) => {
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
            if (evt.Type == "Ping") 
            {
                _lastPingTime = DateTime.Now; // Update last ping time
                _app?.Invoke(() => { UpdateConnectionStatusLabel(); }); // Update status bar
                return; // DO NOT ADD PING EVENTS TO THE GRID
            }

            lock (_lock)
            {
                _events.Insert(0, evt); 
                while (_events.Count > _maxEvents) _events.RemoveAt(_events.Count - 1);
            }

            _app?.Invoke(() =>
            {
                UpdateConnectionStatusLabel(); // Call this to update connection status and event count
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
                
                // Initial title update
                UpdateTitle();
                try {{ Console.Title = $"OpenSnitch CLI v{_appVersion}"; }} catch {{}}

                var filterLabel = new Label() { Text = "Filter (f):", X = 1, Y = 0 };
                _filterField = new TextField() 
                {
                    X = Pos.Right(filterLabel) + 1,
                    Y = 0,
                    Width = Dim.Fill() - 80 // Made narrower from 50 to 80
                };
                _filterField.TextChanged += (s, e) => {
                     if (_tabView != null) {
                        if (_tabView.SelectedTab == _tabView.Tabs.ElementAt(0)) RefreshTable();
                        else RefreshRulesTable();
                     }
                };
                _win.Add(filterLabel, _filterField);

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
                _tableView.SelectedCellChanged += (s, e) => {
                    if (_tabView != null && _tabView.SelectedTab == _tabView.Tabs.ElementAt(0)) UpdateDetails(e.NewRow);
                };

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
                _rulesTableView.SelectedCellChanged += (s, e) => {
                    if (_tabView != null && _tabView.SelectedTab == _tabView.Tabs.ElementAt(1)) UpdateDetails(e.NewRow);
                };

                _tabView.AddTab(new Tab { DisplayText = "   Connections   ", View = _tableView }, true);
                _tabView.AddTab(new Tab { DisplayText = "   Rules   ", View = _rulesTableView }, false);

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

                _detailsFrame = new FrameView() 
                {
                    Title = "Details",
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
                _detailsFrame.Add(_detailsView);

                _win.Add(_tabView, _detailsFrame);

                _tabView.SelectedTabChanged += (s, e) => {
                    UpdateStatusBar();
                    if (_tabView.SelectedTab == _tabView.Tabs.ElementAt(0))
                        UpdateDetails(_tableView.SelectedRow);
                    else
                        UpdateDetails(_rulesTableView.SelectedRow);
                };

                // Use the instance-level Keyboard.KeyDown event for global hotkeys. 
                // This is the correct way to handle keys when using Application.Create() in v2.
                if (_app?.Keyboard != null)
                {
                    _app.Keyboard.KeyDown += (s, e) => {
                        if (e.Handled) return;
                        
                        // ONLY process hotkeys if the main window is the top runnable (no dialogs open)
                        if (_app.TopRunnable != _win) return;

                        // If the filter field is focused, don't process global hotkeys (like 's' for sort)
                        // so that the user can type freely into the filter.
                        if (_filterField != null && _filterField.HasFocus) 
                        {
                            // If user presses Enter or Esc, move focus back to the current table
                            var baseKeyFocus = e.NoAlt.NoCtrl.NoShift;
                            if (baseKeyFocus == Key.Enter || baseKeyFocus == Key.Esc)
                            {
                                if (_tabView?.SelectedTab == _tabView?.Tabs.ElementAt(0)) _tableView?.SetFocus();
                                else _rulesTableView?.SetFocus();
                                e.Handled = true;
                            }
                            return;
                        }

                        // Normalize the key by removing modifiers for comparison
                        var baseKey = e.NoAlt.NoCtrl.NoShift;
                        var keyCode = e.KeyCode;
                        
                        if (baseKey == Key.Q || keyCode == Key.Q.KeyCode) 
                        {
                            _app?.RequestStop(); 
                            e.Handled = true; 
                        }
                        else if (baseKey == Key.F || keyCode == Key.F.KeyCode)
                        {
                            _filterField?.SetFocus();
                            e.Handled = true;
                        }
                        else if (baseKey == Key.D0 || keyCode == Key.D0.KeyCode) 
                        {
                            CycleTheme(); 
                            e.Handled = true; 
                        }
                        else if (baseKey == Key.L || keyCode == Key.L.KeyCode)
                        {
                            CycleLimit();
                            e.Handled = true;
                        }
                        else if (baseKey == Key.S || keyCode == Key.S.KeyCode || keyCode == (Key.S.KeyCode | Terminal.Gui.Drivers.KeyCode.ShiftMask))
                        {
                            if (e.IsShift || (keyCode & Terminal.Gui.Drivers.KeyCode.ShiftMask) != 0) ToggleSortOrder();
                            else CycleSort();
                            e.Handled = true;
                        }
                        else if (baseKey == Key.C || keyCode == Key.C.KeyCode)
                        {
                            if (_tabView != null) {
                                _tabView.SelectedTab = _tabView.Tabs.ElementAt(0);
                                e.Handled = true;
                            }
                        }
                        else if (baseKey == Key.R || keyCode == Key.R.KeyCode)
                        {
                            if (_tabView != null) {
                                _tabView.SelectedTab = _tabView.Tabs.ElementAt(1);
                                e.Handled = true;
                            }
                        }
                        else if (baseKey == Key.J || keyCode == Key.J.KeyCode)
                        {
                            HandleJumpToRule();
                            e.Handled = true;
                        }
                        else if (baseKey == Key.P || keyCode == Key.P.KeyCode)
                        {
                            if (_tabView != null && _tabView.SelectedTab == _tabView.Tabs.ElementAt(0))
                            {
                                _showFullProcessCommand = !_showFullProcessCommand;
                                RefreshTable();
                                e.Handled = true;
                            }
                        }
                        else if (baseKey == Key.T || keyCode == Key.T.KeyCode)
                        {
                            if (_tabView != null && _tabView.SelectedTab == _tabView.Tabs.ElementAt(1) && _rulesTableView != null)
                            {
                                int row = _rulesTableView.SelectedRow;
                                var sortedRules = GetSortedRules();
                                if (row >= 0 && row < sortedRules.Count)
                                {
                                    dynamic rule = sortedRules[row];
                                    rule.Enabled = !rule.Enabled;
                                    OnRuleChanged?.Invoke(rule);
                                    RefreshRulesTable();
                                    e.Handled = true;
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
                        else if (baseKey == (Key)'?' || baseKey == Key.F1)
                        {
                            ShowHelp();
                            e.Handled = true;
                        }
                    };
                }

                // Try to use the modern v2 KeyBindings if they work as an extra layer
                try {
                    _win.KeyBindings.Add(Key.Q, Command.Quit);
                } catch {}

                _statusBar = new StatusBar();
                _win.Add(_statusBar);
                _win.Add(_statusLabel);
                UpdateStatusBar();

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
            catch {{ }}
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
            catch {{ return null; }}
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
                // Moved CreateScheme to be a private method, not a local function
                // to resolve potential compiler confusion with array initializers
                // in InitCustomThemes scope.
                
                CreateScheme("Matrix", Color.BrightGreen, Color.Black, Color.Black, Color.BrightGreen);
                CreateScheme("Red", Color.Red, Color.Black, Color.White, Color.Red);
                CreateScheme("SolarizedDark", Color.Cyan, Color.Black, Color.White, Color.DarkGray);
                CreateScheme("SolarizedLight", Color.Black, Color.White, Color.Blue, Color.White);
                CreateScheme("Monokai", Color.White, Color.Black, Color.Magenta, Color.Black);
                CreateScheme("Dracula", Color.White, Color.DarkGray, Color.Magenta, Color.DarkGray);
                CreateScheme("Nord", Color.White, Color.Blue, Color.Cyan, Color.Blue);

                // Helix inspired themes
                CreateScheme("Catppuccin", Color.BrightMagenta, Color.Black, Color.BrightCyan, Color.Black);
                CreateScheme("AyuMirage", Color.BrightYellow, Color.Black, Color.BrightRed, Color.Black);
                CreateScheme("Everforest", Color.BrightGreen, Color.DarkGray, Color.BrightYellow, Color.DarkGray);
                CreateScheme("Gruvbox", Color.Yellow, Color.Black, Color.BrightRed, Color.Black);

                CreateScheme("RowAllow", Color.BrightGreen, Color.Black, Color.Black, Color.BrightGreen); 
                CreateScheme("RowDeny", Color.BrightRed, Color.Black, Color.White, Color.BrightRed);   
                CreateScheme("RowAsk", Color.BrightYellow, Color.Black, Color.Black, Color.BrightYellow); 

                _themesInitialized = true;
            }
            catch {{ }}
        }
        
        // Converted CreateScheme from local function to private method
        private void CreateScheme(string name, Color fg, Color bg, Color focusFg, Color focusBg)
        {
            try {
                // Re-obtain reflection data within this method's scope
                var schemeManagerType = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType("Terminal.Gui.Configuration.SchemeManager"))
                        .FirstOrDefault(t => t != null);
                var addSchemeMethod = schemeManagerType?.GetMethods()
                    .FirstOrDefault(m => m.Name == "AddScheme" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(string));
                var schemeType = addSchemeMethod?.GetParameters()[1].ParameterType;
                var normalProp = schemeType?.GetProperty("Normal");
                var attrType = normalProp?.PropertyType;

                object CreateAttr(Color fgColor, Color bgColor) // Local CreateAttr
                {
                    return Activator.CreateInstance(attrType, fgColor, bgColor)!;
                }

                var scheme = Activator.CreateInstance(schemeType);
                schemeType.GetProperty("Normal")?.SetValue(scheme, CreateAttr(fg, bg));
                schemeType.GetProperty("Focus")?.SetValue(scheme, CreateAttr(focusFg, focusBg));
                schemeType.GetProperty("HotNormal")?.SetValue(scheme, CreateAttr(fg, bg));
                schemeType.GetProperty("HotFocus")?.SetValue(scheme, CreateAttr(focusFg, focusBg));
                addSchemeMethod?.Invoke(null, new object[] { name, scheme });
                if (name.StartsWith("Row")) _rowSchemes[name] = scheme;
            } catch {{ }} 
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
                if (_statusBar != null) _statusBar.SchemeName = next;
                if (_app?.TopRunnableView != null) _app.TopRunnableView.SchemeName = next;
                _win.SetNeedsDraw();
                UpdateStatusBar();
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery(_app, "Error", $"Failed to cycle: {ex.Message}", "Ok");
            }
        }

        private List<TuiEvent> GetSortedEvents()
        {
            IEnumerable<TuiEvent> filtered = _events;
            if (_filterField != null && _filterField.Text.Length > 0)
            {
                var term = _filterField.Text.ToString()!.ToLower();
                filtered = filtered.Where(e => (e.Source ?? "").ToLower().Contains(term));
            }

            IOrderedEnumerable<TuiEvent> query;
            bool asc = _connSortAsc;
            switch (_connSortCol)
            {
                case 0: query = asc ? filtered.OrderBy(e => e.Timestamp) : filtered.OrderByDescending(e => e.Timestamp); break;
                case 1: query = asc ? filtered.OrderBy(e => e.Type) : filtered.OrderByDescending(e => e.Type); break;
                case 2: query = asc ? filtered.OrderBy(e => e.Pid) : filtered.OrderByDescending(e => e.Pid); break;
                case 3: query = asc ? filtered.OrderBy(e => e.Details) : filtered.OrderByDescending(e => e.Details); break; // User is in details currently
                case 4: query = asc ? filtered.OrderBy(e => e.Source) : filtered.OrderByDescending(e => e.Source); break;
                case 5: query = asc ? filtered.OrderBy(e => e.DestinationIp) : filtered.OrderByDescending(e => e.DestinationIp); break;
                case 6: query = asc ? filtered.OrderBy(e => e.DestinationPort) : filtered.OrderByDescending(e => e.DestinationPort); break;
                case 7: query = asc ? filtered.OrderBy(e => e.Protocol) : filtered.OrderByDescending(e => e.Protocol); break;
                default: query = filtered.OrderByDescending(e => e.Timestamp); break;
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
                // Enforce limit in case it was just lowered
                while (_events.Count > _maxEvents) _events.RemoveAt(_events.Count - 1);

                var sorted = GetSortedEvents();
                foreach (var evt in sorted)
                {
                    string user = "";
                    var match = Regex.Match(evt.Details ?? "", @"U(?:ID|ser):\s*(\d+)");
                    if (match.Success) user = _userManager.GetUser(match.Groups[1].Value);
                    var address = _dnsManager.GetDisplayName(evt.DestinationIp, evt.DestinationHost);
                    string typeStr = evt.Type;
                    if (typeStr == "ALLOW") typeStr = "✓ ALLOW";
                    else if (typeStr == "DENY") typeStr = "❌ DENY";
                    else if (typeStr == "AskRule") typeStr = "? ASK";

                    _dt.Rows.Add(
                        evt.Timestamp.ToString("HH:mm:ss.fff"),
                        typeStr,
                        evt.Pid,
                        user,
                        (evt.IsInNamespace ? "📦 " : "") + ((_showFullProcessCommand && !string.IsNullOrEmpty(evt.Command)) ? evt.Command : evt.Source),
                        address,
                        evt.DestinationPort,
                        evt.Protocol
                    );
                }
            }
            if (_statusLabel != null) _statusLabel.Text = $"{GetConnectionStatusIcon()} {GetConnectionStatusText()} | Events: {_events.Count}/{_maxEvents}";
            _tableView.SetNeedsDraw();
        }
        
        // Helper methods for connection status
        private string GetConnectionStatusIcon()
        {
            bool isAlive = (DateTime.Now - _lastPingTime).TotalSeconds < 5;
            return isAlive ? "●" : "○";
        }

        private string GetConnectionStatusText()
        {
            bool isAlive = (DateTime.Now - _lastPingTime).TotalSeconds < 5;
            return isAlive ? "Online" : "No Signal";
        }

        private void UpdateDetails(int row)
        {
            if (_detailsView == null || row < 0 || _tabView == null || _detailsFrame == null) return;

            lock (_lock)
            {
                if (_tabView.SelectedTab == _tabView.Tabs.ElementAt(0)) // Connections
                {
                    _detailsFrame.Title = "Connection Details";
                    var sorted = GetSortedEvents();
                    if (row >= sorted.Count) return;
                    var evt = sorted[row];

                    var dns = _dnsManager.GetDisplayName(evt.DestinationIp, evt.DestinationHost);
                    string user = "";
                    var match = Regex.Match(evt.Details ?? "", @"U(?:ID|ser):\s*(\d+)");
                    if (match.Success) user = _userManager.GetUser(match.Groups[1].Value);

                    var description = DescriptionManager.Instance.GetDescription(evt.Source ?? "");
                    var aboutText = !string.IsNullOrEmpty(description) ? $"\nAbout:       {description}" : "";
                    
                    var originText = "";
                    if (evt.IsFlatpak) originText = "\nOrigin:      📦 Flatpak (Sandboxed)";
                    else if (evt.IsInNamespace) originText = "\nOrigin:      📦 Container/Namespace";

                    _detailsView.Text = $"Timestamp:   {evt.Timestamp:yyyy-MM-dd HH:mm:ss.fff}\n" +
                                       $"Type:        {evt.Type}\n" +
                                       $"Protocol:    {evt.Protocol}\n" +
                                       $"PID:         {evt.Pid}\n" +
                                       $"User:        {user}\n" +
                                       $"Program:     {evt.Source}{originText}{aboutText}\n" +
                                       $"Destination: {evt.DestinationIp} ({dns}) : {evt.DestinationPort}\n" +
                                       $"Details:     {evt.Details}";
                }
                else // Rules
                {
                    _detailsFrame.Title = "Rule Details";
                    var sorted = GetSortedRules();
                    if (row >= sorted.Count) return;
                    dynamic rule = sorted[row];

                    var created = DateTimeOffset.FromUnixTimeSeconds((long)rule.Created).LocalDateTime;
                    _detailsView.Text = $"Name:        {rule.Name}\n" +
                                       $"Enabled:     {rule.Enabled}\n" +
                                       $"Action:      {rule.Action}\n" +
                                       $"Duration:    {rule.Duration}\n" +
                                       $"Precedence:  {(rule.Precedence ? "High" : "Normal")}\n" +
                                       $"NoLog:       {rule.Nolog}\n" +
                                       $"Created:     {created:yyyy-MM-dd HH:mm:ss}\n" +
                                       $"Condition:   {rule.Operator?.Operand} {rule.Operator?.Type} {rule.Operator?.Data}\n" +
                                       $"Description: {rule.Description}";
                }
            }
        }
    }
}