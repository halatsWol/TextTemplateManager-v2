// Placeholders ($[DATE], $[TIME(HH:mm)], ...) don't survive translation as they are: the model translates
// the names inside ($[TIME] -> $[ZEIT]). Numbered braces like {0} come back unchanged and in a sensible
// position, so placeholders travel as those and are put back afterwards.
const PLACEHOLDER = /\$\[[A-Za-z]+(?:\([^)\]]*\))?\]/g

export function maskPlaceholders(html) {
    // Number past any {n} already in the text, so unmasking never replaces one of the user's own.
    let base = 0
    for (const m of html.matchAll(/\{(\d+)\}/g)) base = Math.max(base, Number(m[1]) + 1)
    const placeholders = []
    const masked = html.replace(PLACEHOLDER, p => `{${base + placeholders.push(p) - 1}}`)
    return { html: masked, base, placeholders }
}

// Puts the placeholders back. One the model dropped is appended to the last paragraph, a duplicated
// one is kept only once, so the template ends up with exactly the placeholders it had.
export function unmaskPlaceholders(html, { base, placeholders }) {
    const used = new Set()
    let out = html.replace(/\{(\d+)\}/g, (token, n) => {
        const i = Number(n) - base
        if (i < 0 || i >= placeholders.length) return token
        if (used.has(i)) return ''
        used.add(i)
        return placeholders[i]
    })
    const missing = placeholders.filter((_, i) => !used.has(i))
    if (missing.length) {
        const tail = ' ' + missing.join(' ')
        const close = out.lastIndexOf('</p>')
        out = close >= 0 ? out.slice(0, close) + tail + out.slice(close) : out + tail
    }
    return { html: out, missing: missing.length }
}
