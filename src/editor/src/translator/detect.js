// Guesses the language of a text among candidate languages (franc: trigrams, plus the script for languages like
// Greek or Japanese). Short texts can't be told apart, so below a sentence or so there is no guess.
import { francAll } from 'franc'

// Template language codes (the models' and ISO 639-1) and franc's ISO 639-3 ones; a few languages franc knows in
// several varieties. Basque, Icelandic, Irish or Welsh, for example, franc doesn't know.
const ISO3 = {
    af: 'afr', am: 'amh', ar: 'arb', ay: 'ayr', az: 'azj', be: 'bel', bg: 'bul', bm: 'bam', bn: 'ben', bo: 'bod', bs: 'bos',
    ca: 'cat', cs: 'ces', da: 'dan', de: 'deu', ee: 'ewe', el: 'ell', en: 'eng', eo: 'epo', es: 'spa', et: 'ekk', fa: 'pes',
    ff: ['fuv', 'fuf'], fi: 'fin', fr: 'fra', gl: 'glg', gu: 'guj', ha: 'hau', he: 'heb', hi: 'hin', hr: 'hrv', ht: 'hat',
    hu: 'hun', hy: 'hye', id: 'ind', ig: 'ibo', ii: 'iii', it: 'ita', ja: 'jpn', jv: 'jav', ka: 'kat', kk: 'kaz', km: 'khm',
    kn: 'kan', ko: 'kor', ku: 'ckb', ky: 'kir', lg: 'lug', ln: 'lin', lo: 'lao', lt: 'lit', lv: 'lvs', mg: 'plt', mk: 'mkd',
    ml: 'mal', mn: 'khk', mr: 'mar', ms: 'zlm', my: 'mya', nb: 'nob', ne: 'npi', nl: 'nld', nn: 'nno', ny: 'nya', pa: 'pan',
    pl: 'pol', ps: 'pbu', pt: 'por', qu: ['quz', 'quy', 'qug'], rn: 'run', ro: 'ron', ru: 'rus', rw: 'kin', sg: 'sag',
    si: 'sin', sk: 'slk', sl: 'slv', sn: 'sna', so: 'som', sq: 'als', sr: 'srp', ss: 'ssw', st: 'sot', sv: 'swe', sw: 'swh',
    ta: 'tam', te: 'tel', tg: 'tgk', th: 'tha', ti: 'tir', tk: 'tuk', tl: 'tgl', tn: 'tsn', tr: 'tur', ts: 'tso', tt: 'tat',
    ug: 'uig', uk: 'ukr', ur: 'urd', uz: 'uzn', ve: 'ven', vi: 'vie', wo: 'wol', xh: 'xho', yi: 'ydd', yo: 'yor', zu: 'zul',
    'zh-Hans': 'cmn', 'zh-Hant': 'cmn',
}
const iso3Of = (code) => [ISO3[code] ?? []].flat()

// Characters needed for a guess; scripts without spaces between words say more per character. Shorter texts down to
// the SHORT lengths get a guess only when no other language comes close, and it isn't sure: in a test of short
// greetings and phrases about one in seven of those was still wrong ("Kind regards" as Hungarian).
const MIN_LENGTH = 20
const MIN_LENGTH_DENSE = 8
const SHORT_LENGTH = 12
const SHORT_LENGTH_DENSE = 5
const SHORT_LEAD = 0.8
const DENSE = /[\u0e00-\u0e7f\u3040-\u30ff\u3400-\u9fff\uac00-\ud7af]/

// The code among `candidates` the text is written in, or null when it can't be told for sure.
export function detectLanguage(text, candidates) {
    const guess = guessLanguage(text, candidates)
    return guess?.sure ? guess.code : null
}

// { code, sure } for the language among `candidates` the text is written in, or null when there is no guess.
export function guessLanguage(text, candidates) {
    const clean = text.replace(/\$\[[^\]]*\]/g, ' ').replace(/\s+/g, ' ').trim()
    const dense = DENSE.test(clean)
    const sure = clean.length >= (dense ? MIN_LENGTH_DENSE : MIN_LENGTH)
    const min = dense ? SHORT_LENGTH_DENSE : SHORT_LENGTH
    if (clean.length < min) return null
    const known = [...new Set(candidates)].filter(c => ISO3[c])
    if (known.length === 0) return null
    if (known.length === 1) return { code: known[0], sure }
    const ranked = francAll(clean, { only: [...new Set(known.flatMap(iso3Of))], minLength: min })
    const iso = ranked[0][0]
    if (iso === 'und' || (!sure && ranked.length > 1 && ranked[1][1] > SHORT_LEAD)) return null
    const code = codeOf(iso, known, clean)
    return code ? { code, sure } : null
}

function codeOf(iso, known, clean) {
    // Simplified and Traditional Chinese share a code: tell them by a few characters only one of them uses.
    if (iso === 'cmn') {
        const hant = /[們這個說會來時對還開關國學點電]/.test(clean)
        const pick = hant ? 'zh-Hant' : 'zh-Hans'
        return known.includes(pick) ? pick : known.find(c => iso3Of(c).includes('cmn'))
    }
    return known.find(c => iso3Of(c).includes(iso)) ?? null
}
