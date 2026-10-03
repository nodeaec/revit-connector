using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NodeAec.Connector.Config;

namespace NodeAec.Connector.Auth;

/// <summary>
/// Desktop authentication service via browser SSO with a local loopback server (RFC 8252).
/// Opens the system default browser, captures the token via a secure redirect with CSRF state,
/// and shuts the listener down.
/// </summary>
public class DesktopAuthService
{
    private readonly string _webAuthBaseUrl;

    public DesktopAuthService(string? webAuthBaseUrl = null)
    {
        _webAuthBaseUrl = webAuthBaseUrl ?? ConnectorConfig.WebAuthUrl;
    }

    /// <summary>
    /// Generates a secure 32-byte CSRF token encoded as Base64Url.
    /// </summary>
    public static string GenerateSecureState()
    {
        byte[] bytes = new byte[32];

        // Instantiated RNG (instead of RandomNumberGenerator.Fill, which only exists on .NET 6+)
        // so it also works on .NET Framework 4.8 — Revit 2023/2024.
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);

        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    /// <summary>
    /// Locates a free ephemeral TCP port on the loopback address (127.0.0.1).
    /// </summary>
    public static int GetAvailableLoopbackPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    /// <summary>
    /// Builds the full URL to start the authorization flow in the browser.
    /// </summary>
    public string BuildAuthUrl(int port, string state)
    {
        string sep = _webAuthBaseUrl.Contains('?') ? "&" : "?";
        return $"{_webAuthBaseUrl}{sep}port={port}&state={Uri.EscapeDataString(state)}";
    }

