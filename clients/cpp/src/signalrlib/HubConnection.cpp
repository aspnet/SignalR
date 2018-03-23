// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#include "HubConnection.h"
#include "WebSocketsTransport.h"

namespace signalr
{
    enum MessageType
    {
        Invocation = 1,
        StreamItem,
        Completion,
        StreamInvocation,
        CancelInvocation,
        Ping,
        Close,
    };

    HubConnection::HubConnection(const utility::string_t& url, Transport transport)
        : mHandshakeReceived(false)
    {
        mUrl = url;
        mTransport = new WebSocketsTransport(mUrl);
        mTransport->OnReceived([&](const utility::string_t& messages)
        {
            try
            {
                std::size_t pos = messages.find(L'\x1e', 0);
                std::size_t prevPos = 0;
                while (pos != utility::string_t::npos)
                {
                    auto message = web::json::value::parse(messages.substr(prevPos, pos - prevPos));
                    {
                        std::lock_guard<std::mutex> lock(mLock);
                        if (!mHandshakeReceived)
                        {
                            // A failed handshake or not a handshake response
                            if (message.has_field(L"error") || message.size() != 0)
                            {
                                // TODO: Logging
                                Stop().wait();
                                return;
                            }
                            mHandshakeReceived = true;
                            return;
                        }
                        // end of lock
                    }

                    auto messageType = message[L"type"];
                    switch (messageType.as_integer())
                    {
                    case MessageType::Invocation:
                    {
                        auto method = message[L"target"];
                        auto args = message[L"arguments"];
                        _ASSERT(args.is_array());
                        auto m = method.serialize();
                        // serializing a string creates "method", strip quotes, also figure out a better way to do this
                        auto handler = mHandlers.find(m.substr(1, m.size() - 2));
                        if (handler != mHandlers.end())
                        {
                            handler->second(args.serialize());
                        }
                        break;
                    }
                    case MessageType::StreamInvocation:
                        // Sent to server only, should not be received by client
                        throw std::runtime_error("Received unexpected message type 'StreamInvocation'.");
                    case MessageType::StreamItem:
                        // TODO
                        break;
                    case MessageType::Completion:
                    {
                        auto id = message[L"invocationId"];
                        // TODO: Lock around mPendingCalls
                        auto it = mPendingCalls.find(id.as_string());
                        auto invocationRequest = it->second;
                        mPendingCalls.erase(it);

                        if (message.has_field(L"error") && message.has_field(L"result"))
                        {
                            //error
                        }

                        if (message.has_field(L"error"))
                        {
                            auto error = message[L"error"].as_string();
                            invocationRequest.set_exception(std::runtime_error(utility::conversions::to_utf8string(error)));
                        }
                        else if (message.has_field(L"result"))
                        {
                            auto val = message[L"result"];
                            invocationRequest.set(val.serialize());
                        }
                        break;
                    }
                    case MessageType::CancelInvocation:
                        // Sent to server only, should not be received by client
                        throw std::runtime_error("Received unexpected message type 'CancelInvocation'.");
                    case MessageType::Ping:
                        // TODO
                        break;
                    case MessageType::Close:
                        // TODO
                        break;
                    }

                    prevPos = pos + 1;
                    pos = messages.find(L'\x1e', prevPos);
                }
            }
            catch (std::exception ex)
            {
                // TODO
                std::cout << "Exception in OnReceived " << ex.what() << std::endl;
            }
        });
    }

    pplx::task<void> HubConnection::Start()
    {
        mInvocationId = 0;
        return mTransport->Start()
            .then([&]()
        {
            return SendCore(L"{\"protocol\":\"json\",\"version\":1}\x1e");
        });
    }

    pplx::task<void> HubConnection::Stop()
    {
        return mTransport->Stop();
    }

    pplx::task<utility::string_t> HubConnection::Invoke(const utility::string_t& target, const utility::string_t& arguments)
    {
        auto args = web::json::value::parse(arguments);
        _ASSERT(args.is_array());

        auto value = mInvocationId++;

        web::json::value invocation;
        auto invocationId = web::json::value::value(value).serialize();
        invocation[L"type"] = web::json::value::value(MessageType::Invocation);
        invocation[L"invocationId"] = web::json::value::string(invocationId);
        invocation[L"target"] = web::json::value::string(target);
        invocation[L"arguments"] = args;

        Concurrency::task_completion_event<utility::string_t> tce;
        mPendingCalls.insert({ invocationId, tce });

        return SendCore(invocation.serialize() + L"\x1e")
            .then([tce]()
        {
            return Concurrency::task<utility::string_t>(tce)
                .then([](Concurrency::task<utility::string_t> prevTask)
            {
                try
                {
                    return prevTask.get();
                }
                catch (std::exception e)
                {
                    return utility::conversions::to_string_t(std::string(e.what()));
                }
            });
        });
    }

    pplx::task<void> HubConnection::Send(const utility::string_t& target, const utility::string_t& arguments)
    {
        auto args = web::json::value::parse(arguments);
        _ASSERT(args.is_array());

        web::json::value invocation;
        invocation[L"type"] = web::json::value::value(MessageType::Invocation);
        invocation[L"target"] = web::json::value::string(target);
        invocation[L"arguments"] = args;
        return SendCore(invocation.serialize() + L"\x1e");
    }

    pplx::task<void> HubConnection::SendCore(const utility::string_t& message)
    {
        return mTransport->Send(message);
    }

    // TODO: Multiple functions registered on the same method
    // TODO: Ability to unregister functions
    void HubConnection::On(const utility::string_t& method, std::function<void(const utility::string_t&)> func)
    {
        mHandlers[method] = func;
    }

    HubConnection::~HubConnection()
    {
        mTransport->Stop().wait();
        delete mTransport;
    }
}