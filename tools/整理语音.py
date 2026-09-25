# -*- coding: utf-8 -*-
r"""
把 mkextract 导出的原始语音整理成开箱可用的语音包：
  1) 每个事件只保留时长最长的那个媒体（人声本身，剔除同事件挂载的公共小音效）
  2) 台词类文件按游戏官方英文台词命名；音效类按事件名转写成可读英文
  3) 输出对照表 CSV（事件ID / 官方英文 / 官方简中 / 官方繁中 / 时长，UTF-8 BOM）
  4) 可选：每条台词一个 .srt（官方英文 + 官方简中）
  5) 可选：把主角台词拼成整段合集 wav + 同步 srt/ass
  6) 可选：打包成 zip

用法示例：
  python 整理语音.py --char Homelander --raw D:\out\wav --out D:\out\Homelander语音包
                    --locres-en D:\out\en.tsv --locres-zh D:\out\zh.tsv --locres-tw D:\out\tw.tsv
"""

import argparse
import array
import csv
import os
import re
import sys
import wave
import zipfile

# 有些台词的文本不在 vs_* 键上（在 FatalityXX.<角色> 之类命名空间里），这里手工兜底
SPECIAL = {
    "vs_Homelander_Fatality_A_Whoops": {
        "en": "whoops...",
        "zh": "哎呦...",
        "tw": "哎呦...",
        "name": "whoops... (Fatality A)",
    },
}

GAP_MS = 400          # 合集里每条之间的间隔
HEAD_KEEP_MS = 40     # 发声点前保留
TAIL_KEEP_MS = 120    # 发声点后保留


def log(msg):
    print(msg, flush=True)


def load_locres(path, only_vs=True):
    out = {}
    if not path or not os.path.exists(path):
        return out
    with open(path, "r", encoding="utf-8-sig", newline="") as fh:
        for row in csv.DictReader(fh, delimiter="\t"):
            key = row["key"]
            if only_vs and not key.startswith("vs_"):
                continue
            if key not in out:
                out[key] = row["value"].strip()
    return out


def wav_duration(path):
    with wave.open(path, "rb") as w:
        return w.getnframes() / w.getframerate()


def read_mono(path):
    with wave.open(path, "rb") as w:
        ch, rate, frames = w.getnchannels(), w.getframerate(), w.getnframes()
        raw = w.readframes(frames)
    data = array.array("h")
    data.frombytes(raw)
    if ch > 1:
        data = data[::ch]
    return rate, data


