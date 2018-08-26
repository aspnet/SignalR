using System;
using System.Text;
using Microsoft.Extensions.Logging;
using UnityEngine;


public class Unity3DDebugLog : Microsoft.Extensions.Logging.ILogger, IDisposable
{
    private readonly string category;

    public Unity3DDebugLog(string categoryName)
    {
        category = categoryName;
    }

    public IDisposable BeginScope<TState>(TState state)
    {
        return this;
    }

    public void Dispose()
    {
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {   
        DateTime timestamp = DateTime.Now;

        string message =
            $"{timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{logLevel}] {category} :" +
            $" {formatter(state, exception)}\n{exception}\n";

        if (logLevel.HasFlag(LogLevel.Error) || logLevel.HasFlag(LogLevel.Critical))
            Debug.LogError(message);
        else
            Debug.Log(message);

    }
}