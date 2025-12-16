using Grpc.Core;
using Protocol;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace OpenSnitchCli.Services
{
    public class UiService : UI.UIBase
    {
        private readonly ILogger<UiService> _logger;
        private readonly JsonFormatter _formatter;

        public UiService(ILogger<UiService> logger)
        {
            _logger = logger;
            _formatter = new JsonFormatter(JsonFormatter.Settings.Default.WithFormatDefaultValues(true));
        }

        public override Task<PingReply> Ping(PingRequest request, ServerCallContext context)
        {
            LogMessage("Ping", request);
            return Task.FromResult(new PingReply { Id = request.Id });
        }

        public override Task<Rule> AskRule(Connection request, ServerCallContext context)
        {
             LogMessage("AskRule", request);
             return Task.FromResult(new Rule());
        }

        public override Task<ClientConfig> Subscribe(ClientConfig request, ServerCallContext context)
        {
            LogMessage("Subscribe", request);
            return Task.FromResult(request);
        }

        public override async Task Notifications(IAsyncStreamReader<NotificationReply> requestStream, IServerStreamWriter<Notification> responseStream, ServerCallContext context)
        {
             try
             {
                 while (await requestStream.MoveNext())
                 {
                     var reply = requestStream.Current;
                     LogMessage("NotificationReply", reply);
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
                var json = _formatter.Format(message);
                Console.WriteLine(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error formatting message: {ex.Message}");
            }
        }
    }
}
