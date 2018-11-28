// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace Microsoft.AspNetCore.SignalR.Common.Tests.Internal.Protocol
{
    public class TestHubMessageEqualityComparer : IEqualityComparer<HubMessage>
    {
        public static readonly TestHubMessageEqualityComparer Instance = new TestHubMessageEqualityComparer();

        private TestHubMessageEqualityComparer() { }

        public bool Equals(HubMessage x, HubMessage y)
        {
            // Types should be equal
            if (!Equals(x.GetType(), y.GetType()))
            {
                return false;
            }

            switch (x)
            {
                case InvocationMessage invocationMessage:
                    return InvocationMessagesEqual(invocationMessage, (InvocationMessage)y);
                case StreamItemMessage streamItemMessage:
                    return StreamItemMessagesEqual(streamItemMessage, (StreamItemMessage)y);
                case CompletionMessage completionMessage:
                    return CompletionMessagesEqual(completionMessage, (CompletionMessage)y);
                case StreamInvocationMessage streamInvocationMessage:
                    return StreamInvocationMessagesEqual(streamInvocationMessage, (StreamInvocationMessage)y);
                case CancelInvocationMessage cancelItemMessage:
                    return string.Equals(cancelItemMessage.InvocationId, ((CancelInvocationMessage)y).InvocationId, StringComparison.Ordinal);
                case PingMessage _:
                    // If the types are equal (above), then we're done.
                    return true;
                case CloseMessage closeMessage:
                    return string.Equals(closeMessage.Error, ((CloseMessage) y).Error);
                case StreamCompleteMessage streamCompleteMessage:
                    return StreamCompleteMessagesEqual(streamCompleteMessage, (StreamCompleteMessage)y);
                case StreamDataMessage streamDataMessage:
                    return StreamDataMessagesEqual(streamDataMessage, (StreamDataMessage)y);
                default:
                    throw new InvalidOperationException($"Unknown message type: {x.GetType().FullName}");
            }
        }

        private bool CompletionMessagesEqual(CompletionMessage x, CompletionMessage y)
        {
            return SequenceEqual(x.Headers, y.Headers)
                && string.Equals(x.InvocationId, y.InvocationId, StringComparison.Ordinal)
                && string.Equals(x.Error, y.Error, StringComparison.Ordinal)
                && x.HasResult == y.HasResult
                && (Equals(x.Result, y.Result) || SequenceEqual(x.Result, y.Result));
        }

        private bool StreamItemMessagesEqual(StreamItemMessage x, StreamItemMessage y)
        {
            return SequenceEqual(x.Headers, y.Headers)
                && string.Equals(x.InvocationId, y.InvocationId, StringComparison.Ordinal)
                && (Equals(x.Item, y.Item) || SequenceEqual(x.Item, y.Item));
        }

        private bool InvocationMessagesEqual(InvocationMessage x, InvocationMessage y)
        {
            return SequenceEqual(x.Headers, y.Headers)
                && string.Equals(x.InvocationId, y.InvocationId, StringComparison.Ordinal)
                && string.Equals(x.Target, y.Target, StringComparison.Ordinal)
                && ArgumentListsEqual(x.Arguments, y.Arguments);
        }

        private bool StreamInvocationMessagesEqual(StreamInvocationMessage x, StreamInvocationMessage y)
        {
            return SequenceEqual(x.Headers, y.Headers)
                && string.Equals(x.InvocationId, y.InvocationId, StringComparison.Ordinal)
                && string.Equals(x.Target, y.Target, StringComparison.Ordinal)
                && ArgumentListsEqual(x.Arguments, y.Arguments);
        }

        private bool StreamCompleteMessagesEqual(StreamCompleteMessage x, StreamCompleteMessage y)
        {
            return x.StreamId == y.StreamId
                && x.Error == y.Error;
        }

        private bool StreamDataMessagesEqual(StreamDataMessage x, StreamDataMessage y)
        {
            return x.StreamId == y.StreamId
                && (Equals(x.Item, y.Item) || SequenceEqual(x.Item, y.Item));
        }

        private bool ArgumentListsEqual(object[] left, object[] right)
        {
            if (left == right)
            {
                return true;
            }

            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            for (var i = 0; i < left.Length; i++)
            {
                if (!(Equals(left[i], right[i]) || SequenceEqual(left[i], right[i]) || PlaceholdersEqual(left[i], right[i])))
                {
                    return false;
                }
            }
            return true;
        }

        private bool PlaceholdersEqual(object left, object right)
        {
            if (left.GetType() != right.GetType())
            {
                return false;
            }
            switch(left)
            {
                case StreamPlaceholder leftPlaceholder:
                    return leftPlaceholder.StreamId == (right as StreamPlaceholder).StreamId;
                default:
                    return false;
            }
        }

        private bool SequenceEqual(object left, object right)
        {
            if (left == null && right == null)
            {
                return true;
            }

            var leftEnumerable = left as IEnumerable;
            var rightEnumerable = right as IEnumerable;
            if (leftEnumerable == null || rightEnumerable == null)
            {
                return false;
            }

            var leftEnumerator = leftEnumerable.GetEnumerator();
            var rightEnumerator = rightEnumerable.GetEnumerator();
            var leftMoved = leftEnumerator.MoveNext();
            var rightMoved = rightEnumerator.MoveNext();
            for (; leftMoved && rightMoved; leftMoved = leftEnumerator.MoveNext(), rightMoved = rightEnumerator.MoveNext())
            {
                if (!Equals(leftEnumerator.Current, rightEnumerator.Current))
                {
                    return false;
                }
            }

            return !leftMoved && !rightMoved;
        }

        public int GetHashCode(HubMessage obj)
        {
            // We never use these in a hash-table
            return 0;
        }
    }
}
