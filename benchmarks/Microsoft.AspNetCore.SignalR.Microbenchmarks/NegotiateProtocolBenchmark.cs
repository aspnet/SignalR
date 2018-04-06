using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Internal;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.Extensions.Logging.Abstractions;

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
