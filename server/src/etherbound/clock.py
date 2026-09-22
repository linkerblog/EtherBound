import asyncio
from collections.abc import Awaitable, Callable


class Clock:
    """Owns real-time scheduling; game time advances only through its tick callback."""

    def __init__(
        self,
        time_scale: float = 1.0,
        on_tick: Callable[[], Awaitable[None]] | None = None,
    ) -> None:
        self.time_scale = time_scale
        self.speed = 1
        self.paused = False
        self._locks = 0
        self._task: asyncio.Task[None] | None = None
        self._on_tick = on_tick

    @property
    def locked(self) -> bool:
        return self._locks > 0

    @property
    def running(self) -> bool:
        return self._task is not None and not self._task.done()

    def load(self, *, speed: int, paused: bool) -> None:
        if speed not in (1, 3, 10):
            raise ValueError("speed must be 1, 3, or 10")
        self.speed = speed
        self.paused = paused

    def acquire_autopause(self) -> None:
        self._locks += 1

    def release_autopause(self) -> None:
        if self._locks == 0:
            raise RuntimeError("autopause lock is not held")
        self._locks -= 1

    def set_speed(self, speed: int) -> None:
        if speed not in (1, 3, 10):
            raise ValueError("speed must be 1, 3, or 10")
        self.speed = speed

    def set_paused(self, paused: bool) -> None:
        self.paused = paused

    async def start(self) -> None:
        if self.running:
            return
        self._task = asyncio.create_task(self._run(), name="etherbound-clock")

    async def stop(self) -> None:
        if self._task is None:
            return
        self._task.cancel()
        try:
            await self._task
        except asyncio.CancelledError:
            pass
        self._task = None

    async def _run(self) -> None:
        while True:
            await asyncio.sleep(self.time_scale / self.speed)
            if not self.paused and not self.locked and self._on_tick is not None:
                await self._on_tick()
