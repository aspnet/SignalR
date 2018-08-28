// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

package com.microsoft.aspnet.signalr;

import java.util.Scanner;

public class Chat {
    public static void main(String[] args) throws Exception {
            System.out.print("Enter your name:");
            Scanner reader = new Scanner(System.in);  // Reading from System.in
            String enteredName = "";
            enteredName = reader.nextLine();

            HubConnection hubConnection = new HubConnectionBuilder()
                    .withUrl("https://signalrservice-chatsample.azurewebsites.net/chat")
                    .configureLogging(LogLevel.Information).build();

            hubConnection.on("Send", (name, message) -> {
                System.out.println(name + ":" + message);
            }, String.class, String.class);

            hubConnection.onClosed((ex) -> {
                if(ex.getMessage() != null){
                    System.out.printf("There was an error: %s", ex.getMessage());
                }
            });

            //This is a blocking call
            hubConnection.start();

            String message = "";
            while (!message.equals("leave")){
                // Scans the next token of the input as an int.
                message = reader.nextLine();
                hubConnection.send("Send", enteredName, message);
            }

            hubConnection.stop();
    }
}
