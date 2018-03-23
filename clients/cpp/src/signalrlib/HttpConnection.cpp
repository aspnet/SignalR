// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#include <cpprest\http_client.h>
#include <cpprest\json.h>
#include <cpprest\ws_client.h>
#include "HttpConnection.h"

namespace signalr
{
    HttpConnection::HttpConnection(const utility::string_t& url)
    {
        web::http::client::http_client client(url);

        web::http::http_request request;
        request.set_method(L"POST");

        client.request(request)
            .then([](const web::http::http_response& response)
        {
            return response.extract_json();
        }).then([](const web::json::value& json)
        {
            auto id = json.at(L"connectionId");
            auto transports = json.at(L"availableTransports");
        }).wait();

        auto webSocket = web::web_sockets::client::websocket_client();
        //webSocket.connect()
    }
}