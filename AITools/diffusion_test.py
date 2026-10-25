#!/usr/bin/env python3
"""
Image-model test bench for Creative Mode: the same keywords as shape_test.py, sent to Draw Things
(or any AUTOMATIC1111-compatible API) with the prompt the game uses, Assets/Resources/AI/ImagePrompt.txt.
Saves every image and writes an HTML report with a chart preview and 1-3 scoring, so the result
sits next to the shape test in the writeup.

Run from the project folder, with Draw Things open and its HTTP API server on:
    python3 AITools/diffusion_test.py
    python3 AITools/diffusion_test.py --keywords bunny,fish --seeds 3     # 3 variations each
    python3 AITools/diffusion_test.py --steps 4 --label "klein 4b"         # label shows in the summary

Only the Python standard library is used. The chart preview is an approximation of
ImageToChart (background removal, crop, downsample, nearest yarn); the game's version also
clusters colours first, so expect small differences.
"""

import argparse
import base64
import html
import json
import os
import struct
import sys
import time
import zlib
from datetime import datetime

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import shape_test as st  # palette loading, colour maths, HTTP helper, SVG grid  # noqa: E402

PROJECT = st.PROJECT
NEGATIVE = ("photorealistic, texture, detailed background, gradient, soft shading, blurry, noise, "
            "text, watermark, signature, 3d render")  # = PromptBuilder.NegativePrompt


# ---- PNG decoding (8-bit RGB/RGBA, non-interlaced: what Draw Things sends) ----------------------

