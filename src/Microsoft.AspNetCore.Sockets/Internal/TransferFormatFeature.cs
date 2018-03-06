// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Sockets.Features;

namespace Microsoft.AspNetCore.Sockets.Internal
{
    public class TransferFormatFeature : ITransferFormatFeature
    {
        public TransferFormat SupportedFormats { get; }
        public TransferFormat ActiveFormat { get; set; }

        public TransferFormatFeature(TransferFormat supportedFormats)
        {
            SupportedFormats = supportedFormats;
        }
    }
}
