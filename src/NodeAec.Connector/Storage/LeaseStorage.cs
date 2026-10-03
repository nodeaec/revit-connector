using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NodeAec.Connector.Models;

namespace NodeAec.Connector.Storage;

/// <summary>
/// Manages local persistence of the Master Entitlements Lease (%APPDATA%\NodeAec\entitlements.lease)
/// encrypted via Windows DPAPI (DataProtectionScope.CurrentUser).
/// </summary>
public static class LeaseStorage
{
    private static string? _customBasePath;

    /// <summary>
    /// Allows injecting an alternate base directory (useful for isolated unit tests).
    /// </summary>
    public static void SetCustomBasePath(string? path)
    {
        _customBasePath = path;
    }

    public static string GetBaseDirectory()
    {
        if (!string.IsNullOrEmpty(_customBasePath))
        {
            if (!Directory.Exists(_customBasePath)) Directory.CreateDirectory(_customBasePath);
            return _customBasePath;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "NodeAec");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    public static string GetLeaseFilePath() => Path.Combine(GetBaseDirectory(), "entitlements.lease");
    public static string GetSessionFilePath() => Path.Combine(GetBaseDirectory(), "session.json");

    /// <summary>
    /// Saves the master lease JWT encrypted with DPAPI.
    /// On Windows, a DPAPI failure does <b>not</b> degrade to plaintext: nothing is written and the
    /// method returns <c>false</c> (fail-closed). On non-Windows systems (development/testing only —
    /// Revit is Windows-only) it writes plaintext.
    /// Writes are atomic (temp file + swap) so a half-written lease is never left behind.
    /// </summary>
    /// <param name="jwtToken">Master lease JWT token.</param>
    /// <returns><c>true</c> when the lease was persisted safely.</returns>
    public static bool SaveMasterLease(string jwtToken)
    {
        if (string.IsNullOrWhiteSpace(jwtToken)) return false;

        byte[] rawBytes = Encoding.UTF8.GetBytes(jwtToken);
        if (!TryProtect(rawBytes, out byte[] bytesToWrite, out string? failure))
        {
            Diagnostics.ConnectorLog.Write("ERROR", $"Não foi possível proteger o lease local: {failure}.");
            return false;
        }

        try
        {
            WriteAllBytesAtomic(GetLeaseFilePath(), bytesToWrite);
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.ConnectorLog.Write("ERROR", $"Falha ao gravar o lease local: {ex.GetType().Name}.");
            return false;
        }
    }

    /// <summary>
    /// Reads and decrypts the local master lease JWT.
    /// On Windows, a file that does not decrypt is treated as corrupt/foreign and
    /// discarded (returns <c>null</c>) instead of being accepted as plaintext.
    /// </summary>
    public static string? LoadMasterLease()
    {
        var path = GetLeaseFilePath();
        if (!File.Exists(path)) return null;

        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) return null;

            if (!TryUnprotect(bytes, out byte[] plain, out string? failure))
            {
                Diagnostics.ConnectorLog.Write("WARN", $"Lease local ilegível descartado: {failure}.");
                return null;
            }

            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Removes the local lease (deactivation / logout).
    /// </summary>
    public static void ClearMasterLease()
    {
        var path = GetLeaseFilePath();
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
    }

    /// <summary>
    /// Saves user session data (name, email, token) encrypted with DPAPI,
    /// with no plaintext fallback on Windows. Atomic write.
    /// </summary>
    /// <param name="userEmail">Account email (display).</param>
    /// <param name="userToken">User session JWT token.</param>
    /// <param name="userName">User display name, when available.</param>
    /// <returns><c>true</c> when the session was persisted safely.</returns>
    public static bool SaveSession(string? userEmail, string? userToken, string? userName = null)
    {
        var path = GetSessionFilePath();
        var json = JsonSerializer.Serialize(new
        {
            name = userName ?? string.Empty,
            email = userEmail ?? string.Empty,
            token = userToken ?? string.Empty
        });
        byte[] rawBytes = Encoding.UTF8.GetBytes(json);

        if (!TryProtect(rawBytes, out byte[] bytesToWrite, out string? failure))
        {
            Diagnostics.ConnectorLog.Write("ERROR", $"Não foi possível proteger a sessão local: {failure}.");
            return false;
        }

        try
        {
            WriteAllBytesAtomic(path, bytesToWrite);
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.ConnectorLog.Write("ERROR", $"Falha ao gravar a sessão local: {ex.GetType().Name}.");
            return false;
        }
    }

    /// <summary>
    /// Reads the saved user session (name, email, token). On Windows, content that fails
    /// to decrypt is discarded (returns <c>null</c> — the user simply signs in again).
    /// Older sessions stored the public id in the "email" field; when the saved user token
    /// carries the real claims, they take priority and silently correct the stored value.
    /// </summary>
    public static (string? Name, string? Email, string? Token)? LoadSession()
    {
        var path = GetSessionFilePath();
        if (!File.Exists(path)) return null;

        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) return null;

            if (!TryUnprotect(bytes, out byte[] plain, out string? failure))
            {
                Diagnostics.ConnectorLog.Write("WARN", $"Sessão local ilegível descartada: {failure}.");
                return null;
            }

            string json = Encoding.UTF8.GetString(plain);
            using var doc = JsonDocument.Parse(json);
            string? name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
            string? email = doc.RootElement.TryGetProperty("email", out var e) ? e.GetString() : null;
            string? token = doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;

            var claims = ParseUserSessionClaims(token);
            if (claims != null)
            {
                if (!string.IsNullOrWhiteSpace(claims.Email)) email = claims.Email;
                if (!string.IsNullOrWhiteSpace(claims.Name)) name = claims.Name;
            }

            return (name, email, token);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Clears the user session.
    /// </summary>
    public static void ClearSession()
    {
        var path = GetSessionFilePath();
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
    }

    /// <summary>
    /// Wipes the whole account state — master lease + session. It is the logout primitive of
    /// <c>ConnectorWindow</c>: signing out must never leave the product lease
    /// behind, or the gate would keep validating with the session closed (M7 — covered by tests).
    /// </summary>
    public static void ClearAll()
    {
        ClearMasterLease();
        ClearSession();
    }

    /// <summary>
    /// Decodes a JWT payload <b>without</b> verifying the cryptographic signature.
    /// Restricted to display use (greeting, dates) — licensing decisions go
    /// mandatorily through <c>Gate.NodeAecGate</c>, which verifies the Ed25519 signature.
    /// </summary>
    public static MasterLeasePayload? ParseJwtPayload(string token)
    {
        var json = DecodeJwtPayloadJson(token);
        if (json == null) return null;

        try
        {
            return JsonSerializer.Deserialize<MasterLeasePayload>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Decodes the identity claims (id, email, name) of the user session token,
    /// without verifying the signature (display material coming from the loopback itself).
    /// Returns <c>null</c> for missing/malformed tokens or ones not carrying those
    /// claims (e.g. the master lease, which only has the technical id in <c>sub</c>).
    /// </summary>
    public static UserSessionClaims? ParseUserSessionClaims(string? token)
    {
        var json = DecodeJwtPayloadJson(token);
        if (json == null) return null;

        try
        {
            var claims = JsonSerializer.Deserialize<UserSessionClaims>(json);
            if (claims == null) return null;
            return string.IsNullOrWhiteSpace(claims.Email) && string.IsNullOrWhiteSpace(claims.Name)
                ? null
                : claims;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the payload (middle base64url segment) of a JWT as JSON,
    /// or <c>null</c> when the token is not a usable JWT.
    /// </summary>
    private static string? DecodeJwtPayloadJson(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var parts = token.Split('.');
        if (parts.Length < 2) return null;

        var base64 = parts[1].Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
            case 1: return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Protects bytes with DPAPI (CurrentUser) on Windows. Off Windows it just
    /// passes the plaintext through (development/test environment; Revit is Windows-only).
    /// </summary>
    /// <param name="plain">Original bytes.</param>
    /// <param name="protectedBytes">Bytes to write when the return is <c>true</c>.</param>
    /// <param name="reason">Human-readable failure reason when the return is <c>false</c>.</param>
    /// <returns><c>false</c> only when DPAPI failed on Windows (never writes plaintext there).</returns>
    private static bool TryProtect(byte[] plain, out byte[] protectedBytes, out string? reason)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            protectedBytes = plain;
            reason = null;
            return true;
        }

        try
        {
            protectedBytes = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            reason = null;
            return true;
        }
        catch (Exception ex)
        {
            protectedBytes = Array.Empty<byte>();
            reason = $"DPAPI indisponível ({ex.GetType().Name})";
            return false;
        }
    }

    /// <summary>
    /// Unprotects written bytes. On Windows, content that fails to decrypt is rejected
    /// (<c>false</c>) — never interpreted as plaintext. Off Windows, plaintext.
    /// </summary>
    /// <param name="stored">Bytes read from disk.</param>
    /// <param name="plain">Original bytes when the return is <c>true</c>.</param>
    /// <param name="reason">Human-readable failure reason when the return is <c>false</c>.</param>
    private static bool TryUnprotect(byte[] stored, out byte[] plain, out string? reason)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            plain = stored;
            reason = null;
            return true;
        }

        try
        {
            plain = ProtectedData.Unprotect(stored, null, DataProtectionScope.CurrentUser);
            reason = null;
            return true;
        }
        catch (Exception ex)
        {
            plain = Array.Empty<byte>();
            reason = $"conteúdo não DPAPI ou de outro usuário ({ex.GetType().Name})";
            return false;
        }
    }

    /// <summary>
    /// Per-process lock on atomic writes. Concurrent writers (background heartbeat,
    /// UI-triggered sync and login) hit the same files: without the
    /// lock, a <c>promote</c> in the middle of another thread's write leaves the file
    /// truncated — DPAPI fails reading and every plugin stays blocked until the next
    /// successful sync.
    /// </summary>
    private static readonly object WriteLock = new();

    /// <summary>
    /// Writes bytes atomically: writes to a uniquely named temp file
    /// (<c>{path}.{guid}.tmp</c>) in the same directory and promotes the file to the destination
    /// (<c>File.Replace</c> when it already exists, which is an atomic rename on Windows;
    /// <c>File.Move</c> on first write), always under <see cref="WriteLock"/>.
    /// On systems without <c>File.Replace</c> (FAT32/exFAT/some shares) promotion falls back to
    /// delete+move, otherwise every write would fail and activation would be impossible.
    /// Prevents a power/process crash from leaving a half-written lease/session on disk
    /// and concurrent writers from corrupting each other's file. Uses only APIs
    /// present on both .NET Framework 4.8 (Revit 2023/2024) and .NET 8/10.
    /// </summary>
    /// <param name="path">Destination file.</param>
    /// <param name="bytes">Content to write.</param>
    public static void WriteAllBytesAtomic(string path, byte[] bytes)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        // Unique name per write: concurrent writers never share the same temp.
        string tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            lock (WriteLock)
            {
                File.WriteAllBytes(tmp, bytes);

                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tmp, path, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        // L4: `File.Replace` does not exist on FAT32/exFAT and some network
                        // shares. Fallback: delete + move under WriteLock — those filesystems
                        // have no atomic rename anyway. The finally
                        // still cleans the temp if the move fails.
                        File.Delete(path);
                        File.Move(tmp, path);
                    }
                }
                else
                {
                    File.Move(tmp, path);
                }
            }
        }
        finally
        {
            // A successful promote renames the temp (it stops existing); any failure
            // cleans the residue here. Best-effort: never mask the original exception.
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch
            {
            }
        }
    }
}
