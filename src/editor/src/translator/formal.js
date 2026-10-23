// The models have no formality setting and sometimes address the reader as "du". Put behind "Dear Sir or Madam,",
// a sentence comes out addressing them as "Sie" (measured: every one of 229 support sentences). That greeting
// rewords sentences that were fine already, so only lines that came out with "du" are translated again, each
// sentence behind the greeting, which is then taken off. The new line is kept only when every greeting came off,
// no "du" is left and it isn't much shorter (the greeting can make the model drop a clause); else the first one stays.
// Lines, not paragraphs: the engine takes a line break as the end of a sentence, and a greeting in front of a line
// that was fine ("Dear Ms/Mr @") changes it too ("Liebe Frau/Herr" instead of "Sehr geehrte Frau/Herr").
export const FORMAL_LANGUAGES = new Set(['de'])

const DU = /\b(du|dich|dir|dein|deine|deinen|deinem|deiner|deines)\b/i
const GREETING = 'Dear Sir or Madam, '
// How the greeting comes back: usually "Sehr geehrte Damen und Herren,", sometimes mixed up ("Sehr geehrte Frau
// oder Madam,").
const GREETING_BACK = /^(?:Sehr geehrte[rn]?|Liebe[rnms]?|Meine)\s+[^,.<>]{0,40}?(?:Damen|Herren|Herr|Frau|Sir|Madam)[^,.<>]{0,20}?,\s*/
const MIN_LENGTH_RATIO = 0.85
const BLOCK = /<(p|h[1-6])(\s[^>]*)?>([\s\S]*?)<\/\1>/g
const BR = /(<br\s*\/?>)/i
const isBr = (tag) => /^<br[\s/>]/i.test(tag)

const textOf = (html) => html.replace(/<[^>]*>/g, '')
const blocks = (html) => [...html.matchAll(BLOCK)].map(m => ({ start: m.index, end: m.index + m[0].length, open: `<${m[1]}${m[2] ?? ''}>`, close: `</${m[1]}>`, inner: m[3] }))

// A line can be translated on its own when it closes every tag it opens.
function balanced(html) {
    const open = [...html.matchAll(/<([a-z][a-z0-9]*)(?:\s[^>]*)?>/gi)].map(m => m[1].toLowerCase()).filter(t => t !== 'br')
    const close = [...html.matchAll(/<\/([a-z][a-z0-9]*)>/gi)].map(m => m[1].toLowerCase())
    return open.length === close.length && open.every(t => close.includes(t))
}

// Puts the greeting in front of every sentence of a paragraph's HTML (in its text, not inside tags; a line break
// starts a sentence). Returns the HTML and how many were put in.
export function addGreetings(inner) {
    let out = '', tag = null, sentenceStart = true, count = 0, afterStop = false
    for (let i = 0; i < inner.length; i++) {
        const c = inner[i]
        if (tag != null) {
            tag += c
            out += c
            if (c === '>') { if (isBr(tag)) sentenceStart = true; tag = null }
            continue
        }
        if (c === '<') { tag = c; out += c; continue }
        if (afterStop) { sentenceStart = /\s/.test(c); afterStop = false }
        if (sentenceStart && /[\p{L}\p{N}{"'(]/u.test(c)) {
            out += GREETING
            count++
            sentenceStart = false
            // A normal word starts lowercase after the greeting; "I", acronyms and names stay as they are.
            out += /[A-Z]/.test(c) && /[a-z]/.test(inner[i + 1] ?? '') && !/^I\b/.test(inner.slice(i)) ? c.toLowerCase() : c
            continue
        }
        if (/[.!?]/.test(c)) afterStop = true
        out += c
    }
    return { html: out, count }
}

// Takes the greeting off the start of each sentence (one per sentence, so a greeting the text itself has stays) and
// capitalizes what follows. Returns the HTML and how many came off.
export function removeGreetings(inner) {
    let out = '', i = 0, removed = 0, sentenceStart = true, capitalize = false
    while (i < inner.length) {
        if (inner[i] === '<') {
            const end = inner.indexOf('>', i)
            const tag = inner.slice(i, end < 0 ? inner.length : end + 1)
            if (isBr(tag)) sentenceStart = true
            out += tag
            i += tag.length
            continue
        }
        if (sentenceStart && !/\s/.test(inner[i])) {
            sentenceStart = false
            const m = GREETING_BACK.exec(inner.slice(i))
            if (m) {
                removed++
                i += m[0].length
                capitalize = true
                continue
            }
        }
        if (capitalize && !/\s/.test(inner[i])) {
            capitalize = false
            out += inner[i++].toUpperCase()
            continue
        }
        if (/[.!?]/.test(inner[i]) && /\s/.test(inner[i + 1] ?? '')) sentenceStart = true
        out += inner[i++]
    }
    return { html: out, removed }
}

// The formal version of one line (or paragraph), or null when the first translation should stay.
function formalLine(source, plain, translate) {
    const greeted = addGreetings(source)
    const again = /^<p>([\s\S]*)<\/p>$/.exec(translate(`<p>${greeted.html}</p>`))
    if (!again) return null
    const formal = removeGreetings(again[1])
    const keep = formal.removed === greeted.count && !DU.test(textOf(formal.html))
        && textOf(formal.html).length >= MIN_LENGTH_RATIO * textOf(plain).length
    return keep ? formal.html : null
}

// Translates German formally: `translate(html)` is the engine's translation into German.
export function translateFormal(source, translate) {
    const plain = translate(source)
    if (!DU.test(textOf(plain))) return plain
    const from = blocks(source), to = blocks(plain)
    if (from.length !== to.length) return plain
    let out = plain
    for (let i = to.length - 1; i >= 0; i--) {
        if (!DU.test(textOf(to[i].inner))) continue
        const sourceLines = from[i].inner.split(BR), plainLines = to[i].inner.split(BR)
        let inner
        if (sourceLines.length > 1 && sourceLines.length === plainLines.length && sourceLines.every(balanced) && plainLines.every(balanced)) {
            // split() with a capture group keeps the <br>s at the odd places.
            inner = plainLines.map((line, k) => k % 2 === 1 || !DU.test(textOf(line)) ? line
                : formalLine(sourceLines[k], line, translate) ?? line).join('')
        } else {
            inner = formalLine(from[i].inner, to[i].inner, translate) ?? to[i].inner
        }
        out = out.slice(0, to[i].start) + to[i].open + inner + to[i].close + out.slice(to[i].end)
    }
    return out
}
