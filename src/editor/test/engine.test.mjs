// Runs the real engine (Assets\translator) on downloaded models. Skipped unless TTM_TEST_MODELS points to a
// folder laid out like the app's TranslationModels folder with en-de and de-en installed.
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync, existsSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import vm from 'node:vm'
import { Engine, route } from '../src/translator/engine.js'

const here = dirname(fileURLToPath(import.meta.url))
const engineDir = resolve(here, '..', '..', 'TextTemplateManager', 'Assets', 'translator')
const models = process.env.TTM_TEST_MODELS
const skip = !models || !existsSync(resolve(models, 'en-de', 'model.json')) || !existsSync(resolve(models, 'de-en', 'model.json'))

test('route goes through English when neither side is English', () => {
    assert.deepEqual(route('en', 'de'), [['en', 'de']])
    assert.deepEqual(route('fr', 'en'), [['fr', 'en']])
    assert.deepEqual(route('de', 'fr'), [['de', 'en'], ['en', 'fr']])
    assert.deepEqual(route('de', 'de'), [])
})

async function startEngine() {
    const context = vm.createContext({ console, WebAssembly, performance, setTimeout, TextDecoder, TextEncoder, URL })
    vm.runInContext(readFileSync(resolve(engineDir, 'bergamot-translator.js'), 'utf8'), context)
    const bergamot = await new Promise((done, fail) => {
        const module = context.loadBergamot({
            wasmBinary: readFileSync(resolve(engineDir, 'bergamot-translator.wasm')),
            print: () => {}, printErr: () => {},
            onAbort: fail,
            onRuntimeInitialized: () => Promise.resolve().then(() => done(module)),
        })
    })
    const engine = new Engine(bergamot)
    for (const pair of ['en-de', 'de-en']) {
        const { name, files } = JSON.parse(readFileSync(resolve(models, pair, 'model.json'), 'utf8'))
        const buffers = {}
        for (const [type, file] of Object.entries(files)) {
            const bytes = readFileSync(resolve(models, pair, file))
            buffers[type] = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength)
        }
        engine.load(...pair.split('-'), buffers, name)
    }
    return engine
}

test('translates HTML and keeps markup and placeholders', { skip }, async () => {
    const engine = await startEngine()
    const out = engine.translate('<p>Thank you for your <strong>quick</strong> reply. I will call you on $[DATE] at $[TIME(HH:mm)].</p>', 'en', 'de')
    assert.match(out.html, /^<p>.*<strong>schnelle<\/strong>.*<\/p>$/)
    assert.ok(out.html.includes('$[DATE]') && out.html.includes('$[TIME(HH:mm)]'), out.html)
    assert.equal(out.missing, 0)

    const back = engine.translate('<p>Bitte senden Sie die Unterlagen bis spätestens $[DATE(DD.MM.YYYY)].</p>', 'de', 'en')
    assert.ok(back.html.includes('$[DATE(DD.MM.YYYY)]'), back.html)
    assert.match(back.html, /documents/)
})
