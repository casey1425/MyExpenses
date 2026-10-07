#!/usr/bin/env python3
"""다크 모드용 색 토큰을 만듭니다.

1) 모든 CSS(Components/**, wwwroot/app.css)의 색 #rrggbb 를 var(--c-rrggbb, #rrggbb)로 바꿉니다.
   밝은 화면의 색은 그대로(기본값)이고, 이미 바뀐 곳은 건드리지 않습니다(여러 번 실행해도 같습니다).
2) wwwroot/theme.css 에 어두운 화면의 토큰 값을 씁니다. 밝기를 뒤집되 색조는 유지하므로
   글자와 배경의 대비가 밝은 화면과 같게 보존됩니다.

새 색을 CSS에 추가했다면 이 스크립트를 다시 실행하세요: python3 tools/theme/generate_theme.py
사이드바·요약(히어로)처럼 두 화면 모두 어두운 곳은 SKIP_SELECTOR/SKIP_FILES 로 제외합니다.
"""
import colorsys, pathlib, re, sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
SKIP_FILES = ("NavMenu", "NavItem", "NavGroup")
SKIP_SELECTOR = re.compile(r"\.hero|\.sidebar|\.add-circle|blazor-error|\.nav-")
# 흰색은 카드 면이므로 페이지 바탕(#0f1217)보다 살짝 밝게 둡니다.
FIXED = {"ffffff": "171b22"}
TOKEN = re.compile(r"var\(--c-[0-9a-f]+,\s*(?:#[0-9a-fA-F]+|white)\)|#([0-9a-fA-F]{3,8})\b|(?<![-\w])(white)(?![-\w(])")
RULE = re.compile(r"([^{}]+)\{([^{}]*)\}")


def expand(h):
    h = h.lower()
    return "".join(c * 2 for c in h) if len(h) in (3, 4) else h


def dark(hexv):
    if hexv[:6] in FIXED and len(hexv) == 6:
        return FIXED[hexv]
    rgb, alpha = hexv[:6], hexv[6:]
    r, g, b = (int(rgb[i:i + 2], 16) / 255 for i in (0, 2, 4))
    h, l, s = colorsys.rgb_to_hls(r, g, b)
    l2 = 0.085 + (1 - l) * 0.835
    s2 = s * 0.85
    r2, g2, b2 = colorsys.hls_to_rgb(h, l2, s2)
    return "%02x%02x%02x%s" % (round(r2 * 255), round(g2 * 255), round(b2 * 255), alpha)


def main(check=False):
    stale = []
    files = list((ROOT / "Components").rglob("*.css")) + [ROOT / "wwwroot" / "app.css", ROOT / "wwwroot" / "ui.css"]
    tokens = set()
    for p in files:
        if any(k in p.name for k in SKIP_FILES):
            continue
        text = p.read_bytes().decode("utf-8")  # 줄 끝(CRLF/LF) 보존

        def body(m):
            sel, decls = m.group(1), m.group(2)
            if SKIP_SELECTOR.search(sel):
                return m.group(0)

            def color(x):
                if x.group(2):  # 색 이름 white 는 #ffffff 와 같은 토큰으로 처리
                    tokens.add("ffffff")
                    return "var(--c-ffffff, white)"
                if x.group(1) is None:
                    tokens.update(re.findall(r"--c-([0-9a-f]+)", x.group(0)))
                    return x.group(0)
                if len(x.group(1)) not in (3, 4, 6, 8):
                    return x.group(0)
                h = expand(x.group(1))
                tokens.add(h)
                return f"var(--c-{h}, #{x.group(1)})"

            return sel + "{" + TOKEN.sub(color, decls) + "}"

        new = RULE.sub(body, text)
        if new != text:
            if check:
                stale.append(p.relative_to(ROOT).as_posix())
            else:
                p.write_bytes(new.encode("utf-8"))

    lines = [
        "/* 자동 생성: tools/theme/generate_theme.py — 직접 고치지 마세요. */",
        ":root[data-theme=\"dark\"] {",
        "    color-scheme: dark;",
    ]
    lines += [f"    --c-{t}: #{dark(t)};" for t in sorted(tokens)]
    lines += [
        "}",
        "",
        ":root[data-theme=\"dark\"] body { background: #0f1217; color: #e2e8f0; }",
        ":root[data-theme=\"dark\"] ::selection { background: #3a5f8a; color: #fff; }",
        ":root[data-theme=\"dark\"] img { filter: brightness(.92); }",
        "/* 중첩 CSS라 위 변환이 닿지 않는 다시 연결 창 */",
        ":root[data-theme=\"dark\"] #components-reconnect-modal { background-color: #171b22; color: #e2e8f0; }",
        "",
    ]
    target = ROOT / "wwwroot" / "theme.css"
    content = "\n".join(lines)
    if check:
        if not target.exists() or target.read_bytes().decode("utf-8") != content:
            stale.append("wwwroot/theme.css")
        if stale:
            print("다크 모드 색 토큰이 최신이 아닙니다(새 색을 추가했다면 python3 tools/theme/generate_theme.py 를 실행하세요):")
            for f in stale:
                print("  -", f)
            sys.exit(1)
        print(f"PASS: dark theme tokens up to date ({len(tokens)} colors, every color in the CSS has a dark counterpart)")
        return
    target.write_text(content, encoding="utf-8", newline="\n")
    print(f"토큰 {len(tokens)}개")


if __name__ == "__main__":
    main("--check" in sys.argv)
