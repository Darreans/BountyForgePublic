using BepInEx.Logging;

namespace BountyForge.Utils
{
    public static class LoggingHelper
    {
        private static ManualLogSource _logger;

        public static void Initialize(ManualLogSource logger)
        {
            _logger = logger;
        }

        public static void Debug(string message) => _logger?.LogDebug(message);
        public static void Info(string message) => _logger?.LogInfo(message);
        public static void Warning(string message) => _logger?.LogWarning(message);
        public static void Error(string message) => _logger?.LogError(message);
        public static void Error(string message, System.Exception ex) => _logger?.LogError($"{message}\n{ex}");
        public static void Fatal(string message) => _logger?.LogFatal(message);
    }
}