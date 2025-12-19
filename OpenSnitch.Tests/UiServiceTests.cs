using Xunit;
using Moq;
using OpenSnitchCli.Services;
using Protocol;
using Microsoft.Extensions.Logging;
using Grpc.Core;
using System.Threading.Tasks;
using System.Threading;
using System;
using Google.Protobuf;

namespace OpenSnitch.Tests
{
    public class UiServiceTests
    {
        private readonly Mock<ILogger<UiService>> _mockLogger;
        private readonly UiService _uiService;

        public UiServiceTests()
        {
            _mockLogger = new Mock<ILogger<UiService>>();
            _uiService = new UiService(_mockLogger.Object, true);
        }

        [Fact]
        public async Task Subscribe_ShouldFireOnDaemonConnected_AndReturnConfig()
        {
            // Arrange
            string receivedVersion = null;
            _uiService.OnDaemonConnected += (ver) => receivedVersion = ver;

            var request = new ClientConfig
            {
                Id = 123,
                Name = "TestDaemon",
                Version = "1.5.0",
                LogLevel = 1
            };

            // Act
            var result = await _uiService.Subscribe(request, new Mock<ServerCallContext>().Object);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.IsFirewallRunning);
            Assert.Contains("InterceptUnknown", result.Config);
            Assert.Equal("1.5.0", receivedVersion);
        }

        [Fact]
        public async Task AskRule_ShouldInvokeHandler_AndReturnResult()
        {
            // Arrange
            var connection = new Connection
            {
                SrcIp = "1.2.3.4",
                DstIp = "8.8.8.8",
                DstPort = 53,
                Protocol = "udp",
                ProcessPath = "/usr/bin/test"
            };

            var expectedRule = new Rule
            {
                Action = "allow",
                Duration = "always",
                Name = "TestRule"
            };

            _uiService.AskRuleHandler = async (conn) =>
            {
                Assert.Equal(connection.ProcessPath, conn.ProcessPath);
                return await Task.FromResult(expectedRule);
            };

            // Act
            var result = await _uiService.AskRule(connection, new Mock<ServerCallContext>().Object);

            // Assert
            Assert.Equal("allow", result.Action);
            Assert.Equal("always", result.Duration);
            Assert.Equal("TestRule", result.Name);
        }

        [Fact]
        public async Task PostAlert_ShouldFireOnMessageReceived()
        {
            // Arrange
            string receivedMethod = null;
            IMessage receivedMsg = null;
            _uiService.OnMessageReceived += (method, msg) => 
            {
                receivedMethod = method;
                receivedMsg = msg;
            };

            var alert = new Alert
            {
                Action = Protocol.Alert.Types.Action.ShowAlert,
                What = Protocol.Alert.Types.What.Generic,
                Text = "Test Alert Text"
            };

            // Act
            await _uiService.PostAlert(alert, new Mock<ServerCallContext>().Object);

            // Assert
            Assert.Equal("PostAlert", receivedMethod);
            Assert.NotNull(receivedMsg);
            Assert.IsType<Alert>(receivedMsg);
            Assert.Equal("Test Alert Text", ((Alert)receivedMsg).Text);
        }

        [Fact]
        public async Task Ping_ShouldReturnId()
        {
            // Arrange
            var request = new PingRequest { Id = 999 };

            // Act
            var result = await _uiService.Ping(request, new Mock<ServerCallContext>().Object);

            // Assert
            Assert.Equal(999UL, result.Id);
        }
    }
}
