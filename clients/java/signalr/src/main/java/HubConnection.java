// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import java.net.URISyntaxException;
import java.util.HashMap;

public class HubConnection {
    private String url;
    private Transport transport;
    private OnReceiveCallBack callback;
    private HashMap<String, Action> handlers = new HashMap<>();
    private HubProtocol protocol;

    public Boolean connected = false;

    public HubConnection(String url, Transport transport){
        this.url = url;
        this.protocol = new JsonHubProtocol();
        this.callback = (payload) -> {

            InvocationMessage[] messages = protocol.parseMessages(payload);

            // message will be null if we receive any message other than an invocation.
            // Adding this to avoid getting error messages on pings for now.
            for (InvocationMessage message : messages) {
                if (message != null && handlers.containsKey(message.target)) {
                    handlers.get(message.target).invoke(message.arguments[0]);
                }
            }
        };

        if (transport == null){
            try {
                this.transport = new WebSocketTransport(this.url);
            } catch (URISyntaxException e) {
                e.printStackTrace();
            }
        } else {
            this.transport = transport;
        }
    }

    public HubConnection(String url) {
        this(url, null);
    }

    public void start() throws InterruptedException {
        transport.setOnReceive(this.callback);
        transport.start();
        connected = true;
    }

    public void stop(){
        transport.stop();
        connected = false;
    }

    public void send(String method, Object... args) {
        InvocationMessage invocationMessage = new InvocationMessage(method, args);
        String message = protocol.writeMessage(invocationMessage);
        transport.send(message);
    }

    public void On(String target, Action callback) {
        handlers.put(target, callback);
    }

    public void On(String target, Action callback) {
        ActionBase actionBase = new ActionBase(callback);
        handlers.put(target, actionBase);
    }

    public <T1> void On(String target, Action1<T1> callback, Class<T1> param1) {
        ActionBase actionBase = new ActionBase(callback, param1);
        handlers.put(target, actionBase);
    }

    public <T1, T2> void On(String target, Action2<T1,T2> callback, Class<T1> param1, Class<T2> param2) {
        ActionBase actionBase = new ActionBase(callback, param1, param2);
        handlers.put(target, actionBase);
    }

    public <T1, T2, T3> void On(String target, Action2<T1,T2, T3> callback, Class<T1> param1, Class<T2> param2, Class<T3> param3) {
        ActionBase actionBase = new ActionBase(callback, param1, param2, param3);
        handlers.put(target, actionBase);
    }

    public <T1, T2, T3, T4> void On(String target, Action2<T1,T2, T3> callback, Class<T1> param1, Class<T2> param2, Class<T3> param3, Class<T4> param4) {
        ActionBase actionBase = new ActionBase(callback, param1, param2, param3, param4);
        handlers.put(target, actionBase);
    }

    public <T1, T2, T3, T4,T5> void On(String target, Action2<T1,T2, T3, T4, T5> callback, Class<T1> param1, Class<T2> param2, Class<T3> param3, Class<T4> param4, Class<T5> param5) {
        ActionBase actionBase = new ActionBase(callback, param1, param2, param3, param4, param5);
        handlers.put(target, actionBase);
    }
}