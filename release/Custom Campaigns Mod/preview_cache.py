"""Shared preview-cache protocol helpers for the Campaign Creator.

The game DLL writes the cache.  This module deliberately contains only
filesystem/protocol code so it can be tested without importing tkinter or
starting the Creator.
"""

from __future__ import annotations

from dataclasses import dataclass, field
import hashlib
import os
import tempfile


# Must match PreviewAssets.ExporterVersion in the DLL. Bumping it invalidates
# every cached PNG, which is the point: a cache built by an older exporter can
# be silently wrong rather than merely incomplete.
PREVIEW_CACHE_VERSION = "10"


def preview_asset_filename(kind: str, role: str, field_name: str, value: str) -> str:
    """Return the collision-proof relative PNG name.

    The Creator reads paths out of manifest.tsv rather than recomputing them, so
    this exists to pin the DLL's naming scheme (PreviewAssets.AssetRelativePath)
    in something testable. Hashing the whole identity is required: values are
    paths like ``Faces/Golfers/Golfer_Lady`` — unusable as filenames, and their
    last segments are not unique across folders.
    """
    identity = f"{role}\n{field_name}\n{value}".encode("utf-8")
    digest = hashlib.sha256(identity).hexdigest()[:24]
    folder = "heads" if kind == "head" else "equipment"
    return f"{folder}/{digest}.png"


LAYER_CHANNELS = ("base", "primary", "secondary", "tertiary")


def layer_relative_path(role: str, field: str, value: str, channel: str) -> str:
    """Mirror of PreviewAssets.LayerRelativePath in the DLL.

    Layers are colour MASKS, not finished art. The exporter renders each piece
    ONCE, isolated, and splits that capture by the key colours baked into the
    atlas art (red/yellow/magenta = primary/secondary/tertiary), so the Creator
    can rebuild any colours with base + sum(mask * colour).
    """
    identity = f"{role}\n{field}\n{value}".encode("utf-8")
    digest = hashlib.sha256(identity).hexdigest()[:24]
    return f"layers/{digest}_{channel}.png"


@dataclass
class PreviewManifest:
    version: str | None = None
    stale: bool = False
    entries: dict[tuple[str, str, str], str] = field(default_factory=dict)
    missing: dict[tuple[str, str, str], str] = field(default_factory=dict)


def parse_preview_manifest(path: str, expected_version: str = PREVIEW_CACHE_VERSION) -> PreviewManifest:
    """Parse manifest.tsv without shortening or otherwise rewriting asset keys.

    ``entries`` contains only PNGs that exist. Missing files are recorded
    separately so a half-written or manually damaged cache fails soft.
    A cache from another exporter version is marked stale and never exposed.
    """
    result = PreviewManifest()
    if not os.path.isfile(path):
        return result

    root = os.path.dirname(path)
    rows: list[tuple[str, str, str, str]] = []
    try:
        with open(path, "r", encoding="utf-8") as stream:
            for raw in stream:
                line = raw.rstrip("\r\n")
                if not line or line.startswith("#"):
                    continue
                parts = line.split("\t")
                if parts[0] == "version" and len(parts) >= 2:
                    result.version = parts[1]
                    continue
                if parts[0] == "kind" or len(parts) < 5:
                    continue
                kind, role, field_name, value, relative_path = parts[:5]
                # Keep the complete value as part of the key. Two paths with the
                # same leaf name must remain two independent entries.
                rows.append((role, field_name, value, relative_path))
    except (OSError, UnicodeError):
        return result

    result.stale = result.version != expected_version
    if result.stale:
        return result

    for role, field_name, value, relative_path in rows:
        key = (role, field_name, value)
        full_path = os.path.normpath(os.path.join(root, relative_path.replace("/", os.sep)))
        if os.path.isfile(full_path):
            result.entries[key] = full_path
        else:
            result.missing[key] = full_path
    return result


def atomic_write_text(path: str, text: str) -> None:
    """Replace a UTF-8 text file atomically within its destination directory."""
    directory = os.path.dirname(path)
    os.makedirs(directory, exist_ok=True)
    fd, temporary = tempfile.mkstemp(prefix=os.path.basename(path) + ".", suffix=".tmp", dir=directory)
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as stream:
            stream.write(text)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    except Exception:
        try:
            os.unlink(temporary)
        except OSError:
            pass
        raise
