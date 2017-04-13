# SignalR Hub Protocol

The SignalR Protocol is a protocol for two-way RPC over any Message-based transport. Either party in the connection may invoke procedures on the other party, and procedures can return zero or more results or an error.

## Terms

* Caller - The node that is issuing an `Invocation` message and receiving `Completion` and `StreamItem` messages (a node can be both Caller and Callee for different invocations simultaneously)
* Callee - The node that is receiving an `Invocation` message and issuing `Completion` and `StreamItem` messages (a node can be both Callee and Caller for different invocations simultaneously)
* Binder - The component on each node that handles mapping `Invocation` messages to method calls and return values to `Completion` and `StreamItem` messages

## Transport Requirements

The SignalR Protocol requires the following attributes from the underlying transport. The protocol was primarily designed for use with WebSockets, though it is relatively straightforward to build an adaptor layer for a different transport.

* Message-based (aka Datagram, as opposed to Streaming)
* A distinction between Text and Binary frames
* Reliable, in-order, delivery of messages - Specifically, the SignalR protocol provides no facility for retransmission or reordering of messages. If that is important to an application scenario, the application must either use a transport that guarantees it (i.e. TCP) or provide their own system for managing message order.

## Overview

There are two encodings of the SignalR protocol: [JSON](http://www.json.org/) and [Protocol Buffers](https://developers.google.com/protocol-buffers/). Only one format can be used for the duration of a connection, and the format must be negotiated in advance (i.e. using a QueryString value, Header, or other indicator). However, each format shares a similar overall structure.

In the SignalR protocol, the following types of messages can be sent:

* `Invocation` Message - Indicates a request to invoke a particular method (the Target) with provided Arguments on the remote endpoint.
* `StreamItem` Message - Indicates individual items of streamed response data from a previous Invocation message.
* `Completion` Message - Indicates a previous Invocation has completed, and no further `StreamItem` messages will be received. Contains an error if the invocation concluded with an error, or the result if the invocation is not a streaming invocation.

In order to perform a single invocation, Caller follows the following basic flow:

1. Allocate a unique `Invocation ID` value (arbitrary string, chosen by the Caller) to represent the invocation
2. Send an `Invocation` message containing the `Invocation ID`, the name of the `Target` being invoked, and the `Arguments` to provide to the method.
3. If the `Invocation` is marked as non-blocking (see "Non-Blocking Invocations" below), stop here and immediately yield back to the application.
4. Wait for a `StreamItem` or `Completion` message with a matching `Invocation ID`
5. If a `Completion` message arrives, go to 8
6. If the `StreamItem` message has a payload, dispatch the payload to the application (i.e. by yielding a result to an `IObservable`, or by collecting the result for dispatching in step 8)
7. Go to 4
8. Complete the invocation, dispatching the final payload item (if any) or the error (if any) to the application

The `Target` of an `Invocation` message must refer to a specific method, overloading is **not** permitted. In the .NET Binder, the `Target` value for a method is defined as the simple name of the Method (i.e. without qualifying type name, since a SignalR endpoint is specific to a single Hub class). `Target` is case-sensitive

**NOTE**: `Invocation ID`s are arbitrarily chosen by the Caller and the Callee is expected to use the same string in all response messages. Callees may establish reasonable limits on `Invocation ID` lengths and terminate the connection when an `Invocation ID` that is too long is received.

## Non-Blocking Invocations

Invocations can be marked as "Non-Blocking" in the `Invocation` message, which indicates to the Callee that the Caller expects no results. When a Callee receives a "Non-Blocking" Invocation, it should dispatch the message, but send no results or errors back to the Caller. In a Caller application, the invocation will immediately return with no results. There is no tracking of completion for Non-Blocking Invocations.

## Streaming

The SignalR protocol allows for multiple `StreamItem` messages to be transmitted in response to an `Invocation` message, and allows the receiver to dispatch these results as they arrive, to allow for streaming data from one endpoint to another.

On the Callee side, it is up to the Callee's Binder to determine if a method call will yield multiple results. For example, in .NET certain return types may indicate multiple results, while others may indicate a single result. Even then, applications may wish for multiple results to be buffered and returned in a single `Completion` frame. It is up to the Binder to decide how to map this. The Callee's Binder must encode each result in separate `StreamItem` messages, indicating the end of results by sending a `Completion` message. Since the `Completion` message accepts an optional payload value, methods with single results can be handled with a single `Completion` message, bearing the complete results.

On the Caller side, the user code which performs the invocation indicates how it would like to receive the results and it is up the Caller's Binder to determine how to handle the result. If the Caller expects only a single result, but multiple results are returned, the Caller's Binder should yield an error indicating that multiple results were returned. However, if a Caller expects multiple results, but only a single result is returned, the Caller's Binder should yield that single result and indicate there are no further results.

## Completion and results

An Invocation is only considered completed when the `Completion` message is recevied. Receiving **any** message using the same `Invocation ID` after a `Completion` message has been received for that invocation is considered a protocol error and the recipient may immediately terminate the connection.

If a Callee is going to stream results, it **MUST** send each individual result in a separate `StreamItem` message, and the `Completion` message **MUST NOT** contain a result. If the Callee is going to return a single result, it **MUST** not send any `StreamItem` messages, and **MUST** send the single result in a `Completion` message. This is to ensure that the Caller can unambiguously determine the intended streaming behavior of the method. As an example of why this distinction is necessary, consider the following C# methods:

```csharp
public int SingleResult();

[return: Streamed]
public IEnumerable<int> StreamedResults();
```

If the caller invokes `SingleResult`, they will get a single result back, and there is no problem. The problem arises with `StreamedResults`. If the caller asks for a single `int`, and is thus not expecting a stream, and the callee returns a single int in a `StreamItem` frame, the caller thinks that it has received the correct data, but actually they have disagreed on the return type of the method (the caller believes it is `int` but the callee believes it is `IEnumerable<int>`). Callers and callees should not disagree on the signatures of these methods, so the difference between a streamed result and a single result should be explicit. Thus, the rules above.

## Errors

Errors are indicated by the presence of the `error` field in a `Completion` message. Errors always indicate the immediate end of the invocation. In the case of streamed responses, the arrival of a `Completion` message indicating an error should **not** stop the dispatching of previously-received results. The error is only yielded after the previously-received results have been dispatched.

If either endpoint commits a Protocol Error (see examples below), the other endpoint may immediately terminate the underlying connection.

* It is a protocol error for any message to be missing a required field, or to have an unrecognized field.
* It is a protocol error for a Caller to send a `StreamItem` or `Completion` message with an `Invocation ID` that has not been received in an `Invocation` message from the Callee
* It is a protocol error for a Caller to send a `StreamItem` or `Completion` message in response to a Non-Blocking Invocation (see "Non-Blocking Invocations" above)
* It is a protocol error for a Caller to send a `Completion` message carrying a result when a `StreamItem` message has previously been sent for the same `Invocation ID`.
* It is a protocol error for a Caller to send a `Completion` message carrying both a result and an error.
* It is a protocol error for an `Invocation` message to have an `Invocation ID` that has already been used by *that* endpoint. However, it is **not an error** for one endpoint to use an `Invocation ID` that was previously used by the other endpoint (allowing each endpoint to track it's own IDs).

## Examples

Consider the following C# methods

```csharp
public int Add(int x, int y)
{
    return x + y;
}

public int SingleResultFailure(int x, int y)
{
    throw new Exception("It didn't work!");
}

public IEnumerable<int> Batched(int count)
{
    for(var i = 0; i < count; i++)
    {
        yield return i;
    }
}

[return: Streamed] // This is a made-up attribute that is used to indicate to the .NET Binder that it should stream results
public IEnumerable<int> Stream(int count)
{
    for(var i = 0; i < count; i++)
    {
        yield return i;
    }
}

[return: Streamed] // This is a made-up attribute that is used to indicate to the .NET Binder that it should stream results
public IEnumerable<int> StreamFailure(int count)
{
    for(var i = 0; i < count; i++)
    {
        yield return i;
    }
    throw new Exception("Ran out of data!");
}

private List<string> _callers = new List<string>();
public void NonBlocking(string caller)
{
    _callers.Add(caller);
}
```

In each of the below examples, lines starting `C->S` indicate messages sent from the Caller ("Client") to the Callee ("Server"), and lines starting `S->C` indicate messages sent from the Callee ("Server") back to the Caller ("Client"). Message syntax is just a pseudo-code and is not intended to match any particular encoding.

### Single Result (`Add` example above)

```
C->S: Invocation { Id = 42, Target = "Add", Arguments = [ 40, 2 ] }
S->C: Completion { Id = 42, Result = 42 }
```

**NOTE:** The following is **NOT** an acceptable encoding of this invocation:

```
C->S: Invocation { Id = 42, Target = "Add", Arguments = [ 40, 2 ] }
S->C: StreamItem { Id = 42, Item = 42 }
S->C: Completion { Id = 42 }
```

### Single Result with Error (`SingleResultFailure` example above)

```
C->S: Invocation { Id = 42, Target = "SingleResultFailure", Arguments = [ 40, 2 ] }
S->C: Completion { Id = 42, Error = "It didn't work!" }
```

### Batched Result (`Batched` example above)

```
C->S: Invocation { Id = 42, Target = "Batched", Arguments = [ 5 ] }
S->C: Completion { Id = 42, Result = [ 0, 1, 2, 3, 4 ] }
```

### Streamed Result (`Stream` example above)

```
C->S: Invocation { Id = 42, Target = "Stream", Arguments = [ 5 ] }
S->C: StreamItem { Id = 42, Item = 0 }
S->C: StreamItem { Id = 42, Item = 1 }
S->C: StreamItem { Id = 42, Item = 2 }
S->C: StreamItem { Id = 42, Item = 3 }
S->C: StreamItem { Id = 42, Item = 4 }
S->C: Completion { Id = 42 }
```

**NOTE:** The following is **NOT** an acceptable encoding of this invocation:

```
C->S: Invocation { Id = 42, Target = "Stream", Arguments = [ 5 ] }
S->C: StreamItem { Id = 42, Item = 0 }
S->C: StreamItem { Id = 42, Item = 1 }
S->C: StreamItem { Id = 42, Item = 2 }
S->C: StreamItem { Id = 42, Item = 3 }
S->C: Completion { Id = 42, Item = 4 }
```

### Streamed Result with Error (`StreamFailure` example above)

```
C->S: Invocation { Id = 42, Target = "Stream", Arguments = [ 5 ] }
S->C: StreamItem { Id = 42, Item = 0 }
S->C: StreamItem { Id = 42, Item = 1 }
S->C: StreamItem { Id = 42, Item = 2 }
S->C: StreamItem { Id = 42, Item = 3 }
S->C: StreamItem { Id = 42, Item = 4 }
S->C: Completion { Id = 42, Error = "Ran out of data!" }
```

This should manifest to the Calling code as a sequence which emits `0`, `1`, `2`, `3`, `4`, but then fails with the error `Ran out of data!`.

### Non-Blocking Call (`NonBlocking` example above)

```
C->S: Invocation { Id = 42, Target = "NonBlocking", Arguments = [ "foo" ] }
```

## JSON Encoding

In the JSON Encoding of the SignalR Protocol, each Message is represented as a single JSON object, which should be the only content of the underlying message from the Transport. All property names are case-sensitive. The underlying protocol is expected to handle encoding and decoding of the text, so the JSON string should be encoded in whatever form is expected by the underlying transport. For example, when using the ASP.NET Sockets transports, UTF-8 encoding is always used for text.

### Invocation Message Encoding

An `Invocation` message is a JSON object with the following properties:

* `type` - A `Number` with the literal value 1, indicating that this message is an Invocation.
* `invocationId` - A `String` encoding the `Invocation ID` for a message.
* `nonblocking` - A `Boolean` indicating if the invocation is Non-Blocking (see "Non-Blocking Invocations" above). Optional and defaults to `false` if not present.
* `target` - A `String` encoding the `Target` name, as expected by the Callee's Binder
* `arguments` - An `Array` containing arguments to apply to the method referred to in Target. This is a sequence of JSON `Token`s, encoded as indicated below in the "JSON Payload Encoding" section

Example:

```json
{
    "type": 1,
    "invocationId": 123,
    "target": "Send",
    "arguments": [
        42,
        "Test Message"
    ]
}
```
Example (Non-Blocking):

```json
{
    "type": 1,
    "invocationId": 123,
    "nonblocking": true,
    "target": "Send",
    "arguments": [
        42,
        "Test Message"
    ]
}
```

### StreamItem Message Encoding

A `StreamItem` message is a JSON object with the following properties:

* `type` - A `Number` with the literal value 2, indicating that this message is a StreamItem.
* `invocationId` - A `String` encoding the `Invocation ID` for a message.
* `item` - A `Token` encoding the stream item (see "JSON Payload Encoding" for details).

Example

```json
{
    "type": 2,
    "invocationId": 123,
    "item": 42
}
```

### Completion Message Encoding

A `Completion` message is a JSON object with the following properties

* `type` - A `Number` with the literal value `3`, indicating that this message is a `Completion`.
* `invocationId` - A `String` encoding the `Invocation ID` for a message.
* `result` - A `Token` encoding the result value (see "JSON Payload Encoding" for details). This field is **ignored** if `error` is present.
* `error` - A `String` encoding the error message.

It is a protocol error to include both a `result` and an `error` property in the `Completion` message. A conforming endpoint may immediately terminate the connection upon receiving such a message.

Example - A `Completion` message with no result or error

```json
{
    "type": 3,
    "invocationId": 123
}
```

Example - A `Completion` message with a result

```json
{
    "type": 3,
    "invocationId": 123,
    "result": 42
}
```

Example - A `Completion` message with an error

```json
{
    "type": 3,
    "invocationId": 123,
    "error": "It didn't work!"
}
```

Example - The following `Completion` message is a protocol error because it has both of `result` and `error`

```json
{
    "type": 3,
    "invocationId": 123,
    "result": 42,
    "error": "It didn't work!"
}
```

### JSON Payload Encoding

Items in the arguments array within the `Invocation` message type, as well as the `item` value of the `StreamItem` message and the `result` value of the `Completion` message, encode values which have meaning to each particular Binder. A general guideline for encoding/decoding these values is provided in the "Type Mapping" section at the end of this document, but Binders should provide configuration to applications to allow them to customize these mappings. These mappings need not be self-describing, because when decoding the value, the Binder is expected to know the destination type (by looking up the definition of the method indicated by the Target).

## Protocol Buffers (ProtoBuf) Encoding

In order to support ProtoBuf, an application must provide a [ProtoBuf service definition](https://developers.google.com/protocol-buffers/docs/proto3) for the Hub. However, implementations may automatically generate these definitions from reflection information, if the underlying platform supports this. For example, the .NET implementation will attempt to generate service definitions for methods that use only simple primitive and enumerated types. The service definition provides a description of how to encode the arguments and return value for the call. For example, consider the following C# method:

```csharp
public bool SendMessageToUser(string userName, string message) {}
```

In order to invoke this method, the application must provide a ProtoBuf schema representing the input and output values and defining the message:

```protobuf
syntax = "proto3";

message SendMessageToUserRequest {
    string userName = 1;
    string message = 2;
}

message SendMessageToUserResponse {
    bool result = 1;
}

service ChatService {
    rpc SendMessageToUser (SendMessageToUserRequest) returns (SendMessageToUserResponse);
}
```

**NOTE**: the .NET implementation will provide a way to automatically generate these definitions at runtime, to avoid needing to generate them in advance, but applications still have the option of doing so. A general guideline for mapping .NET types to ProtoBuf types is listed in the "Type Mapping" section at the end of this document. In the current plan, custom .NET classes/structs not already listed in the table below will require a complete ProtoBuf mapping to be provided by the application.

## SignalR.proto

SignalR provides an outer ProtoBuf schema for encoding the RPC invocation process as a whole, which is defined by the .proto file below. A SignalR frame is encoded as a single message of type `SignalRFrame`, then transmitted using the underlying transport. Since the underlying transport provides the necessary framing, we can reliably decode a message without having to know the length or format of the arguments.

```protobuf
syntax = "proto3";

message Invocation {
    string target = 1;
    bool nonblocking = 2;
    bytes arguments = 3;
}

message StreamItem {
    bytes item = 1;
}

message Completion {
    oneof payload {
        bytes result = 1;
        string error = 2;
    }
}

message SignalRFrame {
    string invocationId = 1;
    oneof message {
        Invocation invocation = 2;
        StreamItem streamItem = 3;
        Completion completion = 4;
    }
}
```

## Invocation Message

When an invocation is issued by the Caller, we generate the necessary Request message according to the service definition, encode it into the ProtoBuf wire format, and then transmit an `Invocation` ProtoBuf message with that encoded argument data as the `arguments` field. The resulting `Invocation` message is wrapped in a `SignalRFrame` message and the `invocationId` is set. The final message is then encoded in the ProtoBuf format and transmitted to the Callee.

## StreamItem Message

When a result is emitted by the Callee, it is encoded using the ProtoBuf schema associated with the service and encoded into the `item` field of a `StreamItem` ProtoBuf message. If an error is emitted, the message is encoded into the error field of a StreamItem ProtoBuf message. The resulting `StreamItem` message is wrapped in a `SignalRFrame` message and the `invocationId` is set. The final message is then encoded in the ProtoBuf format and transmitted to the Callee.

## Completion Message

When a request completes, a `Completion` ProtoBuf message is constructed. If there is a final payload, it is encoded the same way as in the `StreamItem` message and stored in the `result` field of the message. If there is an error, it is encoded in the `error` field of the message. The resulting `Completion` message is wrapped in a `SignalRFrame` message and the `invocationId` is set. The final message is then encoded in the ProtoBuf format and transmitted to the Callee.

## Type Mappings

Below are some sample type mappings between JSON/ProtoBuf types and the .NET client. This is not an exhaustive or authoritative list, just informative guidance. Official clients will provide ways for users to override the default mapping behavior for a particular method, parameter, or parameter type

|                  .NET Type                      |          JSON Type           |                  ProtoBuf Type               |
| ----------------------------------------------- | ---------------------------- | -------------------------------------------- |
| `System.Byte`, `System.UInt16`, `System.UInt32` | `Number`                     | `uint32`                                     |
| `System.SByte`, `System.Int16`, `System.Int32`  | `Number`                     | `int32`                                      |
| `System.UInt64`                                 | `Number`                     | `uint64`                                     |
| `System.Int64`                                  | `Number`                     | `int64`                                      |
| `System.Single`                                 | `Number`                     | `float`                                      |
| `System.Double`                                 | `Number`                     | `double`                                     |
| `System.Boolean`                                | `true` or `false`            | `bool`                                       |
| `System.String`                                 | `String`                     | `string`                                     |
| `System.Byte`[]                                 | `String` (Base64-encoded)    | `bytes`                                      |
| `IEnumerable<T>`                                | `Array`                      | `repeated`                                   |
| custom `enum`                                   | `Number`                     | `uint64`                                     |
| custom `struct` or `class`                      | `Object`                     | Requires an explicit .proto file definition  |
