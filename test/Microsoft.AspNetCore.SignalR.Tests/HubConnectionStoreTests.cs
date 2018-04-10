using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.AspNetCore.SignalR.Tests
{
    public class HubConnectionStoreTests
    {
        [Fact]
        public void ConnectionIdUsesOrdinalComparison()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var s1 = "Strasse";
            var s2 = "Straße";

            bool sdf = s1.Equals(s2, StringComparison.Ordinal);           //false

            bool sdfdf = s1.Equals(s2, StringComparison.InvariantCulture);  //true

            bool sdsdfsdf = EqualityComparer<string>.Default.Equals(s1, s2);

            //ReadOnlySequence<byte> sdd;
            //sdd.Slice()

            HubConnectionStore store = new HubConnectionStore();

            //var s1 = "Strasse";
            //var s2 = "Straße";

            store.Add(new HubConnectionContext(new DefaultConnectionContext(s1), TimeSpan.Zero, NullLoggerFactory.Instance));
            store.Add(new HubConnectionContext(new DefaultConnectionContext(s2), TimeSpan.Zero, NullLoggerFactory.Instance));

            Assert.Equal(2, store.Count);

            IDictionary<string, int> sdf1 = new ConcurrentDictionary<string, int>();

            sdf1.Add(s1, 1);
            sdf1.Add(s2, 2);

        }
    }
}
