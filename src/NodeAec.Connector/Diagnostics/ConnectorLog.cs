using System;
using System.IO;
using System.Text;
using NodeAec.Connector.Storage;

namespace NodeAec.Connector.Diagnostics;

/// <summary>
/// Sanitized local Connector log (<c>%APPDATA%\NodeAec\connector.log</c>).
/// Makes visible failures that used to be swallowed by empty <c>catch</c> blocks.
/// Golden rule (see security skill): calls must NEVER include JWT tokens,
/// license keys, emails, or hardware identifiers — only error type
/// and an already display-ready readable message. Past 512 KB the file rotates
/// to <c>connector.log.1</c> (history preserved, not truncated) and no write
/// failure may take the caller down.
/// </summary>
public static class ConnectorLog
{
    /// <summary>Log file name in the Connector base directory.</summary>
    public const string LogFileName = "connector.log";

    /// <summary>Maximum size (bytes) before rotation.</summary>
    private const long MaxBytes = 512 * 1024;

    private static readonly object Sync = new();

    // Cached writer for the active file: reopens only when the path changes (tests swap
    // the base via SetCustomBasePath) instead of opening/closing the file on every line.
    private static StreamWriter? _writer;
    private static string? _writerPath;

    /// <summary>Returns the absolute log file path.</summary>
    public static string GetLogFilePath() => Path.Combine(LeaseStorage.GetBaseDirectory(), LogFileName);

    /// <summary>
    /// Logs an <c>ISO8601 [LEVEL] message</c> line to the local log.
    /// </summary>
    /// <param name="level">Short level: <c>INFO</c>, <c>WARN</c>, or <c>ERROR</c>.</param>
    /// <param name="message">Already-sanitized message (no tokens, keys, or personal data).</param>
    public static void Write(string level, string message)
    {
        lock (Sync)
        {
            try
            {
                string path = GetLogFilePath();
                RotateIfNeeded(path);

                StreamWriter? writer = _writer;
                if (writer == null || !string.Equals(_writerPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    writer = OpenWriter(path);
                }

                writer.Write($"{DateTimeOffset.Now:yyyy-MM-dd'T'HH:mm:sszzz} [{level}] {message}{Environment.NewLine}");
                writer.Flush();
            }
            catch
            {
                // Diagnostics must never break the main flow: drops the writer so the
                // next write retries from scratch.
                CloseWriter();
            }
        }
    }

    /// <summary>
    /// Opens (or reopens) the writer for the given path. <c>FileShare.ReadWrite|Delete</c>
    /// lets other Revit instances append to the same file and the file
    /// be moved/deleted even with the handle open.
    /// </summary>
    private static StreamWriter OpenWriter(string path)
    {
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        CloseWriter();
        var writer = new StreamWriter(stream, Encoding.UTF8);
        _writer = writer;
        _writerPath = path;
        return writer;
    }

    /// <summary>Closes the current writer (best-effort) and clears the cache.</summary>
    private static void CloseWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch
        {
            // Releasing a handle must not throw outward.
        }

        _writer = null;
        _writerPath = null;
    }

    /// <summary>
    /// Rotates the log once it grows past the limit: the current file becomes
    /// <c>connector.log.1</c> (the previous backup is discarded) instead of being truncated.
    /// </summary>
    private static void RotateIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= MaxBytes)
            {
                return;
            }

            string backup = path + ".1";
            CloseWriter();
            File.Delete(backup);
            File.Move(path, backup);
        }
        catch
        {
            // Rotation failures are ignored; the next write retries.
        }
    }
}
