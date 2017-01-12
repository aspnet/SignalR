# Transport Protocols

This document describes the protocols used by the three ASP.NET Endpoint Transports: WebSockets, Server-Sent Events, Long Polling and HTTP Post

## Transport Requirements

A transport is required to have the following attributes:

1. Duplex - Able to send messages from Server to Client and from Client to Server
1. Frame-based - Messages have fixed length (as opposed to streaming, where data is just pushed throughout the connection)
1. Frame Metadata - Able to encode two pieces of Frame Metadata
    * `Type` - Either `Binary` or `Text`.
    * `EndOfMessage` - Boolean indicating if this frame represents the final frame of a complete message.
1. Binary-safe - Able to transmit arbitrary binary data, regardless of content
1. Text-safe - Able to transmit arbitrary text data, preserving the content. Line-endings must be preserved **but may be converted to a different format**. For example `\r\n` may be converted to `\n`. This is due to quirks in some transports (Server Sent Events)

The only transport which fully implements the duplex requirement is WebSockets, the others are "half-transports" which implement one end of the duplex connection. They are used in combination to achieve a duplex connection.

Throughout this document, the term `[endpoint-base]` is used to refer to the route assigned to a particular end point

## WebSockets (Full Duplex)

The WebSockets transport is unique in that it is full duplex, and a persistent connection that can be established in a single operation. As a result, the client is not required to use the `[endpoint-base]/negotiate` endpoint to establish a connection in advance. It also includes all the necessary metadata in it's own frame metadata.

The WebSocket transport is activated by making a WebSocket connection to `[endpoint-base]/ws`. Upon doing so, the connection is fully established and immediately ready for frames to be sent/received. The WebSocket OpCode field is used to indicate the type of the frame (Text or Binary) and the WebSocket "FIN" flag is used to indicate the end of a message.

Establishing a second WebSocket connection when there is already a WebSocket connection associated with the Endpoints connection is not permitted and will fail with a `400` (Bad Request) status code.

## HTTP Post (Client-to-Server only)

HTTP Post is a half-transport, it is only able to send messages from the Client to the Server, as such it is always used with one of the other half-transports which can send from Server to Client (Server Sent Events and Long Polling).

This transport uses HTTP Post requests to a specific end point to encode messages. Each HTTP Request represents a single message (this achieves the Frame-based requirement), contains raw binary data (Binary-safe) and uses the query string to encode the metadata.

This transport requires that a connection be established using the `[endpoint-base]/negotiate` end point.

The HTTP request is made to the URL `[endpoint-base]/send` with the following query string parameters

* `id` (Required) - The Connection ID of the destination connection.
* `format` (Optional: default `text`) - Either `text` or `binary` (**case-insensitive**), indicating the type of the frame
* `endOfMessage` (Optional: default `true`) - Indicates if this frame completes a message or if futher frames will be sent.

The content of the frame is the entire Body of the HTTP request. There are no other restrictions on the Headers provided (i.e. `Content-Type`, etc.).

## Long Polling (Server-to-Client only)

Long Polling is a server-to-client half-transport, so it is always paired with HTTP Post. It requires a connection already be established using the `[endpoint-base]/negotiate` endpoint.

Long Polling requires that the client poll the server for new messages. Unlike traditional polling, if there are no messages available, the server will simply block the request waiting for messages to be dispatched. At some point, the server, client or an upstream proxy will likely terminate the connection, at which point the client should immediately re-send the request. Long Polling is the only transport that allows a "reconnection" where a new request can be received while the server believes an existing request is in process. This can happen because of a time out. When this happens, the existing request is immediately terminated with an empty result (even if messages are available) and the new request replaces it as the active poll request.

Since there is such a long round-trip-time for messages, given that the client must issue a request before the server can transmit a message back, Long Polling responses contain batches of multiple messages. Also, in order to support browsers which do not support XHR2, which provides the ability to read binary data, there are two different modes for the polling transport.

A Poll is established by sending an HTTP GET request to `[endpoint-base]/poll` with the following query string parameters

* `id` (Required) - The Connection ID of the destination connection.
* `supportsBinary` (Optional: default `false`) - A boolean indicating if the client supports raw binary data in responses

When messages are available, the server responds with a body in one of the two formats below (depending upon the value of `supportsBinary`). The response may be chunked, as per the chunked encoding part of the HTTP spec.

### Text-based encoding (`supportsBinary` = `false` or not present)

The body will be formatted as below. Identifiers in square brackets `[]` indicate fields defined below, and parenthesis `()` indicate grouping.

```
T([Length]:[Type],[Fin]:[Body];)([Length]:[Type],[Fin]:[Body];)... continues until end of the response body ...
```

* `[Length]` - Length of the Body in ASCII Characters, specified as ASCII digits (terminated by `:`). If the body is a binary frame, this length indicates the number of Base64-encoded characters, not the number of bytes!
* `[Type]` - A single ASCII character indicating the type of the frame, `T` indicates Text and `B` indicates Binary (case-sensitive)
* `[Fin]` - A single ASCII character indicating if this frame is the last frame in a message, `T` indicates `true`, `F` indicates `false` (case-sensitive)
* `[Body]` - The body of the message. If `[Type]` is `T`, this is just the sequence of ASCII characters for the text frame. If `[Type]` is `B`, the frame is Base64-encoded, so `[Body]` must be Base64-decoded to get the actual frame content.

For example, when sending the following frames (`\n` indicates the actual Line Feed character, not an escape sequence):

* Type=`Text`, Fin=`true`, "Hello\nWorld"
* Type=`Binary`, Fin=`false`, `0x01 0x02`
* Type=`Binary`, Fin=`true`, `0x03 0x04`

The encoding will be as follows

```
T11:T,T:Hello\nWorld;4:B,F:AQI=;4:B,T:AwQ=;
```

Note that the final frame still ends with the `;` terminator, and that since the body may contain `;`, newlines, etc., the length is specified in order to know exactly where the body ends.

### Binary encoding (`supportsBinary` = `true`)

In JavaScript/Browser clients, this encoding requires XHR2 (or similar HTTP request functionality which allows binary data) and TypedArray support.

The body is encoded as follows. Identifiers in square brackets `[]` indicate fields defined below, and parenthesis `()` indicate grouping. Other symbols indicate ASCII-encoded text in the stream

```
B([Length][TypeAndFin][Body])([Length][Type][Fin][Body])... continues until end of the response body ...
```

* `[Length]` - A 64-bit integer in Network Byte Order (Big-endian) representing the length of the body in bytes
* `[TypeAndFin]` - An 8-bit integer where the most significant bit indicates if the frame is the last frame in a message (`1` indicates `true`, `0` indicates `false`) and the least significant bit indicates if the frame is text or binary (`0` indicates Text and `1` indicates Binary). Thus, the following four values are the only valid values:
    * `0x00` - Fin=`false`, Type=`Text`
    * `0x01` - Fin=`false`, Type=`Binary`
    * `0x80` - Fin=`true`, Type=`Text`
    * `0x81` - Fin=`true`, Type=`Binary`
    * All other bits are reserved and MUST be `0` or the entire response should be rejected.
* `[Body]` - The body of the message, exactly `[Length]` bytes in length.