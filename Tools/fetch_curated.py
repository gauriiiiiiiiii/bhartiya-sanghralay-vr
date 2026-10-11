#!/usr/bin/env python3
"""Downloads the photographs for the curated museum objects (Museum of Indian History, v5).

Run from the project folder in macOS Terminal:
    python3 Tools/fetch_curated.py

For each object it takes the lead image of the named Wikipedia article (when that image is freely licensed on
Wikimedia Commons), otherwise the best freely licensed result of a Commons search. Only public-domain, CC0,
CC BY and CC BY-SA images are used. Photos go to Assets/_Museum/Curated/Photos/<id>.jpg (1920 px), the credits to
Docs/curated_credits.csv, and 4 alternatives per object to Tools/curated_candidates/ for review.
Override any choice in Tools/curated_choices.json: {"W1_03": "File:Some file.jpg"} and run again.
"""
import json, os, re, subprocess, sys, time, urllib.parse, csv

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, "Assets", "_Museum", "Content", "curated.json")
OUT = os.path.join(ROOT, "Assets", "_Museum", "Curated", "Photos")
CAND = os.path.join(ROOT, "Tools", "curated_candidates")
CREDITS = os.path.join(ROOT, "Docs", "curated_credits.csv")
CHOICES = os.path.join(ROOT, "Tools", "curated_choices.json")
UA = "IndianHistoryMuseumVR/1.0 (educational VR museum; contact via project)"
OK = re.compile(r"^(public domain|pd|cc0|cc[- ]by(-sa)?([- ]\d(\.\d)?)?|cc[- ]by[- ]sa[- ][\d.]+.*|attribution|gfdl.*cc[- ]by.*)", re.I)
BAD = re.compile(r"\b(nc|nd|non-?commercial|no ?deriv|fair use|non-free)\b", re.I)


def get(url, out=None, tries=6):
    wait = 4
    for _ in range(tries):
        target = out or os.path.join(CAND, ".tmp")
        r = subprocess.run(["curl", "-sSL", "--max-time", "90", "-A", UA, "-o", target, "-w", "%{http_code}", url], capture_output=True)
        code = r.stdout.decode().strip()
        if r.returncode == 0 and code == "200":
            if not out: return open(target, "rb").read()
            head = open(target, "rb").read(8)
            if head[:3] == b"\xff\xd8\xff": return True
            if head[:8] == b"\x89PNG\r\n\x1a\n" or head[:4] in (b"GIF8", b"RIFF") or head[:2] in (b"II", b"MM"):
                # not a JPEG: convert in place with macOS sips (keeps the .jpg name Unity expects)
                conv = target + ".conv.jpg"
                try:
                    from PIL import Image
                    im = Image.open(target)
                    if im.mode in ("RGBA", "LA", "P"):
                        im = im.convert("RGBA"); bg = Image.new("RGB", im.size, "white"); bg.paste(im, mask=im.split()[-1]); im = bg
                    im.convert("RGB").save(conv, "JPEG", quality=90); os.replace(conv, target); return True
                except Exception:
                    if subprocess.run(["sips", "-s", "format", "jpeg", target, "--out", conv], capture_output=True).returncode == 0:
                        os.replace(conv, target); return True
            code = "bad-content"
        if code in ("404", "400"): return None
        print(f"   busy (HTTP {code or r.returncode}), waiting {wait}s", flush=True)
        time.sleep(wait); wait = min(wait * 2, 90)
    return None


def jget(url):
    d = get(url)
    try: return json.loads(d) if d else None
    except ValueError: return None


def lead_image(article):
    d = jget("https://en.wikipedia.org/api/rest_v1/page/summary/" + urllib.parse.quote(article.replace(" ", "_"), safe=""))
    src = (d or {}).get("originalimage", {}).get("source", "")
    if "/wikipedia/commons/" not in src: return None          # not on Commons (often non-free)
    return "File:" + urllib.parse.unquote(src.split("/")[-1])


def search(query, n=8):
    d = jget("https://commons.wikimedia.org/w/api.php?action=query&format=json&list=search&srnamespace=6&srlimit=%d&srsearch=%s" % (n, urllib.parse.quote(query)))
    return [h["title"] for h in (d or {}).get("query", {}).get("search", []) if re.search(r"\.(jpe?g|png|tiff?)$", h["title"], re.I)]


