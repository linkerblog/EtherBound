from dataclasses import dataclass


@dataclass(frozen=True, slots=True)
class TestMap:
    width: int = 64
    height: int = 64

    def is_blocked(self, x: float, y: float, radius: float = 0.3) -> bool:
        if x - radius < 0 or y - radius < 0:
            return True
        if x + radius >= self.width or y + radius >= self.height:
            return True
        tile_x = int(x)
        tile_y = int(y)
        if tile_x == 8 and 2 <= tile_y < self.height - 2:
            return True
        return False


DEFAULT_MAP = TestMap()
