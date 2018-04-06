// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http.Connections.Internal;
using Microsoft.AspNetCore.SignalR.Internal;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks
{
    public class NegotiateProtocolBenchmark
    {
        private NegotiationResponse _negotiateResponse;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _negotiateResponse = new NegotiationResponse
            {
                ConnectionId = "d100338e-8c01-4281-92c2-9a967fdeebcb",
                AvailableTransports = new List<AvailableTransport>
                {
                    new AvailableTransport
                    {
                        Transport = "WebSockets",
                        TransferFormats = new List<string>
                        {
                            "Text",
                            "Binary"
                        }
                    }
                }
            };
        }

        [Benchmark]
        public void WriteResponse_MemoryStream()
        {
            MemoryStream ms = new MemoryStream();
            NegotiateProtocol.WriteResponse(_negotiateResponse, ms);
            ms.ToArray();
        }

        [Benchmark]
        public void WriteResponse_MemoryBufferWriter()
        {
            var writer = MemoryBufferWriter.Get();
            try
            {
                NegotiateProtocol.WriteResponse(_negotiateResponse, writer);
                writer.ToArray();
            }
            finally
            {
                MemoryBufferWriter.Return(writer);
            }
        }
    }
}
