using System;
using System.IO;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using OpenSnitchCli.Services;
using Protocol; // For UI.BindService
using SysProcess = System.Diagnostics.Process;
using SysProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace OpenSnitchCli
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Setup manual logging since we are not using Host builder
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)
                    .AddFilter("OpenSnitchCli", LogLevel.Debug)
                    .AddConsole();
            });
            var logger = loggerFactory.CreateLogger<UiService>();

            // Clean up socket file
            var socketPath = "/tmp/osui.sock";
            if (File.Exists(socketPath))
            {
                File.Delete(socketPath);
            }

            // Create the service instance
            var uiService = new UiService(logger);

            // Configure the native gRPC server
            // We listen on TCP 50051. The unix socket traffic will be bridged here by socat.
            var server = new Server
            {
                Services = { UI.BindService(uiService) },
                Ports = { new ServerPort("0.0.0.0", 50051, ServerCredentials.Insecure) }
            };

            SysProcess? socatProcess = null;

            try
            {
                server.Start();
                Console.WriteLine("Listening on TCP: 0.0.0.0:50051");
                
                // Start socat to bridge Unix Socket -> TCP
                // This allows us to use Grpc.Core (lenient) with the Daemon's unix socket requirement.
                if (File.Exists(socketPath)) File.Delete(socketPath);
                
                Console.WriteLine($"Starting socat proxy: {socketPath} -> 127.0.0.1:50051");
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
                if (socatProcess != null)
                {
                    Console.WriteLine($"Socat started with PID: {socatProcess.Id}");
                }
                else
                {
                    Console.Error.WriteLine("Failed to start socat!");
                }

                Console.WriteLine("Press any key to stop the server...");
                
                // Keep running until user presses a key or kills the process
                var tcs = new TaskCompletionSource<object>();
                Console.CancelKeyPress += (sender, e) => {
                    e.Cancel = true;
                    tcs.TrySetResult(new object());
                };

                await tcs.Task;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error starting server: {ex.Message}");
            }
            finally
            {
                Console.WriteLine("Shutting down...");
                
                if (socatProcess != null && !socatProcess.HasExited)
                {
                    try { socatProcess.Kill(); socatProcess.WaitForExit(); } catch {}
                }

                await server.ShutdownAsync();
                if (File.Exists(socketPath))
                {
                    File.Delete(socketPath);
                }
            }
        }
    }
}