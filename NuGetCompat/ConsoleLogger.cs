using System;
using System.Threading.Tasks;
using NuGet.Common;

namespace NuGetCompat
{
    public class ConsoleLogger : LoggerBase
    {
        public override void Log(ILogMessage message)
        {
            Console.WriteLine($"[{message.Level.ToString().Substring(0, 4).ToUpperInvariant(),4}] {message.Message}");
        }

        public override Task LogAsync(ILogMessage message)
        {
            Log(message);
            return Task.CompletedTask;
        }
    }
}
