import asyncio
import logging

import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from etherbound.db.models import Base
from etherbound.engine.actions import MoveAction
from etherbound.engine.verbs import register
from etherbound.engine.verbs.move import MoveHandler
from etherbound.engine.world import PLAYER_ID, WorldEngine
from etherbound.events import ClockChanged, ClockTicked, Event, EventBus


@pytest.fixture
def engine() -> WorldEngine:
    database = create_engine("sqlite:///:memory:", connect_args={"check_same_thread": False})
    Base.metadata.create_all(database)
    sessions = sessionmaker(bind=database, expire_on_commit=False, class_=Session)
    world = WorldEngine(sessions)
    world.ensure_world(123)
    return world


async def test_bus_matches_base_types_in_subscription_order() -> None:
    bus = EventBus()
    calls: list[str] = []
    bus.subscribe(Event, lambda _: calls.append("base"), name="base")
    bus.subscribe(ClockChanged, lambda _: calls.append("specific"), name="specific")
    bus.enqueue([ClockChanged(speed=3, paused=False)])

    await bus.drain()

    assert calls == ["base", "specific"]


async def test_bus_drains_reentrant_actions_fifo_without_nesting() -> None:
    bus = EventBus()
    calls: list[str] = []

    async def first(event: ClockChanged | ClockTicked) -> None:
        calls.append(f"first:{event.type}")
        if isinstance(event, ClockChanged):
            bus.enqueue([ClockTicked(seq=2)])
            await bus.drain()

    bus.subscribe(ClockChanged, first, name="first")
    bus.subscribe(Event, lambda event: calls.append(f"last:{event.type}"), name="last")
    bus.enqueue([ClockChanged(seq=1, speed=3, paused=False)])

    await bus.drain()

    assert calls == [
        "first:clock.changed",
        "last:clock.changed",
        "last:clock.ticked",
    ]


async def test_concurrent_drain_returns_while_active_drain_dispatches_fifo() -> None:
    bus = EventBus()
    handler_started = asyncio.Event()
    release_handler = asyncio.Event()
    dispatched: list[int] = []

    async def block_first(event: ClockTicked) -> None:
        if event.seq == 1:
            handler_started.set()
            await release_handler.wait()

    bus.subscribe(ClockTicked, block_first, name="block-first")
    bus.subscribe(Event, lambda event: dispatched.append(event.seq), name="capture")
    bus.enqueue([ClockTicked(seq=1), ClockTicked(seq=2)])
    active_drain = asyncio.create_task(bus.drain())
    await handler_started.wait()

    bus.enqueue([ClockTicked(seq=3)])
    await asyncio.wait_for(bus.drain(), timeout=2)
    release_handler.set()
    await active_drain

    assert dispatched == [1, 2, 3]


async def test_bus_logs_handler_errors_and_continues(caplog: pytest.LogCaptureFixture) -> None:
    bus = EventBus()
    calls: list[int] = []

    def fail(_: Event) -> None:
        raise RuntimeError("handler failed")

    bus.subscribe(Event, fail, name="broken")
    bus.subscribe(Event, lambda event: calls.append(event.seq), name="healthy")
    bus.enqueue([ClockTicked(seq=1), ClockTicked(seq=2)])

    with caplog.at_level(logging.ERROR, logger="etherbound.events"):
        await bus.drain()

    assert calls == [1, 2]
    assert caplog.text.count("handler failed: seq=") == 2
    assert "Traceback" in caplog.text


async def test_bus_stops_self_feeding_cascade(caplog: pytest.LogCaptureFixture) -> None:
    bus = EventBus()
    bus.MAX_CASCADE = 3
    dispatched: list[int] = []

    def feed(event: Event) -> None:
        dispatched.append(event.seq)
        bus.enqueue([ClockTicked(seq=event.seq + 1)])

    bus.subscribe(Event, feed, name="self-feeding")
    bus.enqueue([ClockTicked(seq=1)])

    with caplog.at_level(logging.ERROR, logger="etherbound.events"):
        await bus.drain()

    assert dispatched == [1, 2, 3]
    assert "MAX_CASCADE=3" in caplog.text


