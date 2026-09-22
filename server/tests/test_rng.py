from etherbound.rng import RNGStreams


def test_named_rng_stream_is_deterministic_and_independent() -> None:
    first = RNGStreams(42).stream("movement")
    second = RNGStreams(42).stream("movement")
    other = RNGStreams(42).stream("economy")

    assert [first.random() for _ in range(5)] == [second.random() for _ in range(5)]
    assert first.random() != other.random()
