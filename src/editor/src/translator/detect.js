// Guesses the language of a template among the installed ones (franc: trigrams, plus the script for languages
// like Greek or Japanese). Short texts can't be told apart, so below a sentence or so there is no guess.
import { franc } from 'franc'

// The models' language codes and franc's ISO 639-3 ones. Basque and Icelandic franc doesn't know.
const ISO3 = {
    af: 'afr', ar: 'arb', az: 'azj', be: 'bel', bg: 'bul', bn: 'ben', bs: 'bos', ca: 'cat', cs: 'ces', da: 'dan', de: 'deu',
    el: 'ell', en: 'eng', es: 'spa', et: 'ekk', fa: 'pes', fi: 'fin', fr: 'fra', gl: 'glg', gu: 'guj', he: 'heb', hi: 'hin',
    hr: 'hrv', hu: 'hun', id: 'ind', it: 'ita', ja: 'jpn', kn: 'kan', ko: 'kor', lt: 'lit', lv: 'lvs', ml: 'mal', mr: 'mar',
    ms: 'zlm', nb: 'nob', nl: 'nld', nn: 'nno', pl: 'pol', pt: 'por', ro: 'ron', ru: 'rus', sk: 'slk', sl: 'slv', sq: 'als',
    sr: 'srp', sv: 'swe', ta: 'tam', te: 'tel', th: 'tha', tl: 'tgl', tr: 'tur', ug: 'uig', uk: 'ukr', ur: 'urd', vi: 'vie',
    'zh-Hans': 'cmn', 'zh-Hant': 'cmn',
}

// Characters needed for a guess; scripts without spaces between words say more per character.
const MIN_LENGTH = 20
const MIN_LENGTH_DENSE = 8
const DENSE = /[\u0e00-\u0e7f\u3040-\u30ff\u3400-\u9fff\uac00-\ud7af]/

// The code among `candidates` the text is written in, or null when it can't be told.
export function detectLanguage(text, candidates) {
    const clean = text.replace(/\$\[[^\]]*\]/g, ' ').replace(/\s+/g, ' ').trim()
    const min = DENSE.test(clean) ? MIN_LENGTH_DENSE : MIN_LENGTH
    if (clean.length < min) return null
    const known = candidates.filter(c => ISO3[c])
    if (known.length === 0) return null
    if (known.length === 1) return known[0]
    const iso = franc(clean, { only: [...new Set(known.map(c => ISO3[c]))], minLength: min })
    if (iso === 'und') return null
    // Simplified and Traditional Chinese share a code: tell them by a few characters only one of them uses.
    if (iso === 'cmn') {
        const hant = /[們這個說會來時對還開關國學點電]/.test(clean)
        const pick = hant ? 'zh-Hant' : 'zh-Hans'
        return known.includes(pick) ? pick : known.find(c => ISO3[c] === 'cmn')
    }
    return known.find(c => ISO3[c] === iso) ?? null
}
