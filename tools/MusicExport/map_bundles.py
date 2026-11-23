"""Resolve exported YooAsset bundle names to verified local bundle files.

Reads the length-prefixed bundle records in YooAsset 2.0.0 manifests without
loading game code. Every accepted file must match manifest size, MD5 and CRC32.
Only Python's standard library is required.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import zlib

DEFAULT_GAME = Path("D:/TapTap/PC Games/176228/launcher_Data")
WORKSPACE = Path(__file__).resolve().parents[2]


def read_string(data: bytes, offset: int) -> tuple[str, int]:
    length = struct.unpack_from("<H", data, offset)[0]
    start = offset + 2
    end = start + length
    if end > len(data):
        raise ValueError("Truncated manifest string")
    return data[start:end].decode("utf-8"), end


def bundle_record(data: bytes, name: str) -> dict | None:
    # Manifest bundle entry: ushort UTF-8 name length, name, uint32 name ID,
    # ushort + UTF-8 file hash, ushort + UTF-8 CRC32, int64 file size.
    # The remaining dependency/flag fields are not needed for path resolution.
    encoded = name.encode("utf-8")
    needle = struct.pack("<H", len(encoded)) + encoded
    offset = data.find(needle)
    if offset < 0:
        return None
    if data.find(needle, offset + 1) >= 0:
        raise ValueError(f"Ambiguous bundle record: {name}")
    position = offset + len(needle) + 4
    file_hash, position = read_string(data, position)
    crc, position = read_string(data, position)
    size = struct.unpack_from("<q", data, position)[0]
    if not re.fullmatch(r"[0-9a-f]{32}", file_hash):
        raise ValueError(f"Invalid bundle hash for {name}: {file_hash!r}")
    if not re.fullmatch(r"[0-9a-fA-F]{8}", crc) or size < 0:
        raise ValueError(f"Invalid bundle metadata for {name}")
    # YooAsset stores the CRC digest as a hex string of little-endian bytes.
    return {"hash": file_hash, "crc": int.from_bytes(bytes.fromhex(crc), "little"), "size": size}


def verify(path: Path, record: dict) -> bool:
    if path.stat().st_size != record["size"]:
        return False
    digest = hashlib.md5()
    crc = 0
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
            crc = zlib.crc32(block, crc)
    return digest.hexdigest() == record["hash"] and crc == record["crc"]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-data", type=Path, default=DEFAULT_GAME)
    parser.add_argument("--catalog", type=Path,
                        default=WORKSPACE / "artifacts/music-export-20261122/catalog.json")
    parser.add_argument("--output", type=Path,
                        default=WORKSPACE / "artifacts/music-export-20261122/disk-map.json")
    args = parser.parse_args()
    catalog = json.loads(args.catalog.read_text(encoding="utf-8-sig"))
    builtin = args.game_data / "StreamingAssets/yoo/DefaultPackage"
    cache = args.game_data / "yoo/DefaultPackage/CacheFiles"
    manifests = list((args.game_data / "yoo/DefaultPackage/ManifestFiles").glob("*.bytes"))
    manifests += list(builtin.glob("PackageManifest_*.bytes"))
    # Timestamps are part of manifest filenames, so descending filename order
    # selects the newest package version first. Builtin manifests are fallback.
    manifests.sort(key=lambda path: path.name, reverse=True)
    if not manifests:
        raise FileNotFoundError("No package manifests found")
    loaded = []
    for path in manifests:
        data = path.read_bytes()
        if not data.startswith(b"OOY\0"):
            raise ValueError(f"Unexpected manifest signature: {path}")
        version, _ = read_string(data, 4)
        if version != "2.0.0":
            raise ValueError(f"Unsupported manifest format {version}: {path}")
        loaded.append((path, data))
    result = []
    sources: dict[str, int] = {}
    for item in catalog:
        resolved = None
        seen_hashes = set()
        for manifest, data in loaded:
            record = bundle_record(data, item["bundle"])
            if record is None or record["hash"] in seen_hashes:
                continue
            seen_hashes.add(record["hash"])
            file_hash = record["hash"]
            candidates = [builtin / f"{file_hash}.bundle",
                          cache / file_hash[:2] / file_hash / "__data"]
            for candidate in candidates:
                if candidate.is_file() and verify(candidate, record):
                    resolved = candidate.resolve().as_posix()
                    sources[manifest.name] = sources.get(manifest.name, 0) + 1
                    break
            if resolved:
                break
        result.append({"index": item["index"], "bundle": item["bundle"], "path": resolved})
        if not resolved:
            print(f"MISSING {item['index']}: {item['bundle']}")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    matched = sum(row["path"] is not None for row in result)
    print(f"Matched {matched}/{len(result)} (size + MD5 + CRC32 verified)")
    print(f"Manifest sources: {json.dumps(sources, ensure_ascii=False)}")
    print(f"Output: {args.output}")
    return 0 if matched == len(result) else 1


if __name__ == "__main__":
    raise SystemExit(main())
