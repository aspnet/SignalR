// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import com.google.gson.Gson;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.util.ArrayList;
import java.util.List;

public class JsonHubProtocol implements HubProtocol {
    private final JsonParser jsonParser = new JsonParser();
    private final Gson gson = new Gson();
    private static final String RECORD_SEPARATOR = "\u001e";

    @Override
    public String getName() {
        return "json";
    }

    @Override
    public int getVersion() {
        return 1;
    }

    @Override
    public TransferFormat getTransferFormat() {
        return TransferFormat.Text;
    }

    @Override
    public HubMessage[] parseMessages(String payload) {
        String[] messages = payload.split(RECORD_SEPARATOR);
        List<HubMessage> hubMessages = new ArrayList<>();
        for (String splitMessage : messages) {
            // Empty handshake response "{}". We can ignore it
            if (splitMessage.equals("{}")) {
                continue;
            }

            JsonObject jsonMessage = jsonParser.parse(splitMessage).getAsJsonObject();
            String messageType = jsonMessage.get("type").toString();
            switch (messageType) {
                case "1":
                    //Invocation Message
                    String target = jsonMessage.get("target").getAsString();
                    JsonElement args = jsonMessage.get("arguments");
                    hubMessages.add(new InvocationMessage(target, new Object[] {args}));

                    break;
                case "2":
                    //Stream item
                    throw new UnsupportedOperationException("Support for streaming is not yet available");
                case "3":
                    //Completion
                    //Don't care yet
                    break;
                case "4":
                    //Stream invocation
                    //Don't care yet;
                    throw new UnsupportedOperationException("Support for streaming is not yet available");
                case "5":
                    //Cancel invocation
                    break;
                case "6":
                    //Ping
                    hubMessages.add(new PingMessage());
                    break;
                case "7":
                    // Close message
                    //Don't care yet;
                    break;
            }
        }
        return hubMessages.toArray(new HubMessage[hubMessages.size()]);
    }

    @Override
    public String writeMessage(InvocationMessage invocationMessage) {
        return gson.toJson(invocationMessage) + RECORD_SEPARATOR;
    }
}
