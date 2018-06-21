// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#include "stdafx.h"

#include <iostream>
#include <sstream>
#include "hub_connection.h"
#include "log_writer.h"

#include <vector>

class logger : public signalr::log_writer
{
    // Inherited via log_writer
    virtual void __cdecl write(const utility::string_t & entry) override
    {
        std::cout << utility::conversions::to_utf8string(entry) << std::endl;
    }
};

template <typename T>
web::json::value to_json(T item)
{
    return web::json::value(item);
}

template <>
web::json::value to_json<int>(int item)
{
    return 42;
}

template <typename T>
void tryparse(web::json::value& json, T first)
{
    json[json.size()] = to_json(first);
}

template <typename arg, typename ...T>
void tryparse(web::json::value& json, arg first, T... arguments)
{
    tryparse(json, first);
    tryparse(json, arguments...);
}

//#define FROM_JSON(TYPE, NAME) \
//template <> \
//TYPE from_json<TYPE>(web::json::value item) \
//{ \
//    if (!item.is_##NAME()) \
//    { \
//        throw std::runtime_error("Could not convert json to type "#TYPE); \
//    } \
//    return (TYPE)item.as_##NAME(); \
//}

//template <>
//float from_json<float>(web::json::value item)
//{
//    if (!item.is_double())
//    {
//        throw std::runtime_error("Could not convert json to type 'float'");
//    }
//    return (float)item.as_double();
//}

//template <>
//double from_json<double>(web::json::value item)
//{
//    if (!item.is_double())
//    {
//        throw std::runtime_error("Could not convert json to type 'double'");
//    }
//    return item.as_double();
//}

//template <>
//unsigned int from_json<unsigned int>(web::json::value item)
//{
//    if (!item.is_integer())
//    {
//        throw std::runtime_error("Could not convert json to type 'unsigned int'.");
//    }
//
//    return item.as_number().to_uint32();
//}

template <typename T>
T from_json(const web::json::value& item)
{
    static_assert(false, "No conversion to json could be found for the given type, see below output for more info.");
}

template <>
signed int from_json<signed int>(const web::json::value& item)
{
    if (!item.is_integer())
    {
        throw std::runtime_error("Could not convert json to type 'int'");
    }
    return item.as_integer();
}

template <>
bool from_json<bool>(const web::json::value& item)
{
    if (!item.is_boolean())
    {
        throw std::runtime_error("Could not convert json to type 'bool'");
    }
    return item.as_bool();
}

template <>
std::string from_json<std::string>(const web::json::value& item)
{
    if (!item.is_string())
    {
        throw std::runtime_error("Could not convert json to type 'bool'");
    }
    return utility::conversions::to_utf8string(item.as_string());
}

//template <>
//float from_json<float>(web::json::value item)
//{
//    return (float)item.as_double();
//}
//FROM_JSON(float, double)
//FROM_JSON(double, double)
//FROM_JSON(bool, boolean)
//FROM_JSON(int, integer)

//template <>
//std::vector<int> from_json<std::vector<int>>(web::json::value item)
//{
//    return item.as_array();
//}

//template <>
//int from_json<int>(web::json::value item)
//{
//    if (!item.is_integer())
//    {
//        throw std::runtime_error("Could not convert json to type 'int'");
//    }
//    return item.as_integer();
//}

template <typename T>
T deserialize(web::json::value& json)
{
    return from_json<T>(json);
}

void send_message(signalr::hub_connection& connection, const utility::string_t& name, const utility::string_t& message)
{
    web::json::value args{};
    args[0] = web::json::value::string(name);
    args[1] = web::json::value(message);

    web::json::value tmp{};
    tryparse(tmp, name, message);

    // if you get an internal compiler error uncomment the lambda below or install VS Update 4
    connection.invoke(U("Invoke"), args/*, [](const web::json::value&){}*/)
        .then([](pplx::task<web::json::value> invoke_task)  // fire and forget but we need to observe exceptions
    {
        try
        {
            auto val = invoke_task.get();
            ucout << U("Received: ") << val.serialize() << std::endl;
        }
        catch (const std::exception &e)
        {
            ucout << U("Error while sending data: ") << e.what() << std::endl;
        }
    });
}

