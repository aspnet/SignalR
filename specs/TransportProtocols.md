# Transport Protocols

This document describes the protocols used by the four ASP.NET Endpoint Transports: WebSockets, Server-Sent Events, Long Polling and HTTP Post

## Transport Requirements

A transport is required to have the following attributes:

1. Duplex - Able to send messages from Server to Client and from Client to Server
1. Binary-safe - Able to transmit arbitrary binary data, regardless of content
1. Text-safe - Able to transmit arbitrary text data, preserving the content. Line-endings must be preserved **but may be converted to a different format**. For example `\r\n` may be converted to `\n`. This is due to quirks in some transports (Server Sent Events). If the exact line-ending needs to be preserved, the data should be sent as a `Binary` message.

The only transport which fully implements the duplex requirement is WebSockets, the others are "half-transports" which implement one end of the duplex connection. They are used in combination to achieve a duplex connection.

Throughout this document, the term `[endpoint-base]` is used to refer to the route assigned to a particular end point. The term `[connection-id]` is used to refer to the connection ID provided by the `OPTIONS [endpoint-base]` request.

**NOTE on errors:** In all error cases, by default, the detailed exception message is **never** provided; a short description string may be provided. However, an application developer may elect to allow detailed exception messages to be emitted, which should only be used in the `Development` environment. Unexpected errors are communicated by HTTP `500 Server Error` status codes or WebSockets `1008 Policy Violation` close frames; in these cases the connection should be considered to be terminated.

## `OPTIONS [endpoint-base]` request

The `OPTIONS [endpoint-base]` request is used to establish connection between the client and the server. The response to the `OPTIONS [endpoint-base]` request contains the `connectionId` which will be used to identify the connection on the server and the list of the transports supported by the server. The content type of the response is `application/json`. The following is a sample response to the `OPTIONS [endpoint-base]` request 

```
{
  "connectionId":"807809a5-31bf-470d-9e23-afaee35d8a0d",
  "availableTransports":["WebSockets","ServerSentEvents","LongPolling"]
}
```

## WebSockets (Full Duplex)

The WebSockets transport is unique in that it is full duplex, and a persistent connection that can be established in a single operation. As a result, the client is not required to use the `OPTIONS [endpoint-base]` request to establish a connection in advance. It also includes all the necessary metadata in it's own frame metadata.

The WebSocket transport is activated by making a WebSocket connection to `[endpoint-base]`. The **optional** `connectionId` query string value is used to identify the connection to attach to. If there is no `connectionId` query string value, a new connection is established. If the parameter is specified but there is no connection with the specified ID value, a `404 Not Found` response is returned. Upon receiving this request, the connection is established and the server responds with a WebSocket upgrade (`101 Switching Protocols`) immediately ready for frames to be sent/received. The WebSocket OpCode field is used to indicate the type of the frame (Text or Binary).

Establishing a second WebSocket connection when there is already a WebSocket connection associated with the Endpoints connection is not permitted and will fail with a `409 Conflict` status code.

Errors while establishing the connection are handled by returning a `500 Server Error` status code as the response to the upgrade request. This includes errors initializing EndPoint types. Unhandled application errors trigger a WebSocket `Close` frame with reason code that matches the error as per the spec (for errors like messages being too large, or invalid UTF-8). For other unexpected errors during the connection, the `1008 Policy Violation` status code is used. 

## HTTP Post (Client-to-Server only)

HTTP Post is a half-transport, it is only able to send messages from the Client to the Server, as such it is always used with one of the other half-transports which can send from Server to Client (Server Sent Events and Long Polling).

This transport requires that a connection be established using the `OPTIONS [endpoint-base]` request.

