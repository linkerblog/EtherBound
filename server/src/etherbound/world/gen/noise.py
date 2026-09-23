from math import floor

from etherbound.rng import RNGStreams


class ValueNoise:
    """Small deterministic two-dimensional value-noise source."""

    def __init__(self, seed: int) -> None:
        rng = RNGStreams(seed).stream("worldgen")
        self._values = tuple(rng.uniform(-1.0, 1.0) for _ in range(256))
        permutation = list(range(256))
        rng.shuffle(permutation)
        self._permutation = tuple(permutation + permutation)

    def _value(self, x: int, y: int) -> float:
        return self._values[self._permutation[(self._permutation[x & 255] + y) & 255]]

    @staticmethod
    def _smooth(value: float) -> float:
        return value * value * (3.0 - 2.0 * value)

    def sample(self, x: float, y: float) -> float:
        x0, y0 = floor(x), floor(y)
        tx, ty = x - x0, y - y0
        sx, sy = self._smooth(tx), self._smooth(ty)
        a = self._value(x0, y0)
        b = self._value(x0 + 1, y0)
        c = self._value(x0, y0 + 1)
        d = self._value(x0 + 1, y0 + 1)
        return (a + (b - a) * sx) + ((c + (d - c) * sx) - (a + (b - a) * sx)) * sy

    def fbm(
        self, x: float, y: float, octaves: int = 4, lacunarity: float = 2.0, gain: float = 0.5
    ) -> float:
        value = 0.0
        amplitude = 1.0
        normal = 0.0
        for _ in range(octaves):
            value += self.sample(x, y) * amplitude
            normal += amplitude
            x *= lacunarity
            y *= lacunarity
            amplitude *= gain
        return value / normal
