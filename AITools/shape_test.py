#!/usr/bin/env python3
"""
Shape-drawing test bench for Creative Mode (AI plan v2, steps 4-5).

Sends the same keywords to several Ollama models using exactly the prompt, examples and
palette the game uses, draws every reply, and writes an HTML report you can score by eye.
The report's summary table is the model comparison for the writeup.

Run from the project folder, with Ollama running:
    python3 AITools/shape_test.py
    python3 AITools/shape_test.py --models qwen3.5:9b --keywords heart,bunny
    python3 AITools/shape_test.py --think --timeout 120      # let the model think first

Only the Python standard library is used. The prompt and examples come from
Assets/Resources/AI/, so edit them there and re-run: the game uses the same files.
"""

import argparse
import csv
import html
import json
import math
import os
import re
import statistics
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)

DEFAULT_MODELS = ["qwen3.5:4b", "qwen3.5:9b", "granite4.2:3b", "gemma4:e2b-it-qat", "llama3.2:3b"]
DEFAULT_KEYWORDS = ["heart", "bunny", "tree", "star", "cat", "flower", "snowflake", "house", "mushroom", "fish"]

FALLBACK_PALETTE = [("Cream", (245, 240, 228)), ("Charcoal", (54, 54, 60)), ("Rust", (190, 92, 58)),
                    ("Sage", (126, 150, 112)), ("Mustard", (222, 168, 70)), ("Plum", (108, 71, 100))]


# ---- palette ------------------------------------------------------------------------------------

def find_palette():
    """The YarnPalette asset can live anywhere under Assets/: find it by its script class."""
    found = []
    for root, _, files in os.walk(os.path.join(PROJECT, "Assets")):
        for f in files:
            if f.endswith(".asset"):
                p = os.path.join(root, f)
                try:
                    with open(p, encoding="utf-8", errors="ignore") as fh:
                        if "KnitSprite.CreativeMode.YarnPalette" in fh.read(4000):
                            found.append(p)
                except OSError:
                    pass
    if len(found) > 1:
        print("! Several palettes found, using the first (pick one with --palette):\n  " + "\n  ".join(found))
    return found[0] if found else None


def load_palette(path):
    """Reads displayName + color from a YarnPalette .asset (Unity YAML). Color32 is stored as
    one packed uint 'rgba' with r in the lowest byte."""
    if not path or not os.path.exists(path):
        print(f"! {path} not found, using the placeholder palette")
        return FALLBACK_PALETTE
    text = open(path, encoding="utf-8").read()
    yarns = []
    for m in re.finditer(r"displayName:\s*(.*?)\s*\n\s*color:\s*\n(?:\s*serializedVersion:.*\n)?\s*rgba:\s*(\d+)", text):
        v = int(m.group(2))
        yarns.append((m.group(1).strip().strip("'\""), (v & 255, (v >> 8) & 255, (v >> 16) & 255)))
    if len(yarns) < 2:
        print(f"! couldn't read yarns from {path}, using the placeholder palette")
        return FALLBACK_PALETTE
    return yarns


def rgb_to_lab(rgb):
    def lin(c):
        c /= 255.0
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    r, g, b = (lin(c) for c in rgb)
    # Same constants as ColorMath.RgbToLab (sRGB, D65).
    x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * b) / 0.95047
    y = (0.2126729 * r + 0.7151522 * g + 0.0721750 * b)
    z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * b) / 1.08883
    def f(t):
        return t ** (1 / 3) if t > 0.008856 else 7.787 * t + 16 / 116
    fx, fy, fz = f(x), f(y), f(z)
    return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz))


def lab_dist(a, b):
    return sum((p - q) ** 2 for p, q in zip(a, b))


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def map_example_colors(hexes, palette):
    """Same rule as ShapeSource.MapExampleColors, applied to one example at a time: each distinct
    color, in order of first use, takes the closest yarn not yet taken in that example (never the
    background); once every yarn is taken, the closest yarn overall. Keeps details a different
    yarn from the body they sit on, even with a small palette."""
    labs = [rgb_to_lab(c) for _, c in palette]
    taken, out = set(), {}
    for h in hexes:
        if h in out:
            continue
        lab = rgb_to_lab(hex_rgb(h))
        free = [i for i in range(1, len(palette)) if i not in taken]
        pool = free if free else list(range(1, len(palette)))
        best = min(pool, key=lambda i: lab_dist(lab, labs[i]))
        taken.add(best)
        out[h] = best
    return out


