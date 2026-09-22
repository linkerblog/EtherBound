import inspect
import logging
from collections import deque
from collections.abc import Awaitable, Callable, Iterable
from typing import TypeVar

from etherbound.events.models import Event

E = TypeVar("E", bound=Event)
Handler = Callable[[E], Awaitable[None] | None]
logger = logging.getLogger("etherbound.events")


class EventBus:
    MAX_CASCADE = 10_000

    def __init__(self) -> None:
        self._handlers: list[tuple[type[Event], Handler[Event], str]] = []
        self._queue: deque[Event] = deque()
        self._draining = False

    def subscribe(self, event_type: type[E], handler: Handler[E], *, name: str) -> None:
        self._handlers.append((event_type, handler, name))  # type: ignore[arg-type]

    def enqueue(self, events: Iterable[Event]) -> None:
        self._queue.extend(events)

    async def drain(self) -> None:
        if self._draining:
            return
        self._draining = True
        try:
            dispatched = 0
            while self._queue:
                if dispatched >= self.MAX_CASCADE:
                    logger.error("event dispatch exceeded MAX_CASCADE=%d", self.MAX_CASCADE)
                    self._queue.clear()
                    return
                event = self._queue.popleft()
                dispatched += 1
                for event_type, handler, name in self._handlers:
                    if not isinstance(event, event_type):
                        continue
                    try:
                        result = handler(event)
                        if inspect.isawaitable(result):
                            await result
                    except Exception:
                        logger.exception(
                            "event handler failed: seq=%d type=%s handler=%s",
                            event.seq,
                            event.type,
                            name,
                        )
        finally:
            self._draining = False
