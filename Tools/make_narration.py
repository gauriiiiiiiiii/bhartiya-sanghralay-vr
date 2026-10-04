"""Generate narration for the Museum of Indian History with Kokoro TTS (kokoro-onnx).

Reads Assets/_Museum/Content/museum.json and writes Assets/_Museum/Audio/<id>.ogg — the same 399 clips and
wording as Docs/IndianHistoryMuseum_Narration_Script.xlsx. Existing files are skipped, so it can be stopped
and resumed. Usage:  python3 make_narration.py [--voice bf_emma] [--speed 0.95] [--workers 2] [--only S01_01,S01_02]
"""
import argparse, json, os, sys, time
from concurrent.futures import ProcessPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
MODEL = os.path.join(HERE, "kokoro", "kokoro-v1.0.int8.onnx")
VOICES = os.path.join(HERE, "kokoro", "voices-v1.0.bin")


def spoken(p):
    p = p.replace("c. ", "about ").replace(" – ", " to ").replace("–", " to ")
    return p[:1].upper() + p[1:]


def items():
    d = json.load(open(os.path.join(PROJECT, "Assets/_Museum/Content/museum.json"), encoding="utf-8"))
    yield "L00", d["welcome"]
    for wi, w in enumerate(d["wings"], 1):
        yield f"{w['id']}_00", f"Wing {wi}: {w['title']}. {w['subtitle']}. {w['intro']}"
        for s in w["sections"]:
            yield f"{s['id']}_00", f"Section {s['num']}: {s['title']}. {spoken(s['period'])}. {s['intro']}"
            for e in s["exhibits"]:
                yield e["id"], f"{e['title']}. {e['text']}"


_k = None
def work(args):
    global _k
    cid, text, out, voice, speed = args
    import soundfile as sf
    if _k is None:
        from kokoro_onnx import Kokoro
        _k = Kokoro(MODEL, VOICES)
    t = time.time()
    lang = "en-gb" if voice.startswith("b") else "en-us"
    samples, sr = _k.create(text, voice=voice, speed=speed, lang=lang)
    tmp = out + ".part.ogg"
    sf.write(tmp, samples, sr, format="OGG", subtype="VORBIS")
    os.replace(tmp, out)
    return cid, len(samples) / sr, time.time() - t


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--voice", default="bf_emma")
    ap.add_argument("--speed", type=float, default=0.95)
    ap.add_argument("--workers", type=int, default=2)
    ap.add_argument("--only", default="")
    ap.add_argument("--budget", type=float, default=0, help="stop starting new clips after this many seconds (0 = no limit)")
    ap.add_argument("--out", default=os.path.join(PROJECT, "Assets/_Museum/Audio"))
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    only = set(filter(None, a.only.split(",")))
    jobs = [(cid, t, os.path.join(a.out, cid + ".ogg"), a.voice, a.speed) for cid, t in items()
            if (not only or cid in only) and not os.path.exists(os.path.join(a.out, cid + ".ogg"))]
    print(f"{len(jobs)} clips to generate with {a.voice}", flush=True)
    done, start = 0, time.time()
    from concurrent.futures import FIRST_COMPLETED, wait
    with ProcessPoolExecutor(a.workers) as ex:
        pending, queue = set(), list(jobs)
        while queue or pending:
            while queue and len(pending) < a.workers and not (a.budget and time.time() - start > a.budget):
                pending.add(ex.submit(work, queue.pop(0)))
            if not pending: break
            finished, pending = wait(pending, return_when=FIRST_COMPLETED)
            for f in finished:
                cid, secs, took = f.result(); done += 1
                print(f"[{done}/{len(jobs)}] {cid} {secs:.1f}s audio in {took:.1f}s", flush=True)
    left = len(jobs) - done
    print("ALL DONE" if left == 0 else f"PAUSED, {left} left (run again to continue)", flush=True)