# ---- prompt, examples, schema (mirrors ShapeSource.cs) -----------------------------------------

SIZED = {"circle": ("x", "y", "w"), "ellipse": ("x", "y", "w", "h"), "rect": ("x", "y", "w", "h"),
         "line": ("x", "y", "x2", "y2", "w"), "triangle": ("x", "y", "x2", "y2", "x3", "y3")}


def shape_to_json(s, color_name):
    """Compact: only the fields that kind uses, so the model never sees meaningless zeros."""
    parts = ['"kind":' + json.dumps(s["kind"]), '"color":' + json.dumps(color_name)]
    for f in SIZED.get(s["kind"], ("x", "y")):
        parts.append(f'"{f}":{int(s.get(f, 0))}')
    return "{" + ",".join(parts) + "}"


def build_examples(examples, palette, size):
    names = [n.strip().lower() for n, _ in palette]
    k = size / 32.0
    blocks = []
    for ex in examples:
        d = ex["design"]
        cmap = map_example_colors([s["color"] for s in d["shapes"]], palette)
        lines = []
        for s in d["shapes"]:
            scaled = dict(s)
            for f in ("x", "y", "w", "h", "x2", "y2", "x3", "y3"):
                if f in s:
                    v = int(round(s[f] * k))
                    scaled[f] = max(1, v) if f in ("w", "h") else v
            lines.append(" " + shape_to_json(scaled, names[cmap[s["color"]]]))
        body = ('{"parts":' + json.dumps(d["parts"]) + ',"symmetry":' + json.dumps(d["symmetry"]) +
                ',"shapes":[\n' + ",\n".join(lines) + "]}")
        blocks.append(f'Example for "{ex["keyword"]}":\n{body}')
    return "\n\n".join(blocks)


