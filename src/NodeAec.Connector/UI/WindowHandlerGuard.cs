using System;
using System.Threading.Tasks;
using NodeAec.Connector.Diagnostics;

namespace NodeAec.Connector.UI;

/// <summary>
/// Single entry point for window handlers (click delegates and
/// <c>async void</c> methods). Any exception escaping a handler is swallowed and logged
/// here: in WPF, an <c>async void</c> exception is re-raised on the dispatcher as a
/// <c>DispatcherUnhandledException</c> — and inside Revit that ends the host
/// process, not just the add-in. UI handlers must be top-level noexcept.
/// </summary>
internal static class WindowHandlerGuard
{
    /// <summary>
    /// Runs an async window handler with top-level exception capture.
    /// </summary>
    /// <param name="action">Handler to run.</param>
    /// <param name="onError">Callback (on the dispatcher) to safely report the failure to the UI; never propagates.</param>
    public static async Task RunAsync(Func<Task> action, Action<Exception> onError)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Report(ex, onError);
        }
    }

    /// <summary>
    /// Runs a sync window handler with top-level exception capture.
    /// </summary>
    /// <param name="action">Handler to run.</param>
    /// <param name="onError">Callback (on the dispatcher) to safely report the failure to the UI; never propagates.</param>
    public static void Run(Action action, Action<Exception> onError)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Report(ex, onError);
        }
    }

    /// <summary>
    /// Logs the failure (fixed, sanitized text — never exception content in a token/PII
    /// log) and reports it to the UI; the report itself is guarded so an already
    /// disposed window never rethrows the exception.
    /// </summary>
    private static void Report(Exception ex, Action<Exception> onError)
    {
        ConnectorLog.Write("ERROR", $"Erro não tratado em handler de janela: {ex.GetType().Name}.");

        try
        {
            onError(ex);
        }
        catch
        {
            // The UI may be disposed (window closed); never rethrow from here.
        }
    }
}
