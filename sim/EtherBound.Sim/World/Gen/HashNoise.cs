namespace EtherBound.Sim.World.Gen;

/// <summary>
/// Period-free value noise for the endless world: every lattice value is an integer hash of
/// <c>(seed, salt, x, y)</c>, so any position on any chunk costs the same and nothing repeats.
/// <see cref="ValueNoise"/> stays as it is, because the <c>test</c> and <c>lab</c> goldens pin its output.
/// </summary>
public sealed class HashNoise
{
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;
    private const double Unit = 1.0 / (1UL << 53);

    private readonly ulong _seed;

    public HashNoise(long seed) => _seed = Mix(unchecked((ulong)seed) + GoldenGamma);

    /// <summary>The splitmix64 finaliser: a fixed, process-independent integer mix.</summary>
    public static ulong Mix(ulong z)
    {
        unchecked
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>A uniform integer hash of a salted lattice point, for per-position randomness.</summary>
    public ulong Hash(int salt, int x, int y)
    {
        unchecked
        {
            var h = Mix(_seed ^ ((ulong)(uint)salt * GoldenGamma));
            h = Mix(h ^ ((ulong)(uint)x * 0xD6E8FEB86659FD93UL));
            return Mix(h ^ ((ulong)(uint)y * 0xA0761D6478BD642FUL));
        }
    }

    /// <summary>The lattice value at a point, in [-1, 1).</summary>
    public double Value(int salt, int x, int y) => (Hash(salt, x, y) >> 11) * Unit * 2.0 - 1.0;

    private static double Smooth(double t) => t * t * (3.0 - 2.0 * t);

    public double Sample(int salt, double x, double y)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var sx = Smooth(x - x0);
        var sy = Smooth(y - y0);
        var a = Value(salt, x0, y0);
        var b = Value(salt, x0 + 1, y0);
        var c = Value(salt, x0, y0 + 1);
        var d = Value(salt, x0 + 1, y0 + 1);
        var top = a + (b - a) * sx;
        return top + ((c + (d - c) * sx) - top) * sy;
    }

    /// <summary>Fractal sum of <paramref name="octaves"/> samples, normalised so the range stays within [-1, 1].</summary>
    public double Fbm(int salt, double x, double y, int octaves, double gain = 0.5)
    {
        double value = 0.0, amplitude = 1.0, normal = 0.0;
        for (var i = 0; i < octaves; i++)
        {
            // A fixed offset per octave keeps the octaves from lining up at the origin.
            value += Sample(salt * 31 + i, x + i * 17.31, y + i * 29.77) * amplitude;
            normal += amplitude;
            x *= 2.0;
            y *= 2.0;
            amplitude *= gain;
        }
        return value / normal;
    }
}
