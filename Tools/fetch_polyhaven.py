#!/usr/bin/env python3
"""Downloads CC0 materials and models from Poly Haven (polyhaven.com) for the Museum of Indian History.

Run from the project folder in macOS Terminal:
    python3 Tools/fetch_polyhaven.py

Textures: 2K colour + normal maps, saved as Assets/_Museum/PolyHaven/Textures/<key>_diff.jpg / <key>_nor.jpg
Models:   FBX (1K textures) in Assets/_Museum/PolyHaven/Models/<slug>/
Everything on Poly Haven is CC0 (public domain): free for any use, no credit required.
Re-running skips files that already exist.
"""
import json, os, subprocess, sys, time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASE = os.path.join(ROOT, "Assets", "_Museum", "PolyHaven")
UA = "IndianHistoryMuseumVR/1.0 (educational VR museum; Unity project)"

# key -> candidate slugs (first one that exists is used)
TEXTURES = {
    "stone_buff":    ["sandstone_blocks_05", "large_sandstone_blocks_01", "sandstone_blocks_04"],
    "stone_buff2":   ["sandstone_blocks_04", "seaworn_sandstone_brick", "large_sandstone_blocks_01"],
    "stone_red":     ["sandstone_brick_wall_01", "castle_brick_02_red", "red_bricks_04"],
    "stone_castle":  ["castle_wall_varriation", "castle_wall_slates", "medieval_blocks_03"],
    "brick_red":     ["medieval_red_brick", "large_red_bricks", "red_brick"],
    "floor_slabs":   ["floor_tiles_04", "large_sandstone_blocks_01", "floor_pattern_02"],
    "floor_marble":  ["floor_tiles_02", "marble_01", "floor_tiles_04"],
    "floor_granite": ["granite_tile_03", "floor_tiles_04"],
    "floor_pattern": ["floor_pattern_02", "floor_tiles_04"],
    "floor_concrete":["concrete_floor_worn_001", "concrete_floor_02", "concrete_floor"],
    "plaster":       ["beige_wall_001", "beige_wall_002", "concrete_wall_004"],
    "plaster_rough": ["beige_wall_002", "clay_plaster", "beige_wall_001"],
    "marble":        ["marble_01", "floor_tiles_02"],
    "wood":          ["wood_table_001", "fine_grained_wood", "dark_wooden_planks", "diagonal_parquet"],
    "granite":       ["granite_wall", "granite_tile_03"],
}
MODELS = ["Chandelier_01", "Chandelier_02", "Chandelier_03", "Lantern_01",
          "brass_vase_01", "brass_vase_02", "ceramic_vase_01", "ceramic_vase_02", "ceramic_vase_03"]


def curl(url, out=None, tries=6):
    wait = 5
    for _ in range(tries):
        target = out or os.path.join(BASE, ".tmp")
        r = subprocess.run(["curl", "-sSL", "--max-time", "180", "-A", UA, "-o", target, "-w", "%{http_code}", url], capture_output=True)
        code = r.stdout.decode().strip()
        if r.returncode == 0 and code == "200":
            return None if out else open(target, "rb").read()
        if code == "404": return None
        print(f"   busy (HTTP {code or r.returncode}), waiting {wait}s...", flush=True)
        time.sleep(wait); wait = min(wait * 2, 120)
    return None


def files(slug):
    data = curl(f"https://api.polyhaven.com/files/{slug}")
    try: return json.loads(data) if data else None
    except ValueError: return None


def pick(d, *keys):
    for k in keys:
        if isinstance(d, dict) and k in d: return d[k]
    return None


def find_format(d, fmt):
    """The fbx entry ({"1k": {"fbx": {...}}, ...}), wherever it sits in the files JSON."""
    if not isinstance(d, dict): return None
    v = d.get(fmt)
    if isinstance(v, dict) and any(k in v for k in ("1k", "2k", "4k")): return v
    for child in d.values():
        r = find_format(child, fmt)
        if r: return r
    return None


def main():
    os.makedirs(os.path.join(BASE, "Textures"), exist_ok=True)
    os.makedirs(os.path.join(BASE, "Models"), exist_ok=True)
    manifest_path = os.path.join(BASE, "manifest.json")
    manifest = json.load(open(manifest_path)) if os.path.exists(manifest_path) else {"textures": {}, "models": []}
    for key, slugs in TEXTURES.items():
        diff = os.path.join(BASE, "Textures", f"{key}_diff.jpg")
        nor = os.path.join(BASE, "Textures", f"{key}_nor.jpg")
        if os.path.exists(diff) and os.path.exists(nor):
            print(f"{key}: already have {manifest['textures'].get(key, '?')}"); continue
        for slug in slugs:
            f = files(slug); time.sleep(0.5)
            if not f: print(f"   {slug}: not found"); continue
            d = pick(f, "Diffuse", "diff", "diffuse"); n = pick(f, "nor_gl", "Normal", "nor")
            d = pick(pick(pick(d, "2k", "1k"), "jpg", "png"), "url")
            n = pick(pick(pick(n, "2k", "1k"), "jpg", "png"), "url")
            if not d or not n: print(f"   {slug}: no 2K jpg maps"); continue
            curl(d, diff); curl(n, nor)
            manifest["textures"][key] = slug
            print(f"{key}: {slug}"); break
        else:
            print(f"{key}: NONE of {slugs} available (a plain colour will be used)")
        json.dump(manifest, open(manifest_path, "w"), indent=1)
    for slug in MODELS:
        folder = os.path.join(BASE, "Models", slug)
        fbx = os.path.join(folder, slug + ".fbx")
        if os.path.exists(fbx): print(f"{slug}: already have"); continue
        f = files(slug); time.sleep(0.5)
        entry = pick(pick(find_format(f, "fbx"), "1k", "2k"), "fbx") if f else None
        if not entry or "url" not in entry: print(f"{slug}: no FBX available"); continue
        os.makedirs(folder, exist_ok=True)
        for rel, inc in (entry.get("include") or {}).items():
            p = os.path.join(folder, rel); os.makedirs(os.path.dirname(p), exist_ok=True)
            if not os.path.exists(p): curl(inc["url"], p)
        curl(entry["url"], fbx)
        if slug not in manifest["models"]: manifest["models"].append(slug)
        json.dump(manifest, open(manifest_path, "w"), indent=1)
        print(f"{slug}: model downloaded")
    manifest["complete"] = True                       # tells the Unity builder the download is finished
    json.dump(manifest, open(manifest_path, "w"), indent=1)
    tmp = os.path.join(BASE, ".tmp")
    if os.path.exists(tmp): os.remove(tmp)
    print("\nDONE. Poly Haven assets are in Assets/_Museum/PolyHaven (all CC0).")


if __name__ == "__main__":
    main()
