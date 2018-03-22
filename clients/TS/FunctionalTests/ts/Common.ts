// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import { IHubProtocol, JsonHubProtocol, TransportType } from "@aspnet/signalr";
import { MessagePackHubProtocol } from "@aspnet/signalr-protocol-msgpack";

export const ECHOENDPOINT_URL = "http://" + document.location.host + "/echo";

export function getTransportTypes(): TransportType[] {
    const transportTypes = [];
    if (typeof WebSocket !== "undefined") {
        transportTypes.push(TransportType.WebSockets);
    }
    if (typeof EventSource !== "undefined") {
        transportTypes.push(TransportType.ServerSentEvents);
    }
    transportTypes.push(TransportType.LongPolling);

    return transportTypes;
}

export function eachTransport(action: (transport: TransportType) => void) {
    getTransportTypes().forEach((t) => {
        return action(t);
    });
}

export function eachTransportAndProtocol(action: (transport: TransportType, protocol: IHubProtocol) => void) {
    const protocols: IHubProtocol[] = [new JsonHubProtocol()];
    // IE9 does not support XmlHttpRequest advanced features so disable for now
    // This can be enabled if we fix: https://github.com/aspnet/SignalR/issues/742
    if (typeof new XMLHttpRequest().responseType === "string") {
        // Because of TypeScript stuff, we can't get "ambient" or "global" declarations to work with the MessagePackHubProtocol module
        // This is only a limitation of the .d.ts file.
        // Everything works fine in the module
        protocols.push(new MessagePackHubProtocol());
    }
    getTransportTypes().forEach((t) => {
        return protocols.forEach((p) => {
            if (t !== TransportType.ServerSentEvents || !(p instanceof MessagePackHubProtocol)) {
                return action(t, p);
            }
        });
    });
}
