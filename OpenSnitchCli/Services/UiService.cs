using Grpc.Core;
using Protocol;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace OpenSnitchCli.Services
{
    public class UiService : UI.UIBase
    {
        private readonly ILogger<UiService> _logger;
        private readonly bool _logEntries;
        private readonly JsonFormatter _formatter;

        // Event to notify subscribers (CLI logger or TUI)
        public event Action<string, IMessage>? OnMessageReceived;
        public event Action<IEnumerable<Rule>>? OnRulesReceived;
        
        // Handler for interactive rule decisions
        public Func<Connection, Task<Rule>>? AskRuleHandler { get; set; }

        public UiService(ILogger<UiService> logger, bool logEntries)
        {
            _logger = logger;
            this._logEntries = logEntries;
            _formatter = new JsonFormatter(JsonFormatter.Settings.Default.WithFormatDefaultValues(true));
        }

        public override Task<PingReply> Ping(PingRequest request, ServerCallContext context)
        {
            LogMessage("Ping", request);
            return Task.FromResult(new PingReply { Id = request.Id });
        }

        public override async Task<Rule> AskRule(Connection request, ServerCallContext context)
        {
             LogMessage("AskRule", request);
             
             if (AskRuleHandler != null)
             {
                 try 
                 {
                     var rule = await AskRuleHandler(request);
                     LogMessage(rule.Action.ToUpper(), request);
                     return rule;
                 }
                 catch (Exception ex)
                 {
                     if(_logEntries) _logger.LogError(ex, "Error in AskRuleHandler");
                 }
             }
             
             return new Rule();
        }

        public override Task<ClientConfig> Subscribe(ClientConfig request, ServerCallContext context)
        {
            if(_logEntries)
            _logger.LogDebug("Received Subscribe request: {Request}", request);

            if (request.Rules != null)
            {
                OnRulesReceived?.Invoke(request.Rules);
            }
            
            // Create a response that attempts to force monitoring/interception
            var responseConfig = new ClientConfig
            {
                Id = request.Id,
                Name = request.Name,
                Version = request.Version,
                IsFirewallRunning = true, // Tell daemon we want it running
                // Also send back configuration to enable interception and eBPF
                // This is a JSON string passed directly.
                Config = "{\"InterceptUnknown\":true,\"ProcMonitorMethod\":\"ebpf\"}",
                LogLevel = request.LogLevel,
                Rules = { request.Rules } // Send back existing rules
            };

            LogMessage("Subscribe (Response)", responseConfig);
            return Task.FromResult(responseConfig);
        }

        public override async Task Notifications(IAsyncStreamReader<NotificationReply> requestStream, IServerStreamWriter<Notification> responseStream, ServerCallContext context)
        {
             try
             {
                 // 1. Start SocketsMonitor
                 var socketsConfig = "{\"Name\":\"SocketsMonitor\",\"Data\":{\"interval\":\"2s\",\"states\":\"1,10\"}}";
                 await responseStream.WriteAsync(new Notification
                 {
                     Id = 1,
                     Type = Protocol.Action.TaskStart,
                     Data = socketsConfig
                 });
                 _logger.LogDebug("Sent SocketsMonitor: {Command}", socketsConfig);

                 // 2. Start PidMonitor
                 var pidConfig = "{\"Name\":\"PidMonitor\",\"Data\":{\"interval\":\"2s\"}}";
                 await responseStream.WriteAsync(new Notification
                 {
                     Id = 2,
                     Type = Protocol.Action.TaskStart,
                     Data = pidConfig
                 });
                 _logger.LogDebug("Sent PidMonitor: {Command}", pidConfig);

                 while (await requestStream.MoveNext())
                 {
                     var reply = requestStream.Current;
                     _logger.LogTrace("Received NotificationReply: Code={Code}, Data={Data}", reply.Code, reply.Data);
                 }
             }
             catch (Exception ex)
             {
                 _logger.LogError(ex, "Error in Notifications stream");
             }
        }

        public override Task<MsgResponse> PostAlert(Alert request, ServerCallContext context)
        {
            LogMessage("PostAlert", request);
            return Task.FromResult(new MsgResponse { Id = request.Id });
        }

        private void LogMessage(string method, IMessage message)
        {
            try 
            {
                OnMessageReceived?.Invoke(method, message);
            }
            catch (Exception ex)
            {
                if(_logEntries)
                _logger.LogError(ex, "Error invoking message handler");
            }
        }
    }
}
