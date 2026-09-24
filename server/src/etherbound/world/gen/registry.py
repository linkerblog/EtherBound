from collections.abc import Callable
from dataclasses import dataclass, field
from typing import Any, Literal, cast, get_args, get_origin

from annotated_types import Ge, Le, MultipleOf
from pydantic import BaseModel

from etherbound.world.gen.types import GeneratedWorld, GeneratorBay
from etherbound.world.materials import MaterialRegistry

OptionsModel = type[BaseModel]


@dataclass(frozen=True, slots=True)
class OptionField:
    """One flat, form-ready description of a generator option."""

    path: str
    label: str
    kind: Literal["choice", "bool", "int", "float"]
    default: Any
    minimum: float | None = None
    maximum: float | None = None
    step: float | None = None
    choices: tuple[str, ...] = ()
    group: str | None = None


@dataclass(frozen=True, slots=True)
class GeneratorSpec:
    key: str
    name: str
    version: int
    options: OptionsModel
    generate: Callable[[int, Any, MaterialRegistry], GeneratedWorld]
    spawn: Callable[[int, Any], tuple[float, float, int]]
    bays: tuple[GeneratorBay, ...] = field(default_factory=tuple)


def _label(name: str) -> str:
    return " ".join(part.capitalize() for part in name.split("_"))


def option_fields(
    model: OptionsModel, *, prefix: str = "", group: str | None = None
) -> list[OptionField]:
    """Flatten a Pydantic options model into form-ready field descriptions."""
    fields: list[OptionField] = []
    for name, info in model.model_fields.items():
        annotation = info.annotation
        path = f"{prefix}{name}"
        if get_origin(annotation) is Literal:
            choices = tuple(str(choice) for choice in get_args(annotation))
            fields.append(
                OptionField(
                    path=path,
                    label=_label(name),
                    kind="choice",
                    default=info.default,
                    choices=choices,
                    group=group,
                )
            )
            continue
        if isinstance(annotation, type) and issubclass(annotation, BaseModel):
            fields.extend(option_fields(annotation, prefix=f"{path}.", group=name))
            continue
        if annotation is bool:
            kind: Literal["bool", "int", "float"] = "bool"
        elif annotation is int:
            kind = "int"
        elif annotation is float:
            kind = "float"
        else:
            raise TypeError(f"unsupported option type for {path}: {annotation!r}")
        metadata = info.metadata
        minimum = next((cast(float, item.ge) for item in metadata if isinstance(item, Ge)), None)
        maximum = next((cast(float, item.le) for item in metadata if isinstance(item, Le)), None)
        step = next(
            (cast(float, item.multiple_of) for item in metadata if isinstance(item, MultipleOf)),
            None,
        )
        fields.append(
            OptionField(
                path=path,
                label=_label(name),
                kind=kind,
                default=info.default,
                minimum=minimum,
                maximum=maximum,
                step=step,
                group=group,
            )
        )
    return fields


def _load_generators() -> dict[str, GeneratorSpec]:
    from etherbound.world.gen.lab import GEN_VERSION as LAB_VERSION
    from etherbound.world.gen.lab import LAB_BAYS, LabOptions, generate_lab, lab_spawn
    from etherbound.world.gen.testworld import (
        GEN_VERSION as TEST_VERSION,
    )
    from etherbound.world.gen.testworld import (
        SPAWN_POINT,
        TestOptions,
        generate_test_world,
    )

    return {
        "test": GeneratorSpec(
            key="test",
            name="Test world",
            version=TEST_VERSION,
            options=TestOptions,
            generate=lambda seed, options, registry: generate_test_world(seed, registry),
            spawn=lambda seed, options: (*SPAWN_POINT, 2),
        ),
        "lab": GeneratorSpec(
            key="lab",
            name="Debug lab",
            version=LAB_VERSION,
            options=LabOptions,
            generate=generate_lab,
            spawn=lab_spawn,
            bays=LAB_BAYS,
        ),
    }


GENERATORS = _load_generators()
DEFAULT_GENERATOR = "test"


def get_generator(key: str) -> GeneratorSpec | None:
    return GENERATORS.get(key)


def option_defaults(spec: GeneratorSpec) -> dict[str, Any]:
    return spec.options().model_dump(mode="json")


__all__ = [
    "DEFAULT_GENERATOR",
    "GENERATORS",
    "GeneratorBay",
    "GeneratorSpec",
    "OptionField",
    "get_generator",
    "option_defaults",
    "option_fields",
]