The HTTP POST request is made to the URL `[endpoint-base]`. The **mandatory** `connectionId` query string value is used to identify the connection to send to. If there is no `connectionId` query string value, a `400 Bad Request` response is returned. The content consists of frames in the same format as the Long Polling transport. It is up to the client which of the Text or Binary protocol they use, and the server is able to detect which they use either via the `Content-Type` (see the Long Polling transport section) or via the first byte (`T` for the Text-based protocol, `B` for the binary protocol). Upon receipt of the **entire** request, the server will process and deliver all the messages, responding with `202 Accepted` if all the messages are successfully processed. If a client makes another request to `/` while an existing one is outstanding, the new request is immediately terminated by the server with the `409 Conflict` status code.

If the client transmits a `Close` or `Error` frame, the server will ignore and discard any frames following that frame and immediately return `202 Accepted`. Any further attempts to send to that connection will receive a `404 Not Found` response, as the connection will have been terminated.

If a client receives either a `202 Accepted` or `409 Conflict` request, the connection remains open. Any other response indicates that the connection has been terminated due to an error.

This transport will always produce single-frame messages, since the Long-Polling protocol below does not support encoding an end-of-message flag.

If the relevant connection has been terminated, a `404 Not Found` status code is returned. If there is an error instantiating an EndPoint or dispatching the message, a `500 Server Error` status code is returned.

## Server-Sent Events (Server-to-Client only)

Server-Sent Events (SSE) is a protocol specified by WHATWG at [https://html.spec.whatwg.org/multipage/comms.html#server-sent-events](https://html.spec.whatwg.org/multipage/comms.html#server-sent-events). It is capable of sending data from server to client only, so it must be paired with the HTTP Post transport. It also requires a connection already be established using the `OPTIONS [endpoint-base]` request.


The protocol is similar to Long Polling in that the client opens a request to an endpoint and leaves it open. The server transmits frames as "events" using the SSE protocol. The protocol encodes a single event as a sequence of key-value pair lines, separated by `:` and using any of `\r\n`, `\n` or `\r` as line-terminators, followed by a final blank line. Keys can be duplicated and their values are concatenated with `\n`. So the following represents two events:

```
foo: bar
baz: boz
baz: biz
quz: qoz
baz: flarg

foo: boz

```

In the first event, the value of `baz` would be `boz\nbiz\nflarg`, due to the concatenation behavior above. Full details can be found in the spec linked above.

In this transport, the client establishes an SSE connection to `[endpoint-base]` with an `Accept` header of `text/event-stream`, and the server responds with an HTTP response with a `Content-Type` of `text/event-stream`. The **mandatory** `connectionId` query string value is used to identify the connection to send to. If there is no `connectionId` query string value, a `400 Bad Request` response is returned, if there is no connection with the specified ID, a `404 Not Found` response is returned. Each SSE event represents a single frame from client to server. The transport uses unnamed events, which means only the `data` field is available. Thus we use the first line of the `data` field for frame metadata.

TBD: Keep Alive - Should it be done at this level?

## Long Polling (Server-to-Client only)

Long Polling is a server-to-client half-transport, so it is always paired with HTTP Post. It requires a connection already be established using the `OPTIONS [endpoint-base]` request.

Long Polling requires that the client poll the server for new messages. Unlike traditional polling, if there is no data available, the server will simply wait for messages to be dispatched. At some point, the server, client or an upstream proxy will likely terminate the connection, at which point the client should immediately re-send the request. Long Polling is the only transport that allows a "reconnection" where a new request can be received while the server believes an existing request is in process. This can happen because of a time out. When this happens, the existing request is immediately terminated with status code `204 No Content`. Any messages which have already been written to the existing request will be flushed and considered sent. In the case of a server side timeout with no data, a `200 OK` with a 0 `Content-Length` will be sent and the client should poll again for more data.

A Poll is established by sending an HTTP GET request to `[endpoint-base]` with the following query string parameters

* `connectionId` (Required) - The Connection ID of the destination connection.

When data is available, the server responds with a body in one of the two formats below (depending upon the value of the `Accept` header). The response may be chunked, as per the chunked encoding part of the HTTP spec.

If the `connectionId` parameter is missing, a `400 Bad Request` response is returned. If there is no connection with the ID specified in `connectionId`, a `404 Not Found` response is returned.
