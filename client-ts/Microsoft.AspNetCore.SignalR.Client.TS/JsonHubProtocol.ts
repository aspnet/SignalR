﻿import { TextMessageFormat } from "./Formatters";
import { IHubProtocol, ProtocolType, HubMessage } from "./IHubProtocol";

export class JsonHubProtocol implements IHubProtocol {
    name(): string {
        return "json"
    }

    type(): ProtocolType {
        return ProtocolType.Text;
    }

    parseMessages(input: string): HubMessage[] {
        if (!input) {
            return [];
        }

        // Parse the messages
        let messages = TextMessageFormat.parse(input);
        let hubMessages = [];
        for (var i = 0; i < messages.length; ++i) {
            hubMessages.push(JSON.parse(messages[i]));
        }

        return hubMessages;
    }

    writeMessage(message: HubMessage): string {
        return TextMessageFormat.write(JSON.stringify(message));
    }
}