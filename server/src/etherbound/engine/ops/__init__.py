from etherbound.engine.verbs.base import ActionContext, VerbHandler
from etherbound.engine.verbs.move import MoveHandler

_handlers: dict[str, VerbHandler] = {}


def register(handler: VerbHandler) -> None:
    if handler.verb in _handlers:
        raise ValueError(f"verb handler already registered: {handler.verb}")
    _handlers[handler.verb] = handler


def handler_for(verb: str) -> VerbHandler:
    try:
        return _handlers[verb]
    except KeyError:
        raise KeyError(f"unknown verb: {verb}") from None


register(MoveHandler())

__all__ = ["ActionContext", "VerbHandler", "handler_for", "register"]
