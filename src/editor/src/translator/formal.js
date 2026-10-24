// The models have no formality setting: German comes out mostly with "Sie", sometimes with "du". A hidden greeting
// steers a sentence. Behind "Dear Sir or Madam," it says "Sie" (every one of 229 support sentences); between
// "Hey Tom," and ", Tom" it says "du", but only for about half of them: the models lean hard towards "Sie", so
// informal is best effort and a template can come out mixed. The greeting rewords sentences that were fine already,
// so only lines that came out in the other form are translated again, each sentence with the greeting, which is
// then taken off. The new line is kept only when every greeting came off, the other form is gone and the line isn't
// much shorter (the greeting can make the model drop a clause); else the first one stays.
// Lines, not paragraphs: the engine takes a line break as the end of a sentence, and a greeting in front of a line
// that was fine ("Dear Ms/Mr @") changes it too ("Liebe Frau/Herr" instead of "Sehr geehrte Frau/Herr").
export const FORMAL_LANGUAGES = new Set(['de'])

const DU = /\b(du|dich|dir|dein|deine|deinen|deinem|deiner|deines)\b/i
const SIE = /\b(Sie|Ihnen|Ihr|Ihre|Ihren|Ihrem|Ihrer|Ihres)\b/

export const FORMAL = {
    other: DU,
    prefix: 'Dear Sir or Madam, ',
    // How it comes back: usually "Sehr geehrte Damen und Herren,", sometimes mixed up ("Sehr geehrte Frau oder Madam,").
    prefixBack: /^(?:Sehr geehrte[rn]?|Liebe[rnms]?|Meine)\s+[^,.<>]{0,40}?(?:Damen|Herren|Herr|Frau|Sir|Madam)[^,.<>]{0,20}?,\s*/,
}

export const INFORMAL = {
    other: SIE,
    prefix: 'Hey Tom, ',
    suffix: ', Tom',
    prefixBack: /^(?:Hey|Hallo|Hi)\s+Tom\s*,\s*/,
    suffixBack: /^,\s*Tom(?=\s*[.!?]|\s*$|\s*<)/,
}

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

// Puts the greeting around every sentence of a paragraph's HTML (in its text, not inside tags; a line break starts
// a sentence): the prefix in front, the suffix, if any, before its closing punctuation or at the end of its line.
// Returns the HTML and how many sentences got one.
export function addGreetings(inner, form = FORMAL) {
    const suffix = form.suffix ?? ''
    let out = '', tag = null, sentenceStart = true, inSentence = false, count = 0, afterStop = false
    const close = () => { if (inSentence) { out += suffix; inSentence = false } }
    for (let i = 0; i < inner.length; i++) {
        const c = inner[i]
        if (tag != null) {
            tag += c
            if (c === '>') {
                if (isBr(tag)) { close(); sentenceStart = true; afterStop = false }
                out += tag
                tag = null
            }
            continue
        }
        if (c === '<') { tag = c; continue }
        if (afterStop) { sentenceStart = /\s/.test(c); afterStop = false }
        if (sentenceStart && /[\p{L}\p{N}{"'(]/u.test(c)) {
            out += form.prefix
            count++
            sentenceStart = false
            inSentence = true
            // A normal word starts lowercase after the greeting; "I", acronyms and names stay as they are.
            out += /[A-Z]/.test(c) && /[a-z]/.test(inner[i + 1] ?? '') && !/^I\b/.test(inner.slice(i)) ? c.toLowerCase() : c
            continue
        }
        if (/[.!?]/.test(c) && /^(\s|<|$)/.test(inner.slice(i + 1))) {
            close()
            afterStop = true
        }
        out += c
    }
    if (tag != null) out += tag
    close()
    return { html: out, count }
}

// Takes the greeting off each sentence (one per sentence, so a greeting the text itself has stays) and capitalizes
// what follows the prefix. Returns the HTML and how many came off; with a suffix, only sentences that lost both count.
export function removeGreetings(inner, form = FORMAL) {
    let out = '', i = 0, prefixes = 0, suffixes = 0, sentenceStart = true, capitalize = false
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
            const m = form.prefixBack.exec(inner.slice(i))
            if (m) {
                prefixes++
                i += m[0].length
                capitalize = true
                continue
            }
        }
        if (form.suffixBack && inner[i] === ',') {
            const m = form.suffixBack.exec(inner.slice(i))
            if (m) { suffixes++; i += m[0].length; continue }
        }
        if (capitalize && !/\s/.test(inner[i])) {
            capitalize = false
            out += inner[i++].toUpperCase()
            continue
        }
        if (/[.!?]/.test(inner[i]) && /\s/.test(inner[i + 1] ?? '')) sentenceStart = true
        out += inner[i++]
    }
    return { html: out, removed: form.suffixBack ? Math.min(prefixes, suffixes) : prefixes }
}

// The line (or paragraph) in the wanted form, or null when the first translation should stay.
function steerLine(source, plain, translate, form) {
    const greeted = addGreetings(source, form)
    const again = /^<p>([\s\S]*)<\/p>$/.exec(translate(`<p>${greeted.html}</p>`))
    if (!again) return null
    const back = removeGreetings(again[1], form)
    const keep = back.removed === greeted.count && !form.other.test(textOf(back.html))
        && textOf(back.html).length >= MIN_LENGTH_RATIO * textOf(plain).length
    return keep ? back.html : null
}

// Translates into German in one form of address (FORMAL or INFORMAL): `translate(html)` is the engine's
// translation into German.
export function translateAddressing(source, translate, form) {
    const plain = translate(source)
    if (!form.other.test(textOf(plain))) return plain
    const from = blocks(source), to = blocks(plain)
    if (from.length !== to.length) return plain
    let out = plain
    for (let i = to.length - 1; i >= 0; i--) {
        if (!form.other.test(textOf(to[i].inner))) continue
        const sourceLines = from[i].inner.split(BR), plainLines = to[i].inner.split(BR)
        let inner
        if (sourceLines.length > 1 && sourceLines.length === plainLines.length && sourceLines.every(balanced) && plainLines.every(balanced)) {
            // split() with a capture group keeps the <br>s at the odd places.
            inner = plainLines.map((line, k) => k % 2 === 1 || !form.other.test(textOf(line)) ? line
                : steerLine(sourceLines[k], line, translate, form) ?? line).join('')
        } else {
            inner = steerLine(from[i].inner, to[i].inner, translate, form) ?? to[i].inner
        }
        out = out.slice(0, to[i].start) + to[i].open + inner + to[i].close + out.slice(to[i].end)
    }
    return out
}

export const translateFormal = (source, translate) => translateAddressing(source, translate, FORMAL)
