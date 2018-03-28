// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.SignalR.Microbenchmarks.Shared
{
    public class TestPipeReader : PipeReader
    {
        private readonly List<ReadResult> _readResults;

        public TestPipeReader(ReadResult? readResult = null)
        {
            _readResults = new List<ReadResult>();
            if (readResult != null)
            {
                _readResults.Add(readResult.Value);
            }
        }

        public override void AdvanceTo(SequencePosition consumed)
        {
        }

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
        }

        public override void CancelPendingRead()
        {
            throw new NotImplementedException();
        }

        public override void Complete(Exception exception = null)
        {
            throw new NotImplementedException();
        }

        public override void OnWriterCompleted(Action<Exception, object> callback, object state)
        {
            throw new NotImplementedException();
        }

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            if (_readResults.Count == 0)
            {
                return new ValueTask<ReadResult>(new ReadResult(default, false, true));
            }

            var result = _readResults[0];
            _readResults.RemoveAt(0);

            return new ValueTask<ReadResult>(result);
        }

        public override bool TryRead(out ReadResult result)
        {
            throw new NotImplementedException();
        }
    }
}