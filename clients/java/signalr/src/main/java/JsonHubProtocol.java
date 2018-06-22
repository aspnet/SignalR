// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import com.google.gson.Gson;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

public class JsonHubProtocol implements IHubProtocol{
    private JsonParser jsonParser = new JsonParser();
    private static final String RECORD_SEPARATOR = "\u001e";

    @Override
    public String name() {
        return "json";
    }

    @Override
    public int version() {
        return 1;
    }

    @Override
    public TransferFormat transferFormat() {
        return TransferFormat.Text;
    }

    @Override
    public InvocationMessage parseMessage(String message) {
        JsonObject jsonMessage = jsonParser.parse(message).getAsJsonObject();
        String messageType = jsonMessage.get("type").toString();
        switch(messageType) {
            case "1":
                //Invocation Message
                String target = jsonMessage.get("target").getAsString();
                JsonElement args = jsonMessage.get("arguments");
                return new InvocationMessage(target, new Object[] {args});
            case "2":
                //Stream item
                //Don't care yet
                break;
            case "3":
                //Completion
                //Don't care yet
                break;
            case "4":
                //Stream invocation
                //Don't care yet;
                break;
            case "5":
                //Cancel invocation
                //Don't care yet
                break;
            case "6":
                //Ping
                //Don't care yet
                break;
            case "7":
                // Close message
                //Don't care yet;
                break;
        }
        return null;
    }

    @Override
    public String writeMessage(InvocationMessage invocationMessage) {
        Gson gson = new Gson();
        return gson.toJson(invocationMessage) + RECORD_SEPARATOR;
    }
}