def info(title):
    d = jget("https://commons.wikimedia.org/w/api.php?action=query&format=json&prop=imageinfo&iiprop=url|extmetadata|size&iiurlwidth=1920&titles=" + urllib.parse.quote(title))
    pages = (d or {}).get("query", {}).get("pages", {})
    for p in pages.values():
        ii = (p.get("imageinfo") or [None])[0]
        if not ii: return None
        md = ii.get("extmetadata", {})
        val = lambda k: re.sub("<[^>]+>", "", md.get(k, {}).get("value", "")).strip()
        lic = val("LicenseShortName")
        return dict(title=title, url=ii.get("thumburl") or ii.get("url"), w=ii.get("width", 0), h=ii.get("height", 0),
                    licence=lic, licence_url=val("LicenseUrl"), author=val("Artist")[:120] or "Unknown",
                    source="https://commons.wikimedia.org/wiki/" + urllib.parse.quote(title.replace(" ", "_")))
    return None


def acceptable(i):
    return i and i["url"] and OK.match(i["licence"] or "") and not BAD.search(i["licence"] or "") and min(i["w"], i["h"]) >= 400


def main():
    data = json.load(open(DATA))
    os.makedirs(OUT, exist_ok=True); os.makedirs(CAND, exist_ok=True); os.makedirs(os.path.dirname(CREDITS), exist_ok=True)
    choices = json.load(open(CHOICES)) if os.path.exists(CHOICES) else {}
    credits = {}
    if os.path.exists(CREDITS):
        for row in csv.DictReader(open(CREDITS)): credits[row["id"]] = row
    items = [i for w in data["wings"] for i in w["items"]]
    for it in items:
        oid = it["id"]; dest = os.path.join(OUT, oid + ".jpg")
        forced = choices.get(oid)
        valid = os.path.exists(dest) and open(dest, "rb").read(3) == b"\xff\xd8\xff"
        if valid and not forced and oid in credits:
            print(f"{oid}: already have"); continue
        titles = []
        if forced: titles = [forced]
        else:
            if it.get("wiki"):
                t = lead_image(it["wiki"]); time.sleep(0.3)
                if t: titles.append(t)
            if it.get("query"):
                titles += [t for t in search(it["query"]) if t not in titles]; time.sleep(0.3)
        chosen, alts = None, []
        for t in titles:
            i = info(t); time.sleep(0.3)
            if not acceptable(i): continue
            if chosen is None: chosen = i
            elif len(alts) < 4: alts.append(i)
            if chosen and len(alts) >= 4: break
        if not chosen:
            print(f"{oid}: NO freely licensed photo found ({it['title']})"); continue
        part = dest + ".part"
        ok = get(chosen["url"], part)
        if not ok:
            if os.path.exists(part): os.remove(part)
            print(f"{oid}: download failed (run the script again later)"); continue
        os.replace(part, dest); time.sleep(1.5)
        credits[oid] = dict(id=oid, file=chosen["title"], author=chosen["author"], licence=chosen["licence"], licence_url=chosen["licence_url"], source=chosen["source"])
        for k, a in enumerate(alts):
            get(a["url"].replace("1920px", "330px"), os.path.join(CAND, f"{oid}_alt{k + 1}.jpg"))
        json.dump({oid + f"_alt{k + 1}": a["title"] for k, a in enumerate(alts)}, open(os.path.join(CAND, oid + "_alts.json"), "w"), indent=1)
        print(f"{oid}: {chosen['title']}  [{chosen['licence']}]")
        with open(CREDITS, "w", newline="") as f:
            wr = csv.DictWriter(f, fieldnames=["id", "file", "author", "licence", "licence_url", "source"]); wr.writeheader()
            for k in sorted(credits): wr.writerow(credits[k])
    tmp = os.path.join(CAND, ".tmp")
    if os.path.exists(tmp): os.remove(tmp)
    have = sum(os.path.exists(os.path.join(OUT, i["id"] + ".jpg")) and open(os.path.join(OUT, i["id"] + ".jpg"), "rb").read(3) == b"\xff\xd8\xff" for i in items)
    print(f"\nDONE. {have}/{len(items)} photos in Assets/_Museum/Curated/Photos. Credits: Docs/curated_credits.csv")


if __name__ == "__main__":
    main()
