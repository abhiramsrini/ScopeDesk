using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using System;
using System.IO;

namespace ScopeDesk.Logging
{
    public static class SerilogConfig
    {
        public static void Configure(LoggerConfiguration loggerConfiguration, IConfiguration configuration)
        {
            var fileSection = configuration.GetSection("Logging:File");
            var logPath = Environment.ExpandEnvironmentVariables(fileSection["Path"] ?? "%LocalAppData%/ScopeDesk/logs/scope.log");
            var directory = Path.GetDirectoryName(logPath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var levelText = configuration.GetValue<string>("Logging:Level") ?? "Information";
            var level = Enum.TryParse(levelText, ignoreCase: true, out LogEventLevel parsedLevel)
                ? parsedLevel
                : LogEventLevel.Information;

            var sizeLimit = fileSection.GetValue<long?>("FileSizeLimitBytes") ?? 5_242_880; // default ~5MB
            var retainCount = fileSection.GetValue<int?>("RetainedFileCountLimit") ?? 10;
            var rollOnLimit = fileSection.GetValue<bool?>("RollOnFileSizeLimit") ?? true;

            loggerConfiguration
                .MinimumLevel.Is(level)
                .Enrich.FromLogContext()
                .WriteTo.File(
                    logPath,
                    rollingInterval: RollingInterval.Infinite,
                    fileSizeLimitBytes: sizeLimit,
                    rollOnFileSizeLimit: rollOnLimit,
                    retainedFileCountLimit: retainCount);
        }
    }
}
