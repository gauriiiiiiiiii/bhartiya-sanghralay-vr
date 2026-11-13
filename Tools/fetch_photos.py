#!/usr/bin/env python3
"""Downloads exhibit photos for the Museum of Indian History from Wikimedia Commons.

Run from the project folder in macOS Terminal:
    python3 Tools/fetch_photos.py            # find candidates (once) and download the chosen photo for every exhibit
    python3 Tools/fetch_photos.py S02_11     # only these ids (comma separated)

What it does
  1. For each exhibit it searches Commons with the queries in Tools/photo_sourcing.json and keeps up to 4 candidates
     with reusable licences (public domain, CC0, CC BY, CC BY-SA; no NC/ND/fair use), saved as small previews in
     Tools/photo_candidates/<id>_1..4.jpg plus <id>.json with their details.
  2. It downloads the chosen candidate (default: the first; override in Tools/photo_choices.json) at 1600 px to
     Assets/_Museum/Photos/<id>.jpg, and writes Docs/photo_credits.csv (author, licence, link) for attribution.
Re-running skips work already done, so it can be interrupted and resumed.
"""
import csv, html, json, os, re, subprocess, sys, time, urllib.parse

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TOOLS = os.path.join(ROOT, "Tools")
CAND = os.path.join(TOOLS, "photo_candidates")
PHOTOS = os.path.join(ROOT, "Assets", "_Museum", "Photos")
CREDITS = os.path.join(ROOT, "Docs", "photo_credits.csv")
API = "https://commons.wikimedia.org/w/api.php"
UA = "BhartiyaSanghralayVR/1.0 (educational VR museum; Unity project)"
OK_LICENCE = re.compile(r"(public domain|^pd|cc0|cc[ -]by(-sa)?[ -]?\d|cc[ -]by(-sa)?$|attribution)", re.I)
BAD_LICENCE = re.compile(r"(\bnc\b|non-?commercial|\bnd\b|no ?deriv|fair use|copyrighted)", re.I)


def curl(url, out=None, tries=6):
    """GET with retries. Wikimedia answers 429 / an HTML error page when we go too fast: back off and retry."""
    wait = 5
    for i in range(tries):
        target = out or os.path.join(TOOLS, ".curl_tmp")
        r = subprocess.run(["curl", "-sSL", "--max-time", "60", "-A", UA, "-o", target, "-w", "%{http_code}", url], capture_output=True)
        code = r.stdout.decode().strip()
        if r.returncode == 0 and code == "200":
            if out: return b""
            data = open(target, "rb").read()
            if data[:1] in (b"{", b"["): return data
        print(f"   busy (HTTP {code or r.returncode}), waiting {wait}s...", flush=True)
        time.sleep(wait); wait = min(wait * 2, 120)
    raise RuntimeError(f"gave up after {tries} tries: {url[:120]}")


def strip(s):
    return html.unescape(re.sub(r"<[^>]+>", "", s or "")).strip()


def search(query):
    params = {"action": "query", "format": "json", "generator": "search", "gsrsearch": f"{query} filetype:bitmap",
              "gsrnamespace": "6", "gsrlimit": "10", "prop": "imageinfo",
              "iiprop": "url|size|mime|extmetadata", "iiurlwidth": "1600"}
    data = json.loads(curl(API + "?" + urllib.parse.urlencode(params)) or b"{}")
    pages = sorted(data.get("query", {}).get("pages", {}).values(), key=lambda p: p.get("index", 99))
    out = []
    for p in pages:
        ii = (p.get("imageinfo") or [{}])[0]
        meta = ii.get("extmetadata", {})
        lic = strip(meta.get("LicenseShortName", {}).get("value", ""))
        if not lic or BAD_LICENCE.search(lic) or not OK_LICENCE.search(lic): continue
        if ii.get("mime") not in ("image/jpeg", "image/png", "image/tiff", "image/webp"): continue
        if min(ii.get("width", 0), ii.get("height", 0)) < 500: continue
        out.append({"title": p["title"], "page": ii.get("descriptionurl", ""), "thumb": ii.get("thumburl") or ii.get("url"),
                    "width": ii.get("width"), "height": ii.get("height"), "licence": lic,
                    "licence_url": strip(meta.get("LicenseUrl", {}).get("value", "")),
                    "author": strip(meta.get("Artist", {}).get("value", ""))[:200],
                    "description": strip(meta.get("ImageDescription", {}).get("value", ""))[:300]})
    return out


def preview_url(thumb):
    return re.sub(r"/\d+px-", "/480px-", thumb) if "/thumb/" in thumb else thumb


