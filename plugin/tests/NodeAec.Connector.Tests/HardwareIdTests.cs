using System;
using System.Security.Cryptography;
using System.Text;
using NodeAec.Connector.Hardware;
using Xunit;

namespace NodeAec.Connector.Tests;

public class HardwareIdTests
{
    [Fact]
    public void ComputeMachineId_MatchesSha256OfTheGuid()
    {
        // Vetor conhecido: SHA-256("abc") — prova que a entrada do hash é o GUID puro.
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            HardwareId.ComputeMachineId("abc"));
    }

    [Fact]
    public void ComputeMachineId_TrimsSurroundingWhitespace()
    {
        Assert.Equal(HardwareId.ComputeMachineId("abc"), HardwareId.ComputeMachineId("  abc  "));
    }

    [Fact]
    public void ComputeMachineId_DoesNotIncludeMachineName()
    {
        const string guid = "c62d4e57-b686-4cd0-a067-0fdbf9a51302";

        // Regressão H3: incluir o hostname (comportamento antigo: SHA-256 de
        // "guid:MachineName") mudava o Machine ID a cada rename da máquina — a licença
        // deixava de bater e um segundo assento era consumido até o reap de 60 dias.
        Assert.Equal(Sha256Hex(guid), HardwareId.ComputeMachineId(guid));
        Assert.NotEqual(Sha256Hex($"{guid}:{Environment.MachineName}"), HardwareId.ComputeMachineId(guid));
    }

    [Fact]
    public void TryGetMachineId_WithGuid_ReturnsStableHex64()
    {
        using var machineGuidScope = TestHelpers.WithMachineGuid("c62d4e57-b686-4cd0-a067-0fdbf9a51302");

        Assert.True(HardwareId.TryGetMachineId(out string first, out string? reason));
        Assert.Null(reason);
        Assert.Matches("^[0-9a-f]{64}$", first);

        Assert.True(HardwareId.TryGetMachineId(out string second, out _));
        Assert.Equal(first, second);
        Assert.Equal(HardwareId.ComputeMachineId("c62d4e57-b686-4cd0-a067-0fdbf9a51302"), first);
    }

    [Fact]
    public void TryGetMachineId_WithoutGuid_FailsClosed()
    {
        using var machineGuidScope = TestHelpers.WithMachineGuid(null);

        Assert.False(HardwareId.TryGetMachineId(out string machineId, out string? reason));
        Assert.Empty(machineId);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void TryGetMachineId_WithBlankGuid_FailsClosed()
    {
        using var machineGuidScope = TestHelpers.WithMachineGuid("   ");

        Assert.False(HardwareId.TryGetMachineId(out _, out string? reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void TryGetMachineId_OnThisMachine_ReadsTheRealGuid()
    {
        // Sem seam: numa sessão Windows o HKLM é legível (inclusive SSH/Session 0).
        Assert.True(HardwareId.TryGetMachineId(out string machineId, out string? reason));
        Assert.Null(reason);
        Assert.Matches("^[0-9a-f]{64}$", machineId);
        Assert.Equal(TestHelpers.CurrentMachineId(), machineId);
    }

    private static string Sha256Hex(string input)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(input)))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }
}
