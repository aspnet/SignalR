// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

public enum HubMessageType {
    INVOCATION,
    STREAM_INVOCATION,
    STREAM_ITEM,
    CANCEL_INVOCATION,
    COMPLETION,
    ERROR,
    CLOSE,
    PING
}
