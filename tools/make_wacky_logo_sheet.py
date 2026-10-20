from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "assets" / "Wacky Crazy Funtime Logos"
OUT = SOURCE / "Wacky Logos Preview.png"

files = sorted(
    p for p in SOURCE.glob("Wacky - *.png")
    if ".transparent." not in p.name and p.name != OUT.name
)
tile_w, tile_h = 300, 340
cols = 4
rows = (len(files) + cols - 1) // cols
sheet = Image.new("RGB", (cols * tile_w, rows * tile_h), "#20242b")
draw = ImageDraw.Draw(sheet)

for index, path in enumerate(files):
    image = Image.open(path).convert("RGBA").resize((260, 260), Image.Resampling.LANCZOS)
    checker = Image.new("RGBA", image.size, "#ececec")
    cdraw = ImageDraw.Draw(checker)
    size = 20
    for y in range(0, image.height, size):
        for x in range(0, image.width, size):
            if (x // size + y // size) % 2:
                cdraw.rectangle((x, y, x + size - 1, y + size - 1), fill="#c8c8c8")
    checker.alpha_composite(image)
    x = (index % cols) * tile_w + 20
    y = (index // cols) * tile_h + 15
    sheet.paste(checker.convert("RGB"), (x, y))
    label = path.stem.replace("Wacky - ", "")
    draw.text((x, y + 275), label, fill="white")

sheet.save(OUT, "PNG", optimize=True)
print(OUT)
