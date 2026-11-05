"""Prepara las fuentes del sitio de documentación sin modificar docs/.

Copia docs/ a artifacts/docs-site/docs con la estructura del sitio: el manual
de usuario queda en la raíz, y design/ y milestones/ en sus subrutas. Renderiza
PlantUML a SVG (bloques ```plantuml embebidos y archivos .puml), reescribe los
enlaces y agrega los recursos propios del sitio. Después se construye con
`zensical build`, que lee esa copia.

El manual sale del último tag que lo contiene, para que coincida con la versión
publicada; design/ y milestones/ salen del árbol de trabajo.

Uso:
  uv run docs-site/build.py                   prepara las fuentes
  uv run docs-site/build.py --worktree-guide  usa el manual del árbol de trabajo
  uv run docs-site/build.py --finalize        marca el idioma tras `zensical build`
"""

from __future__ import annotations

import argparse
import hashlib
import io
import os
import re
import shutil
import subprocess
import sys
import tarfile
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "docs"
SITE_ASSETS = ROOT / "docs-site" / "assets"
PLANTUML_STYLE = ROOT / "docs-site" / "plantuml-coquena.puml"
WORK = ROOT / "artifacts" / "docs-site"
OUTPUT = WORK / "docs"
SITE = WORK / "site"
GUIDE = "user-guide"
SPANISH_SECTIONS = ("design", "milestones")
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


def published_path(path: Path) -> Path:
    """Traduce una ruta relativa a docs/ a su ubicación en el sitio."""
    if path.parts and path.parts[0] == GUIDE:
        return Path(*path.parts[1:])
    return path


def source_path(page: Path) -> Path:
    """Devuelve la ruta relativa a docs/ de una página de la copia preparada."""
    relative = page.relative_to(OUTPUT)
    if relative.parts[0] in SPANISH_SECTIONS:
        return relative
    return Path(GUIDE) / relative


def rewrite_links(page: Path) -> None:
    text = page.read_text(encoding="utf-8")
    source = source_path(page)
    published = published_path(source)

    def replace(match: re.Match[str]) -> str:
        prefix, target, anchor, suffix = match.groups()
        anchor = anchor or ""
        if re.match(r"^[a-z]+:", target) or target.startswith("/"):
            return match.group(0)
        if target.endswith(".puml"):
            target = target[: -len(".puml")] + ".svg"
        resolved = Path(os.path.normpath(source.parent / target))
        if resolved.parts and resolved.parts[0] == "..":
            repo_path = Path(os.path.normpath(Path("docs") / resolved))
            target = REPO_BLOB + repo_path.as_posix()
        else:
            target = Path(os.path.relpath(published_path(resolved), published.parent)).as_posix()
        return f"{prefix}{target}{anchor}{suffix}"

    page.write_text(LINK.sub(replace, text), encoding="utf-8")


def git(*args: str) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(["git", *args], cwd=ROOT, capture_output=True)


def released_guide_ref() -> str | None:
    """Devuelve el tag más reciente que contiene el manual."""
    tags = git("tag", "--list", "v*", "--sort=-v:refname").stdout.decode().split()
    return next((t for t in tags if git("cat-file", "-e", f"{t}:docs/{GUIDE}").returncode == 0), None)


def copy_guide(use_worktree: bool) -> None:
    ref = None if use_worktree else released_guide_ref()
    if ref is None:
        print(f"Manual desde el árbol de trabajo{'' if use_worktree else ' (ningún tag lo contiene)'}")
        shutil.copytree(SOURCE / GUIDE, OUTPUT, dirs_exist_ok=True)
        return
    print(f"Manual desde {ref}")
    archive = subprocess.run(
        ["git", "archive", ref, f"docs/{GUIDE}"], cwd=ROOT, capture_output=True, check=True
    ).stdout
    extracted = WORK / "guide"
    shutil.rmtree(extracted, ignore_errors=True)
    with tarfile.open(fileobj=io.BytesIO(archive)) as tar:
        tar.extractall(extracted, filter="data")
    shutil.copytree(extracted / "docs" / GUIDE, OUTPUT, dirs_exist_ok=True)


def prepare(use_worktree: bool) -> None:
    if OUTPUT.exists():
        shutil.rmtree(OUTPUT)
    OUTPUT.mkdir(parents=True)
    copy_guide(use_worktree)
    for section in (*SPANISH_SECTIONS, "assets"):
        shutil.copytree(SOURCE / section, OUTPUT / section)
    shutil.copytree(SITE_ASSETS, OUTPUT / "stylesheets", dirs_exist_ok=True)

    diagrams = sorted(OUTPUT.rglob("*.puml"))
    for page in sorted(OUTPUT.rglob("*.md")):
        extract_fences(page, diagrams)
        rewrite_links(page)

    index = OUTPUT / "README.md"
    banner = '![Coquena](assets/coquena-banner.jpg){ .cq-banner }\n\n'
    index.write_text(banner + index.read_text(encoding="utf-8"), encoding="utf-8")

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


def finalize() -> None:
    """Declara el idioma real de las secciones en castellano; el tema admite uno solo."""
    for section in SPANISH_SECTIONS:
        for page in (SITE / section).rglob("*.html"):
            if page.parent == SITE / "milestones":
                continue  # El índice del roadmap está en inglés.
            text = page.read_text(encoding="utf-8")
            page.write_text(text.replace('<html lang="en"', '<html lang="es"', 1), encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description="Prepara el sitio de documentación.")
    parser.add_argument("--worktree-guide", action="store_true")
    parser.add_argument("--finalize", action="store_true")
    args = parser.parse_args()
    if args.finalize:
        finalize()
    else:
        prepare(args.worktree_guide)


if __name__ == "__main__":
    main()
