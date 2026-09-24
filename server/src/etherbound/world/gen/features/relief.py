from pydantic import BaseModel, Field

from etherbound.world.gen.canvas import GenCanvas
from etherbound.world.gen.noise import ValueNoise
from etherbound.world.gen.types import GeneratedObject


class ReliefOptions(BaseModel):
    amplitude: int = Field(default=12, ge=0, le=24)
    scale: float = Field(default=96.0, ge=8, le=256, multiple_of=8)
    octaves: int = Field(default=3, ge=1, le=6)
    gain: float = Field(default=0.5, ge=0.1, le=0.9, multiple_of=0.05)
    edge: int = Field(default=4, ge=0, le=12)


def stamp(
    canvas: GenCanvas,
    rect: tuple[int, int, int, int],
    seed: int,
    options: ReliefOptions,
) -> list[GeneratedObject]:
    """Raise noise hills inside ``rect``, blended to the flat base at the bay border."""
    x0, y0, x1, y1 = rect
    noise = ValueNoise(seed)
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            value = noise.fbm(
                x / options.scale, y / options.scale, octaves=options.octaves, gain=options.gain
            )
            if options.edge > 0:
                distance = min(x - x0, x1 - x, y - y0, y1 - y)
                blend = min(1.0, max(0.0, distance / options.edge))
            else:
                blend = 1.0
            canvas.set_ground(x, y, 2 + round(value * options.amplitude * blend))
    return []