void chat(const utility::string_t& name)
{
    signalr::hub_connection connection(U("http://localhost:5000/default"), U(""), signalr::trace_level::all, std::make_shared<logger>());
    connection.on(U("Send"), [](const web::json::value& m)
    {
        ucout << std::endl << m.at(0).as_string() << /*U(" wrote:") << m.at(1).as_string() <<*/ std::endl << U("Enter your message: ");
    });

    connection.start()
        .then([&connection, name]()
        {
            ucout << U("Enter your message:");
            for (;;)
            {
                utility::string_t message;
                std::getline(ucin, message);

                if (message == U(":q"))
                {
                    break;
                }

                send_message(connection, name, message);
            }
        })
        .then([&connection]() // fine to capture by reference - we are blocking so it is guaranteed to be valid
        {
            return connection.stop();
        })
        .then([](pplx::task<void> stop_task)
        {
            try
            {
                stop_task.get();
                ucout << U("connection stopped successfully") << std::endl;
            }
            catch (const std::exception &e)
            {
                ucout << U("exception when starting or stopping connection: ") << e.what() << std::endl;
            }
        }).get();
}

class IProtocol
{
public:
    template <typename ...T>
    IProtocol(std::function<std::tuple<T...>(const std::string &)> parse)
    {
        auto f = parse;
    }

    template <typename ...T>
    std::tuple<T...> parse_message(const std::string& data)
    {
        return reinterpret_cast<Protocol*>(this)->parse_message<T...>(data);
    }
private:

};

class ProtocolTest
{
public:
    template <typename ...T>
    std::tuple<T...> parse_message(const std::string& data) const
    {
        auto parsed = web::json::value::parse(utility::conversions::to_string_t(data));
        if (!parsed.is_array())
        {
            throw std::exception("expected json array");
        }
        if (parsed.size() != sizeof...(T))
        {
            throw std::exception("incorrect number of arguments");
        }
        return parse_args<T...>(parsed);
    }
private:
    /*template <typename ...T>
    std::tuple<T...> parse_args(web::json::value& items)
    {
        static_assert(false, "too many args");
    }*/

    template <typename T, typename T2, typename T3>
    std::tuple<T, T2, T3> parse_args(const web::json::value& items) const
    {
        return std::make_tuple(from_json<T>(items.at(0)), from_json<T2>(items.at(1)), from_json<T3>(items.at(2)));
    }

    template <typename T, typename T2>
    std::tuple<T, T2> parse_args(const web::json::value& items) const
    {
        return std::make_tuple(from_json<T>(items.at(0)), from_json<T2>(items.at(1)));
    }

    template <typename T>
    std::tuple<T> parse_args(const web::json::value& items) const
    {
        return std::make_tuple(from_json<T>(items.at(0)));
    }
};

#include <map>
std::map<std::string, std::function<void(const std::string&)>> map;

template <typename T>
void invoke_with_args(const std::function<void(T)>& func, std::tuple<T> args)
{
    func(std::get<0>(args));
}

template <typename T, typename T2>
void invoke_with_args(const std::function<void(T, T2)>& func, std::tuple<T, T2> args)
{
    func(std::get<0>(args), std::get<1>(args));
}

template <typename T, typename T2, typename T3>
void invoke_with_args(const std::function<void(T, T2, T3)>& func, std::tuple<T, T2, T3> args)
{
    func(std::get<0>(args), std::get<1>(args), std::get<2>(args));
}

template <typename ...T>
void on(const std::string& name, const std::function<void(T...)>& handler)
{
    map[name] = [handler](const std::string& args)
    {
        ProtocolTest p = ProtocolTest();
        auto tuple = p.parse_message<T...>(args);

        invoke_with_args<T...>(handler, tuple);
    };
}

int main()
{
    ucout << U("Enter your name: ");
    utility::string_t name;
    //std::getline(ucin, name);

    web::json::value tmp{};
    //tryparse(tmp, name, U("test"));

    tmp[0] = web::json::value(10);
    tmp[1] = web::json::value(true);
    tmp[2] = web::json::value("t");

    on<int, bool>(std::string("methodName"), [](int i, bool b)
    {
        std::cout << i << " " << b << std::endl;
    });

    on<int, bool, std::string>(std::string("methodName"), [](int i, bool b, std::string s)
    {
        std::cout << i << " " << b << std::endl;
    });

    map[std::string("methodName")](utility::conversions::to_utf8string(tmp.serialize()));
    /*de<int>(tmp, [](int i)
    {
        std::cout << i << " " << std::endl;
    });*/

    //chat(name);

    return 0;
}

//template <typename T>
//void tryparse(web::json::value& json, T first)
//{
//    json[json.size()] = to_json(first);
//}
//
//template <typename arg, typename ...T>
//void tryparse(web::json::value& json, arg first, T... arguments)
//{
//    tryparse(json, first);
//    tryparse(json, arguments...);
//}
