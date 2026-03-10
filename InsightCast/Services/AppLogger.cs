using System;

namespace InsightCast.Services
{
    public class AppLogger : IAppLogger
    {
        public event Action<string>? LogReceived;

        public void Log(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var line = $"[{timestamp}] {message}";
            LogReceived?.Invoke(line);
        }

        public void LogError(string message, Exception? ex = null)
        {
            if (ex != null)
            {
                Log($"{message}: {ex.Message}");
                System.Diagnostics.Trace.TraceError($"{message}: {ex}");
            }
            else
            {
                Log(message);
            }
        }
    }
}
