// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.Azure.SignalR.Protocol;
using Newtonsoft.Json;

namespace SignalRSamples
{
    internal class SignalR20ClientConnectionHandler : ConnectionHandler
    {
        internal static ConnectionList Connections = new ConnectionList();

        private static ServiceProtocol _protocol = new ServiceProtocol();

        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            // HACK: Map all client connections to the first server connection
            var serverConnection = SignalR20ServerConnectionHandler.Connections.First();

            Connections.Add(connection);

            // HACK: We shouldn't send this directly from the proxy
            await connection.Transport.Output.WriteAsync(GetJsonBytes(new
            {
                M = new object[0],
                S = 1
            }));

            var openMessage = new OpenConnectionMessage(connection.ConnectionId, null);
            _protocol.WriteMessage(openMessage, serverConnection.Transport.Output);
            await serverConnection.Transport.Output.FlushAsync();

            try
            {
                while (true)
                {
                    var result = await connection.Transport.Input.ReadAsync();
                    var buffer = result.Buffer;

                    if (!buffer.IsEmpty)
                    {
                        if (buffer.IsSingleSegment)
                        {
                            var message = new ConnectionDataMessage(connection.ConnectionId, buffer.First);
                            _protocol.WriteMessage(message, serverConnection.Transport.Output);
                            await serverConnection.Transport.Output.FlushAsync();
                        }
                        else
                        {
                            foreach (var memory in buffer)
                            {
                                var message = new ConnectionDataMessage(connection.ConnectionId, memory);
                                _protocol.WriteMessage(message, serverConnection.Transport.Output);
                                await serverConnection.Transport.Output.FlushAsync();
                            }
                        }
                    }

                    if (result.IsCompleted)
                    {
                        break;
                    }

                    connection.Transport.Input.AdvanceTo(buffer.End);
                }
            }
            finally
            {
                Connections.Remove(connection);

                var closeMessage = new CloseConnectionMessage(connection.ConnectionId, errorMessage: null);
                _protocol.WriteMessage(closeMessage, serverConnection.Transport.Output);
                await serverConnection.Transport.Output.FlushAsync();
            }
        }

        private byte[] GetJsonBytes(object o)
        {
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(o));
        }
    }
}