def decode_png(data):
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("not a PNG")
    pos, idat, w = 8, [], None
    while pos < len(data):
        length, ctype = struct.unpack(">I4s", data[pos:pos + 8])
        chunk = data[pos + 8:pos + 8 + length]
        pos += 12 + length
        if ctype == b"IHDR":
            w, h, depth, color, _, _, interlace = struct.unpack(">IIBBBBB", chunk)
            if depth != 8 or color not in (2, 6) or interlace:
                raise ValueError(f"unsupported PNG (depth {depth}, colour type {color}, interlace {interlace})")
            bpp = 3 if color == 2 else 4
        elif ctype == b"IDAT":
            idat.append(chunk)
        elif ctype == b"IEND":
            break
    raw = zlib.decompress(b"".join(idat))
    stride = w * bpp
    prev = bytearray(stride)
    px = []
    i = 0
    for _ in range(h):
        f = raw[i]; i += 1
        line = bytearray(raw[i:i + stride]); i += stride
        if f == 1:
            for x in range(bpp, stride): line[x] = (line[x] + line[x - bpp]) & 255
        elif f == 2:
            for x in range(stride): line[x] = (line[x] + prev[x]) & 255
        elif f == 3:
            for x in range(stride): line[x] = (line[x] + ((line[x - bpp] if x >= bpp else 0) + prev[x]) // 2) & 255
        elif f == 4:
            for x in range(stride):
                a = line[x - bpp] if x >= bpp else 0
                b, c = prev[x], (prev[x - bpp] if x >= bpp else 0)
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        px.extend(tuple(line[x:x + 3]) for x in range(0, stride, bpp))
        prev = line
    return w, h, px


# ---- rough ImageToChart ---------------------------------------------------------------------------

def image_to_chart(w, h, px, palette, cols, max_rows=0, tolerance=90.0, gauge=1.25, sample=2):
    """Background flood from the corners, crop to the subject, area-downsample to `cols`
    stitches (rows from the 5:4 gauge; a tall subject is narrowed to fit `max_rows`, as
    ImageToChart.Settings.maxRows does), nearest non-background yarn per stitch."""
    # Work at half resolution: plenty for a 20-30 stitch chart and 4x faster in Python.
    sw, sh = w // sample, h // sample
    small = [px[(y * sample) * w + x * sample] for y in range(sh) for x in range(sw)]
    labs = {}
    def lab(c):
        v = labs.get(c)
        if v is None:
            v = labs[c] = st.rgb_to_lab(c)
        return v

    bg = lab(small[(sh - 1) * sw])  # bottom-left, as ImageToChart
    fg = [True] * (sw * sh)
    seen = bytearray(sw * sh)
    stack = [0, sw - 1, (sh - 1) * sw, sh * sw - 1]
    while stack:
        i = stack.pop()
        if seen[i]:
            continue
        seen[i] = 1
        if st.lab_dist(lab(small[i]), bg) > tolerance:
            continue
        fg[i] = False
        x, y = i % sw, i // sw
        if x > 0: stack.append(i - 1)
        if x < sw - 1: stack.append(i + 1)
        if y > 0: stack.append(i - sw)
        if y < sh - 1: stack.append(i + sw)

    pts = [i for i in range(sw * sh) if fg[i]]
    if not pts:
        return [], 0, 0
    xs = [i % sw for i in pts]; ys = [i // sw for i in pts]
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    cw, ch = x1 - x0 + 1, y1 - y0 + 1
    rows = max(2, round(cols * ch / cw * gauge))
    if max_rows and rows > max_rows:
        cols = max(2, round(cols * max_rows / rows))
        rows = max_rows
    pal_lab = [st.rgb_to_lab(c) for _, c in palette]
    out = []
    for r in range(rows):
        ya = y0 + r * ch // rows; yb = max(ya + 1, y0 + (r + 1) * ch // rows)
        for c in range(cols):
            xa = x0 + c * cw // cols; xb = max(xa + 1, x0 + (c + 1) * cw // cols)
            n = tot = 0; sr = sg = sb = 0
            for yy in range(ya, yb):
                for xx in range(xa, xb):
                    tot += 1
                    i = yy * sw + xx
                    if fg[i]:
                        n += 1; p = small[i]; sr += p[0]; sg += p[1]; sb += p[2]
            if n * 2 < tot:
                out.append(0)
                continue
            l = st.rgb_to_lab((sr // n, sg // n, sb // n))
            out.append(min(range(1, len(palette)), key=lambda k: st.lab_dist(l, pal_lab[k])))
    return out, cols, rows


# ---- report -------------------------------------------------------------------------------------

def write_report(path, results, keywords, seeds, palette, s):
    colors = ["#%02x%02x%02x" % c for _, c in palette]
    rows = []
    for kw in keywords:
        cells = []
        for sd in range(seeds):
            r = next((x for x in results if x["keyword"] == kw and x["run"] == sd), None)
            if r is None:
                continue
            key = html.escape(f"{kw}|{sd}")
            if r["error"]:
                body = f'<div class="err">{html.escape(r["error"])}</div>'
            else:
                body = (f'<div class="pics"><img src="{html.escape(r["file"])}" width="180" height="180">'
                        f'{st.svg_grid(r["chart"], r["cols"], r["rows"], colors, 7.5, 6, grid_lines=True)}</div>'
                        f'<div class="meta">{r["seconds"]:.1f}s · {r["cols"]}×{r["rows"]} stitches</div>')
            score = "".join(f'<button data-k="{key}" data-v="{v}">{v}</button>' for v in (1, 2, 3))
            cells.append(f'<td>{body}<div class="score">{score}</div></td>')
        rows.append(f"<tr><th class='kw'>{html.escape(kw)}</th>{''.join(cells)}</tr>")

    data = json.dumps([{"keyword": r["keyword"], "run": r["run"], "ok": not r["error"], "seconds": r["seconds"]} for r in results])
    doc = f"""<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Image test {html.escape(s['stamp'])}</title><style>
:root{{--bg:#faf8f3;--ink:#222;--muted:#777;--line:#ddd;--card:#fff;--sel:#2e4f8f}}
@media (prefers-color-scheme:dark){{:root:not([data-theme=light]){{--bg:#1c1c1e;--ink:#eee;--muted:#999;--line:#3a3a3c;--card:#2a2a2c;--sel:#7fa2e6}}}}
body{{background:var(--bg);color:var(--ink);font:14px/1.4 -apple-system,system-ui,sans-serif;margin:16px}}
h1{{font-size:20px;margin:0 0 4px}} .sub{{color:var(--muted);margin-bottom:16px;max-width:900px}}
table{{border-collapse:collapse}} td,th{{border:1px solid var(--line);padding:8px;vertical-align:top;background:var(--card)}}
th.kw{{text-align:left;white-space:nowrap}} .pics{{display:flex;gap:8px;align-items:flex-start}} img,svg{{display:block;border:1px solid var(--line)}}
.gl{{stroke:#0002;stroke-width:.5}} .meta{{color:var(--muted);font-size:12px;margin-top:4px}} .err{{color:#c0392b;max-width:240px;font-size:12px}}
.score button{{border:1px solid var(--line);background:none;color:var(--ink);width:28px;height:24px;margin:4px 2px 0 0;border-radius:4px;cursor:pointer}}
.score button.on{{background:var(--sel);color:#fff;border-color:var(--sel)}}
#summary td,#summary th{{text-align:right}} #summary td:first-child,#summary th:first-child{{text-align:left}}
.wrap{{overflow-x:auto}} button.copy{{margin:8px 0 20px;padding:6px 12px;border-radius:6px;border:1px solid var(--line);background:var(--card);color:var(--ink);cursor:pointer}}
code{{font-size:12px}}
</style></head><body>
<h1>Image test · {html.escape(s['stamp'])}</h1>
<div class="sub">{html.escape(s['label'])} · {s['size']}px · steps {s['steps'] or 'app setting'} · chart preview {s['chart']} stitches wide (approximate) · palette: {html.escape(', '.join(n for n, _ in palette))}<br>
Prompt: <code>{html.escape(s['prompt'])}</code><br>
Score the <b>chart</b> 1–3 for how recognizable it is (1 = no idea, 2 = with the keyword, 3 = at a glance). Scores stay in this browser.</div>
<div class="wrap"><table id="summary"><thead><tr><th>Source</th><th>Usable image</th><th>Median time</th><th>Recognizable (1–3)</th></tr></thead><tbody></tbody></table></div>
<button class="copy">Copy summary as Markdown</button>
<div class="wrap"><table><tbody>{''.join(rows)}</tbody></table></div>
<script>
const DATA={data}, LABEL={json.dumps(s['label'])}, STORE='image-test-{s['stamp']}';
let scores={{}}; try{{scores=JSON.parse(localStorage.getItem(STORE)||'{{}}')}}catch(e){{}}
function save(){{try{{localStorage.setItem(STORE,JSON.stringify(scores))}}catch(e){{}}}}
function row(){{const ok=DATA.filter(d=>d.ok); const t=ok.map(d=>d.seconds).sort((a,b)=>a-b);
 const sc=DATA.map(d=>scores[d.keyword+'|'+d.run]).filter(Boolean);
 return [LABEL, DATA.length?Math.round(100*ok.length/DATA.length)+'%':'–', t.length?t[Math.floor(t.length/2)].toFixed(1)+'s':'–',
  sc.length?(sc.reduce((a,b)=>a+b,0)/sc.length).toFixed(2)+' ('+sc.length+'/'+DATA.length+')':'–']}}
function render(){{document.querySelector('#summary tbody').innerHTML='<tr>'+row().map(c=>'<td>'+c+'</td>').join('')+'</tr>';
 document.querySelectorAll('.score button').forEach(b=>b.classList.toggle('on',scores[b.dataset.k]==+b.dataset.v))}}
document.querySelectorAll('.score button').forEach(b=>b.onclick=()=>{{const k=b.dataset.k,v=+b.dataset.v;
 if(scores[k]===v)delete scores[k];else scores[k]=v;save();render()}});
document.querySelector('.copy').onclick=()=>{{const h=['Source','Usable image','Median time','Recognizable (1–3)'];
 const md=['| '+h.join(' | ')+' |','|'+h.map(()=>'---').join('|')+'|','| '+row().join(' | ')+' |'].join('\\n');
 navigator.clipboard.writeText(md).then(()=>{{const b=document.querySelector('.copy');b.textContent='Copied';setTimeout(()=>b.textContent='Copy summary as Markdown',1500)}})}};
render();
</script></body></html>"""
    with open(path, "w", encoding="utf-8") as f:
        f.write(doc)


# ---- main ---------------------------------------------------------------------------------------

def join_and(items):
    return items[0] if len(items) == 1 else ", ".join(items[:-1]) + " and " + items[-1]


def build_prompt(template, keyword, palette):
    """Same placeholders as DiffusionSource.BuildPrompt: {outline} = yarn 2, {fills} = yarns 3+."""
    names = [n.strip().lower() for n, _ in palette]
    outline = names[1] if len(names) > 1 else names[0]
    fills = names[2:] or [outline]
    return (template.strip().replace("{keyword}", keyword).replace("{background}", names[0])
            .replace("{outline}", outline).replace("{fills}", join_and(fills))
            .replace("{colors}", ", ".join(names)))


def main():
    ap = argparse.ArgumentParser(description="Test an image model on knitting motifs.")
    ap.add_argument("--keywords", default=",".join(st.DEFAULT_KEYWORDS))
    ap.add_argument("--seeds", type=int, default=1, help="variations per keyword")
    ap.add_argument("--url", default="http://127.0.0.1:7860")
    ap.add_argument("--steps", type=int, default=0, help="0 = the app's setting")
    ap.add_argument("--size", type=int, default=512)
    ap.add_argument("--chart", type=int, default=24, help="canvas width in stitches for the chart preview")
    ap.add_argument("--rows", type=int, default=0, help="canvas height in rows (default: same as --chart)")
    ap.add_argument("--timeout", type=float, default=180)
    ap.add_argument("--label", default="Draw Things", help="name for this run in the summary table")
    ap.add_argument("--prompt-file", default=os.path.join(PROJECT, "Assets", "Resources", "AI", "ImagePrompt.txt"))
    ap.add_argument("--palette", default=None)
    ap.add_argument("--out", default=os.path.join(HERE, "results"))
    a = ap.parse_args()

    template = open(a.prompt_file, encoding="utf-8").read()
    palette = st.load_palette(a.palette or st.find_palette())
    print("Palette:", ", ".join(n for n, _ in palette))
    keywords = [k.strip() for k in a.keywords.split(",") if k.strip()]

    stamp = datetime.now().strftime("%Y-%m-%d_%H%M%S") + "_images"
    out_dir = os.path.join(a.out, stamp)
    os.makedirs(out_dir, exist_ok=True)

    print("Loading the model (first image is slow)...", end="", flush=True)
    t = time.time()
    _, err = st.post(a.url + "/sdapi/v1/txt2img", {"prompt": "a dot", "width": 256, "height": 256, "steps": 1}, 600)
    if err:
        sys.exit(f"\nCan't use {a.url}: {err}\nIs Draw Things open with its HTTP API server on?")
    print(f" {time.time() - t:.1f}s")

    results = []
    for kw in keywords:
        for run in range(a.seeds):
            prompt = build_prompt(template, kw, palette)
            payload = {"prompt": prompt, "negative_prompt": NEGATIVE, "width": a.size, "height": a.size, "seed": -1}
            if a.steps:
                payload["steps"] = a.steps
            t = time.time()
            data, err = st.post(a.url + "/sdapi/v1/txt2img", payload, a.timeout)
            secs = time.time() - t
            r = {"keyword": kw, "run": run, "seconds": secs, "error": err, "file": "", "chart": [], "cols": 0, "rows": 0}
            if not err:
                try:
                    b64 = data["images"][0]
                    png = base64.b64decode(b64.split(",", 1)[1] if b64.startswith("data:") else b64)
                    name = f"{kw.replace(' ', '_')}_{run}.png"
                    with open(os.path.join(out_dir, name), "wb") as f:
                        f.write(png)
                    r["file"] = name
                    w, h, px = decode_png(png)
                    r["chart"], r["cols"], r["rows"] = image_to_chart(w, h, px, palette, a.chart, a.rows or a.chart)
                    if not r["chart"]:
                        r["error"] = "nothing left after background removal"
                except Exception as e:  # bad reply or unexpected PNG
                    r["error"] = f"couldn't read image: {e}"
            results.append(r)
            print(f"  {'ok ' if not r['error'] else 'ERR'} {kw:<12} #{run}  {secs:5.1f}s  {r['error'] or ''}")

    report = os.path.join(out_dir, "report.html")
    write_report(report, results, keywords, a.seeds, palette,
                 {"stamp": stamp, "label": a.label, "size": a.size, "steps": a.steps, "chart": a.chart,
                  "prompt": build_prompt(template, "{keyword}", palette)})
    print(f"\nReport: {report}")
    if sys.platform == "darwin":
        os.system(f'open "{report}"')


if __name__ == "__main__":
    main()