def build_prompt(template, keyword, palette, size, examples_text):
    names = [n.strip().lower() for n, _ in palette]
    return (template.replace("{size}", str(size)).replace("{max}", str(size - 1))
            .replace("{halfmax}", str(size // 2 - 1)).replace("{half}", str(size // 2))
            .replace("{background}", names[0]).replace("{colors}", ", ".join(names[1:]))
            .replace("{examples}", examples_text).replace("{keyword}", keyword))


def build_schema(palette, min_shapes):
    names = [n.strip().lower() for n, _ in palette[1:]]
    return {
        "type": "object",
        "properties": {
            "parts": {"type": "string"},
            "symmetry": {"type": "string", "enum": ["none", "mirror"]},
            "shapes": {"type": "array", "minItems": min_shapes, "maxItems": 20, "items": {
                "type": "object",
                "properties": {
                    "kind": {"type": "string", "enum": ["circle", "ellipse", "rect", "line", "triangle"]},
                    "color": {"type": "string", "enum": names},
                    **{f: {"type": "integer"} for f in ("x", "y", "w", "h", "x2", "y2", "x3", "y3")},
                },
                "required": ["kind", "color", "x", "y"]}},
        },
        "required": ["parts", "symmetry", "shapes"],
    }


# ---- rasterizer (port of ShapeRasterizer.cs; keep the two in step) -----------------------------

def clamp(v, lo, hi):
    return lo if v < lo else hi if v > hi else v


def rasterize(design, size, color_index):
    cells = [0] * (size * size)
    mx, half = size - 1, max(1, size // 2)
    st = {"drawn": 0, "dropped": 0, "out_of_bounds": 0, "mirrored": False}

    def put(x, y, c):
        if 0 <= x < size and 0 <= y < size:
            cells[y * size + x] = c

    for s in design.get("shapes") or []:
        if not isinstance(s, dict):
            st["dropped"] += 1
            continue
        c = color_index(s.get("color"))
        if c < 0:
            st["dropped"] += 1
            continue
        g = lambda f: int(s.get(f) or 0)
        raw = [g(f) for f in ("x", "y", "x2", "y2", "x3", "y3")]
        if any(v < 0 or v > mx for v in raw):
            st["out_of_bounds"] += 1
        # Clip rather than clamp: a shape partly off the grid is cut off, one fully off draws nothing.
        x, y, x2, y2, x3, y3 = (clamp(v, -size, 2 * size) for v in raw)
        kind = (s.get("kind") or "").strip().lower()
        ok = False
        if kind in ("circle", "ellipse"):
            rx = clamp(g("w"), 1, half)
            ry = rx if kind == "circle" or g("h") <= 0 else clamp(g("h"), 1, half)
            ex, ey = rx * rx + rx, ry * ry + ry
            for yy in range(max(0, y - ry), min(mx, y + ry) + 1):
                for xx in range(max(0, x - rx), min(mx, x + rx) + 1):
                    if (xx - x) ** 2 / ex + (yy - y) ** 2 / ey <= 1:
                        put(xx, yy, c); ok = True
        elif kind in ("rect", "rectangle", "square"):
            w = clamp(g("w"), 1, size)
            h = clamp(g("h"), 1, size) if g("h") > 0 else w
            for yy in range(max(0, y), min(size, y + h)):
                for xx in range(max(0, x), min(size, x + w)):
                    put(xx, yy, c); ok = True
        elif kind == "line":
            t = clamp(g("w"), 1, 4)
            steps = max(abs(x2 - x), abs(y2 - y))
            lo, hi = -((t - 1) // 2), t // 2
            for i in range(steps + 1):
                f = 0 if steps == 0 else i / steps
                px, py = round(x + (x2 - x) * f), round(y + (y2 - y) * f)
                for oy in range(lo, hi + 1):
                    for ox in range(lo, hi + 1):
                        if 0 <= px + ox < size and 0 <= py + oy < size:
                            put(px + ox, py + oy, c); ok = True
        elif kind == "triangle":
            def edge(ax, ay, bx, by, px, py):
                return (bx - ax) * (py - ay) - (by - ay) * (px - ax)
            area = edge(x, y, x2, y2, x3, y3)
            if area != 0:
                for yy in range(max(0, min(y, y2, y3)), min(mx, max(y, y2, y3)) + 1):
                    for xx in range(max(0, min(x, x2, x3)), min(mx, max(x, x2, x3)) + 1):
                        e0, e1, e2 = edge(x2, y2, x3, y3, xx, yy), edge(x3, y3, x, y, xx, yy), edge(x, y, x2, y2, xx, yy)
                        if (area > 0 and e0 >= 0 and e1 >= 0 and e2 >= 0) or (area < 0 and e0 <= 0 and e1 <= 0 and e2 <= 0):
                            put(xx, yy, c); ok = True
        st["drawn" if ok else "dropped"] += 1

    if str(design.get("symmetry", "")).strip().lower() == "mirror":
        st["mirrored"] = True
        for yy in range(size):
            for xx in range(size // 2):
                cells[yy * size + (mx - xx)] = cells[yy * size + xx]

    filled = [v for v in cells if v]
    st["coverage"] = len(filled) / (size * size)
    st["yarns"] = len(set(filled))
    return cells, st


def chart_preview(cells, size, cols, gauge=1.25):
    """Rough stand-in for ImageToChart: crop to the subject, then majority colour per stitch
    (background if under half the area is filled). Rows follow the 5:4 row/stitch gauge."""
    pts = [(i % size, i // size) for i, v in enumerate(cells) if v]
    if not pts:
        return [], 0, 0
    x0, x1 = min(p[0] for p in pts), max(p[0] for p in pts)
    y0, y1 = min(p[1] for p in pts), max(p[1] for p in pts)
    cw, ch = x1 - x0 + 1, y1 - y0 + 1
    rows = max(2, round(cols * ch / cw * gauge))
    out = []
    for r in range(rows):
        ya, yb = y0 + r * ch // rows, max(y0 + r * ch // rows + 1, y0 + (r + 1) * ch // rows)
        for c in range(cols):
            xa, xb = x0 + c * cw // cols, max(x0 + c * cw // cols + 1, x0 + (c + 1) * cw // cols)
            counts, total = {}, 0
            for yy in range(ya, yb):
                for xx in range(xa, xb):
                    total += 1
                    v = cells[yy * size + xx]
                    if v:
                        counts[v] = counts.get(v, 0) + 1
            fg = sum(counts.values())
            out.append(max(counts, key=counts.get) if fg * 2 >= total and counts else 0)
    return out, cols, rows


# ---- Ollama -------------------------------------------------------------------------------------

def post(url, payload, timeout):
    req = urllib.request.Request(url, data=json.dumps(payload).encode(), headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return json.loads(r.read().decode()), None
    except urllib.error.HTTPError as e:
        body = e.read().decode(errors="replace")
        try:
            return None, json.loads(body).get("error", body)
        except ValueError:
            return None, body or str(e)
    except Exception as e:  # timeout, connection refused
        return None, str(e)


def installed_models(base):
    try:
        with urllib.request.urlopen(base + "/api/tags", timeout=5) as r:
            return {m["name"] for m in json.loads(r.read().decode()).get("models", [])}
    except Exception as e:
        sys.exit(f"Can't reach Ollama at {base} ({e}). Is it running?")


class Client:
    def __init__(self, base, model, think, temperature, timeout):
        self.base, self.model, self.timeout = base, model, timeout
        self.temperature = temperature
        self.think = think  # True/False, or None = don't send the field

    def generate(self, prompt, schema, timeout=None):
        payload = {"model": self.model, "prompt": prompt, "stream": False, "keep_alive": "10m",
                   "options": {"temperature": self.temperature}}
        if schema is not None:
            payload["format"] = schema
        if self.think is not None:
            payload["think"] = self.think
        t = time.time()
        data, err = post(self.base + "/api/generate", payload, timeout or self.timeout)
        if err and "think" in err.lower() and self.think is not None:
            self.think = None  # model has no thinking switch: retry without the field
            return self.generate(prompt, schema, timeout)
        return data, err, time.time() - t

    def unload(self):
        post(self.base + "/api/generate", {"model": self.model, "keep_alive": 0}, 30)


# ---- report -------------------------------------------------------------------------------------

def svg_grid(cells, cols, rows, colors, cell_w, cell_h, grid_lines=False):
    if not cells:
        return '<div class="empty">nothing drawn</div>'
    w, h = cols * cell_w, rows * cell_h
    rects = [f'<rect width="{w}" height="{h}" fill="{colors[0]}"/>']
    for i, v in enumerate(cells):
        if v:
            rects.append(f'<rect x="{(i % cols) * cell_w}" y="{(i // cols) * cell_h}" width="{cell_w + 0.3}" height="{cell_h + 0.3}" fill="{colors[v]}"/>')
    if grid_lines:
        for c in range(1, cols):
            rects.append(f'<line x1="{c * cell_w}" y1="0" x2="{c * cell_w}" y2="{h}" class="gl"/>')
        for r in range(1, rows):
            rects.append(f'<line x1="0" y1="{r * cell_h}" x2="{w}" y2="{r * cell_h}" class="gl"/>')
    return f'<svg viewBox="0 0 {w} {h}" width="{w}" height="{h}">{"".join(rects)}</svg>'


def write_report(path, results, models, keywords, palette, settings):
    colors = ["#%02x%02x%02x" % c for _, c in palette]
    by = {(r["model"], r["keyword"]): r for r in results}
    head = "".join(f"<th>{html.escape(m)}</th>" for m in models)
    rows = []
    for kw in keywords:
        cells = []
        for m in models:
            r = by.get((m, kw))
            if r is None:
                cells.append("<td></td>")
                continue
            key = html.escape(f"{m}|{kw}")
            if r["error"]:
                body = f'<div class="err">{html.escape(r["error"])}</div>'
            else:
                pv = svg_grid(r["preview"], r["pcols"], r["prows"], colors, 7.5, 6, grid_lines=True)
                body = (f'<div class="pics">{svg_grid(r["cells"], settings["grid"], settings["grid"], colors, 4, 4)}{pv}</div>'
                        f'<div class="meta">{r["seconds"]:.1f}s · {r["drawn"]} shapes · {r["yarns"]} yarn{"s" if r["yarns"] != 1 else ""}'
                        f'{" · mirror" if r["mirrored"] else ""}{" · " + str(r["dropped"]) + " dropped" if r["dropped"] else ""}'
                        f'{" · " + str(r["out_of_bounds"]) + " off-grid" if r["out_of_bounds"] else ""}</div>'
                        f'<details><summary>parts</summary>{html.escape(r["parts"] or "(none)")}<pre>{html.escape(r["raw"])}</pre></details>')
            score = "".join(f'<button data-k="{key}" data-v="{v}">{v}</button>' for v in (1, 2, 3))
            cells.append(f'<td class="{"bad" if r["error"] else ""}">{body}<div class="score">{score}</div></td>')
        rows.append(f"<tr><th class='kw'>{html.escape(kw)}</th>{''.join(cells)}</tr>")

    data = json.dumps([{k: r[k] for k in ("model", "keyword", "valid", "ok", "in_bounds", "seconds")} for r in results])
    doc = f"""<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Shape test {html.escape(settings['stamp'])}</title><style>
:root{{--bg:#faf8f3;--ink:#222;--muted:#777;--line:#ddd;--card:#fff;--sel:#2e4f8f}}
@media (prefers-color-scheme:dark){{:root:not([data-theme=light]){{--bg:#1c1c1e;--ink:#eee;--muted:#999;--line:#3a3a3c;--card:#2a2a2c;--sel:#7fa2e6}}}}
body{{background:var(--bg);color:var(--ink);font:14px/1.4 -apple-system,system-ui,sans-serif;margin:16px}}
h1{{font-size:20px;margin:0 0 4px}} .sub{{color:var(--muted);margin-bottom:16px}}
table{{border-collapse:collapse}} td,th{{border:1px solid var(--line);padding:8px;vertical-align:top;background:var(--card)}}
th.kw{{text-align:left;white-space:nowrap}} td.bad{{opacity:.75}}
.pics{{display:flex;gap:8px;align-items:flex-start}} svg{{display:block;border:1px solid var(--line)}}
.gl{{stroke:#0002;stroke-width:.5}} .meta{{color:var(--muted);font-size:12px;margin-top:4px;max-width:260px}}
.err{{color:#c0392b;max-width:240px;font-size:12px}} details{{font-size:12px;max-width:260px}} pre{{white-space:pre-wrap;font-size:10px}}
.score button{{border:1px solid var(--line);background:none;color:var(--ink);width:28px;height:24px;margin:4px 2px 0 0;border-radius:4px;cursor:pointer}}
.score button.on{{background:var(--sel);color:#fff;border-color:var(--sel)}}
#summary td,#summary th{{text-align:right}} #summary th:first-child,#summary td:first-child{{text-align:left}}
.wrap{{overflow-x:auto}} button.copy{{margin:8px 0 20px;padding:6px 12px;border-radius:6px;border:1px solid var(--line);background:var(--card);color:var(--ink);cursor:pointer}}
</style></head><body>
<h1>Shape test · {html.escape(settings['stamp'])}</h1>
<div class="sub">{settings['grid']}×{settings['grid']} grid · chart preview {settings['chart']} stitches wide · thinking {'on' if settings['think'] else 'off'} · palette: {html.escape(', '.join(n for n, _ in palette))}<br>
Score each result 1–3 for how recognizable it is (1 = no idea, 2 = with the keyword, 3 = at a glance). Scores stay in this browser.</div>
<div class="wrap"><table id="summary"><thead><tr><th>Model</th><th>Valid JSON</th><th>Usable drawing</th><th>Shapes on grid</th><th>Median time</th><th>Recognizable (1–3)</th></tr></thead><tbody></tbody></table></div>
<button class="copy">Copy summary as Markdown</button>
<div class="wrap"><table><thead><tr><th></th>{head}</tr></thead><tbody>{''.join(rows)}</tbody></table></div>
<script>
const DATA={data}, MODELS={json.dumps(models)}, STORE='shape-test-{settings['stamp']}';
let scores={{}}; try{{scores=JSON.parse(localStorage.getItem(STORE)||'{{}}')}}catch(e){{}}
function save(){{try{{localStorage.setItem(STORE,JSON.stringify(scores))}}catch(e){{}}}}
function pct(a){{return a.length?Math.round(100*a.filter(Boolean).length/a.length)+'%':'–'}}
function rows(){{return MODELS.map(m=>{{const r=DATA.filter(d=>d.model===m);
 const t=r.filter(d=>d.valid).map(d=>d.seconds).sort((a,b)=>a-b); const med=t.length?t[Math.floor(t.length/2)].toFixed(1)+'s':'–';
 const sc=r.map(d=>scores[m+'|'+d.keyword]).filter(Boolean); const avg=sc.length?(sc.reduce((a,b)=>a+b,0)/sc.length).toFixed(2)+' ('+sc.length+'/'+r.length+')':'–';
 const ib=r.filter(d=>d.valid).map(d=>d.in_bounds); const ibp=ib.length?Math.round(100*ib.reduce((a,b)=>a+b,0)/ib.length)+'%':'–';
 return [m,pct(r.map(d=>d.valid)),pct(r.map(d=>d.ok)),ibp,med,avg]}})}}
function render(){{document.querySelector('#summary tbody').innerHTML=rows().map(r=>'<tr>'+r.map(c=>'<td>'+c+'</td>').join('')+'</tr>').join('');
 document.querySelectorAll('.score button').forEach(b=>b.classList.toggle('on',scores[b.dataset.k]==+b.dataset.v))}}
document.querySelectorAll('.score button').forEach(b=>b.onclick=()=>{{const k=b.dataset.k,v=+b.dataset.v;
 if(scores[k]===v)delete scores[k];else scores[k]=v;save();render()}});
document.querySelector('.copy').onclick=()=>{{const h=['Model','Valid JSON','Usable drawing','Shapes on grid','Median time','Recognizable (1–3)'];
 const md=['| '+h.join(' | ')+' |','|'+h.map(()=>'---').join('|')+'|',...rows().map(r=>'| '+r.join(' | ')+' |')].join('\\n');
 navigator.clipboard.writeText(md).then(()=>{{const b=document.querySelector('.copy');b.textContent='Copied';setTimeout(()=>b.textContent='Copy summary as Markdown',1500)}})}};
render();
</script></body></html>"""
    with open(path, "w", encoding="utf-8") as f:
        f.write(doc)


# ---- main ---------------------------------------------------------------------------------------

def main():
    ap = argparse.ArgumentParser(description="Compare Ollama models at drawing knitting motifs with shapes.")
    ap.add_argument("--models", default=",".join(DEFAULT_MODELS), help="comma-separated Ollama tags")
    ap.add_argument("--keywords", default=",".join(DEFAULT_KEYWORDS), help="comma-separated keywords")
    ap.add_argument("--url", default="http://localhost:11434")
    ap.add_argument("--think", action="store_true", help="let thinking models think first (slower)")
    ap.add_argument("--timeout", type=float, default=60, help="seconds per request")
    ap.add_argument("--temperature", type=float, default=0.6)
    ap.add_argument("--grid", type=int, default=32, help="drawing grid size (game default 32)")
    ap.add_argument("--chart", type=int, default=24, help="stitches wide for the chart preview")
    ap.add_argument("--min-shapes", type=int, default=4)
    ap.add_argument("--palette", default=None, help="YarnPalette .asset (default: found under Assets/)")
    ap.add_argument("--out", default=os.path.join(HERE, "results"))
    a = ap.parse_args()

    res_dir = os.path.join(PROJECT, "Assets", "Resources", "AI")
    template = open(os.path.join(res_dir, "ShapePrompt.txt"), encoding="utf-8").read()
    examples = json.load(open(os.path.join(res_dir, "ShapeExamples.json"), encoding="utf-8"))["examples"]
    palette = load_palette(a.palette or find_palette())
    names = [n.strip().lower() for n, _ in palette]
    print("Palette:", ", ".join(n for n, _ in palette))
    if len(palette) < 4:
        print(f"! Only {len(palette) - 1} drawing colours. Add more yarns to the palette for better motifs.")

    examples_text = build_examples(examples, palette, a.grid)
    schema = build_schema(palette, a.min_shapes)
    color_index = lambda n: names.index(str(n).strip().lower()) if str(n).strip().lower() in names else -1

    have = installed_models(a.url)
    models, keywords = [], [k.strip() for k in a.keywords.split(",") if k.strip()]
    for m in (m.strip() for m in a.models.split(",") if m.strip()):
        if m in have or (":" not in m and m + ":latest" in have):
            models.append(m)
        else:
            print(f"- skipping {m}: not pulled (ollama pull {m})")
    if not models:
        sys.exit("No models to test.")

    stamp = datetime.now().strftime("%Y-%m-%d_%H%M%S")
    out_dir = os.path.join(a.out, stamp)
    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(out_dir, "prompt_example.txt"), "w", encoding="utf-8") as f:
        f.write(build_prompt(template, keywords[0], palette, a.grid, examples_text))

    results = []
    raw_log = open(os.path.join(out_dir, "raw.jsonl"), "w", encoding="utf-8")
    for m in models:
        cl = Client(a.url, m, a.think, a.temperature, a.timeout)
        print(f"\n== {m}: loading...", end="", flush=True)
        _, err, t = cl.generate("Reply with an empty JSON object.", {"type": "object"}, timeout=300)
        print(f" {t:.1f}s" + (f" (warm-up error: {err})" if err else ""))
        for kw in keywords:
            data, err, secs = cl.generate(build_prompt(template, kw, palette, a.grid, examples_text), schema)
            r = {"model": m, "keyword": kw, "seconds": secs, "error": None, "raw": "", "parts": "",
                 "valid": False, "ok": False, "in_bounds": 0.0, "cells": [], "preview": [], "pcols": 0, "prows": 0,
                 "drawn": 0, "dropped": 0, "out_of_bounds": 0, "yarns": 0, "mirrored": False, "coverage": 0.0}
            if err:
                r["error"] = err
            else:
                r["raw"] = data.get("response", "")
                try:
                    design = json.loads(r["raw"])
                    if not isinstance(design, dict) or not isinstance(design.get("shapes"), list):
                        raise ValueError("no shapes list")
                    r["valid"] = True
                    r["parts"] = str(design.get("parts", ""))
                    cells, st = rasterize(design, a.grid, color_index)
                    r.update(st)
                    r["cells"] = cells
                    n = len(design["shapes"])
                    r["in_bounds"] = (n - st["out_of_bounds"]) / n if n else 0.0
                    r["preview"], r["pcols"], r["prows"] = chart_preview(cells, a.grid, a.chart)
                    r["ok"] = st["drawn"] > 0 and st["coverage"] >= 0.05
                    if not r["ok"]:
                        r["error"] = f"almost empty ({st['coverage']:.0%} filled)" if st["drawn"] else "no usable shapes"
                except ValueError as e:
                    r["error"] = f"bad JSON: {e}"
            raw_log.write(json.dumps({k: r[k] for k in ("model", "keyword", "seconds", "error", "raw")}) + "\n")
            results.append(r)
            flag = "ok " if r["ok"] else "ERR"
            print(f"  {flag} {kw:<12} {secs:5.1f}s  {r['drawn']:>2} shapes  {r['yarns']} yarns  {r['error'] or r['parts'][:60]}")
        cl.unload()
    raw_log.close()

    with open(os.path.join(out_dir, "results.csv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["model", "keyword", "seconds", "valid_json", "usable", "in_bounds", "shapes", "dropped", "yarns", "mirror", "coverage", "parts", "error"])
        for r in results:
            w.writerow([r["model"], r["keyword"], f"{r['seconds']:.2f}", r["valid"], r["ok"], f"{r['in_bounds']:.2f}", r["drawn"],
                        r["dropped"], r["yarns"], r["mirrored"], f"{r['coverage']:.2f}", r["parts"], r["error"] or ""])

    report = os.path.join(out_dir, "report.html")
    write_report(report, results, models, keywords, palette,
                 {"stamp": stamp, "grid": a.grid, "chart": a.chart, "think": a.think})
    print(f"\nReport: {report}")
    if sys.platform == "darwin":
        os.system(f'open "{report}"')


if __name__ == "__main__":
    main()
