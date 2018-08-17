using Microsoft.Extensions.Logging;

public class Unity3DDebugLogProvider : ILoggerProvider
{
    public void Dispose()
    {
        
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new Unity3DDebugLog(categoryName);
    }
}