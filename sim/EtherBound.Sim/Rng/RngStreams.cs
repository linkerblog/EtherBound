using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EtherBound.Sim.Rng;

/// <summary>
/// One deterministic stream per system, derived from the world seed: SHA-256 of
/// <c>"{seed}:{system}"</c>, first 8 bytes big-endian, as in <c>server/.../rng.py</c>.
/// </summary>
public sealed class RngStreams
{
    public RngStreams(long seed) => Seed = seed;

    public long Seed { get; }

    public PyRandom Stream(string system)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{Seed}:{system}");
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return new PyRandom(BinaryPrimitives.ReadUInt64BigEndian(digest));
    }
}
