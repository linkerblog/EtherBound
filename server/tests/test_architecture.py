import ast
from pathlib import Path

SOURCE = Path(__file__).parents[1] / "src" / "etherbound"
ALLOWED_DIRS = {"engine", "db"}
ALLOWED_FILES = {"app.py"}


def _imported_modules(tree: ast.AST) -> set[str]:
    modules: set[str] = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            modules.update(alias.name for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.level == 0 and node.module:
            modules.add(node.module)
    return modules


def _is_database_import(module: str) -> bool:
    if module == "etherbound.db" or module.startswith("etherbound.db."):
        return True
    return module == "sqlalchemy.orm" or module.startswith("sqlalchemy.orm.")


def test_database_imports_stay_inside_the_engine() -> None:
    offenders = []
    for path in sorted(SOURCE.rglob("*.py")):
        relative = path.relative_to(SOURCE)
        if relative.parts[0] in ALLOWED_DIRS or relative.name in ALLOWED_FILES:
            continue
        for module in _imported_modules(ast.parse(path.read_text(encoding="utf-8"))):
            if _is_database_import(module):
                offenders.append(f"{relative.as_posix()}: {module}")
    assert offenders == []
