import { parseMessages, Text, SSE } from "../../src/Microsoft.AspNetCore.SignalR.Client.TS/Formatters"
import { Message, MessageType } from "../../src/Microsoft.AspNetCore.SignalR.Client.TS/Message";

describe("Message Formatter", () => {
    it("should return empty array on empty input", () => {
        let [error, messages] = parseMessages("");
        expect(error).toBeNull();
        expect(messages).toEqual([]);
    });

    it("should fail to parse payload with unknown format indicator", () => {
        let [error, messages] = parseMessages("X");
        expect(error).toEqual("Unsupported message format: 'X'");
        expect(messages).toEqual([]);
    });

    it("should parse text-formatted messages correctly", () => {
        let [error, messages] = parseMessages("T5:T:Hello;5:C:World;5:E:Error;");
        expect(error).toBeNull("Unsupported message format: 'X'");
        expect(messages).toEqual([new Message(MessageType.Text, "Hello"), new Message(MessageType.Close, "World"), new Message(MessageType.Error, "Error")]);
    });
});

describe("Text Message Formatter", () => {
    it("should return empty array on empty input", () => {
        let [error, messages] = Text.parseMessages("");
        expect(error).toBeNull();
        expect(messages).toEqual([]);
    });
    ([
        ["0:T:;", [new Message(MessageType.Text, "")]],
        ["0:C:;", [new Message(MessageType.Close, "")]],
        ["0:E:;", [new Message(MessageType.Error, "")]],
        ["5:T:Hello;", [new Message(MessageType.Text, "Hello")]],
        ["5:T:Hello;5:C:World;5:E:Error;", [new Message(MessageType.Text, "Hello"), new Message(MessageType.Close, "World"), new Message(MessageType.Error, "Error")]],
    ] as [[string, Message[]]]).forEach(([payload, expected_messages]) => {
        it(`should parse '${payload}' correctly`, () => {
            let [error, messages] = Text.parseMessages(payload);
            expect(error).toBeNull();
            expect(messages).toEqual(expected_messages);
        })
    });

    ([
        ["ABC", "Invalid length: 'ABC'", []],
        ["1:T:A;12ab34:", "Invalid length: '12ab34'", [new Message(MessageType.Text, "A")]],
        ["1:T:A;1:asdf:", "Unknown type value: 'asdf'", [new Message(MessageType.Text, "A")]],
        ["1:T:A;1::", "Message is incomplete", [new Message(MessageType.Text, "A")]],
        ["1:T:A;1:AB:", "Message is incomplete", [new Message(MessageType.Text, "A")]],
        ["1:T:A;5:T:A", "Message is incomplete", [new Message(MessageType.Text, "A")]],
        ["1:T:A;5:T:AB", "Message is incomplete", [new Message(MessageType.Text, "A")]],
        ["1:T:A;5:T:ABCDE", "Message is incomplete", [new Message(MessageType.Text, "A")]],
        ["1:T:A;5:X:ABCDE", "Message is incomplete", [new Message(MessageType.Text, "A")]],
        ["1:T:A;5:T:ABCDEF", "Message missing trailer character", [new Message(MessageType.Text, "A")]],
    ] as [[string, string, Message[]]]).forEach(([payload, expected_error, expected_messages]) => {
        it(`should fail to parse '${payload}'`, () => {
            let [error, messages] = Text.parseMessages(payload);
            expect(error).toEqual(expected_error);
            expect(messages).toEqual(expected_messages);
        });
    });
});

describe("Server-Sent Events Formatter", () => {
    ([
        ["", "Message is missing header"],
        ["A", "Unknown type value: 'A'"],
        ["BOO\r\nBlarg", "Unknown type value: 'BOO'"]
    ] as [string, string][]).forEach(([payload, expected_error]) => {
        it(`should fail to parse '${payload}`, () => {
            let [error, message] = SSE.parseMessage(payload);
            expect(error).toEqual(expected_error);
            expect(message).toBeNull();
        });
    });

    ([
        ["T\r\nTest", new Message(MessageType.Text, "Test")],
        ["C\r\nTest", new Message(MessageType.Close, "Test")],
        ["E\r\nTest", new Message(MessageType.Error, "Test")],
        ["T", new Message(MessageType.Text, "")],
        ["T\r\n", new Message(MessageType.Text, "")],
        ["T\r\nFoo\r\nBar", new Message(MessageType.Text, "Foo\r\nBar")]
    ] as [string, Message][]).forEach(([payload, expected_message]) => {
        it(`should parse '${payload}' correctly`, () => {
            let [error, message] = SSE.parseMessage(payload);
            expect(error).toBeNull();
            expect(message).toEqual(expected_message);
        });
    });
});