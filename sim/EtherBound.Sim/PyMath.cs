namespace EtherBound.Sim;

/// <summary>
/// Python's integer and float semantics where C# differs: floor division and modulo, banker's
/// rounding, and CPython's own <c>math.hypot</c> (F6), so positions match the goldens to the bit.
/// </summary>
public static class PyMath
{
    public static int FloorDiv(int a, int b)
    {
        var q = a / b;
        return (a % b != 0) && ((a < 0) != (b < 0)) ? q - 1 : q;
    }

    public static int Mod(int a, int b)
    {
        var r = a % b;
        return r != 0 && ((r < 0) != (b < 0)) ? r + b : r;
    }

    public static long FloorDiv(long a, long b)
    {
        var q = a / b;
        return (a % b != 0) && ((a < 0) != (b < 0)) ? q - 1 : q;
    }

    /// <summary><c>math.floor</c> of a float, as an int.</summary>
    public static int Floor(double value) => (int)Math.Floor(value);

    /// <summary><c>round(x)</c> with one argument: half to even, returning an int.</summary>
    public static int Round(double value) => (int)Math.Round(value, MidpointRounding.ToEven);

    /// <summary>CPython 3.14 <c>vector_norm</c> for two coordinates, with its error-free steps.</summary>
    public static double Hypot(double x, double y)
    {
        x = Math.Abs(x);
        y = Math.Abs(y);
        var max = Math.Max(x, y);
        if (double.IsInfinity(max)) return max;
        if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;
        if (max == 0.0) return max;
        var maxE = Math.ILogB(max) + 1; // frexp exponent
        if (maxE < -1023)
        {
            // CPython lifts subnormals to normals first, or ldexp(1.0, -max_e) would overflow.
            const double dblMin = 2.2250738585072014e-308;
            return dblMin * Hypot(x / dblMin, y / dblMin);
        }
        var scale = Math.ScaleB(1.0, -maxE);
        double csum = 1.0, frac1 = 0.0, frac2 = 0.0;
        foreach (var v in new[] { x, y })
        {
            var s = v * scale;
            var (prHi, prLo) = Mul(s, s);
            var (smHi, smLo) = FastSum(csum, prHi);
            csum = smHi;
            frac1 += prLo;
            frac2 += smLo;
        }
        var h = Math.Sqrt(csum - 1.0 + (frac1 + frac2));
        {
            var (prHi, prLo) = Mul(-h, h);
            var (smHi, smLo) = FastSum(csum, prHi);
            csum = smHi;
            frac1 += prLo;
            frac2 += smLo;
        }
        var correction = csum - 1.0 + (frac1 + frac2);
        h += correction / (2.0 * h);
        return h / scale;
    }

    private static (double Hi, double Lo) FastSum(double a, double b)
    {
        var x = a + b;
        return (x, (a - x) + b);
    }

    private static (double Hi, double Lo) Mul(double x, double y)
    {
        var z = x * y;
        return (z, Math.FusedMultiplyAdd(x, y, -z));
    }
}
