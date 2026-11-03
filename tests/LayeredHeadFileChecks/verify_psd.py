"""Check actual PSD exports using psd-tools, independent of the C# writer.

Usage: python verify_psd.py FIXTURE_DIRECTORY TASK_LOCAL_PYTHON_DEPS
Success is silent; the JSON receipt records the independent check count.
"""

import json
from pathlib import Path
import sys

fixtures = Path(sys.argv[1])
sys.path.insert(0, sys.argv[2])
import psd_tools
from psd_tools import PSDImage

checks = 0


def check(condition, message):
    global checks
    if not condition:
        raise AssertionError(message)
    checks += 1


def pixel_values(image):
    data = image.tobytes()
    return [tuple(data[i:i + 4]) for i in range(0, len(data), 4)]


def open_psd(name, size, names, visibility):
    path = fixtures / (name + ".psd")
    check(path.is_file(), name + ": exported file exists")
    psd = PSDImage.open(path)
    check(psd.size == size, name + ": correct canvas size")
    check(psd.depth == 8 and psd.channels == 4, name + ": 8-bit RGBA")
    check([layer.name for layer in psd] == names, name + ": bottom-to-top layer names")
    check([layer.visible for layer in psd] == visibility, name + ": layer visibility")
    check(psd._record.layer_and_mask_information.layer_info.layer_count == -len(names),
          name + ": merged alpha is declared")
    check(all(layer.size == size for layer in psd), name + ": editable full-canvas layers")
    return psd


def pixels_match(image, expected, tolerance, message):
    check(image is not None and image.mode == "RGBA", message + ": RGBA image")
    actual = pixel_values(image)
    check(len(actual) == len(expected), message + ": pixel count")
    for index, (got, want) in enumerate(zip(actual, expected)):
        check(abs(got[3] - want[3]) <= tolerance, f"{message}: alpha {index}: {got} != {want}")
        if want[3] != 0:
            check(all(abs(got[c] - want[c]) <= tolerance for c in range(3)),
                  f"{message}: RGB {index}: {got} != {want}")


names = ["Base head", "Face", "Hair", "Guides"]
psd = open_psd("four-layers", (3, 2), names, [True, True, True, False])
base = [(200, 50, 10, 255), (40, 80, 120, 128), (12, 34, 56, 0),
        (10, 20, 30, 255), (80, 120, 160, 64), (240, 120, 60, 128)]
face = [(20, 220, 40, 128), (0, 0, 0, 0), (1, 250, 128, 255),
        (0, 0, 0, 0), (0, 0, 0, 0), (0, 0, 0, 0)]
hair = [(0, 0, 0, 0)] * 3 + [(0, 0, 255, 255), (200, 40, 80, 128), (0, 0, 0, 0)]
for layer, expected in zip(psd, [base, face, hair, [(255, 0, 255, 255)] * 6]):
    check(pixel_values(layer.topil(apply_icc=False)) == expected,
          layer.name + ": exact editable RGBA, including RGB below zero alpha")
merged = [(110, 135, 25, 255), (40, 80, 120, 128), (1, 250, 128, 255),
          (0, 0, 255, 255), (176, 56, 96, 160), (240, 120, 60, 128)]
pixels_match(psd.topil(apply_icc=False), merged, 2, "cached merged image excludes guides")
pixels_match(psd.composite(ignore_preview=True, apply_icc=False), merged, 1,
             "independent recomposition preserves order and excludes guides")
psd[-1].visible = True
# psd-tools truncates floating alpha 0.99999994 to 254; layer and saved composite
# checks remain exact, while independent re-composition permits one quantization step.
pixels_match(psd.composite(ignore_preview=True, apply_icc=False), [(255, 0, 255, 255)] * 6, 1,
             "guide layer is independently editable and visible when enabled")

visible = open_psd("visible-guides", (3, 2), names, [True] * 4)
pixels_match(visible.topil(apply_icc=False), [(255, 0, 255, 255)] * 6, 0,
             "visible guide layer appears in saved composite")
hidden = open_psd("all-hidden", (3, 2), names, [False] * 4)
pixels_match(hidden.topil(apply_icc=False), [(0, 0, 0, 0)] * 6, 0,
             "all hidden layers produce transparent merged image")

clear = open_psd("transparent-color", (1, 1), ["Face – 顔 😀"], [True])
check(clear[0].topil(apply_icc=False).getpixel((0, 0)) == (22, 44, 66, 0),
      "fully transparent straight RGB is retained for later editing")
pixels_match(clear.topil(apply_icc=False), [(0, 0, 0, 0)], 0, "transparent file opens with alpha")
open_psd("long-name", (1, 1), ["a" * 300 + "顔"], [True])
premul = open_psd("premultiplied-bgra", (1, 1), ["Base head"], [True])
pixels_match(premul[0].topil(apply_icc=False), [(90, 60, 30, 85)], 0,
             "premultiplied BGRA input converts to straight RGBA")

patterns = open_psd("packbits-boundaries", (257, 3), ["Patterns"], [True])
expected = []
for y in range(3):
    for x in range(257):
        red = x % 256 if y == 0 else (7 if x < 127 else 8 if x < 130 else 9) if y == 1 else x % 2
        expected.append((red, x % 5, 255 - x % 256, 255))
check(pixel_values(patterns[0].topil(apply_icc=False)) == expected,
      "PackBits literal and run boundaries decode independently")
check(pixel_values(patterns.topil(apply_icc=False)) == expected,
      "merged PackBits channel rows decode independently")

starter = open_psd("starter-512", (512, 512), names, [True, True, True, False])
check(starter.topil(apply_icc=False).getpixel((511, 511)) == (211, 177, 134, 255),
      "full-size starter composite reaches the last pixel")
check(all(value == (0, 0) for value in starter[1].topil().getextrema()),
      "empty Face layer is fully transparent")
check(all(value == (0, 0) for value in starter[2].topil().getextrema()),
      "empty Hair layer is fully transparent")
check((fixtures / "starter-512.psd").stat().st_size < 128 * 1024, "512px starter uses compact encoding")

# Re-save through the independent reader and open its result, proving this is an
# editable layered document rather than only a flattened preview it can decode.
roundtrip = fixtures / "psd-tools-roundtrip.psd"
starter.save(roundtrip)
again = PSDImage.open(roundtrip)
check([layer.name for layer in again] == names, "independent save preserves editable layer names")
check([layer.visible for layer in again] == [True, True, True, False], "independent save preserves hidden guides")
check(again.topil(apply_icc=False).getpixel((256, 256)) == (211, 177, 134, 255),
      "independent save preserves cached composite")

(fixtures / "psd-tools-results.json").write_text(json.dumps({
    "reader": "psd-tools", "version": psd_tools.__version__, "checks": checks,
    "fixtures": 8, "roundtrip": str(roundtrip),
}, indent=2), encoding="utf-8")
