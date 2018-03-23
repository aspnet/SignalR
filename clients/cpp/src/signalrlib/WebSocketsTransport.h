// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#pragma once

#include <cpprest\ws_client.h>
#include <cpprest\json.h>
#include "ITransport.h"

namespace signalr
{
    class WebSocketsTransport : public ITransport
    {
    public:
        WebSocketsTransport(const utility::string_t& url);
        pplx::task<void> Start();
        pplx::task<void> Send(const utility::string_t& message);
        pplx::task<void> Stop();

        void OnReceived(std::function<void(const utility::string_t&)> func);

        ~WebSocketsTransport();
    private:
        web::web_sockets::client::websocket_callback_client mWebSocket;
        utility::string_t mUrl;
        std::function<void(const utility::string_t&)> mReceivedCallback;
    };
}