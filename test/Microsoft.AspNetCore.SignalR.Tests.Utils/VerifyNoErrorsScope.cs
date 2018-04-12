using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public class VerifyNoErrorsScope : IDisposable
    {
        private readonly ITestSink _sink;

        public TestLoggerFactory LoggerFactory { get; }

        public VerifyNoErrorsScope()
        {
            _sink = new TestSink();
            LoggerFactory = new TestLoggerFactory(_sink, true);
        }

        public void Dispose()
        {
            var results = _sink.Writes.Where(w => w.LogLevel >= LogLevel.Error).ToList();

            if (results.Count > 0)
            {
                string errorMessage = $"{results.Count} error(s) logged.";
                errorMessage += Environment.NewLine;
                errorMessage += string.Join(Environment.NewLine, results.Select(r =>
                {
                    string lineMessage = r.LoggerName + " - " + r.EventId.ToString() + " - " + r.Formatter(r.State, r.Exception);
                    if (r.Exception != null)
                    {
                        lineMessage += Environment.NewLine;
                        lineMessage += "===================";
                        lineMessage += Environment.NewLine;
                        lineMessage += r.Exception;
                        lineMessage += Environment.NewLine;
                        lineMessage += "===================";
                    }
                    return lineMessage;
                }));

                throw new Exception(errorMessage);
            }
        }
    }
}