async def test_move_events_are_stored_once_per_tile(engine: WorldEngine) -> None:
    await engine.bus.drain()
    start_count = len(engine.read_events(event_type="actor.moved"))

    await engine.submit(PLAYER_ID, MoveAction(dx=1, dy=0), delta_seconds=0.2)
    await engine.submit(PLAYER_ID, MoveAction(dx=1, dy=0), delta_seconds=0.02)

    moved = engine.read_events(event_type="actor.moved")
    assert len(moved) - start_count == 1
    assert moved[-1].data["from_tile"]["x"] == 121
    assert moved[-1].data["to_tile"]["x"] == 122


async def test_paused_move_is_rejected_without_event_or_dispatch(engine: WorldEngine) -> None:
    await engine.bus.drain()
    seen: list[Event] = []
    engine.bus.subscribe(Event, seen.append, name="test.capture")
    await engine.set_clock(paused=True)
    seen.clear()
    before = len(engine.read_events())

    result = await engine.submit(PLAYER_ID, MoveAction(dx=1, dy=0))

    assert not result.accepted
    assert result.reason == "paused"
    assert len(engine.read_events()) == before
    assert seen == []


async def test_new_game_restarts_log_with_initial_events(engine: WorldEngine) -> None:
    await engine.submit(PLAYER_ID, MoveAction(dx=1, dy=0), delta_seconds=0.2)

    await engine.new_game(7)

    events = engine.read_events()
    assert [(event.seq, event.type) for event in events] == [
        (1, "world.generated"),
        (2, "actor.spawned"),
        (3, "clock.changed"),
    ]


async def test_submit_from_clock_handler_dispatches_after_clock_event(
    engine: WorldEngine,
) -> None:
    await engine.bus.drain()
    dispatched: list[Event] = []
    submissions = []

    async def move_on_clock_change(_: ClockChanged) -> None:
        submissions.append(
            await engine.submit(PLAYER_ID, MoveAction(dx=1, dy=0), delta_seconds=0.2)
        )

    engine.bus.subscribe(ClockChanged, move_on_clock_change, name="test.submit-on-clock")
    engine.bus.subscribe(Event, dispatched.append, name="test.capture")

    await asyncio.wait_for(engine.set_clock(speed=3), timeout=2)

    assert len(submissions) == 1
    assert submissions[0].accepted
    assert [event.type for event in dispatched] == ["clock.changed", "actor.moved"]
    assert dispatched[1].seq == dispatched[0].seq + 1
    moved = engine.read_events(event_type="actor.moved")
    assert moved[-1].seq == dispatched[1].seq


async def test_ticks_dispatch_but_are_not_persisted(engine: WorldEngine) -> None:
    await engine.bus.drain()
    seen: list[Event] = []
    engine.bus.subscribe(ClockTicked, seen.append, name="test.tick")
    before = len(engine.read_events())

    await engine.advance_time()

    assert len(seen) == 1
    assert seen[0].game_minute == 1
    assert len(engine.read_events()) == before


async def test_clock_events_only_on_changes(engine: WorldEngine) -> None:
    await engine.bus.drain()
    before = len(engine.read_events(event_type="clock.changed"))

    await engine.set_clock(speed=1, paused=False)
    await engine.set_clock(speed=3)

    assert len(engine.read_events(event_type="clock.changed")) == before + 1


async def test_sequence_continues_after_engine_restart(engine: WorldEngine) -> None:
    await engine.submit(PLAYER_ID, MoveAction(dx=1, dy=0), delta_seconds=0.2)
    last_seq = engine.read_events()[-1].seq
    restarted = WorldEngine(engine.sessions)
    event_count = len(restarted.read_events())
    restarted.ensure_world()
    assert len(restarted.read_events()) == event_count

    await restarted.submit(PLAYER_ID, MoveAction(dx=1, dy=0), delta_seconds=0.2)

    assert restarted.read_events()[-1].seq == last_seq + 1


def test_duplicate_verb_registration_is_rejected() -> None:
    with pytest.raises(ValueError, match="already registered: move"):
        register(MoveHandler())
