using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Google.Protobuf;
using OpenSnitchCli.Services;
using OpenSnitchTUI;
using OpenSnitchTGUI;
using Protocol; // For UI.BindService & Message types
using SysProcess = System.Diagnostics.Process;
using SysProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace OpenSnitchCli
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Contains("--help") || args.Contains("-h"))
            {
                Console.WriteLine("OpenSnitch C# CLI Listener");
                Console.WriteLine("Usage: dotnet run -- [options]");
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine("  --tui        Start in Spectre.Console streaming mode");
                Console.WriteLine("  --dump       Start in raw JSON dumping mode");
                Console.WriteLine("  --help, -h   Show this help message");
                Console.WriteLine();
                Console.WriteLine("Default: Starts in Terminal.Gui v2 interactive mode");
                return;
            }

            bool useTui = args.Contains("--tui");
            bool useDump = args.Contains("--dump");
            bool useTui2 = !useTui && !useDump; // Default to TUI2 if no other mode specified
            string? debugLogFilePath = null;

            // Setup manual logging
            var loggerFactoryBuilder = LoggerFactory.Create(builder =>
            {
                builder.AddFilter("Microsoft", LogLevel.Warning);
                builder.AddFilter("System", LogLevel.Warning);
                builder.AddFilter("OpenSnitchCli", LogLevel.Debug);
                
                if (useTui || useTui2)
                {
                    debugLogFilePath = Path.Combine(Path.GetTempPath(), $"opensnitch_tui_debug_{DateTime.Now:yyyyMMddHHmmss}.log");
                    try
                    {
                        var logFileStream = File.AppendText(debugLogFilePath);
                        builder.AddProvider(new CustomTextWriterLoggerProvider(logFileStream));
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Failed to create debug log file {debugLogFilePath}: {ex.Message}");
                        debugLogFilePath = null;
                    }
                }
                else
                {
                    builder.AddConsole();
                }
            });
            var logger = loggerFactoryBuilder.CreateLogger<UiService>();

            // Clean up socket file
            var socketPath = "/tmp/osui.sock";
            if (File.Exists(socketPath)) File.Delete(socketPath);

            var uiService = new UiService(logger, !(useTui || useTui2));

            // TUI / CLI Setup
            TuiManager? tuiManager = null;
            TGuiManager? tguiManager = null;
            CancellationTokenSource cts = new CancellationTokenSource();

            if (useTui)
            {
                tuiManager = new TuiManager();
                uiService.OnMessageReceived += (method, msg) =>
                {
                    if (method == "NotificationReply")
                    {
                        // logger.LogDebug("TUI: Filtering NotificationReply from display.");
                        return;
                    }
                    var events = MapToTui(method, msg);
                    foreach (var evt in events) tuiManager.AddEvent(evt);
                };
            }
            else if (useTui2)
            {
                tguiManager = new TGuiManager();
                uiService.OnMessageReceived += (method, msg) =>
                {
                    if (method == "NotificationReply")
                    {
                        // logger.LogDebug("TGUI: Filtering NotificationReply.");
                        return;
                    }
                    var events = MapToTui(method, msg);
                    foreach (var evt in events) tguiManager.AddEvent(evt);
                };

                uiService.OnRulesReceived += (rules) => {
                    tguiManager.UpdateRules(rules);
                };

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
                    var req = new PromptRequest
                    {
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
                    
                    var rule = new Protocol.Rule 
                    {
                        Action = res.Action,
                        Duration = res.Duration,
                        Enabled = true,
                        Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        Name = res.IsCustom ? res.CustomName : $"Rule for {conn.ProcessPath}",
                        Operator = new Operator 
                        { 
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
                uiService.OnMessageReceived += (method, msg) =>
                {
                    try
                    {
                        Console.WriteLine(formatter.Format(msg));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error formatting: {ex.Message}");
                    }
                };
            }

            // Configure Server
            var server = new Server
            {
                Services = { UI.BindService(uiService) },
                Ports = { new ServerPort("0.0.0.0", 50051, ServerCredentials.Insecure) }
            };

            SysProcess? socatProcess = null;

            try
            {
                server.Start();
                
                if (File.Exists(socketPath)) File.Delete(socketPath);
                
                if (!useTui && !useTui2) Console.WriteLine($"Starting socat proxy: {socketPath} -> 127.0.0.1:50051");
                
                var startInfo = new SysProcessStartInfo
                {
                    FileName = "socat",
                    Arguments = $"UNIX-LISTEN:{socketPath},fork,mode=777 TCP:127.0.0.1:50051",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                socatProcess = SysProcess.Start(startInfo);

                if (!useTui && !useTui2)
                {
                    Console.WriteLine("Listening on TCP: 0.0.0.0:50051");
                    Console.WriteLine("Press any key to stop the server...");
                }

                // Handle Shutdown
                Console.CancelKeyPress += (sender, e) => {
                    e.Cancel = true;
                    cts.Cancel();
                    if (useTui2) tguiManager.Stop();
                };

                if (useTui && tuiManager != null)
                {
                    await tuiManager.RunAsync(cts.Token, () => cts.Cancel());
                }
                else if (useTui2 && tguiManager != null)
                {
                    // TGUI runs on main thread blocking
                    tguiManager.Run();
                    // When Run returns (Application.RequestStop called), we exit
                    cts.Cancel();
                }
                else
                {
                    try { await Task.Delay(-1, cts.Token); } catch (TaskCanceledException) { }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FATAL ERROR: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                if (!useTui && !useTui2) Console.Error.WriteLine($"Error starting server: {ex.Message}");
            }
            finally
            {
                loggerFactoryBuilder.Dispose();

                if (!useTui && !useTui2) Console.WriteLine("Shutting down...");
                
                if (socatProcess != null && !socatProcess.HasExited)
                {
                    try { socatProcess.Kill(); socatProcess.WaitForExit(); } catch {}
                }

                await server.ShutdownAsync();
                if (File.Exists(socketPath)) File.Delete(socketPath);

                if ((useTui || useTui2) && !string.IsNullOrEmpty(debugLogFilePath) && File.Exists(debugLogFilePath))
                {
                    Console.WriteLine("\n--- TUI Debug Log ---");
                    Console.WriteLine(File.ReadAllText(debugLogFilePath));
                    Console.WriteLine("---------------------\n");
                    File.Delete(debugLogFilePath);
                }
            }
        }

        static IEnumerable<TuiEvent> MapToTui(string method, IMessage msg)
        {
            var list = new List<TuiEvent>();

            if (msg is Protocol.Connection conn)
            {
                var type = method == "ALLOW" || method == "DENY" ? method : (method == "AskRule" ? "AskRule" : "Connection");
                var evt = new TuiEvent { Timestamp = DateTime.Now, Type = type };
                evt.Protocol = conn.Protocol;
                evt.Source = !string.IsNullOrEmpty(conn.ProcessPath) ? conn.ProcessPath : $"PID: {conn.ProcessId}";
                evt.Pid = conn.ProcessId.ToString();
                evt.DestinationIp = conn.DstIp;
                evt.DestinationPort = conn.DstPort.ToString();
                evt.Details = $"User: {conn.UserId}";
                
                // Simple heuristic for namespace/container: empty path usually means 
                // daemon couldn't find it in host /proc, often due to namespaces.
                // Also check if path starts with known container patterns if needed.
                evt.IsInNamespace = string.IsNullOrEmpty(conn.ProcessPath);
                
                list.Add(evt);
            }
            else if (msg is Protocol.Alert alert)
            {
                var evt = new TuiEvent { Timestamp = DateTime.Now, Type = "Alert" };
                evt.Details = $"{alert.Action} ({alert.What})";
                
                if (alert.Conn != null)
                {
                    evt.Protocol = alert.Conn.Protocol;
                    evt.Source = alert.Conn.ProcessPath;
                    evt.Pid = alert.Conn.ProcessId.ToString();
                    evt.DestinationIp = alert.Conn.DstIp;
                    evt.DestinationPort = alert.Conn.DstPort.ToString();
                }
                else if (alert.Proc != null)
                {
                    evt.Source = alert.Proc.Path;
                    evt.Pid = alert.Proc.Pid.ToString();
                }
                list.Add(evt);
            }
            else if (msg is Protocol.PingRequest ping)
            {
                list.Add(new TuiEvent { Timestamp = DateTime.Now, Type = "Ping", Details = "Ping" });

                if (ping.Stats != null && ping.Stats.Events != null)
                {
                    foreach (var e in ping.Stats.Events)
                    {
                        var evt = new TuiEvent 
                        {
                            Timestamp = e.Unixnano > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(e.Unixnano / 1000000).LocalDateTime : DateTime.Now,
                            Type = "Monitor" 
                        };
                        
                        if (e.Connection != null)
                        {
                            evt.Protocol = e.Connection.Protocol;
                            evt.Source = e.Connection.ProcessPath;
                            evt.Pid = e.Connection.ProcessId.ToString();
                            evt.DestinationIp = e.Connection.DstIp;
                            evt.DestinationPort = e.Connection.DstPort.ToString();
                            evt.Details = $"UID: {e.Connection.UserId}";

                            if (e.Rule != null)
                            {
                                evt.Type = e.Rule.Action.ToUpper();
                                evt.Details += $" [Rule: {e.Rule.Name}]";
                            }
                        }
                        else if (e.Rule != null)
                        {
                            evt.Type = e.Rule.Action.ToUpper();
                            evt.Details = $"Rule Hit: {e.Rule.Name}";
                        }
                        
                        list.Add(evt);
                    }
                }
            }
            
            return list;
        }
    }

    // Custom logger provider to write to a TextWriter (e.g., a file)
    public class CustomTextWriterLoggerProvider : ILoggerProvider
    {
        private readonly TextWriter _writer;

        public CustomTextWriterLoggerProvider(TextWriter writer)
        {
            _writer = writer;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new CustomTextWriterLogger(categoryName, _writer);
        }

        public void Dispose()
        {
            _writer.Dispose();
        }
    }

    public class CustomTextWriterLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly TextWriter _writer;

        public CustomTextWriterLogger(string categoryName, TextWriter writer)
        {
            _categoryName = categoryName;
            _writer = writer;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default;

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Debug;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{logLevel}] [{_categoryName}] {message}");
            if (exception != null)
            {
                _writer.WriteLine(exception.ToString());
            }
            _writer.Flush();
        }
    }
}