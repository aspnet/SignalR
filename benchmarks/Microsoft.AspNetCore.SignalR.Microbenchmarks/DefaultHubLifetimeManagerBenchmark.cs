using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.AspNetCore.SignalR.Microbenchmarks.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks
{
    public class TestConnectionContext : ConnectionContext
    {
        public override string ConnectionId { get; set; }
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override IDictionary<object, object> Items { get; set; }
        public override IDuplexPipe Transport { get; set; }
    }

    public class DefaultHubLifetimeManagerBenchmark
    {
        private DefaultHubLifetimeManager<Hub> _hubLifetimeManager;
        private List<string> _connectionIds;
        private List<string> _subsetConnectionIds;
        private List<string> _groupNames;
        private List<string> _userIdentifiers;

        [Params(true, false)]
        public bool WriteSlow { get; set; }

        [GlobalSetup]
        public void GlobalSetup()
        {
            _hubLifetimeManager = new DefaultHubLifetimeManager<Hub>(NullLogger<DefaultHubLifetimeManager<Hub>>.Instance);
            _connectionIds = new List<string>();
            _subsetConnectionIds = new List<string>();
            _groupNames = new List<string>();
            _userIdentifiers = new List<string>();

            var jsonHubProtocol = new JsonHubProtocol();

            for (int i = 0; i < 100; i++)
            {
                string connectionId = "connection-" + i;
                string groupName = "group-" + i % 10;
                string userIdentifier = "user-" + i % 20;
                AddUnique(_connectionIds, connectionId);
                AddUnique(_groupNames, groupName);
                AddUnique(_userIdentifiers, userIdentifier);
                if (i % 2 == 0)
                {
                    _subsetConnectionIds.Add(connectionId);
                }

                var connectionContext = new TestConnectionContext
                {
                    ConnectionId = connectionId,
                    Transport = new TestDuplexPipe(WriteSlow)
                };
                var hubConnectionContext = new HubConnectionContext(connectionContext, TimeSpan.Zero, NullLoggerFactory.Instance);
                hubConnectionContext.UserIdentifier = userIdentifier;
                hubConnectionContext.Protocol = jsonHubProtocol;

                _hubLifetimeManager.OnConnectedAsync(hubConnectionContext).GetAwaiter().GetResult();
                _hubLifetimeManager.AddGroupAsync(connectionId, groupName);
            }
        }

        private void AddUnique(List<string> list, string connectionId)
        {
            if (!list.Contains(connectionId))
            {
                list.Add(connectionId);
            }
        }

        [Benchmark]
        public Task SendAllAsync()
        {
            return _hubLifetimeManager.SendAllAsync("MethodName", Array.Empty<object>());
        }

        [Benchmark]
        public Task SendGroupAsync()
        {
            return _hubLifetimeManager.SendGroupAsync(_groupNames[0], "MethodName", Array.Empty<object>());
        }

        [Benchmark]
        public Task SendGroupsAsync()
        {
            return _hubLifetimeManager.SendGroupsAsync(_groupNames, "MethodName", Array.Empty<object>());
        }

        [Benchmark]
        public Task SendGroupExceptAsync()
        {
            return _hubLifetimeManager.SendGroupExceptAsync(_groupNames[0], "MethodName", Array.Empty<object>(), _subsetConnectionIds);
        }

        [Benchmark]
        public Task SendAllExceptAsync()
        {
            return _hubLifetimeManager.SendAllExceptAsync("MethodName", Array.Empty<object>(), _subsetConnectionIds);
        }

        [Benchmark]
        public Task SendConnectionAsync()
        {
            return _hubLifetimeManager.SendConnectionAsync(_connectionIds[0], "MethodName", Array.Empty<object>());
        }

        [Benchmark]
        public Task SendConnectionsAsync()
        {
            return _hubLifetimeManager.SendConnectionsAsync(_subsetConnectionIds, "MethodName", Array.Empty<object>());
        }

        [Benchmark]
        public Task SendUserAsync()
        {
            return _hubLifetimeManager.SendUserAsync(_userIdentifiers[0], "MethodName", Array.Empty<object>());
        }

        [Benchmark]
        public Task SendUsersAsync()
        {
            return _hubLifetimeManager.SendUsersAsync(_userIdentifiers, "MethodName", Array.Empty<object>());
        }
    }
}