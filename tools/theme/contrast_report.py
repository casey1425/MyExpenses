#!/usr/bin/env python3
"""흰 배경 기준으로 글자 색(color:)의 대비를 점검합니다. 4.5:1 미만이면 목록에 올립니다.

사용법: python3 tools/theme/contrast_report.py [--fix]
--fix 를 주면 어두운 면(히어로·사이드바 등)이 아닌 규칙의 글자 색을 같은 색조로 진하게 고쳐 씁니다.
"""
import colorsys, pathlib, re, sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
SKIP_FILES = ("NavMenu", "NavItem", "NavGroup", "ReconnectModal")
# 배경이 어두운 규칙(흰 글자·연한 글자가 의도된 곳)
SKIP_SELECTOR = re.compile(r"\.hero|\.sidebar|\.add-circle|blazor-error|\.primary|\.add-button|\.edit-save|\.more-button|\.selected|\.start-button|\.empty-action|\.today-chip|\.cta|\.btn|\.google-login")


def lum(hexv):
    r, g, b = (int(hexv[i:i + 2], 16) / 255 for i in (0, 2, 4))
    f = lambda c: c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4
    return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b)


def contrast_white(hexv):
    return 1.05 / (lum(hexv) + 0.05)


def expand(h):
    h = h.lstrip("#").lower()
    return "".join(c * 2 for c in h) if len(h) in (3, 4) else h


def darken(hexv, target=4.6):
    r, g, b = (int(hexv[i:i + 2], 16) / 255 for i in (0, 2, 4))
    h, l, s = colorsys.rgb_to_hls(r, g, b)
    while l > 0 and contrast_white("%02x%02x%02x" % tuple(round(x * 255) for x in colorsys.hls_to_rgb(h, l, s))) < target:
        l -= 0.005
    return "#%02x%02x%02x" % tuple(round(x * 255) for x in colorsys.hls_to_rgb(h, l, s))


def files():
    for p in list((ROOT / "Components").rglob("*.css")) + [ROOT / "wwwroot" / "app.css", ROOT / "wwwroot" / "ui.css"]:
        if not any(k in p.name for k in SKIP_FILES):
            yield p


def main(fix):
    rule = re.compile(r"([^{}]+)\{([^{}]*)\}")
    decl = re.compile(r"(?<![-\w])color:\s*(#[0-9a-fA-F]{3,8})\b")
    seen = {}
    for p in files():
        text = p.read_bytes().decode("utf-8")  # 줄 끝(CRLF/LF)을 그대로 보존

        def patch(m):
            sel, body = m.group(1), m.group(2)
            if SKIP_SELECTOR.search(sel) or "background" in body and re.search(r"background[^;]*#(0|1|2)[0-9a-f]{5}\b", body):
                return m.group(0)

            def d(x):
                c = expand(x.group(1))[:6]
                ratio = contrast_white(c)
                # 흰색에 가까운 글자는 어두운 배경 위에 쓰는 것이므로 건드리지 않습니다.
                if ratio < 4.5 and ratio > 1.4 and not re.search(r"background[^;]*#(?!f|e)[0-9a-f]{6}\b", body):
                    new = darken(c)
                    seen.setdefault((p.relative_to(ROOT).as_posix(), sel.strip()[:50], x.group(1)), (round(ratio, 2), new))
                    return x.group(0).replace(x.group(1), new) if fix else x.group(0)
                return x.group(0)

            return sel + "{" + decl.sub(d, body) + "}"

        new = rule.sub(patch, text)
        if fix and new != text:
            p.write_bytes(new.encode("utf-8"))
    for (f, sel, old), (ratio, new) in sorted(seen.items()):
        print(f"{f}: {sel!r} {old} ({ratio}:1) -> {new}")
    if fix:
        print(f"{len(seen)}개 수정")
    elif seen:
        print(f"글자 색 대비가 4.5:1 미만인 곳이 {len(seen)}개 있습니다. --fix 로 고치거나 직접 진하게 바꿔 주세요.")
        sys.exit(1)
    else:
        print("PASS: text colors keep at least 4.5:1 contrast on light surfaces")


if __name__ == "__main__":
    main("--fix" in sys.argv)
