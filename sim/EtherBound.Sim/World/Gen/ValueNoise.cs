using EtherBound.Sim.Rng;

namespace EtherBound.Sim.World.Gen;

/// <summary>Small deterministic two-dimensional value-noise source (<c>gen/noise.py</c>).</summary>
public sealed class ValueNoise
{
    private readonly double[] _values = new double[256];
    private readonly int[] _permutation = new int[512];

    public ValueNoise(long seed)
    {
        var rng = new RngStreams(seed).Stream("worldgen");
        for (var i = 0; i < 256; i++) _values[i] = rng.Uniform(-1.0, 1.0);
        var permutation = Enumerable.Range(0, 256).ToList();
        rng.Shuffle(permutation);
        for (var i = 0; i < 512; i++) _permutation[i] = permutation[i & 255];
    }

    private double Value(int x, int y) => _values[_permutation[(_permutation[x & 255] + y) & 255]];

    private static double Smooth(double value) => value * value * (3.0 - 2.0 * value);

    public double Sample(double x, double y)
    {
        int x0 = PyMath.Floor(x), y0 = PyMath.Floor(y);
        double tx = x - x0, ty = y - y0;
        double sx = Smooth(tx), sy = Smooth(ty);
        var a = Value(x0, y0);
        var b = Value(x0 + 1, y0);
        var c = Value(x0, y0 + 1);
        var d = Value(x0 + 1, y0 + 1);
        return (a + (b - a) * sx) + ((c + (d - c) * sx) - (a + (b - a) * sx)) * sy;
    }

    public double Fbm(double x, double y, int octaves = 4, double lacunarity = 2.0, double gain = 0.5)
    {
        double value = 0.0, amplitude = 1.0, normal = 0.0;
        for (var i = 0; i < octaves; i++)
        {
            value += Sample(x, y) * amplitude;
            normal += amplitude;
            x *= lacunarity;
            y *= lacunarity;
            amplitude *= gain;
        }
        return value / normal;
    }
}
