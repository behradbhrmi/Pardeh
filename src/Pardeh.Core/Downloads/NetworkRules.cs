using System.Net;
using System.Net.Sockets;

namespace Pardeh.Core.Downloads;

/// <summary>
/// Which names and addresses are on the public internet. An address from a stranger (a pasted link, a redirect, a
/// database entry) must never lead to this machine or its network.
/// </summary>
public static class NetworkRules
{
    private static readonly string[] LocalEndings = [".localhost", ".local", ".internal", ".lan", ".home", ".home.arpa", ".localdomain", ".intranet", ".corp"];

    /// <summary>A full host name that is not an address and not a local one. It says nothing about what it resolves to.</summary>
    public static bool IsPublicName(string host)
    {
        var name = host.TrimEnd('.').ToLowerInvariant();
        return name.Length > 0 && name.Contains('.') && !IPAddress.TryParse(name.Trim('[', ']'), out _) && !name.Contains(':')
            && !LocalEndings.Any(e => name.EndsWith(e, StringComparison.Ordinal));
    }

    // Everything that is not an ordinary public address, as network and prefix length.
    private static readonly (byte[] Network, int Bits)[] Reserved4 =
    [
        ([0, 0, 0, 0], 8), ([10, 0, 0, 0], 8), ([100, 64, 0, 0], 10), ([127, 0, 0, 0], 8), ([169, 254, 0, 0], 16), ([172, 16, 0, 0], 12),
        ([192, 0, 0, 0], 24), ([192, 0, 2, 0], 24), ([192, 88, 99, 0], 24), ([192, 168, 0, 0], 16), ([198, 18, 0, 0], 15), ([198, 51, 100, 0], 24),
        ([203, 0, 113, 0], 24), ([224, 0, 0, 0], 4), ([240, 0, 0, 0], 4),
    ];

    private static bool Within(ReadOnlySpan<byte> address, byte[] network, int bits)
    {
        for (var i = 0; bits > 0; i++, bits -= 8)
        {
            var mask = bits >= 8 ? 0xFF : 0xFF << (8 - bits) & 0xFF;
            if ((address[i] & mask) != (network[i] & mask))
                return false;
        }

        return true;
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return !Reserved4.Any(r => Within(bytes, r.Network, r.Bits));
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return false;
        // 64:ff9b::/96 is how a network with only IPv6 (many phone networks) reaches an IPv4 address: that one decides.
        if (Within(bytes, [0x00, 0x64, 0xFF, 0x9B, 0, 0, 0, 0, 0, 0, 0, 0], 96))
            return IsPublicAddress(new IPAddress(bytes.AsSpan(12, 4)));
        // Otherwise only ordinary global addresses, less the ranges that carry another address inside or are for examples.
        return Within(bytes, [0x20], 3)
            && !Within(bytes, [0x20, 0x01, 0x00, 0x00], 23)
            && !Within(bytes, [0x20, 0x01, 0x0D, 0xB8], 32)
            && !Within(bytes, [0x20, 0x02], 16)
            && !Within(bytes, [0x3F, 0xFF, 0x00], 20);
    }
}
