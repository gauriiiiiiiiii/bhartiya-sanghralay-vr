"""Audio-guide narration for the curated museum (v5), with Kokoro TTS. Writes Assets/_Museum/Curated/Audio/<id>.ogg.
Usage: python make_curated_narration.py [--budget 150] [--workers 3]"""
import argparse, json, os, re, sys, time
from concurrent.futures import ProcessPoolExecutor
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_narration import work, PROJECT

def spoken(t):
    t = t.replace("c. ", "about ").replace(" – ", " to ").replace("–", " to ").replace(" × ", " by ").replace("₹", "rupees ")
    t = re.sub(r"(\d),(\d{3})", r"\1\2", t)
    return t

def items():
    d = json.load(open(os.path.join(PROJECT, "Assets/_Museum/Content/curated.json"), encoding="utf-8"))
    yield "L00", d["lobby"]["welcome"]
    for w in d["wings"]:
        yield w["id"] + "_00", f"{w['title']}. {w['subtitle']}. {w['intro']}"
        for i in w["items"] + [w["centre"]]:
            yield i["id"], i["narration"]

if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--voice", default="bf_emma"); ap.add_argument("--speed", type=float, default=0.95)
    ap.add_argument("--workers", type=int, default=3); ap.add_argument("--budget", type=float, default=0)
    a = ap.parse_args()
    out = os.path.join(PROJECT, "Assets/_Museum/Curated/Audio"); os.makedirs(out, exist_ok=True)
    jobs = [(cid, spoken(t), os.path.join(out, cid + ".ogg"), a.voice, a.speed) for cid, t in items()]
    todo = [j for j in jobs if not os.path.exists(j[2])]
    print(f"{len(jobs) - len(todo)}/{len(jobs)} done, {len(todo)} to go", flush=True)
    t0 = time.time()
    with ProcessPoolExecutor(a.workers) as ex:
        futs = []
        for j in todo:
            futs.append(ex.submit(work, j))
        for f in futs:
            cid, dur, el = f.result(); print(f"{cid}: {dur:.0f}s audio in {el:.0f}s", flush=True)
            if a.budget and time.time() - t0 > a.budget:
                for g in futs: g.cancel()
                break
    print("remaining:", sum(not os.path.exists(j[2]) for j in jobs))
