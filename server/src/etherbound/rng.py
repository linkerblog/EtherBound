import hashlib
from random import Random

# The seeded stream type, re-exported so systems never import `random` themselves.
RngStream = Random


class RNGStreams:
    """Create independent deterministic random streams from one world seed."""

    def __init__(self, seed: int) -> None:
        self.seed = seed

    def stream(self, system: str) -> RngStream:
        digest = hashlib.sha256(f"{self.seed}:{system}".encode()).digest()
        return Random(int.from_bytes(digest[:8], "big"))
