// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Listener.Configuration;
using Agent.Sdk;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Listener;
using Microsoft.VisualStudio.Services.Agent.Capabilities;
using Microsoft.VisualStudio.Services.Agent.Listener.Configuration;
using Microsoft.VisualStudio.Services.Common;
using Moq;
using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Xunit;
using System.Threading;
using System.Reflection;
using System.Collections.Generic;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Listener
{
    public sealed class MessageListenerL0 : IDisposable
    {
        private AgentSettings _settings;
        private Mock<IConfigurationManager> _config;
        private Mock<IAgentServer> _agentServer;
        private Mock<ICredentialManager> _credMgr;
        private Mock<ICapabilitiesManager> _capabilitiesManager;
        private Mock<IFeatureFlagProvider> _featureFlagProvider;
        private Mock<IRSAKeyManager> _rsaKeyManager;
        private readonly RSACryptoServiceProvider rsa;

        public MessageListenerL0()
        {
            _settings = new AgentSettings { AgentId = 1, AgentName = "myagent", PoolId = 123, PoolName = "default", ServerUrl = "http://myserver", WorkFolder = "_work" };
            _config = new Mock<IConfigurationManager>();
            _config.Setup(x => x.LoadSettings()).Returns(_settings);
            _agentServer = new Mock<IAgentServer>();
            _credMgr = new Mock<ICredentialManager>();
            _capabilitiesManager = new Mock<ICapabilitiesManager>();
            _featureFlagProvider = new Mock<IFeatureFlagProvider>();
            _rsaKeyManager = new Mock<IRSAKeyManager>();

            _featureFlagProvider.Setup(x => x.GetFeatureFlagAsync(It.IsAny<IHostContext>(), It.IsAny<string>(), It.IsAny<ITraceWriter>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(new FeatureAvailability.FeatureFlag("", "", "", "Off", "Off")));
            _featureFlagProvider.Setup(x => x.GetFeatureFlagWithCred(It.IsAny<IHostContext>(), It.IsAny<string>(), It.IsAny<ITraceWriter>(), It.IsAny<AgentSettings>(), It.IsAny<VssCredentials>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(new FeatureAvailability.FeatureFlag("", "", "", "Off", "Off")));

            rsa = new RSACryptoServiceProvider(2048);
            _rsaKeyManager.Setup(x => x.CreateKey(It.IsAny<bool>())).Returns(rsa);
        }

        private TestHostContext CreateTestContext([CallerMemberName] String testName = "")
        {
            TestHostContext tc = new TestHostContext(this, testName);
            tc.SetSingleton<IConfigurationManager>(_config.Object);
            tc.SetSingleton<IAgentServer>(_agentServer.Object);
            tc.SetSingleton<ICredentialManager>(_credMgr.Object);
            tc.SetSingleton<ICapabilitiesManager>(_capabilitiesManager.Object);
            tc.SetSingleton<IFeatureFlagProvider>(_featureFlagProvider.Object);
            tc.SetSingleton<IRSAKeyManager>(_rsaKeyManager.Object);
            return tc;
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void CreatesSession()
        {
            using (TestHostContext tc = CreateTestContext())
            using (var tokenSource = new CancellationTokenSource())
            {
                Tracing trace = tc.GetTrace();

                // Arrange.
                var expectedSession = new TaskAgentSession();
                _agentServer
                    .Setup(x => x.CreateAgentSessionAsync(
                        _settings.PoolId,
                        It.Is<TaskAgentSession>(y => y != null),
                        tokenSource.Token))
                    .Returns(Task.FromResult(expectedSession));

                _capabilitiesManager.Setup(x => x.GetCapabilitiesAsync(_settings, It.IsAny<CancellationToken>())).Returns(Task.FromResult(new Dictionary<string, string>()));

                _credMgr.Setup(x => x.LoadCredentials()).Returns(new Common.VssCredentials());

                // Act.
                MessageListener listener = new MessageListener();
                listener.Initialize(tc);

                bool result = await listener.CreateSessionAsync(tokenSource.Token);
                trace.Info("result: {0}", result);

                // Assert.
                Assert.True(result);
                _agentServer
                    .Verify(x => x.CreateAgentSessionAsync(
                        _settings.PoolId,
                        It.Is<TaskAgentSession>(y => y != null),
                        tokenSource.Token), Times.Once());
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void DeleteSession()
        {
            using (TestHostContext tc = CreateTestContext())
            using (var tokenSource = new CancellationTokenSource())
            {
                Tracing trace = tc.GetTrace();

                // Arrange.
                var expectedSession = new TaskAgentSession();
                PropertyInfo sessionIdProperty = expectedSession.GetType().GetProperty("SessionId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Assert.NotNull(sessionIdProperty);
                sessionIdProperty.SetValue(expectedSession, Guid.NewGuid());

                _agentServer
                    .Setup(x => x.CreateAgentSessionAsync(
                        _settings.PoolId,
                        It.Is<TaskAgentSession>(y => y != null),
                        tokenSource.Token))
                    .Returns(Task.FromResult(expectedSession));

                _capabilitiesManager.Setup(x => x.GetCapabilitiesAsync(_settings, It.IsAny<CancellationToken>())).Returns(Task.FromResult(new Dictionary<string, string>()));

                _credMgr.Setup(x => x.LoadCredentials()).Returns(new Common.VssCredentials());

                // Act.
                MessageListener listener = new MessageListener();
                listener.Initialize(tc);

                bool result = await listener.CreateSessionAsync(tokenSource.Token);
                Assert.True(result);

                _agentServer
                    .Setup(x => x.DeleteAgentSessionAsync(
                        _settings.PoolId, expectedSession.SessionId, It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                await listener.DeleteSessionAsync();

                //Assert
                _agentServer
                    .Verify(x => x.DeleteAgentSessionAsync(
                        _settings.PoolId, expectedSession.SessionId, It.IsAny<CancellationToken>()), Times.Once());
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Agent")]
        public async void GetNextMessage()
        {
            using (TestHostContext tc = CreateTestContext())
            using (var tokenSource = new CancellationTokenSource())
            {
                Tracing trace = tc.GetTrace();

                // Arrange.
                var expectedSession = new TaskAgentSession();
                PropertyInfo sessionIdProperty = expectedSession.GetType().GetProperty("SessionId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                Assert.NotNull(sessionIdProperty);
                sessionIdProperty.SetValue(expectedSession, Guid.NewGuid());

                _agentServer
                    .Setup(x => x.CreateAgentSessionAsync(
                        _settings.PoolId,
                        It.Is<TaskAgentSession>(y => y != null),
                        tokenSource.Token))
                    .Returns(Task.FromResult(expectedSession));

                _capabilitiesManager.Setup(x => x.GetCapabilitiesAsync(_settings, It.IsAny<CancellationToken>())).Returns(Task.FromResult(new Dictionary<string, string>()));

                _credMgr.Setup(x => x.LoadCredentials()).Returns(new Common.VssCredentials());

                // Act.
                MessageListener listener = new MessageListener();
                listener.Initialize(tc);

                bool result = await listener.CreateSessionAsync(tokenSource.Token);
                Assert.True(result);

                var arMessages = new TaskAgentMessage[]
                {
                        new TaskAgentMessage
                        {
                            Body = "somebody1",
                            MessageId = 4234,
                            MessageType = JobRequestMessageTypes.AgentJobRequest
                        },
                        new TaskAgentMessage
                        {
                            Body = "somebody2",
                            MessageId = 4235,
                            MessageType = JobCancelMessage.MessageType
                        },
                        null,  //should be skipped by GetNextMessageAsync implementation
                        null,
                        new TaskAgentMessage
                        {
                            Body = "somebody3",
                            MessageId = 4236,
                            MessageType = JobRequestMessageTypes.AgentJobRequest
                        }
                };
                var messages = new Queue<TaskAgentMessage>(arMessages);

                _agentServer
                    .Setup(x => x.GetAgentMessageAsync(
                        _settings.PoolId, expectedSession.SessionId, It.IsAny<long?>(), tokenSource.Token))
                    .Returns(async (Int32 poolId, Guid sessionId, Int64? lastMessageId, CancellationToken cancellationToken) =>
                    {
                        await Task.Yield();
                        return messages.Dequeue();
                    });
                TaskAgentMessage message1 = await listener.GetNextMessageAsync(tokenSource.Token);
                TaskAgentMessage message2 = await listener.GetNextMessageAsync(tokenSource.Token);
                TaskAgentMessage message3 = await listener.GetNextMessageAsync(tokenSource.Token);
                Assert.Equal(arMessages[0], message1);
                Assert.Equal(arMessages[1], message2);
                Assert.Equal(arMessages[4], message3);

                //Assert
                _agentServer
                    .Verify(x => x.GetAgentMessageAsync(
                        _settings.PoolId, expectedSession.SessionId, It.IsAny<long?>(), tokenSource.Token), Times.Exactly(arMessages.Length));
            }
        }

        public void Dispose()
        {
            rsa.Dispose();
        }
    }
}
