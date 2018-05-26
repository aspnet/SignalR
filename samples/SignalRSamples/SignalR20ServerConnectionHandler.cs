using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.Azure.SignalR.Protocol;

namespace SignalRSamples
{
    public class SignalR20ServerConnectionHandler : ConnectionHandler
    {
        internal static ConnectionList Connections = new ConnectionList();

        private static ServiceProtocol _protocol = new ServiceProtocol();

        public override async Task OnConnectedAsync(ConnectionContext connection)
        {
            Connections.Add(connection);

            var transferFormat = connection.Features.Get<ITransferFormatFeature>();

            if (transferFormat != null)
            {
                transferFormat.ActiveFormat = TransferFormat.Binary;
            }

            try
            {
                while (true)
                {
                    var result = await connection.Transport.Input.ReadAsync();
                    var buffer = result.Buffer;

                    while (_protocol.TryParseMessage(ref buffer, out var message))
                    {
                        switch (message)
                        {
                            case ConnectionDataMessage m:
                                {
                                    var clientConnection = SignalR20ClientConnectionHandler.Connections[m.ConnectionId];

                                    await clientConnection.Transport.Output.WriteAsync(m.Payload);
                                }
                                break;
                            case BroadcastDataMessage m:
                                {
                                    foreach (var clientConnection in SignalR20ClientConnectionHandler.Connections)
                                    {
                                        await clientConnection.Transport.Output.WriteAsync(m.Payloads["json"]);
                                    }
                                }
                                break;
                        }
                    }

                    connection.Transport.Input.AdvanceTo(buffer.Start, buffer.End);

                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
            }
            finally
            {
                Connections.Remove(connection);
            }
        }
    }
}