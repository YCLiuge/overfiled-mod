"""Independently decode fixtures, validate every PNG chunk CRC, and compare with Pillow.

Run after `dotnet run --project tools/PaintingExportVerification -c Release`.
Pass the printed manifest path as the sole argument.
"""
import base64
import binascii
import io
import json
from pathlib import Path
import struct
import sys
import zlib

from PIL import Image


def validate(fixture):
    png = Path(fixture["path"]).read_bytes()
    assert png[:8] == b"\x89PNG\r\n\x1a\n"
    chunks = []
    compressed = bytearray()
    position = 8
    while position < len(png):
        length = struct.unpack_from(">I", png, position)[0]
        kind = png[position + 4:position + 8]
        body = png[position + 8:position + 8 + length]
        assert len(body) == length
        stored_crc = struct.unpack_from(">I", png, position + 8 + length)[0]
        assert stored_crc == (binascii.crc32(kind + body) & 0xffffffff), kind
        chunks.append(kind)
        if kind == b"IHDR":
            assert struct.unpack(">IIBBBBB", body) == (fixture["width"], fixture["height"], 8, 6, 0, 0, 0)
        if kind == b"IDAT":
            compressed.extend(body)
        if kind == b"IEND":
            assert not body
        position += length + 12
    assert position == len(png)
    assert chunks == [b"IHDR", b"IDAT", b"IEND"]
    decompressor = zlib.decompressobj()
    raw = decompressor.decompress(compressed) + decompressor.flush()
    assert decompressor.eof and not decompressor.unused_data
    stride = fixture["width"] * 4
    height = fixture["height"]
    source = base64.b64decode(fixture["bottomUp"])
    expected = b"".join(source[y * stride:(y + 1) * stride] for y in range(height - 1, -1, -1))
    assert len(raw) == height * (stride + 1)
    decoded = bytearray()
    for y in range(height):
        start = y * (stride + 1)
        assert raw[start] == 0, "Expected filter 0"
        decoded.extend(raw[start + 1:start + 1 + stride])
    assert bytes(decoded) == expected, fixture["label"]
    with Image.open(io.BytesIO(png)) as image:
        image.load()
        assert image.mode == "RGBA"
        assert image.size == (fixture["width"], height)
        assert image.tobytes() == expected, fixture["label"]


fixtures = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
for fixture in fixtures:
    validate(fixture)
print(f"PASS: {len(fixtures)} PNG files independently parsed/decompressed with CRC/filter/orientation/RGBA checks and Pillow roundtrips.")
