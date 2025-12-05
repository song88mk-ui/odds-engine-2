using Microsoft.Extensions.Logging;
using System;
using System.Runtime.CompilerServices;

namespace OE.Shared.Extensions
{

    public static class LoggerExtensions
    {
        public static void LogInformationEx<T>(
            this ILogger<T> logger,
            string message,
            object? data = null,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            var logMessage = BuildMessage(typeof(T), memberName, message, data, sourceFilePath, sourceLineNumber);
            logger.LogInformation(logMessage);
        }

        public static void LogWarningEx<T>(
            this ILogger<T> logger,
            string message,
            object? data = null,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            var logMessage = BuildMessage(typeof(T), memberName, message, data, sourceFilePath, sourceLineNumber);
            logger.LogWarning(logMessage);
        }

        public static void LogErrorEx<T>(
            this ILogger<T> logger,
            string message,
            Exception? ex = null,
            object? data = null,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            var logMessage = BuildMessage(typeof(T), memberName, message, data, sourceFilePath, sourceLineNumber);
            if (ex != null)
            {
                logger.LogError(ex, logMessage);
            }
            else
            {
                logger.LogError(logMessage);
            }
        }

        private static string BuildMessage(
            Type type,
            string memberName,
            string message,
            object? data,
            string sourceFilePath,
            int sourceLineNumber)
        {
            string className = type.Name;
            string projectName = type.Assembly.GetName().Name ?? "UnknownProject";
            string fileName = System.IO.Path.GetFileName(sourceFilePath);
            string dataPart = data != null ? $" | Data: {System.Text.Json.JsonSerializer.Serialize(data)}" : string.Empty;

            return $"[{projectName}.{className}.{memberName} ({fileName}:{sourceLineNumber})] {message}{dataPart}";
        }
    }
}