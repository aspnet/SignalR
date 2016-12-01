﻿using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Sockets.Client
{
    internal class ReadableBufferContent : HttpContent
    {
        private ReadableBuffer _buffer;

        public ReadableBufferContent(ReadableBuffer buffer)
        {
            _buffer = buffer;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) => _buffer.CopyToAsync(stream);

        protected override bool TryComputeLength(out long length)
        {
            length = _buffer.Length;
            return true;
        }
    }
}