    /// <summary>
    /// Indicates whether the path received on loopback is the accepted authentication callback.
    /// The web portal redirects to <c>/callback</c> (no trailing slash); the listener also
    /// accepts <c>/callback/</c> and ignores any other path (favicon, probes, etc.).
    /// </summary>
    /// <param name="path">Absolute path of the incoming request.</param>
    public static bool IsCallbackPath(string? path)
    {
        return string.Equals(path, "/callback", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/callback/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compares two strings in constant time. Naively comparing <c>state</c>
    /// leaks the correct prefix via timing — not practically exploitable here, but the
    /// comparator is cheap. <c>CryptographicOperations.FixedTimeEquals</c> does not exist on
    /// net48 (Revit 2023/2024), so the comparison is manual with no early exit.
    /// </summary>
    /// <param name="left">First string (may be null).</param>
    /// <param name="right">Second string (may be null).</param>
    /// <returns><c>true</c> when the strings are byte-for-byte identical (UTF-8).</returns>
    internal static bool FixedTimeEquals(string? left, string? right)
    {
        if (left is null || right is null) return left is null && right is null;

        byte[] a = Encoding.UTF8.GetBytes(left);
        byte[] b = Encoding.UTF8.GetBytes(right);
        int diff = a.Length ^ b.Length;
        int min = Math.Min(a.Length, b.Length);
        for (int i = 0; i < min; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }

    /// <summary>
    /// Probes a loopback port, binds the <see cref="HttpListener"/> and starts it,
    /// retrying on another port on a race (L2): the port is released between probing
    /// and binding, so another process may claim it in that window. When every attempt
    /// fails, throws <see cref="InvalidOperationException"/> with a friendly message —
    /// never a raw networking exception.
    /// </summary>
    /// <param name="port">Port actually bound (for building the redirect URL).</param>
    /// <returns>Started listener, ready for <c>GetContextAsync</c>.</returns>
    private static HttpListener StartLoopbackListener(out int port)
    {
        const int maxAttempts = 3;
        Exception? lastFailure = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            port = GetAvailableLoopbackPort();
            var listener = new HttpListener();
            // Root prefix to accept exactly the path the portal emits (`/callback`),
            // which has no trailing slash — a trailing-slash prefix would not match it.
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");

            try
            {
                listener.Start();
                return listener;
            }
            catch (Exception ex) when (ex is HttpListenerException || ex is InvalidOperationException)
            {
                // Bind lost in the race (or port refused) → close and try another port.
                lastFailure = ex;
                try { listener.Close(); } catch { }
            }
        }

        throw new InvalidOperationException(
            "Não foi possível abrir a porta local de login (a porta foi ocupada ou o acesso à rede local está restrito). Feche outras janelas de login e tente novamente.",
            lastFailure);
    }

    /// <summary>
    /// Starts the local loopback listener and opens the default browser so the user can sign in.
    /// Waits for the response for up to 120 seconds.
    /// </summary>
    public async Task<string> LoginViaBrowserAsync(CancellationToken cancellationToken = default)
    {
        using var listener = StartLoopbackListener(out int port);
        string state = GenerateSecureState();
        string authUrl = BuildAuthUrl(port, state);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            listener.Stop();
            throw new InvalidOperationException($"Não foi possível abrir o navegador padrão: {ex.Message}", ex);
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            using (linkedCts.Token.Register(() =>
            {
                try { listener.Abort(); } catch { }
            }))
            {
                while (true)
                {
                    var context = await listener.GetContextAsync().ConfigureAwait(false);
                    var request = context.Request;
                    var response = context.Response;

                    // Ignores requests that are not the callback (favicon, health probes...).
                    if (!IsCallbackPath(request.Url?.AbsolutePath))
                    {
                        response.StatusCode = 404;
                        response.Close();
                        continue;
                    }

                    string? receivedState = request.QueryString["state"];
                    string? userToken = request.QueryString["token"];

                    if (string.IsNullOrEmpty(receivedState) || !FixedTimeEquals(receivedState, state) || string.IsNullOrEmpty(userToken))
                    {
                        byte[] errorBytes = Encoding.UTF8.GetBytes("Falha na autenticação: Estado inválido ou token ausente.");
                        response.StatusCode = 400;
                        response.ContentType = "text/plain; charset=utf-8";
                        await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length, cancellationToken).ConfigureAwait(false);
                        response.Close();
                        // L1: answers 400 and KEEPS waiting. The loopback port is
                        // discoverable via `netstat` — any local process may fire a
                        // `/callback` with the wrong `state`, and that must not end the
                        // legitimate user's wait within the 120 s.
                        continue;
                    }

                    string successHtml = @"<!DOCTYPE html>
<html lang=""pt-BR"">
<head>
  <meta charset=""utf-8"">
  <title>Node.aec — Autenticado</title>
  <style>
    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #0f172a; color: #f8fafc; text-align: center; padding: 60px 20px; }
    .card { max-width: 480px; margin: 0 auto; background: #1e293b; padding: 40px; border-radius: 12px; border: 1px solid #334155; }
    h2 { color: #38bdf8; margin-top: 0; }
    p { color: #94a3b8; font-size: 15px; line-height: 1.5; }
    .check { font-size: 48px; color: #4ade80; margin-bottom: 12px; }
  </style>
</head>
<body>
  <div class=""card"">
    <div class=""check"">&#10003;</div>
    <h2>Login Concluído com Sucesso!</h2>
    <p>Sua conta foi conectada ao Autodesk Revit.<br>Você já pode fechar esta aba do navegador e voltar ao Revit.</p>
  </div>
</body>
</html>";
                    byte[] buffer = Encoding.UTF8.GetBytes(successHtml);
                    response.ContentType = "text/html; charset=utf-8";
                    response.StatusCode = 200;
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    response.Close();

                    return userToken;
                }
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Login cancelado.", cancellationToken);
        }
        catch (Exception) when (timeoutCts.IsCancellationRequested)
        {
            throw new InvalidOperationException("Tempo esgotado aguardando a confirmação do login no navegador. Verifique se a aba foi concluída e tente novamente.");
        }
        finally
        {
            if (listener.IsListening)
            {
                try { listener.Stop(); } catch { }
            }
        }
    }
}