def speech_bounds(data, rate, win_ms=10):
    win = max(1, int(rate * win_ms / 1000))
    peaks = [max(abs(v) for v in data[i:i + win])
             for i in range(0, len(data) - win + 1, win)]
    if not peaks:
        return 0.0, len(data) / rate
    peak, floor = max(peaks), sorted(peaks)[len(peaks) // 10]
    thresh = max(floor * 4, peak * 0.02, 200)
    first = next((i for i, p in enumerate(peaks) if p > thresh), 0)
    last = next((i for i in range(len(peaks) - 1, -1, -1) if peaks[i] > thresh), len(peaks) - 1)
    return (first * win_ms / 1000.0,
            min(last * win_ms / 1000.0 + win_ms / 1000.0, len(data) / rate))


def srt_time(sec):
    ms = int(round(sec * 1000))
    h, ms = divmod(ms, 3600000)
    m, ms = divmod(ms, 60000)
    s, ms = divmod(ms, 1000)
    return f"{h:02d}:{m:02d}:{s:02d},{ms:03d}"


def ass_time(sec):
    cs = int(round(sec * 100))
    h, cs = divmod(cs, 360000)
    m, cs = divmod(cs, 6000)
    s, cs = divmod(cs, 100)
    return f"{h}:{m:02d}:{s:02d}.{cs:02d}"


def sanitize(name, maxlen=90):
    s = re.sub(r'[\\/:*?"<>|]', "", name)
    s = re.sub(r"\s+", " ", s).strip().rstrip(".")
    if len(s) > maxlen:
        s = s[:maxlen].strip()
    return s


def readable(event, char):
    s = re.sub(r"^(vo|sfx|mus)_", "", event, flags=re.I)
    s = re.sub(re.escape(char), "", s, flags=re.I)
    s = s.replace("_", " ")
    s = re.sub(r"(?<=[a-z])(?=[A-Z])", " ", s)
    s = s.replace("CharSelect", "Character Select").replace("FStyle", "F-Style")
    return re.sub(r"\s+", " ", s).strip(" -_")


CATEGORY_LABEL = {
    "01": "对战台词",
    "02": "用力声_吼叫",
    "03": "招式音效",
    "04": "无障碍解说_非本人",
    "05": "其他角色对{char}的台词",
    "06": "其他音效",
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--char", required=True, help="角色名（与导出时用的关键词一致）")
    ap.add_argument("--raw", required=True, help="mkextract voices 输出的 wav 目录")
    ap.add_argument("--out", required=True, help="整理后的输出目录")
    ap.add_argument("--locres-en", default=None)
    ap.add_argument("--locres-zh", default=None)
    ap.add_argument("--locres-tw", default=None)
    ap.add_argument("--no-srt", action="store_true", help="不生成逐条字幕")
    ap.add_argument("--no-merge", action="store_true", help="不生成台词合集")
    ap.add_argument("--skip-cat", default="", help="跳过的分类编号，逗号分隔，如 04")
    ap.add_argument("--zip", action="store_true", help="输出后打包 zip")
    args = ap.parse_args()

    char, raw_dir, out_root = args.char, args.raw, args.out
    skip = {c.strip() for c in args.skip_cat.split(",") if c.strip()}

    en = load_locres(args.locres_en)
    zh = load_locres(args.locres_zh)
    tw = load_locres(args.locres_tw)
    if not en:
        log("  [提示] 没有官方英文文本表，文件名将退回事件名转写")

    if not os.path.isdir(raw_dir):
        log(f"  [!] 找不到原始目录: {raw_dir}")
        return 1

    # 每个事件取最长的媒体
    picked = {}
    for cat_dir in sorted(os.listdir(raw_dir)):
        cat_path = os.path.join(raw_dir, cat_dir)
        if not os.path.isdir(cat_path):
            continue
        code = cat_dir.split("_")[0]
        if code in skip:
            continue
        for fname in sorted(os.listdir(cat_path)):
            if not fname.lower().endswith(".wav"):
                continue
            parts = fname[:-4].split("__")
            if len(parts) < 2:
                continue
            event, path = parts[1], os.path.join(cat_path, fname)
            dur = wav_duration(path)
            key = (cat_dir, event)
            if key not in picked or dur > picked[key][1]:
                picked[key] = (path, dur)

    rows, dialogue_items = [], []
    used = {}
    for (cat_dir, event), (path, dur) in sorted(picked.items()):
        code = cat_dir.split("_")[0]
        label = CATEGORY_LABEL.get(code, code).format(char=char)
        out_dir = os.path.join(out_root, f"{code}_{char}_{label}")

        official = en.get(event)
        special = SPECIAL.get(event)
        if special:
            official = special["en"]
        if code in ("01", "05") and special:
            base = sanitize(special["name"])
        elif code in ("01", "05") and official:
            base = sanitize(official)
        else:
            base = sanitize(readable(event, char)) or sanitize(event)
        if not base:
            base = sanitize(event)

        per_dir = used.setdefault(out_dir.lower(), {})
        n = per_dir.get(base.lower(), 0) + 1
        per_dir[base.lower()] = n
        name = base if n == 1 else f"{base} ({n})"

        os.makedirs(out_dir, exist_ok=True)
        dst = os.path.join(out_dir, name + ".wav")
        with open(path, "rb") as src, open(dst, "wb") as fh:
            fh.write(src.read())

        item = dict(cat=code, event=event, name=name, path=dst, dur=dur,
                    en=official or "",
                    zh=(special["zh"] if special else zh.get(event, "")),
                    tw=(special["tw"] if special else tw.get(event, "")))
        rows.append(item)
        if code == "01":
            dialogue_items.append(item)

    # 对照表
    csv_path = os.path.join(out_root, f"{char}_语音对照表.csv")
    with open(csv_path, "w", encoding="utf-8-sig", newline="") as fh:
        w = csv.writer(fh)
        w.writerow(["分类", "事件ID", "文件名", "时长秒", "官方英文", "官方简中", "官方繁中"])
        for it in sorted(rows, key=lambda x: (x["cat"], x["event"])):
            w.writerow([it["cat"], it["event"], it["name"] + ".wav",
                        f"{it['dur']:.2f}", it["en"], it["zh"], it["tw"]])

    # 逐条字幕
    srt_count = 0
    if not args.no_srt:
        for it in rows:
            if it["cat"] not in ("01", "05"):
                continue
            lines = [it["en"] or it["event"]]
            if it["zh"]:
                lines.append(it["zh"])
            with open(os.path.splitext(it["path"])[0] + ".srt", "w",
                      encoding="utf-8-sig", newline="") as fh:
                fh.write("1\r\n" + f"{srt_time(0)} --> {srt_time(it['dur'])}\r\n"
                         + "\r\n".join(lines) + "\r\n")
            srt_count += 1

    # 台词合集
    merged_seconds = 0.0
    if dialogue_items and not args.no_merge:
        merged = array.array("h")
        rate = None
        plan = []
        for idx, it in enumerate(sorted(dialogue_items, key=lambda x: x["name"]), 1):
            r, data = read_mono(it["path"])
            rate = rate or r
            start, end = speech_bounds(data, r)
            a = max(0, int((start - HEAD_KEEP_MS / 1000) * r))
            b = min(len(data), int((end + TAIL_KEEP_MS / 1000) * r))
            cue_start = len(merged) / r + (start - a / r)
            merged.extend(data[a:b])
            plan.append((idx, it, cue_start, len(merged) / r))
            if idx != len(dialogue_items):
                merged.extend(array.array("h", [0] * int(GAP_MS / 1000 * r)))
        merged_seconds = len(merged) / rate

        with wave.open(os.path.join(out_root, f"{char}_台词合集.wav"), "wb") as w:
            w.setnchannels(1)
            w.setsampwidth(2)
            w.setframerate(rate)
            w.writeframes(merged.tobytes())

        with open(os.path.join(out_root, f"{char}_台词合集.srt"), "w",
                  encoding="utf-8-sig", newline="") as fh:
            for idx, it, cs, ce in plan:
                fh.write(f"{idx}\r\n{srt_time(cs)} --> {srt_time(ce)}\r\n")
                fh.write((it["en"] or it["event"]) + "\r\n")
                if it["zh"]:
                    fh.write(it["zh"] + "\r\n")
                fh.write("\r\n")

        header = f"""[Script Info]
Title: {char} voice lines (MK1)
ScriptType: v4.00+
WrapStyle: 0
PlayResX: 1920
PlayResY: 1080
ScaledBorderAndShadow: yes

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,Microsoft YaHei,64,&H00FFFFFF,&H000000FF,&H00101010,&H80000000,0,0,0,0,100,100,0,0,1,3,1,2,40,40,90,1
Style: CN,Microsoft YaHei,52,&H0000D7FF,&H000000FF,&H00101010,&H80000000,0,0,0,0,100,100,0,0,1,3,1,2,40,40,40,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
"""
        with open(os.path.join(out_root, f"{char}_台词合集.ass"), "w",
                  encoding="utf-8-sig", newline="") as fh:
            fh.write(header.replace("\n", "\r\n"))
            for idx, it, cs, ce in plan:
                s, e = ass_time(cs), ass_time(ce)
                fh.write(f"Dialogue: 0,{s},{e},Default,,0,0,0,,{{\\c&H00FFFFFF&}}{it['en'] or it['event']}{{\\r}}\r\n")
                if it["zh"]:
                    fh.write(f"Dialogue: 0,{s},{e},CN,,0,0,0,,{it['zh']}\r\n")

    # 打个 zip
    zip_path = ""
    if args.zip:
        zip_path = os.path.join(out_root, f"{char}语音包.zip")
        if os.path.exists(zip_path):
            os.remove(zip_path)
        base = os.path.basename(os.path.normpath(out_root))
        with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
            for root, _dirs, files in os.walk(out_root):
                for f in files:
                    if f.endswith(".zip"):
                        continue
                    full = os.path.join(root, f)
                    rel = os.path.join(base, os.path.relpath(full, out_root))
                    z.write(full, rel)

    # 汇总
    by_cat = {}
    for it in rows:
        c = by_cat.setdefault(it["cat"], [0, 0.0])
        c[0] += 1
        c[1] += it["dur"]
    log("")
    log(f"  角色 {char}：共 {len(rows)} 条音频")
    for code in sorted(by_cat):
        n, d = by_cat[code]
        log(f"    {code}   {n:4d} 条   {d:7.1f} 秒")
    if srt_count:
        log(f"  逐条字幕 {srt_count} 个")
    if merged_seconds:
        log(f"  台词合集 {merged_seconds/60:.2f} 分钟")
    log(f"  对照表 {csv_path}")
    if zip_path:
        log(f"  ZIP    {zip_path}  ({os.path.getsize(zip_path)/1048576:.1f} MB)")
    log(f"[SUMMARY] files={len(rows)} srt={srt_count} merged={merged_seconds:.1f} zip={zip_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
