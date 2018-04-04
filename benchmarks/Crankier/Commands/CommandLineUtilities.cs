using System;
using Microsoft.Extensions.CommandLineUtils;

namespace Microsoft.AspNetCore.SignalR.Crankier.Commands
{
    internal static class CommandLineUtilities
    {
        public static int Fail(string message)
        {
            Error(message);
            return 1;
        }

        public static void Error(string message)
        {
            Console.WriteLine($"error: {message}");
        }

        public static int MissingRequiredArg(CommandOption option)
        {
            return Fail($"Missing required argument: {option.LongName}");
        }

        public static int InvalidArg(CommandOption option)
        {
            return Fail($"Invalid value '{option.Value()}' for argument: {option.LongName}");
        }
    }
}
