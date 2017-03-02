import { Message, MessageType } from './Message';

let knownTypes = {
    "T": MessageType.Text,
    "B": MessageType.Binary,
    "C": MessageType.Close,
    "E": MessageType.Error
};

function splitAt(input: string, searchString: string, position: number): [string, number] {
    let index = input.indexOf(searchString, position);
    if (index < 0) {
        return [input.substr(position), input.length];
    }
    let left = input.substring(position, index);
    return [left, index + searchString.length];
}

export namespace SSE {
    export function parseMessage(input: string): [string, Message] {
        // The SSE protocol is pretty simple. We just look at the first line for the type, and then process the remainder.
        // Binary messages require Base64-decoding and ArrayBuffer support, just like in the other formats below

        if (input.length == 0) {
            return ["Message is missing header", null];
        }

        let [header, offset] = splitAt(input, "\n", 0);
        let payload = input.substring(offset);

        // Just in case the header used CRLF as the line separator, carve it off
        if (header.endsWith('\r')) {
            header = header.substr(0, header.length - 1);
        }

        // Parse the header
        var type = knownTypes[header];
        if (type === undefined) {
            return ["Unknown type value: '" + header + "'", null];
        }

        if (type == MessageType.Binary) {
            // We need to decode and put in an ArrayBuffer. Throw for now
            // This will require our own Base64-decoder because the browser
            // built-in one only decodes to strings and throws if invalid UTF-8
            // characters are found.
            throw "TODO: Support for binary messages";
        }

        // Create the message
        return [null, new Message(type, payload)];
    }
}

export namespace Text {
    const InvalidPayloadError = new Error("Invalid text message payload");
    const LengthRegex = /^[0-9]+$/;

    function hasSpace(input: string, offset: number, length: number): boolean {
        let requiredLength = offset + length;
        return input.length >= requiredLength;
    }

    function parseMessage(input: string, position: number, log?: (message: string) => void): [string, number, Message] {
        var offset = position;

        // Read the length
        var [lenStr, offset] = splitAt(input, ":", offset);

        // parseInt is too leniant, we need a strict check to see if the string is an int

        if (!LengthRegex.test(lenStr)) {
            return [`Invalid length: '${lenStr}'`, position, null];
        }
        let length = Number.parseInt(lenStr);

        // Required space is: 3 (type flag, ":", ";") + length (payload len)
        if (!hasSpace(input, offset, 3 + length)) {
            return ["Message is incomplete", position, null];
        }

        // Read the type
        var [typeStr, offset] = splitAt(input, ":", offset);

        // Parse the type
        var type = knownTypes[typeStr];
        if (type === undefined) {
            return ["Unknown type value: '" + typeStr + "'", position, null];
        }

        // Read the payload
        var payload = input.substr(offset, length);
        offset += length;

        // Verify the final trailing character
        if (input[offset] != ';') {
            return ["Message missing trailer character", position, null]
        }
        offset += 1;

        if (type == MessageType.Binary) {
            // We need to decode and put in an ArrayBuffer. Throw for now
            // This will require our own Base64-decoder because the browser
            // built-in one only decodes to strings and throws if invalid UTF-8
            // characters are found.
            throw "TODO: Support for binary messages";
        }

        return [null, offset, new Message(type, payload)];
    }

    export function parseMessages(input: string): [string, Message[]] {
        if (input.length == 0) {
            return [null, []];
        }

        let messages = [];
        var offset = 0;
        while (offset < input.length) {
            var [error, offset, message] = parseMessage(input, offset);
            if (error) {
                // Failed to parse message. Report the error and the messages we parsed so far
                return [error, messages];
            }
            messages.push(message);
        }
        return [null, messages];
    }

    export function formatMessages(input: Message[]): string {
        throw "Not implemented";
    }
}

export function parseMessages(input: string): [string, Message[]] {
    if (input.length == 0) {
        return [null, []];
    }

    if (input[0] == 'T') {
        return Text.parseMessages(input.substr(1));
    }
    else {
        return [`Unsupported message format: '${input[0]}'`, []]
    }
}