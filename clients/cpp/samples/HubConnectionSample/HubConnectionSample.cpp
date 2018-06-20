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
T from_json(web::json::value item)
{
    static_assert(false, "No conversion to json could be found for the given type, see below output for more info.");
}

template <>
signed int from_json<signed int>(web::json::value item)
{
    if (!item.is_integer())
    {
        throw std::runtime_error("Could not convert json to type 'int'");
    }
    return item.as_integer();
}

template <>
bool from_json<bool>(web::json::value item)
{
    if (!item.is_boolean())
    {
        throw std::runtime_error("Could not convert json to type 'bool'");
    }
    return item.as_bool();
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

#include <map>
std::map<std::string, std::function<void(web::json::value)>> map;

template <typename ...T>
void de(web::json::value items, std::function<void(T...)> call)
{
    static_assert(false, "too many args");
}

template <typename T, typename T2>
void de(web::json::value items, std::function<void(T, T2)> call)
{
    call(from_json<T>(items[0]), from_json<T2>(items[1]));
}

template <typename T>
void de(web::json::value items, std::function<void(T)> call)
{
    call(from_json<T>(items[0]));
}

template <typename ...T>
void on(const std::string& name, const std::function<void(T...)>& rest)
{
    map[name] = [rest](web::json::value args)
    {
        de(args, rest);
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

    on<int, bool>(std::string("methodName"), [](int i, bool b)
    {
        std::cout << i << " " << b << std::endl;
    });

    map[std::string("methodName")](tmp);
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
