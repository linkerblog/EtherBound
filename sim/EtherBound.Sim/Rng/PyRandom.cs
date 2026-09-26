namespace EtherBound.Sim.Rng;

/// <summary>
/// A bit-exact port of CPython's <c>random.Random</c> (MT19937, integer seeding and the methods the
/// sim uses), so worlds and populations match the Python goldens byte for byte. A faster
/// generator later is a <c>gen_version</c> bump, not a silent swap.
/// </summary>
public sealed class PyRandom
{
    private const int N = 624;
    private const int M = 397;
    private const uint MatrixA = 0x9908b0dfU;
    private const uint UpperMask = 0x80000000U;
    private const uint LowerMask = 0x7fffffffU;

    private readonly uint[] _mt = new uint[N];
    private int _mti = N + 1;

    /// <summary>Seeds like <c>random.Random(seed)</c> for a non-negative integer seed.</summary>
    public PyRandom(ulong seed)
    {
        // CPython splits abs(seed) into 32-bit words, least significant first; zero is one word.
        var key = (seed >> 32) == 0 ? new[] { (uint)seed } : new[] { (uint)seed, (uint)(seed >> 32) };
        InitByArray(key);
    }

    private void InitGenrand(uint s)
    {
        _mt[0] = s;
        for (_mti = 1; _mti < N; _mti++)
            _mt[_mti] = 1812433253U * (_mt[_mti - 1] ^ (_mt[_mti - 1] >> 30)) + (uint)_mti;
    }

    private void InitByArray(uint[] key)
    {
        InitGenrand(19650218U);
        int i = 1, j = 0;
        for (var k = Math.Max(N, key.Length); k > 0; k--)
        {
            _mt[i] = (_mt[i] ^ ((_mt[i - 1] ^ (_mt[i - 1] >> 30)) * 1664525U)) + key[j] + (uint)j;
            i++;
            j++;
            if (i >= N)
            {
                _mt[0] = _mt[N - 1];
                i = 1;
            }
            if (j >= key.Length) j = 0;
        }
        for (var k = N - 1; k > 0; k--)
        {
            _mt[i] = (_mt[i] ^ ((_mt[i - 1] ^ (_mt[i - 1] >> 30)) * 1566083941U)) - (uint)i;
            i++;
            if (i >= N)
            {
                _mt[0] = _mt[N - 1];
                i = 1;
            }
        }
        _mt[0] = 0x80000000U;
    }

    private uint NextUInt32()
    {
        uint y;
        if (_mti >= N)
        {
            int kk;
            for (kk = 0; kk < N - M; kk++)
            {
                y = (_mt[kk] & UpperMask) | (_mt[kk + 1] & LowerMask);
                _mt[kk] = _mt[kk + M] ^ (y >> 1) ^ ((y & 1U) != 0 ? MatrixA : 0U);
            }
            for (; kk < N - 1; kk++)
            {
                y = (_mt[kk] & UpperMask) | (_mt[kk + 1] & LowerMask);
                _mt[kk] = _mt[kk + (M - N)] ^ (y >> 1) ^ ((y & 1U) != 0 ? MatrixA : 0U);
            }
            y = (_mt[N - 1] & UpperMask) | (_mt[0] & LowerMask);
            _mt[N - 1] = _mt[M - 1] ^ (y >> 1) ^ ((y & 1U) != 0 ? MatrixA : 0U);
            _mti = 0;
        }
        y = _mt[_mti++];
        y ^= y >> 11;
        y ^= (y << 7) & 0x9d2c5680U;
        y ^= (y << 15) & 0xefc60000U;
        y ^= y >> 18;
        return y;
    }

    /// <summary><c>random()</c>: 53 random bits in [0, 1).</summary>
    public double Random()
    {
        var a = NextUInt32() >> 5;
        var b = NextUInt32() >> 6;
        return (a * 67108864.0 + b) * (1.0 / 9007199254740992.0);
    }

    /// <summary><c>getrandbits(k)</c> for 0 &lt; k &lt;= 64; words fill from the least significant.</summary>
    public ulong GetRandBits(int k)
    {
        if (k is <= 0 or > 64) throw new ArgumentOutOfRangeException(nameof(k));
        if (k <= 32) return NextUInt32() >> (32 - k);
        ulong low = NextUInt32();
        ulong high = NextUInt32() >> (64 - k);
        return low | (high << 32);
    }

    /// <summary><c>_randbelow_with_getrandbits(n)</c>: rejection sampling on n's bit length.</summary>
    public long RandBelow(long n)
    {
        if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
        var k = 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)n);
        var r = GetRandBits(k);
        while (r >= (ulong)n) r = GetRandBits(k);
        return (long)r;
    }

    /// <summary><c>randint(a, b)</c>, both ends included.</summary>
    public long RandInt(long a, long b)
    {
        if (b < a) throw new ArgumentException($"empty range in randint({a}, {b})");
        return a + RandBelow(b - a + 1);
    }

    public int RandInt(int a, int b) => (int)RandInt((long)a, b);

    /// <summary><c>uniform(a, b)</c>: <c>a + (b - a) * random()</c>, rounding included.</summary>
    public double Uniform(double a, double b) => a + (b - a) * Random();

    /// <summary><c>shuffle(x)</c>: Fisher-Yates from the end.</summary>
    public void Shuffle<T>(IList<T> items)
    {
        for (var i = items.Count - 1; i >= 1; i--)
        {
            var j = (int)RandBelow(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary><c>sample(population, k)</c>: CPython's pool or set strategy, chosen the same way.</summary>
    public List<T> Sample<T>(IReadOnlyList<T> population, int k)
    {
        var n = population.Count;
        if (k < 0 || k > n) throw new ArgumentException("Sample larger than population or is negative");
        var result = new List<T>(k);
        var setsize = 21;
        if (k > 5) setsize += (int)Math.Pow(4, Math.Ceiling(Math.Log(k * 3) / Math.Log(4)));
        if (n <= setsize)
        {
            var pool = population.ToList();
            for (var i = 0; i < k; i++)
            {
                var j = (int)RandBelow(n - i);
                result.Add(pool[j]);
                pool[j] = pool[n - i - 1];
            }
        }
        else
        {
            var selected = new HashSet<long>();
            for (var i = 0; i < k; i++)
            {
                var j = RandBelow(n);
                while (selected.Contains(j)) j = RandBelow(n);
                selected.Add(j);
                result.Add(population[(int)j]);
            }
        }
        return result;
    }
}
