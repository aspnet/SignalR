// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#pragma once

#include <cpprest\uri.h>
#include "Transports.h"
#include "ITransport.h"
#include <unordered_map>

namespace signalr
{
    class HubConnection
    {
    public:
        HubConnection(const utility::string_t& url, Transport transport = Transport::WebSockets);
        pplx::task<void> Start();
        pplx::task<void> Stop();

        void On(const utility::string_t& method, std::function<void(const utility::string_t&)> func);

        //On
        //Stream
        pplx::task<utility::string_t> Invoke(const utility::string_t& target, const utility::string_t& arguments = L"");
        pplx::task<void> Send(const utility::string_t& target, const utility::string_t& arguments = L"");

        ~HubConnection();
    private:
        pplx::task<void> SendCore(const utility::string_t& message);
        utility::string_t mUrl;
        ITransport* mTransport;
        std::unordered_map<utility::string_t, std::function<void(const utility::string_t&)>> mHandlers;
        std::unordered_map<utility::string_t, pplx::task_completion_event<utility::string_t>> mPendingCalls;
        bool mHandshakeReceived;
        std::mutex mLock;
        std::atomic<long> mInvocationId;
    };
}