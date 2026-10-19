"""Export retained UI SVG sources without replacing hand-edited themes or catalogs.

Run from any directory: python tools/import-ui-source-kit.py [--check]
--check compares outputs without writing files. Runtime resource paths stay stable.
"""
from argparse import ArgumentParser
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "art-source/ui/source-kit"
ASSETS = ROOT / "Content.Client/Assets/UI"
SVG = "{http://www.w3.org/2000/svg}"
COMPONENTS = (
    "panel_parchment", "panel_dark", "button_default", "button_hover",
    "button_pressed", "button_disabled", "button_focus", "button_gold",
    "button_secondary", "item_default", "item_selected",
)


def component_bytes(path: Path) -> bytes:
    root = ET.parse(path).getroot()
    for parent in root.iter():
        for child in list(parent):
            if child.tag in (SVG + "text", SVG + "title", SVG + "desc"):
                parent.remove(child)
    if path.stem.startswith("item_"):
        for child in list(root):
            if child.tag == SVG + "g":
                root.remove(child)
    if path.stem == "button_focus":
        for rect in root.findall(SVG + "rect")[:-1]:
            root.remove(rect)
    root.attrib.pop("aria-label", None)
    # Match the original Windows exporter, including line endings.
    return ET.tostring(root, encoding="unicode").replace("\n", "\r\n").encode("utf-8")


def main() -> int:
    parser = ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Compare without writing outputs")
    args = parser.parse_args()
    ET.register_namespace("", "http://www.w3.org/2000/svg")
    icons = sorted((SOURCE / "icons").glob("*.svg"))
    if not icons:
        raise ValueError(f"No icon sources found in {SOURCE / 'icons'}")
    # Read and validate every source before updating any output.
    outputs = {}
    for icon in icons:
        ET.parse(icon)
        outputs[ASSETS / "Icons/SourceKit" / icon.name] = icon.read_bytes()
    for name in COMPONENTS:
        outputs[ASSETS / "Textures/SourceKit" / f"{name}.svg"] = component_bytes(
            SOURCE / "components" / f"{name}.svg")
    changed = []
    for path, data in outputs.items():
        if path.is_file() and path.read_bytes() == data:
            continue
        changed.append(path.relative_to(ROOT).as_posix())
        if not args.check:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
    for path in changed:
        print(("Out of date: " if args.check else "Updated: ") + path)
    print(f"{'Checked' if args.check else 'Exported'} {len(icons)} icons, "
          f"{len(COMPONENTS)} component textures; {len(changed)} differences. "
          "Themes and catalogs preserved.")
    return 1 if args.check and changed else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, ET.ParseError) as error:
        print(f"UI source export failed: {error}", file=sys.stderr)
        sys.exit(1)
