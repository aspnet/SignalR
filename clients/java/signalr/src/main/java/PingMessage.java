// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

public class PingMessage extends HubMessage {
    HubMessageType type = HubMessageType.PING;

    @Override
    HubMessageType getMessageType() {
        return this.type;
    }
}
