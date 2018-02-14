using System;
using System.Collections;
using System.IO.Pipelines;
using System.Threading;

namespace Microsoft.AspNetCore.Sockets.Client
{
    public partial class HttpConnection
    {
        private class HttpConnectionPipeReader : PipeReader
        {
            private readonly HttpConnection _connection;

            public HttpConnectionPipeReader(HttpConnection connection)
            {
                _connection = connection;
            }

            public override void AdvanceTo(SequencePosition consumed)
            {
                _connection._transportChannel.Input.AdvanceTo(consumed);
            }

            public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
            {
                _connection._transportChannel.Input.AdvanceTo(consumed, examined);
            }

            public override void CancelPendingRead()
            {
                _connection._transportChannel.Input.CancelPendingRead();
            }

            public override void Complete(Exception exception = null)
            {
                _connection._transportChannel.Input.Complete(exception);
            }

            public override void OnWriterCompleted(Action<Exception, object> callback, object state)
            {
                _connection._transportChannel.Input.OnWriterCompleted(callback, state);
            }

            public override ValueAwaiter<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
            {
                return _connection._transportChannel.Input.ReadAsync(cancellationToken);
            }

            public override bool TryRead(out ReadResult result)
            {
                return _connection._transportChannel.Input.TryRead(out result);
            }
        }
    }
}
