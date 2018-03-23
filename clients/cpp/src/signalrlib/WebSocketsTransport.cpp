// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#include "WebSocketsTransport.h"

namespace signalr
{
    WebSocketsTransport::WebSocketsTransport(const utility::string_t& url)
        : mReceivedCallback([](const utility::string_t &) {})
    {
        mWebSocket = web::web_sockets::client::websocket_callback_client();
        auto uri = web::uri_builder(url);
        if (uri.scheme() == L"http")
        {
            uri.set_scheme(L"ws");
        }
        else if (uri.scheme() == L"https")
        {
            uri.set_scheme(L"wss");
        }
        else
        {
            //???
        }
        mUrl = uri.to_string();
    }

    pplx::task<void> WebSocketsTransport::Start()
    {
        return mWebSocket.connect(mUrl)
            .then([&]()
        {
            mWebSocket.set_message_handler([&](const web::websockets::client::websocket_incoming_message &message)
            {
                return message.extract_string().then([&](const std::string& response)
                {
                    std::cout << "From websockets transport: " << response << std::endl;
                    mReceivedCallback(utility::conversions::to_string_t(response));
                });
            });
        });
    }

    pplx::task<void> WebSocketsTransport::Send(const utility::string_t& message)
    {
        auto request = web::websockets::client::websocket_outgoing_message();
        request.set_utf8_message(utility::conversions::to_utf8string(message));

        return mWebSocket.send(request);
    }

    void WebSocketsTransport::OnReceived(std::function<void(const utility::string_t&)> func)
    {
        mReceivedCallback = func;
    }

    pplx::task<void> WebSocketsTransport::Stop()
    {
        return mWebSocket.close();
    }

    WebSocketsTransport::~WebSocketsTransport()
    {
        mWebSocket.close().wait();
    }
}