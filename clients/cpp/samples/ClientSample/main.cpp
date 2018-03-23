// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#include <iostream>

#include "HubConnection.h"
#include "WebSocketsTransport.h"

int main(void)
{
    signalr::HubConnection hubConnection(L"http://localhost:5000/default", Transport::WebSockets);

    hubConnection.On(L"Send", [](const utility::string_t& message)
    {
        std::cout << "From 'Send' HubConnection method: " << utility::conversions::to_utf8string(message) << std::endl;
    });

    hubConnection.Start().wait();

    std::string msg;
    while (true)
    {
        std::cin >> msg;
        if (msg == "s")
            break;
        web::json::value args{};
        args[0] = web::json::value(utility::conversions::to_string_t(msg));

        auto ret = hubConnection.Invoke(L"Send", args.serialize()).get();
        std::cout << "result is: " << utility::conversions::to_utf8string(ret) << std::endl;
    }

    hubConnection.Stop().wait();
}