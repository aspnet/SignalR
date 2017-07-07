// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

// TODO: Where shouold this live?

namespace Microsoft.AspNetCore.Sockets
{
    public interface ITransferModeFeature
    {
        TransferMode TransferMode { get; set; }
    }

    public class TransferModeFeature : ITransferModeFeature
    {
        public TransferMode TransferMode { get; set; }
    }
}
