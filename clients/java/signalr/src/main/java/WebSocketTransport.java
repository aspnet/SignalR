// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import com.google.gson.Gson;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import org.java_websocket.client.WebSocketClient;
import org.java_websocket.handshake.ServerHandshake;

import java.net.URI;
import java.net.URISyntaxException;

public class WebSocketTransport implements ITransport {
    private char record_separator = 0x1e;
    private WebSocketClient _webSocket;
    private OnReceiveCallBack onReceiveCallBack;
    private URI _url;
    private JsonParser jsonParser = new JsonParser();

    public WebSocketTransport(String url) throws URISyntaxException {
        // To Do: Format the  incoming URL for a websocket connection.
        _url = new URI(url);
        _webSocket = createWebSocket();
    }

    @Override
    public void start() throws InterruptedException {
        _webSocket.connectBlocking();
        _webSocket.send(createHandshakeMessage() + record_separator);
    }

    @Override
    public void send(InvocationMessage invocationMessage) {
        Gson gson = new Gson();
        String message = gson.toJson(invocationMessage) + record_separator;
        _webSocket.send(message);
    }

    @Override
    public void setOnReceive(OnReceiveCallBack callback) {
        this.onReceiveCallBack = callback;
    }

    @Override
    public void onReceive(String message) {
        //Hacking parsing the message type
        String[] messages = message.split(Character.toString(record_separator));
        for (String splitMessage : messages) {

            // Empty handshake response "{}". We can ignore it
            if(splitMessage.length() == 2){
                continue;
            }
            JsonObject jObject = jsonParser.parse(splitMessage).getAsJsonObject();
            this.onReceiveCallBack.invoke(jObject);
        }
    }

    private String createHandshakeMessage(){
        Gson gson = new Gson();
        return gson.toJson(new DefaultJsonProtocolHandShakeMessage());
    }

    @Override
    public void stop() {
        _webSocket.closeConnection(0, "HubConnection Stopped");
    }

    private WebSocketClient createWebSocket(){
     return new WebSocketClient(_url) {
         @Override
         public void onOpen(ServerHandshake handshakedata) {
             System.out.println("Connected to " + _url);
         }

         @Override
         public void onMessage(String message) {
             onReceive(message);
         }

         @Override
         public void onClose(int code, String reason, boolean remote) {
            System.out.println("Connection Closed");
         }

         @Override
         public void onError(Exception ex) {
            System.out.println("Error: " + ex.getMessage());
         }
     };
    }
}
