using System;
using System.IO.Pipelines;
using System.Threading;
using Microsoft.AspNetCore.Sockets.Client.Internal;

namespace Microsoft.AspNetCore.Sockets.Client
{
    public partial class HttpConnection
    {
        private class HttpConnectionPipeWriter : PipeWriter
        {
            private readonly HttpConnection _connection;

            public HttpConnectionPipeWriter(HttpConnection connection)
            {
                _connection = connection;
            }

            public override void Advance(int bytes)
            {
                _connection.EnsureConnected();

                _connection._transportChannel.Output.Advance(bytes);
            }

            public override void CancelPendingFlush()
            {
                _connection.EnsureConnected();

                _connection._transportChannel.Output.CancelPendingFlush();
            }

            public override void Commit()
            {
                _connection.EnsureConnected();

                _connection._transportChannel.Output.Commit();
            }

            public override void Complete(Exception exception = null)
            {
                _connection.EnsureConnected();

                _connection._transportChannel.Output.Complete(exception);
            }

            public override ValueAwaiter<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
            {
                _connection.EnsureConnected();

                _connection._logger.SendingMessage();

                return _connection._transportChannel.Output.FlushAsync(cancellationToken);
            }

            public override Memory<byte> GetMemory(int minimumLength = 0)
            {
                _connection.EnsureConnected();

                return _connection._transportChannel.Output.GetMemory(minimumLength);
            }

            public override Span<byte> GetSpan(int minimumLength = 0)
            {
                _connection.EnsureConnected();

                return _connection._transportChannel.Output.GetSpan(minimumLength);
            }

            public override void OnReaderCompleted(Action<Exception, object> callback, object state)
            {
                // REVIEW: This could work without being connected but it requires more book keeping

                _connection.EnsureConnected();

                _connection._transportChannel.Output.OnReaderCompleted(callback, state);
            }
        }
    }
}
