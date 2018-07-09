// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace Microsoft.AspNetCore.SignalR
{
    internal class StreamTracker
    {
        private static readonly MethodInfo _buildConverterMethod = typeof(StreamTracker).GetMethods().Single(m => m.Name.Equals("BuildStream"));
        public Dictionary<string, IChannelConverter> Lookup = new Dictionary<string, IChannelConverter>();

        /// <summary>
        /// Creates a new stream and returns the ChannelReader for it as an object.
        /// </summary>
        public object NewStream(string streamId, Type itemType)
        {
            var newConverter = (IChannelConverter)_buildConverterMethod.MakeGenericMethod(itemType).Invoke(null, Array.Empty<object>());
            Lookup[streamId] = newConverter;
            return newConverter.GetReaderAsObject();
        }

        public Task ProcessItem(StreamItemMessage message)
        {
            return Lookup[message.InvocationId].WriteToChannel(message.Item);
        }

        public void Complete(StreamCompleteMessage message)
        {
            var ConverterToClose = Lookup[message.StreamId];
            Lookup.Remove(message.StreamId);
            ConverterToClose.TryComplete(message.HasError ? new Exception(message.Error) : null);
        }

        public static IChannelConverter BuildStream<T>()
        {
            return new ChannelConverter<T>();
        }

        public interface IChannelConverter
        {
            Type GetReturnType();
            object GetReaderAsObject();
            Task WriteToChannel(object item);
            void TryComplete(Exception ex);
        }

        internal class ChannelConverter<T> : IChannelConverter
        {
            private Channel<T> _channel;

            public ChannelConverter()
            {
                _channel = Channel.CreateUnbounded<T>();
            }

            public Type GetReturnType()
            {
                return typeof(T);
            }

            public object GetReaderAsObject()
            {
                return _channel.Reader;
            }

            public Task WriteToChannel(object o)
            {
                return _channel.Writer.WriteAsync((T)o).AsTask();
            }

            public void TryComplete(Exception ex)
            {
                _channel.Writer.TryComplete(ex);
            }
        }
    }
}
