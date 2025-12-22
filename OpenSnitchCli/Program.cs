using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text.Json;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Google.Protobuf;
using OpenSnitchCli.Services;
using OpenSnitchTUI;
using OpenSnitchTGUI;
using Protocol; 

namespace OpenSnitchCli
{
    class Program
    {
        private static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        private static ILogger<UiService> StaticLogger = null!; // Will be initialized in Main

        static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                File.AppendAllText("crash_log.txt", $"[{DateTime.Now}] UNHANDLED DOMAIN EXCEPTION: {e.ExceptionObject}\n");
            };

            try {
                try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch {}

                if (args.Contains("--help") || args.Contains("-h"))
                {
                    Console.WriteLine($"OpenSnitch C# CLI Listener v{Version}");
                    Console.WriteLine("Usage: opensnitch-cli [options]");
                    Console.WriteLine();
                    Console.WriteLine("Options:");
                    Console.WriteLine("  --tui        Start in Spectre.Console streaming mode");
                    Console.WriteLine("  --dump       Start in raw JSON dumping mode");
                    Console.WriteLine("  --cfg <file> Load initial configuration from JSON file");
                    Console.WriteLine("  --gen-cfg    Generate a sample configuration file (cfg.json)");
                    Console.WriteLine("  --help, -h   Show this help message");
                    Console.WriteLine();
                    Console.WriteLine("Default: Starts in Terminal.Gui v2 interactive mode");
                    return;
                }

                if (args.Contains("--gen-cfg"))
                {
                    var sample = new AppConfig { 
                        Theme = "Dracula", 
                        Filter = "", 
                        SortColumn = 0, 
                        SortAscending = false, 
                        ShowFullCommand = false, 
                        HistoryLimit = 500 
                    };
                    var json = JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText("cfg.json", json);
                    Console.WriteLine("Generated sample configuration in cfg.json");
                    return;
                }

                // Persistence setup
                string configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "opensnitch-cli");
                string defaultConfigPath = Path.Combine(configDir, "config.json");
                string? targetConfigPath = null;

                AppConfig? config = null;
                int cfgIdx = Array.IndexOf(args, "--cfg");
                if (cfgIdx != -1 && cfgIdx + 1 < args.Length)
                {
                    targetConfigPath = args[cfgIdx + 1];
                }
                else if (File.Exists(defaultConfigPath))
                {
                    targetConfigPath = defaultConfigPath;
                }

                if (targetConfigPath != null)
                {
                    try {
                        var json = File.ReadAllText(targetConfigPath);
                        config = JsonSerializer.Deserialize<AppConfig>(json);
                        // If target was not default but we loaded it, we'll keep using it for saves
                    } catch (Exception ex) {
                        Console.Error.WriteLine($"Error loading config: {ex.Message}");
                    }
                }
                else 
                {
                    // Ensure directory exists for future saves even if file doesn't exist yet
                    try { Directory.CreateDirectory(configDir); } catch {}
                    targetConfigPath = defaultConfigPath;
                }

                bool useTui = args.Contains("--tui");
                bool useDump = args.Contains("--dump");
                bool useTui2 = !useTui && !useDump; 

                string? debugLogFilePath = null;
                ILoggerProvider? fileLoggerProvider = null;

                if (useTui || useTui2)
                {
                    debugLogFilePath = Path.Combine(Path.GetTempPath(), $"opensnitch_tui_debug_{DateTime.Now:yyyyMMddHHmmss}.log");
                    try
                    {
                        var logFileStream = File.AppendText(debugLogFilePath);
                        fileLoggerProvider = new CustomTextWriterLoggerProvider(logFileStream);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Failed to create debug log file {debugLogFilePath}: {ex.Message}");
                    }
                }

