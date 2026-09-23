from etherbound.engine.ops.base import ActionContext, OpHandler, Resolution
from etherbound.engine.ops.catalog import OpSpec, catalog, spec_for
from etherbound.engine.ops.climb import ClimbHandler
from etherbound.engine.ops.dig import DigHandler
from etherbound.engine.ops.inspect import InspectHandler
from etherbound.engine.ops.move import MoveHandler
from etherbound.engine.ops.wait import WaitHandler

_handlers: dict[str, OpHandler] = {}


def register(handler: OpHandler) -> None:
    if spec_for(handler.op) is None:
        raise ValueError(f"op handler for a key missing from ops.toml: {handler.op}")
    if handler.op in _handlers:
        raise ValueError(f"op handler already registered: {handler.op}")
    _handlers[handler.op] = handler


def handler_for(op: str) -> OpHandler:
    try:
        return _handlers[op]
    except KeyError:
        raise KeyError(f"unknown op: {op}") from None


def handled_ops() -> list[tuple[OpSpec, OpHandler]]:
    """Catalog entries that have behaviour, in catalog order."""
    return [(spec, _handlers[spec.key]) for spec in catalog() if spec.key in _handlers]


for _handler in (MoveHandler(), ClimbHandler(), WaitHandler(), InspectHandler(), DigHandler()):
    register(_handler)

__all__ = [
    "ActionContext",
    "OpHandler",
    "OpSpec",
    "Resolution",
    "catalog",
    "handled_ops",
    "handler_for",
    "register",
]
