from pathlib import Path
import subprocess
import sys

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "assets" / "Wacky Crazy Funtime Logos"
RAW = OUT / "_chroma_sources"
HELPER = Path.home() / ".codex" / "skills" / ".system" / "imagegen" / "scripts" / "remove_chroma_key.py"
GENERATED = Path.home() / ".codex" / "generated_images" / "019fbfec-32c5-7f62-8555-a5a1d48fb0c1"

LOGOS = [
    ("Wacky - Little Guys", "exec-adbb932c-58fc-4c48-a4b7-2efe91432f00.png", "magenta"),
    ("Wacky - The Pain", "exec-c4b909f0-9877-4166-b06c-48ffdea39730.png", "magenta"),
    ("Wacky - Funt Lemons", "exec-f05c79f0-0d90-45bf-8b3c-36df8d28f100.png", "magenta"),
    ("Wacky - Double Trouble", "exec-d6c5e9b7-896b-400f-932e-61e21a6b8349.png", "magenta"),
    ("Wacky - One Man", "exec-efc84ee7-ca9f-405c-bdca-6216cde2730c.png", "magenta"),
    ("Wacky - Sogger Team", "exec-c4adfc21-97ff-4c8f-9307-e18c12181362.png", "magenta"),
    ("Wacky - Fastest Team Alive", "exec-b3ac1000-0d1f-4bb8-b265-24d08d04ec11.png", "green"),
    ("Wacky - Quickest Team Alive", "exec-618c86fa-53db-4f85-aa78-a93d4d2b45cf.png", "green"),
    ("Wacky - Rage Men", "exec-d94c52bc-d31d-476c-8b4b-1b6b5c4deba7.png", "magenta"),
    ("Wacky - True Random", "exec-44c37225-e2f3-4ced-9b34-c95439b8b7f7.png", "blue"),
    ("Wacky - Canucks 25-26", "exec-8855e68e-7302-47b1-8a5e-3ad19223b81a.png", "magenta"),
]


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    RAW.mkdir(parents=True, exist_ok=True)
    for name, source_name, _key in LOGOS:
        source = GENERATED / source_name
        if not source.is_file():
            raise FileNotFoundError(source)
        chroma_copy = RAW / f"{name}.png"
        chroma_copy.write_bytes(source.read_bytes())

        transparent = OUT / f"{name}.transparent.png"
        subprocess.run(
            [
                sys.executable,
                str(HELPER),
                "--input", str(chroma_copy),
                "--out", str(transparent),
                "--auto-key", "border",
                "--soft-matte",
                "--transparent-threshold", "12",
                "--opaque-threshold", "220",
                "--despill",
                "--force",
            ],
            check=True,
        )

        final = OUT / f"{name}.png"
        with Image.open(transparent) as image:
            rgba = image.convert("RGBA")
            rgba = rgba.resize((500, 500), Image.Resampling.LANCZOS)
            rgba.save(final, "PNG", optimize=True)
        transparent.unlink()

    print(f"Created {len(LOGOS)} logos in {OUT}")
    for name, _, _ in LOGOS:
        path = OUT / f"{name}.png"
        with Image.open(path) as image:
            alpha = image.getchannel("A")
            print(
                f"{path.name}: {image.size[0]}x{image.size[1]} {image.mode}; "
                f"alpha={alpha.getextrema()} transparent_corner={alpha.getpixel((0, 0))}"
            )


if __name__ == "__main__":
    main()
