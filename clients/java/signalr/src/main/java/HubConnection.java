// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import com.google.gson.JsonObject;

import java.net.URISyntaxException;
import java.util.HashMap;

public class HubConnection {
    private String _url;
    private ITransport _transport;
    private int _invocationId = 0;
    private OnReceiveCallBack callback;
    private HashMap<String, Action> handlers = new HashMap<>();

    public  Boolean connected = false;

    public HubConnection(String url){
        _url = url;
        callback = (message)-> processMessage(message);

        try {
            _transport = new WebSocketTransport(_url);
        } catch (URISyntaxException e) {
            e.printStackTrace();
        }
    }

    private void processMessage(JsonObject message){
        String messageType = message.get("type").toString();
        switch(messageType) {
            case "1":
                //Invocation Message
                String target = message.get("target").getAsString();
                if(handlers.containsKey(target)){
                    handlers.get(target).invoke(message.get("arguments"));
                }
                break;
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
    }

    public void start() throws InterruptedException {
        _transport.setOnReceive(this.callback);
        _transport.start();
        connected = true;
    }

    public void stop(){
        _transport.stop();
        connected = false;
    }

    public void send(String method, Object arg1){
        InvocationMessage message = new InvocationMessage(method, new Object[]{arg1});
        _transport.send(message);
    }

    public void On(String target, Action callback){
        handlers.put(target, callback);
    }

    private String getNextInvocationId(){
        _invocationId++;
        return String.valueOf(_invocationId);
    }
}
