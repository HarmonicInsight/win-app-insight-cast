using System;
using System.Diagnostics;
using System.IO;

namespace InsightCast.Services;

/// <summary>
/// Writes crash reports to local disk for post-mortem analysis.
/// Reports are stored in %LOCALAPPDATA%\InsightCast\crash-reports\.
/// </summary>
public static class CrashReporter
{
    private static readonly string CrashDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "InsightCast", "crash-reports");

    /// <summary>
    /// Writes an exception to a timestamped crash report file.
    /// </summary>
    public static void WriteCrashReport(Exception ex, string context = "Unhandled")
    {
        try
        {
            Directory.CreateDirectory(CrashDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filePath = Path.Combine(CrashDir, $"crash_{timestamp}.txt");

            var version = typeof(CrashReporter).Assembly.GetName().Version;
            var versionStr = version != null
                ? $"{version.Major}.{version.Minor}.{version.Build}"
                : "unknown";

            var report = $"""
                InsightCast Crash Report
                ========================
                Timestamp : {DateTime.Now:yyyy-MM-dd HH:mm:ss}
                Version   : {versionStr}
                OS        : {Environment.OSVersion}
                CLR       : {Environment.Version}
                Context   : {context}

                Exception
                ---------
                Type    : {ex.GetType().FullName}
                Message : {ex.Message}

                Stack Trace
                -----------
                {ex.StackTrace}
                """;

            // Include inner exception chain
            var inner = ex.InnerException;
            var depth = 0;
            while (inner != null && depth < 5)
            {
                depth++;
                report += $"""


                    Inner Exception ({depth})
                    -------------------
                    Type    : {inner.GetType().FullName}
                    Message : {inner.Message}
                    Stack   : {inner.StackTrace}
                    """;
                inner = inner.InnerException;
            }

            File.WriteAllText(filePath, report);

            // Keep only last 20 crash reports
            TrimOldReports();
        }
        catch (Exception writeEx)
        {
            Trace.TraceError($"CrashReporter.WriteCrashReport failed: {writeEx.Message}");
        }
    }

    private static void TrimOldReports()
    {
        try
        {
            var files = Directory.GetFiles(CrashDir, "crash_*.txt");
            if (files.Length > 20)
            {
                Array.Sort(files);
                for (int i = 0; i < files.Length - 20; i++)
                {
                    File.Delete(files[i]);
                }
            }
        }
        catch { /* best-effort */ }
    }
}