                var loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder.AddFilter("Microsoft", LogLevel.Warning);
                    builder.AddFilter("System", LogLevel.Warning);
                    builder.AddFilter("OpenSnitchCli", LogLevel.Debug);
                    builder.AddFilter("OpenSnitchTUI", LogLevel.Debug); // Add this filter
                    if (fileLoggerProvider != null) builder.AddProvider(fileLoggerProvider); else builder.AddConsole();
                });
                var logger = loggerFactory.CreateLogger<UiService>();
                Program.StaticLogger = logger;

                var uiService = new UiService(logger, !(useTui || useTui2));

                // TUI / CLI Setup
                TuiManager? tuiManager = null;
                TGuiManager? tguiManager = null;
                CancellationTokenSource cts = new CancellationTokenSource();

                if (useTui)
                {
                    tuiManager = new TuiManager(loggerFactory.CreateLogger<TuiManager>());
                    uiService.OnMessageReceived += (method, msg) => {
                        if (method == "NotificationReply") return;
                        var events = MapToTui(method, msg);
                        foreach (var evt in events) tuiManager.AddEvent(evt);
                    };
                }
                else if (useTui2)
                {
                    tguiManager = new TGuiManager();
                    tguiManager.ConfigPath = targetConfigPath;
                    if (config != null) tguiManager.ApplyConfig(config);
                    tguiManager.SetVersions(Version, "Connecting...");
                    uiService.OnMessageReceived += (method, msg) => {
                        if (method == "NotificationReply") return;
                        var events = MapToTui(method, msg);
                        foreach (var evt in events) tguiManager.AddEvent(evt);
                    };
                    uiService.OnRulesReceived += (rules) => tguiManager.UpdateRules(rules);
                    uiService.OnDaemonConnected += (daemonVer) => tguiManager.SetVersions(Version, daemonVer);
                    tguiManager.OnRuleChanged += async (ruleObj) => {
                        var rule = (Protocol.Rule)ruleObj;
                        await uiService.SendNotificationAsync(new Protocol.Notification {
                            Id = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            Type = Protocol.Action.ChangeRule,
                            Rules = { rule }
                        });
                    };
                    tguiManager.OnRuleDeleted += async (ruleObj) => {
                        var rule = (Protocol.Rule)ruleObj;
                        await uiService.SendNotificationAsync(new Protocol.Notification {
                            Id = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            Type = Protocol.Action.DeleteRule,
                            Rules = { rule }
                        });
                    };
                    uiService.AskRuleHandler = async (conn) => {
                        var isFlatpak = !string.IsNullOrEmpty(conn.ProcessPath) && 
                                    (conn.ProcessPath.StartsWith("/app/") || 
                                        conn.ProcessPath.Contains("/flatpak/"));
                        var req = new PromptRequest { 
                            Process = conn.ProcessPath,
                            Destination = $"{conn.DstIp}:{conn.DstPort}",
                            Description = $"{conn.Protocol} connection from {conn.SrcIp}",
                            Protocol = conn.Protocol,
                            DestHost = conn.DstHost,
                            DestIp = conn.DstIp,
                            DestPort = conn.DstPort.ToString(),
                            UserId = conn.UserId.ToString()
                        };
                        var res = await tguiManager.PromptForRule(req);
                        var rule = new Protocol.Rule { 
                            Action = res.Action,
                            Duration = res.Duration,
                            Enabled = true,
                            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            Name = res.IsCustom ? res.CustomName : $"Rule for {conn.ProcessPath}",
                            Operator = new Operator { 
                                Type = "simple", 
                                Operand = res.IsCustom ? res.CustomOperand : "process.path", 
                                Data = res.IsCustom ? res.CustomData : conn.ProcessPath 
                            }
                        };
                        tguiManager.AddRule(rule);
                        return rule;
                    };
                }
                else if (useDump)
                {
                    var formatter = new JsonFormatter(JsonFormatter.Settings.Default.WithFormatDefaultValues(true));
                    uiService.OnMessageReceived += (method, msg) => {
                        try { Console.WriteLine(formatter.Format(msg)); } catch {}
                    };
                }

                // 1. Start Grpc.Core Server on TCP
                const int tcpPort = 50052;
                var server = new Server {
                    Services = { UI.BindService(uiService) },
                    Ports = { new ServerPort("0.0.0.0", tcpPort, ServerCredentials.Insecure) }
                };

                try {
                    server.Start();
                    if (!useTui && !useTui2) Console.WriteLine($"gRPC Server started on 0.0.0.0:{tcpPort}");
                    else if (useTui2) Console.Title = $"OpenSnitch CLI v{Version} | UDS Proxy /tmp/osui.sock -> 127.0.0.1:{tcpPort}";

                    // 2. Start UDS -> TCP Proxy
                    var socketPath = "/tmp/osui.sock";
                    _ = Task.Run(() => RunUdsProxy(socketPath, tcpPort, logger, cts.Token));

                    // Handle Shutdown
                    Console.CancelKeyPress += (sender, e) => {
                        e.Cancel = true;
                        cts.Cancel();
                        if (useTui2) tguiManager?.Stop();
                    };

                    if (useTui && tuiManager != null) {
                        await tuiManager.RunAsync(cts.Token, () => cts.Cancel());
                    } else if (useTui2 && tguiManager != null) {
                        tguiManager.Run();
                        cts.Cancel();
                    } else {
                        try { await Task.Delay(-1, cts.Token); } catch (TaskCanceledException) {}
                    }
                } catch (Exception ex) {
                    if (!useTui && !useTui2) Console.Error.WriteLine($"FATAL: {ex.Message}");
                    File.AppendAllText("crash_log.txt", $"[{DateTime.Now}] SERVER EXCEPTION: {ex}\n");
                } finally {
                    loggerFactory.Dispose();
                    await server.ShutdownAsync();
                    if (File.Exists("/tmp/osui.sock")) File.Delete("/tmp/osui.sock");
                    
                    if ((useTui || useTui2) && !string.IsNullOrEmpty(debugLogFilePath) && File.Exists(debugLogFilePath)) {
                        Console.WriteLine("\n--- TUI Debug Log ---");
                        Console.WriteLine(File.ReadAllText(debugLogFilePath));
                        File.Delete(debugLogFilePath);
                    }
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText("crash_log.txt", $"[{DateTime.Now}] FATAL MAIN EXCEPTION: {ex}\n");
                Console.Error.WriteLine($"A fatal error occurred. Details written to crash_log.txt");
            }
        }

        private static async Task RunUdsProxy(string socketPath, int tcpPort, ILogger logger, CancellationToken ct)
        {
            try {
                if (File.Exists(socketPath)) File.Delete(socketPath);
                
                using var listenSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                listenSocket.Bind(new UnixDomainSocketEndPoint(socketPath));
                
                // Set 777 permissions
                try {
                    File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                                   UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                                                   UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
                } catch (Exception ex) {
                    logger.LogWarning($"Could not set socket permissions: {ex.Message}");
                }

                listenSocket.Listen(128);
                logger.LogInformation($"UDS Proxy listening on {socketPath} -> 127.0.0.1:{tcpPort}");

                while (!ct.IsCancellationRequested) {
                    var clientSocket = await listenSocket.AcceptAsync(ct);
                    _ = Task.Run(async () => {
                        try {
                            using var tcpClient = new TcpClient();
                            // Retry logic for backend connection
                            int retries = 3;
                            while (retries > 0)
                            {
                                try
                                {
                                    await tcpClient.ConnectAsync("127.0.0.1", tcpPort, ct);
                                    break; // Connected
                                }
                                catch (SocketException) when (retries > 1)
                                {
                                    retries--;
                                    await Task.Delay(100, ct);
                                }
                            }
                            
                            if (!tcpClient.Connected) throw new Exception("Failed to connect to backend after retries");

                            using var udsStream = new NetworkStream(clientSocket, true);
                            using var tcpStream = tcpClient.GetStream();

                            var t1 = udsStream.CopyToAsync(tcpStream, ct);
                            var t2 = tcpStream.CopyToAsync(udsStream, ct);
                            await Task.WhenAny(t1, t2);
                        } catch (Exception ex) {
                            logger.LogDebug($"Proxy connection error: {ex.Message}");
                        }
                    });
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                logger.LogError($"UDS Proxy critical error: {ex.Message}");
            }
        }

        static IEnumerable<TuiEvent> MapToTui(string method, IMessage msg)
        {
            var list = new List<TuiEvent>();
            // Add debug logging for incoming messages
            // logger is not directly accessible here, so use Console.WriteLine to the debug file
            // The logger for UiService is created with fileLoggerProvider if useTui/useTui2 is true.
            // So we need to pass logger here or create a new one.

            // Since logger is a static field, it's not possible to pass it directly.
            // I'll make a helper that uses ILogger from UiService to log messages.
            // This requires making logger a field in Program class.
            // This is a static method, so I can't use instance logger.

            // I'll use a static logger or pass it.
            // Let's modify Program.Main to pass the logger.
            // This will affect Main signature.

            // I'll change MapToTui to accept an ILogger.
            // The method is static.

            // Alternative: use a static logger reference.
            // I'll add a static ILogger field to Program.

            // Let's create a temporary logger here.
            // No, the logger is already set up to go to debug file if in TUI mode.
            // I'll make an anonymous function to map.

            // Best way: pass ILogger logger to MapToTui.
            // This means I have to change the signature of MapToTui.
            // This changes a lot of calls to MapToTui.

            // The simplest approach without refactoring everything:
            // Just use a Console.WriteLine to stderr, and assume user is running with redirection to file.
            // No, the debug file is used.

            // I need to make `logger` available in `MapToTui`.
            // The `logger` is a local variable in `Main`.
            // I'll make `logger` a static field in `Program` class.
            
            // Program.StaticLogger.LogDebug($"MapToTui received: Method={method}, MsgType={msg.GetType().Name}");

            if (msg is Protocol.Connection conn) {
                var type = method == "ALLOW" || method == "DENY" ? method : (method == "AskRule" ? "AskRule" : "Connection");
                
                var containerInfo = ProcessInfoHelper.GetProcessContext((int)conn.ProcessId);
                var isFlatpak = containerInfo.Type == "Flatpak" || (!string.IsNullOrEmpty(conn.ProcessPath) && (conn.ProcessPath.StartsWith("/app/") || conn.ProcessPath.Contains("/flatpak/")));
                var isNamespace = (containerInfo.IsContainer && containerInfo.Type != "Flatpak") || string.IsNullOrEmpty(conn.ProcessPath);

                var evt = new TuiEvent { 
                    Timestamp = DateTime.Now, 
                    Type = type, 
                    Protocol = conn.Protocol, 
                    Source = !string.IsNullOrEmpty(conn.ProcessPath) ? conn.ProcessPath : $"PID: {conn.ProcessId}",
                    Command = (conn.ProcessArgs != null && conn.ProcessArgs.Count > 0) ? string.Join(" ", conn.ProcessArgs) : conn.ProcessPath,
                    Pid = conn.ProcessId.ToString(), 
                    DestinationIp = conn.DstIp, 
                    DestinationPort = conn.DstPort.ToString(), 
                    DestinationHost = conn.DstHost,
                    Details = $"User: {conn.UserId}", 
                    IsInNamespace = isNamespace,
                    IsFlatpak = isFlatpak,
                    IsDaemon = containerInfo.IsDaemon,
                    ContainerType = containerInfo.Type
                };
                
                if (containerInfo.IsContainer && !string.IsNullOrEmpty(containerInfo.Details) && containerInfo.Details != "Unknown")
                {
                    evt.Details += $" [{containerInfo.Type}: {containerInfo.Details}]";
                }
                
                list.Add(evt);
            } else if (msg is Protocol.Alert alert) {
                Program.StaticLogger.LogDebug($"MapToTui Alert: {alert.What} from {alert.Conn?.DstIp ?? alert.Proc?.Path}");
                var evt = new TuiEvent { Timestamp = DateTime.Now, Type = "Alert", Details = $"{alert.Action} ({alert.What})" };
                int pid = 0;
                if (alert.Conn != null) { 
                    evt.Protocol = alert.Conn.Protocol; 
                    evt.Source = alert.Conn.ProcessPath;
                    evt.Command = (alert.Conn.ProcessArgs != null && alert.Conn.ProcessArgs.Count > 0) ? string.Join(" ", alert.Conn.ProcessArgs) : alert.Conn.ProcessPath;
                    evt.Pid = alert.Conn.ProcessId.ToString(); 
                    evt.DestinationIp = alert.Conn.DstIp; 
                    evt.DestinationPort = alert.Conn.DstPort.ToString(); 
                    evt.DestinationHost = alert.Conn.DstHost;
                    pid = (int)alert.Conn.ProcessId;
                }
                else if (alert.Proc != null) { 
                    evt.Source = alert.Proc.Path; 
                    evt.Command = (alert.Proc.Args != null && alert.Proc.Args.Count > 0) ? string.Join(" ", alert.Proc.Args) : alert.Proc.Path;
                    evt.Pid = alert.Proc.Pid.ToString(); 
                    pid = (int)alert.Proc.Pid;
                }
                if (pid > 0) {
                    var ctx = ProcessInfoHelper.GetProcessContext(pid);
                    evt.IsDaemon = ctx.IsDaemon;
                    evt.ContainerType = ctx.Type;
                    evt.IsFlatpak = ctx.Type == "Flatpak";
                    evt.IsInNamespace = ctx.IsContainer && ctx.Type != "Flatpak";
                }
                list.Add(evt);
            } else if (msg is Protocol.PingRequest ping && ping.Stats?.Events != null) {
                list.Add(new TuiEvent { Timestamp = DateTime.Now, Type = "Ping", Details = "Ping" });
                foreach (var e in ping.Stats.Events) {
                    var evt = new TuiEvent { Timestamp = e.Unixnano > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(e.Unixnano / 1000000).LocalDateTime : DateTime.Now, Type = "Monitor" };
                    if (e.Connection != null) {
                        evt.Protocol = e.Connection.Protocol; 
                        evt.Source = e.Connection.ProcessPath; 
                        evt.Command = (e.Connection.ProcessArgs != null && e.Connection.ProcessArgs.Count > 0) ? string.Join(" ", e.Connection.ProcessArgs) : e.Connection.ProcessPath;
                        evt.Pid = e.Connection.ProcessId.ToString(); 
                        evt.DestinationIp = e.Connection.DstIp; 
                        evt.DestinationPort = e.Connection.DstPort.ToString(); 
                        evt.DestinationHost = e.Connection.DstHost;
                        evt.Details = $"UID: {e.Connection.UserId}";
                        if (e.Rule != null) { evt.Type = e.Rule.Action.ToUpper(); evt.Details += $" [Rule: {e.Rule.Name}]"; }
                        
                        var ctx = ProcessInfoHelper.GetProcessContext((int)e.Connection.ProcessId);
                        evt.IsDaemon = ctx.IsDaemon;
                        evt.ContainerType = ctx.Type;
                        evt.IsFlatpak = ctx.Type == "Flatpak";
                        evt.IsInNamespace = ctx.IsContainer && ctx.Type != "Flatpak";
                    } else if (e.Rule != null) { evt.Type = e.Rule.Action.ToUpper(); evt.Details = $"Rule Hit: {e.Rule.Name}"; }
                    list.Add(evt);
                }
            }
            return list;
        }
    }

    public class CustomTextWriterLoggerProvider : ILoggerProvider {
        private readonly TextWriter _writer;
        public CustomTextWriterLoggerProvider(TextWriter writer) => _writer = writer;
        public ILogger CreateLogger(string categoryName) => new CustomTextWriterLogger(categoryName, _writer);
        public void Dispose() => _writer.Dispose();
    }

    public class CustomTextWriterLogger : ILogger {
        private readonly string _categoryName;
        private readonly TextWriter _writer;
        public CustomTextWriterLogger(string categoryName, TextWriter writer) { _categoryName = categoryName; _writer = writer; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
            var message = formatter(state, exception);
            _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{logLevel}] [{_categoryName}] {message}");
            if (exception != null) _writer.WriteLine(exception.ToString());
            _writer.Flush();
        }
    }
}