using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace NodeAec.Connector.Hardware;

/// <summary>
/// Provedor canônico do identificador de hardware (Machine ID): SHA-256 do
/// <c>MachineGuid</c> do Windows, em hex minúsculo de 64 caracteres.
/// O nome da máquina (<c>Environment.MachineName</c>) NÃO participa do hash — ele é
/// renomeável pelo usuário e um Machine ID dependente de hostname invalidaria a licença
/// (e queimaria um assento) a cada rename. Sem <c>MachineGuid</c> legível o provedor
/// falha fechado: nunca degrada para um identificador mais fraco.
/// </summary>
public static class HardwareId
{
    private static string? _cachedMachineId;

    /// <summary>
    /// Leitor do <c>MachineGuid</c> do registro do Windows. Substituível apenas pelos
    /// testes (mesma assembly): nulo/vazio faz o provedor falhar fechado. A suíte de testes
    /// roda com paralelismo desabilitado, então trocar o leitor global é seguro.
    /// </summary>
    internal static Func<string?> MachineGuidReader { get; set; } = ReadMachineGuidFromRegistry;

    /// <summary>
    /// Tenta obter o identificador desta máquina (SHA-256 em hex minúsculo, 64 caracteres).
    /// </summary>
    /// <param name="machineId">Identificador canônico quando o retorno é <c>true</c>.</param>
    /// <param name="reason">Motivo legível da indisponibilidade quando o retorno é <c>false</c>.</param>
    /// <returns><c>true</c> quando o MachineGuid foi lido e o hash derivado.</returns>
    public static bool TryGetMachineId(out string machineId, out string? reason)
    {
        // Checagem explícita de nulo/empty (em vez de string.IsNullOrEmpty) porque as
        // referências do net48 não trazem [NotNullWhen]: sem ela o compilador só enxerga
        // CS8601 nesse ano.
        string? cached = _cachedMachineId;
        if (cached != null && cached.Length > 0)
        {
            machineId = cached;
            reason = null;
            return true;
        }

        string? guid = MachineGuidReader();
        if (string.IsNullOrWhiteSpace(guid))
        {
            machineId = string.Empty;
            reason = "MachineGuid do Windows indisponível (registro ilegível ou sistema não-Windows)";
            return false;
        }

        _cachedMachineId = ComputeMachineId(guid);
        machineId = _cachedMachineId;
        reason = null;
        return true;
    }

    /// <summary>
    /// Deriva o Machine ID canônico de um <c>MachineGuid</c>: SHA-256 do GUID (aparado) em
    /// hex minúsculo. O formato de 64 caracteres não pode mudar — é o que os leases
    /// gravados referenciam no claim <c>mid</c>.
    /// </summary>
    /// <param name="machineGuid">Valor de <c>HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid</c>.</param>
    /// <returns>Hash SHA-256 em 64 caracteres hexadecimais minúsculos.</returns>
    internal static string ComputeMachineId(string machineGuid)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(machineGuid.Trim()));

        // BitConverter em vez de Convert.ToHexString (que só existe em .NET 5+): ambos
        // produzem o mesmo hexadecimal maiúsculo; os traços são removidos e por fim o
        // valor é convertido para minúsculas.
        return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
    }

    /// <summary>Limpa o cache do identificador; usado pelos testes que trocam o leitor.</summary>
    internal static void ResetCacheForTests() => _cachedMachineId = null;

    private static string? ReadMachineGuidFromRegistry()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                       .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid")?.ToString();
        }
        catch
        {
            // Silencioso: a ausência do GUID vira falha fechada reportada pelo chamador.
            return null;
        }
    }
}
