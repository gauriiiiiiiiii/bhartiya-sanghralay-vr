"""Contact sheets of the photo candidates, for picking the right image per exhibit (Tools/review/sheet_NN.png)."""
import json, os, sys, glob
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__))
rows = {r["id"]: r for r in json.load(open(os.path.join(HERE, "photo_sourcing.json"), encoding="utf-8"))}
out = os.path.join(HERE, "review"); os.makedirs(out, exist_ok=True)
skip = set(json.load(open(os.path.join(HERE, "photo_choices.json"))).keys()) if os.path.exists(os.path.join(HERE, "photo_choices.json")) else set()
ids = [i for i in rows if os.path.exists(os.path.join(HERE, "photo_candidates", i + ".json")) and i not in skip
       and json.load(open(os.path.join(HERE, "photo_candidates", i + ".json")))]
per, TW, TH, LW = 8, 230, 170, 300
try: font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 15); small = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 12)
except Exception: font = small = ImageFont.load_default()
def wrap(t, w, f, d):
    out, line = [], ""
    for word in t.split():
        if d.textlength(line + " " + word, font=f) > w and line: out.append(line); line = word
        else: line = (line + " " + word).strip()
    return out + [line]
for f in glob.glob(os.path.join(out, "sheet_*.png")): os.remove(f)
for s in range(0, len(ids), per):
    chunk = ids[s:s + per]
    img = Image.new("RGB", (LW + 4 * (TW + 8), len(chunk) * (TH + 30)), "white"); d = ImageDraw.Draw(img)
    for k, cid in enumerate(chunk):
        y = k * (TH + 30); cands = json.load(open(os.path.join(HERE, "photo_candidates", cid + ".json")))
        d.text((6, y + 4), cid, font=font, fill=(150, 0, 0))
        for j, ln in enumerate(wrap(rows[cid]["show"], LW - 12, small, d)[:9]): d.text((6, y + 26 + j * 15), ln, font=small, fill="black")
        for j, c in enumerate(cands[:4]):
            x = LW + j * (TW + 8)
            p = os.path.join(HERE, "photo_candidates", f"{cid}_{j + 1}.jpg")
            try:
                t = Image.open(p).convert("RGB"); t.thumbnail((TW, TH)); img.paste(t, (x, y + 2))
            except Exception: d.rectangle([x, y + 2, x + TW, y + TH], outline="red")
            d.text((x, y + TH + 4), f"{j + 1}: {c['title'][5:38]}", font=small, fill=(0, 0, 120))
        d.line([(0, y + TH + 26), (img.width, y + TH + 26)], fill=(200, 200, 200))
    img.save(os.path.join(out, f"sheet_{s // per + 1:02d}.png"))
print(len(ids), "items,", (len(ids) + per - 1) // per, "sheets")
