// The engine puts tags back by word alignment, and a tagged word aligned to two places comes back tagged twice:
// "find it <a>here</a>" becomes "<a>Die</a> Kontaktdaten … <a>hier</a>". Where an inline element (same tag and
// attributes) comes back more often than it went in, copies right next to each other are merged, and of the rest
// the ones closest to the originals' places in the text are kept and the others unwrapped.
const INLINE = ['a', 'strong', 'b', 'em', 'i', 'u', 's', 'code', 'mark', 'sub', 'sup', 'span']

function elements(html) {
    const found = []
    for (const tag of INLINE) {
        const re = new RegExp(`<${tag}(\\s[^>]*)?>((?:(?!<${tag}[\\s>])[\\s\\S])*?)</${tag}>`, 'g')
        for (const m of html.matchAll(re)) found.push({ key: `${tag}${m[1] ?? ''}`, tag, start: m.index, end: m.index + m[0].length, inner: m[2] })
    }
    return found
}

// Where in the text (0 to 1, tags not counted) a position of the HTML lies.
function place(html, index) {
    const textLength = (s) => s.replace(/<[^>]*>/g, '').length
    return textLength(html.slice(0, index)) / Math.max(1, textLength(html))
}

function count(list) {
    const n = new Map()
    for (const e of list) n.set(e.key, (n.get(e.key) ?? 0) + 1)
    return n
}

// Copies with only whitespace between them become one element.
function mergeAdjacent(html, key) {
    const copies = elements(html).filter(e => e.key === key).sort((a, b) => a.start - b.start)
    for (let i = copies.length - 1; i > 0; i--) {
        const a = copies[i - 1], b = copies[i]
        const gap = html.slice(a.end, b.start)
        if (!/^\s*$/.test(gap)) continue
        const merged = html.slice(a.start, a.end - `</${a.tag}>`.length) + gap + b.inner + `</${a.tag}>`
        html = html.slice(0, a.start) + merged + html.slice(b.end)
        copies[i - 1] = { ...a, end: a.start + merged.length, inner: a.inner + gap + b.inner }
    }
    return html
}

export function repairInline(source, translated) {
    const want = count(elements(source))
    let out = translated
    for (const [key, have] of count(elements(out))) {
        if (have <= (want.get(key) ?? 0)) continue
        out = mergeAdjacent(out, key)
        const copies = elements(out).filter(e => e.key === key)
        const originals = elements(source).filter(e => e.key === key)
        if (copies.length <= originals.length) continue
        const keep = new Set()
        for (const o of originals) {
            const p = place(source, o.start)
            let best = null
            for (const c of copies) {
                if (keep.has(c)) continue
                if (!best || Math.abs(place(out, c.start) - p) < Math.abs(place(out, best.start) - p)) best = c
            }
            if (best) keep.add(best)
        }
        // Unwrap from the end, so earlier positions stay valid.
        for (const c of copies.filter(c => !keep.has(c)).sort((a, b) => b.start - a.start))
            out = out.slice(0, c.start) + c.inner + out.slice(c.end)
    }
    return out
}
