"""
Reference CMM driver using Pillow's ImageCms (lcms2-backed).

Invocation:
    python lcms_reference.py <src_profile> <dst_profile> <intent> <bpc 0|1>

Stdin: lines of N floats in [0, 1] (whitespace separated), one pixel per line.
       N = channel count of source profile (3 = RGB, 4 = CMYK, 1 = Gray).
Stdout: corresponding output pixels — M floats in [0, 1] per line.
       M = channel count of destination profile.

Pillow's color modes are 8-bit per channel for RGB/CMYK/Lab — this driver
exchanges values via uint8 internally, which limits precision to ~1/255.
That is well below the algorithmic-bug threshold we're checking for
(systematic deviations of ΔE > 1 between CMMs).

Intent: 0=Perceptual, 1=Relative Colorimetric, 2=Saturation, 3=Absolute Colorimetric.
BPC: 1 to enable Black Point Compensation, 0 to disable.
"""
from __future__ import annotations

import sys

from PIL import Image, ImageCms

_CHANNELS_FOR_MODE = {"RGB": 3, "CMYK": 4, "L": 1, "LAB": 3}


def _pillow_mode_for(color_space: str) -> str:
    color_space = color_space.strip().upper()
    if color_space.startswith("RGB"):  return "RGB"
    if color_space.startswith("CMYK"): return "CMYK"
    if color_space.startswith("GRAY"): return "L"
    if color_space.startswith("LAB"):  return "LAB"
    raise ValueError(f"Unsupported colour space '{color_space}'")


def main() -> int:
    if len(sys.argv) != 5:
        sys.stderr.write("usage: lcms_reference.py <src.icc> <dst.icc> <intent 0-3> <bpc 0|1>\n")
        return 2
    src_path, dst_path, intent_str, bpc_str = sys.argv[1:]
    intent = int(intent_str)
    bpc = bool(int(bpc_str))

    src_profile = ImageCms.getOpenProfile(src_path)
    dst_profile = ImageCms.getOpenProfile(dst_path)
    src_mode = _pillow_mode_for(src_profile.profile.xcolor_space)
    dst_mode = _pillow_mode_for(dst_profile.profile.xcolor_space)

    n_in = _CHANNELS_FOR_MODE[src_mode]
    n_out = _CHANNELS_FOR_MODE[dst_mode]

    # cmsFLAGS_BLACKPOINTCOMPENSATION = 0x2000. The Pillow public symbol moved over time
    # (ImageCms.FLAGS vs ImageCms._FLAGS), so use the integer literal directly.
    flags = 0x2000 if bpc else 0
    transform = ImageCms.buildTransform(
        src_profile, dst_profile, src_mode, dst_mode,
        renderingIntent=intent, flags=flags)

    pixels = []
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        parts = [float(x) for x in line.split()]
        if len(parts) != n_in:
            sys.stderr.write(f"expected {n_in} values per line, got {len(parts)}\n")
            return 3
        pixels.append(tuple(parts))

    if not pixels:
        return 0

    # Pack as uint8 image with width = len(pixels), height = 1.
    raw_in = bytearray(len(pixels) * n_in)
    for i, pix in enumerate(pixels):
        for c in range(n_in):
            v = max(0.0, min(1.0, pix[c]))
            raw_in[i * n_in + c] = int(round(v * 255))
    img_in = Image.frombytes(src_mode, (len(pixels), 1), bytes(raw_in))
    img_out = ImageCms.applyTransform(img_in, transform)
    raw_out = img_out.tobytes()

    for i in range(len(pixels)):
        vals = [raw_out[i * n_out + c] / 255.0 for c in range(n_out)]
        sys.stdout.write(" ".join(f"{v:.6f}" for v in vals) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
