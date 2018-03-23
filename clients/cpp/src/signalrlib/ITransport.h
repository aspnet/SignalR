// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#pragma once

#include <cpprest\json.h>
#include <functional>

namespace signalr
{
    class ITransport
    {
    public:
        virtual pplx::task<void> Start() = 0;
        virtual pplx::task<void> Send(const utility::string_t& message) = 0;
        virtual pplx::task<void> Stop() = 0;
        virtual void OnReceived(std::function<void(const utility::string_t&)> func) = 0;
    };
}