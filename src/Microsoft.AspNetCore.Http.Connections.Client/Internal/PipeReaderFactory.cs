// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using System.Threading.Tasks;

namespace System.IO.Pipelines
{
    internal class PipeReaderFactory
    {
        public static PipeReader CreateFromStream(PipeOptions options, Stream stream, CancellationToken cancellationToken)
        {
            if (!stream.CanRead)
            {
                throw new NotSupportedException();
            }

            var pipe = new Pipe(options);
            _ = CopyToAsync(stream, pipe.Writer, cancellationToken);

            return pipe.Reader;
        }

        private static async Task CopyToAsync(Stream stream, PipeWriter writer, CancellationToken cancellationToken)
        {
            try
            {
                // REVIEW: Should we use the default buffer size here?
                // 81920 is the default bufferSize, there is no stream.CopyToAsync overload that takes only a cancellationToken
                await stream.CopyToAsync(new PipeWriterStream(writer), bufferSize: 81920, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Ignore the cancellation signal (the pipe reader is already wired up)
            }
            catch (Exception ex)
            {
                writer.Complete(ex);
                return;
            }
            writer.Complete();
        }
    }
}
