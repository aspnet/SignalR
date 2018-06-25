#include <string>
#include <iostream>
#include <functional>
#include <future>

namespace signalR
{
    struct HttpConnectionOptions
    {
        // placeholders
        std::string Cookies;
        std::string Headers;
        std::string Certs;
    };

    enum LogLevel
    {
        Trace,
        Information,
        Warning,
        Error
    };

    template <typename Protocol>
    class hub_connection_builder_impl;

    class hub_connection_builder
    {
    public:
        hub_connection_builder();

        hub_connection_builder& configure_logging(LogLevel);

        hub_connection_builder& with_url(const std::string& url, std::function<void(HttpConnectionOptions&)> configure = nullptr);

        template <typename Protocol>
        hub_connection_builder_impl<Protocol> use_protocol(Protocol);

        template <typename Protocol>
        hub_connection<Protocol> build(Protocol protocol);

        // default protocol if none specified
        hub_connection<JsonHubProtocol> build();
    };

    template <typename Protocol>
    class hub_connection_builder_impl : public hub_connection_builder
    {
    public:
        hub_connection_builder_impl(hub_connection_builder& internalBuilder, Protocol protocol)
            : mBuilder(internalBuilder), mProtocol(protocol)
        {
        }

        hub_connection_builder_impl<Protocol>& configure_logging()
        {
            return *this;
        }

        hub_connection_builder_impl<Protocol>& with_url(const std::string& url, std::function<void(HttpConnectionOptions&)> configure = nullptr)
        {
            mBuilder.with_url(url, configure);
            return *this;
        }

        hub_connection<Protocol> build()
        {
            static_assert(has_parse_message<Protocol>::value, "parse_message function expected from protocol");
            return mBuilder.build<Protocol>(mProtocol);
        }
    private:
        hub_connection_builder & mBuilder;
        Protocol mProtocol;
    };

    template <typename Protocol>
    class hub_connection
    {
    public:
        explicit hub_connection(Protocol hubProtocol, const std::string url, const std::string& query_string = "");

        ~hub_connection();

        hub_connection(hub_connection &&rhs);

        hub_connection(const hub_connection&) = delete;

        hub_connection& operator=(const hub_connection&) = delete;

        std::future<void> start();

        std::future<void> stop();

        void on_closed(const std::function<void()>& closed_callback);

        template <typename ...T>
        void on(const std::string& name, const std::function<void(T...)>& methodHandler);

        template <typename R, typename ...T>
        std::future<R> invoke(const std::string& method_name, T... args);

        template <typename ...T>
        std::future<void> send(const std::string& method_name, T... args);
    };

    class JsonHubProtocol
    {
    public:
        template <typename ...T>
        std::tuple<T...> parse_message(const std::string& data) const;

        template <typename ...T>
        std::string write_message(T... args);
    };
}

int main(void)
{
    auto connection = signalR::hub_connection_builder()
        .use_protocol(signalR::JsonHubProtocol())
        .with_url("http://example.com", [](signalR::HttpConnectionOptions&) { /* … */ })
        .build();

    connection.on<int>("test", [](int i)
    {
        std::cout << i;
    });

    connection.on<int, std::string>("test2", [](int i, std::string s)
    {
        std::cout << i << " " << s;
    });

    connection.start().get();
    auto integer = connection.invoke<int>("echo", 10, "hello").get();
    connection.stop().get();
}