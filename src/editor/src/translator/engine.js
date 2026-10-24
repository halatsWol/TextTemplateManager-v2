// Thin wrapper around the Bergamot wasm module (Firefox Translations). Settings follow Firefox's
// translations-engine.worker.js, which these models are built and tested with.
import { maskPlaceholders, unmaskPlaceholders } from './mask.js'
import { repairInline } from './inline.js'
import { FORMAL, FORMAL_LANGUAGES, INFORMAL, translateAddressing } from './formal.js'

const ALIGNMENT = { model: 256, lex: 64, vocab: 64, srcvocab: 64, trgvocab: 64 }

// Marian reads this as YAML and needs the indentation.
function config(options) {
    const indent = '            '
    let text = '\n'
    for (const [key, value] of Object.entries(options)) text += `${indent}${key}: ${value}\n`
    return text + indent
}

// Pairs only exist to and from English; anything else goes through it.
export function route(from, to) {
    if (from === to) return []
    if (from === 'en' || to === 'en') return [[from, to]]
    return [[from, 'en'], ['en', to]]
}

export class Engine {
    constructor(bergamot) {
        this.bergamot = bergamot
        this.service = new bergamot.BlockingService({ cacheSize: 0 })
        this.models = new Map()
    }

    has(from, to) { return this.models.has(`${from}-${to}`) }

    // files: { model, lex, vocab } or { model, lex, srcvocab, trgvocab } as ArrayBuffers; modelName is
    // the registry file name of the model, which tells its gemm precision.
    load(from, to, files, modelName) {
        const b = this.bergamot
        const memory = {}
        for (const [type, buffer] of Object.entries(files)) {
            if (!buffer) continue
            const aligned = new b.AlignedMemory(buffer.byteLength, ALIGNMENT[type])
            aligned.getByteArrayView().set(new Uint8Array(buffer))
            memory[type] = aligned
        }
        const vocabs = new b.AlignedMemoryList()
        if (memory.vocab) vocabs.push_back(memory.vocab)
        else { vocabs.push_back(memory.srcvocab); vocabs.push_back(memory.trgvocab) }

        const model = new b.TranslationModel(from, to, config({
            'beam-size': '1',
            normalize: '1.0',
            'word-penalty': '0',
            'max-length-break': '128',
            'mini-batch-words': '1024',
            workspace: '128',
            'max-length-factor': '2.0',
            'skip-cost': 'true',
            'cpu-threads': '0',
            quiet: 'true',
            'quiet-translation': 'true',
            'gemm-precision': modelName.endsWith('intgemm8.bin') ? 'int8shiftAll' : 'int8shiftAlphaAll',
            alignment: 'soft',
        }), memory.model, memory.lex ?? null, vocabs, null)
        this.models.get(`${from}-${to}`)?.delete()
        this.models.set(`${from}-${to}`, model)
    }

    // Translates editor HTML from one language to another; the needed models must be loaded. `address` ('formal' or
    // 'informal') asks for that form where the target language has one the models can be steered to (see formal.js).
    translate(html, from, to, { address = null } = {}) {
        const steps = route(from, to)
        if (steps.length === 0) return { html, missing: 0 }
        const models = steps.map(([f, t]) => {
            const m = this.models.get(`${f}-${t}`)
            if (!m) throw new Error(`model ${f}-${t} not loaded`)
            return m
        })

        const masked = maskPlaceholders(html)
        let out
        const form = { formal: FORMAL, informal: INFORMAL }[address]
        if (form && FORMAL_LANGUAGES.has(to)) {
            // Through English first, so the last step can translate lines again on their own.
            const english = models.length === 2 ? this.run(masked.html, [models[0]]) : masked.html
            out = translateAddressing(english, (s) => this.run(s, [models.at(-1)]), form)
        } else {
            out = this.run(masked.html, models)
        }
        return unmaskPlaceholders(repairInline(masked.html, out), masked)
    }

    run(html, models) {
        const b = this.bergamot
        const messages = new b.VectorString()
        const options = new b.VectorResponseOptions()
        let responses
        try {
            messages.push_back(html)
            options.push_back({ qualityScores: false, alignment: true, html: true })
            responses = models.length === 1
                ? this.service.translate(models[0], messages, options)
                : this.service.translateViaPivoting(models[0], models[1], messages, options)
            return responses.get(0).getTranslatedText()
        } finally {
            messages.delete()
            options.delete()
            responses?.delete()
        }
    }
}