def is_image(path):
    try:
        with open(path, "rb") as f: head = f.read(4)
        return head[:3] == b"\xff\xd8\xff" or head == b"\x89PNG"
    except OSError:
        return False


def fetch_previews(cid, cands):
    for i, c in enumerate(cands, 1):
        p = os.path.join(CAND, f"{cid}_{i}.jpg")
        if is_image(p): continue
        try: curl(preview_url(c["thumb"]), p); time.sleep(0.5)
        except Exception as e: print("   preview failed:", e)


def find_candidates(row):
    meta_path = os.path.join(CAND, row["id"] + ".json")
    if os.path.exists(meta_path):
        cached = json.load(open(meta_path))
        if cached:                                     # an empty cache came from an earlier failed run: search again
            fetch_previews(row["id"], cached)
            return cached
    seen, cands, failed = set(), [], False
    for q in row["queries"]:
        try: found = search(q)
        except Exception as e: print("   search failed:", e); found = []; failed = True
        for c in found:
            if c["title"] in seen: continue
            seen.add(c["title"]); c["query"] = q; cands.append(c)
        time.sleep(1.0)                                 # stay well under Wikimedia's rate limit
        if len(cands) >= 4: break
    cands = cands[:4]
    fetch_previews(row["id"], cands)
    if cands or not failed:                             # never cache a result that came from failed searches
        json.dump(cands, open(meta_path, "w"), indent=1, ensure_ascii=False)
    return cands


def main():
    os.makedirs(CAND, exist_ok=True); os.makedirs(PHOTOS, exist_ok=True); os.makedirs(os.path.dirname(CREDITS), exist_ok=True)
    rows = json.load(open(os.path.join(TOOLS, "photo_sourcing.json"), encoding="utf-8"))
    only = set(sys.argv[1].split(",")) if len(sys.argv) > 1 else None
    choices_path = os.path.join(TOOLS, "photo_choices.json")
    choices = json.load(open(choices_path)) if os.path.exists(choices_path) else {}
    credits = {}
    if os.path.exists(CREDITS):
        for r in csv.DictReader(open(CREDITS, encoding="utf-8")): credits[r["id"]] = r
    done = 0
    for n, row in enumerate(rows, 1):
        cid = row["id"]
        if only and cid not in only: continue
        cands = find_candidates(row)
        choice = choices.get(cid, 1)                    # 1-4 = candidate number, 0 = no photo, "File:..." = a specific Commons file
        if isinstance(choice, str) and choice.startswith("File:"):
            hits = [c for c in cands if c["title"] == choice] or [c for c in search(choice.replace("File:", "")) if c["title"] == choice]
            pick = hits[0] if hits else None
        else:
            pick = cands[choice - 1] if isinstance(choice, int) and 0 < choice <= len(cands) else None
        out = os.path.join(PHOTOS, cid + ".jpg")
        if pick is None:
            if os.path.exists(out) and credits.get(cid, {}).get("file"): os.remove(out)
            credits.pop(cid, None)
            print(f"[{n}/{len(rows)}] {cid}: no photo ({len(cands)} candidates)")
            continue
        if credits.get(cid, {}).get("file") != pick["title"] or not is_image(out):
            tmp = out + ".part"
            curl(pick["thumb"], tmp)
            if pick["thumb"].lower().endswith((".png", ".tif", ".tiff", ".webp")) or open(tmp, "rb").read(4)[:4] == b"\x89PNG":
                subprocess.run(["sips", "-s", "format", "jpeg", tmp, "--out", out], capture_output=True); os.remove(tmp)
            else:
                os.replace(tmp, out)
            credits[cid] = {"id": cid, "file": pick["title"], "author": pick["author"], "licence": pick["licence"],
                            "licence_url": pick["licence_url"], "source": pick["page"]}
            time.sleep(0.3)
        done += 1
        print(f"[{n}/{len(rows)}] {cid}: {pick['title'][5:70]}  ({pick['licence']})")
        if done % 20 == 0: write_credits(credits)
    write_credits(credits)
    print(f"\nDONE: {sum(1 for r in rows if os.path.exists(os.path.join(PHOTOS, r['id'] + '.jpg')))} photos in Assets/_Museum/Photos, credits in Docs/photo_credits.csv")


def write_credits(credits):
    with open(CREDITS, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=["id", "file", "author", "licence", "licence_url", "source"])
        w.writeheader()
        for k in sorted(credits): w.writerow(credits[k])


if __name__ == "__main__":
    main()
