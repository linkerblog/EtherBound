import asyncio

from etherbound.clock import Clock


async def _wait_for_ticks(ticks: list[int], target: int) -> None:
    while len(ticks) < target:
        await asyncio.sleep(0.01)


async def test_clock_ticks_pauses_and_honors_autopause_lock() -> None:
    ticks: list[int] = []
    clock = Clock(time_scale=0.02, on_tick=lambda: _record_tick(ticks))
    await clock.start()
    await _wait_for_ticks(ticks, 2)
    clock.set_paused(True)
    count = len(ticks)
    await asyncio.sleep(0.05)
    assert len(ticks) == count
    clock.set_paused(False)
    clock.acquire_autopause()
    await asyncio.sleep(0.05)
    assert len(ticks) == count
    clock.release_autopause()
    await _wait_for_ticks(ticks, count + 1)
    await clock.stop()


async def _record_tick(ticks: list[int]) -> None:
    ticks.append(1)
