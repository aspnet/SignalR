// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import com.google.gson.JsonArray;

import java.net.URISyntaxException;
import java.util.Scanner;

public class Chat {
    public static void main(String[] args) throws URISyntaxException, InterruptedException {
            System.out.println("From Chat");
            HubConnection hubConnection = new HubConnection("wss://signalr-samples.azurewebsites.net/default");
            //This is a blocking call

            hubConnection.On("Send", (message) -> {
                String newMessage = ((JsonArray) message).get(0).getAsString();
                System.out.println("REGISTERED HANDLER: " + newMessage);
            });

            hubConnection.start();

            Scanner reader = new Scanner(System.in);  // Reading from System.in
            String input = "";
            while (!input.equals("leave")){
                input = reader.nextLine(); // Scans the next token of the input as an int.
                hubConnection.send("Send", input);
            }

            hubConnection.stop();
    }
}
