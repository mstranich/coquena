"""Prepara las fuentes del sitio de documentación sin modificar docs/.

Copia docs/ a artifacts/docs-site/docs, renderiza PlantUML a SVG (bloques
```plantuml embebidos y archivos .puml), reescribe los enlaces que salen de
docs/ hacia el repositorio en GitHub y agrega los recursos propios del sitio.
Después se construye con `zensical build`, que lee esa copia.

Uso: uv run docs-site/build.py
"""

from __future__ import annotations

import hashlib
import os
import re
import shutil
import subprocess
import sys
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "docs"
SITE_ASSETS = ROOT / "docs-site" / "assets"
PLANTUML_STYLE = ROOT / "docs-site" / "plantuml-coquena.puml"
WORK = ROOT / "artifacts" / "docs-site"
OUTPUT = WORK / "docs"
REPO_BLOB = "https://github.com/mstranich/coquena/blob/main/"

PLANTUML_VERSION = "1.2026.8"
PLANTUML_SHA256 = "5e1ecfa8ecd32c90b03bbf3b1eb6f020943f98ab0fcf4032be31a0002ee2c462"
PLANTUML_URL = (
    "https://github.com/plantuml/plantuml/releases/download/"
    f"v{PLANTUML_VERSION}/plantuml-{PLANTUML_VERSION}.jar"
)

FENCE = re.compile(r"^```plantuml[ \t]*\n(.*?)^```[ \t]*$", re.MULTILINE | re.DOTALL)
LINK = re.compile(r"(\]\()([^)\s#]+)(#[^)\s]*)?(\))")


def plantuml_jar() -> Path:
    jar = WORK / "tools" / f"plantuml-{PLANTUML_VERSION}.jar"
    if not jar.exists():
        jar.parent.mkdir(parents=True, exist_ok=True)
        print(f"Descargando PlantUML {PLANTUML_VERSION}")
        urllib.request.urlretrieve(PLANTUML_URL, jar)
    digest = hashlib.sha256(jar.read_bytes()).hexdigest()
    if digest != PLANTUML_SHA256:
        jar.unlink()
        sys.exit(f"SHA-256 inesperado para {jar.name}: {digest}")
    return jar


def extract_fences(page: Path, diagrams: list[Path]) -> None:
    text = page.read_text(encoding="utf-8")
    count = 0

    def replace(match: re.Match[str]) -> str:
        nonlocal count
        count += 1
        source = page.with_name(f"{page.stem}.diagram-{count}.puml")
        source.write_text(match.group(1), encoding="utf-8")
        diagrams.append(source)
        return f"![Diagrama {count}]({source.stem}.svg)"

    updated = FENCE.sub(replace, text)
    if count:
        page.write_text(updated, encoding="utf-8")


def rewrite_links(page: Path) -> None:
    text = page.read_text(encoding="utf-8")

    def replace(match: re.Match[str]) -> str:
        prefix, target, anchor, suffix = match.groups()
        anchor = anchor or ""
        if re.match(r"^[a-z]+:", target) or target.startswith("/"):
            return match.group(0)
        if target.endswith(".puml"):
            target = target[: -len(".puml")] + ".svg"
        relative = page.parent.relative_to(OUTPUT) / target
        resolved = Path(os.path.normpath(relative))
        if resolved.parts and resolved.parts[0] == "..":
            repo_path = Path("docs") / resolved
            target = REPO_BLOB + Path(os.path.normpath(repo_path)).as_posix()
        return f"{prefix}{target}{anchor}{suffix}"

    page.write_text(LINK.sub(replace, text), encoding="utf-8")


def main() -> None:
    if OUTPUT.exists():
        shutil.rmtree(OUTPUT)
    shutil.copytree(SOURCE, OUTPUT)
    shutil.copytree(SITE_ASSETS, OUTPUT / "stylesheets", dirs_exist_ok=True)

    index = OUTPUT / "README.md"
    banner = '![Coquena](assets/coquena-banner.jpg){ .cq-banner }\n\n'
    index.write_text(banner + index.read_text(encoding="utf-8"), encoding="utf-8")

    diagrams = sorted(OUTPUT.rglob("*.puml"))
    for page in sorted(OUTPUT.rglob("*.md")):
        extract_fences(page, diagrams)
        rewrite_links(page)

    if diagrams:
        jar = plantuml_jar()
        command = ["java", "-jar", str(jar), "-tsvg", "-charset", "UTF-8", "-failfast2"]
        # Salt no admite el estilo de marca: los wireframes se publican como boceto claro.
        wireframes = [d for d in diagrams if d.name.endswith(".salt.puml")]
        styled = [d for d in diagrams if d not in wireframes]
        if styled:
            subprocess.run([*command, "-config", str(PLANTUML_STYLE), *map(str, styled)], check=True)
        if wireframes:
            subprocess.run([*command, *map(str, wireframes)], check=True)
        for source in diagrams:
            source.unlink()

    print(f"Fuentes del sitio en {OUTPUT.relative_to(ROOT)} ({len(diagrams)} diagramas)")


if __name__ == "__main__":
    main()
