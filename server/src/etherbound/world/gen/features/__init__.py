"""Feature stampers shared by generators. A feature fills a rectangle with content."""

from collections.abc import Callable
from typing import Any

from etherbound.world.gen.canvas import GenCanvas
from etherbound.world.gen.features.relief import stamp as relief_stamp
from etherbound.world.gen.types import GeneratedObject

Stamp = Callable[[GenCanvas, tuple[int, int, int, int], int, Any], list[GeneratedObject]]

FEATURES: dict[str, Stamp] = {
    "relief": relief_stamp,
}

__all__ = ["FEATURES", "Stamp"]
