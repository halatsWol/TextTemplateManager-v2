// Runs the translation engine off the editor's thread. The engine comes from the app's Assets\translator
// folder (translator host), the installed language models from the models host, one folder per pair
// ("en-de") with a model.json naming its files. Both are virtual hosts the editor's WebView2 maps.
import { Engine, route } from './engine.js'

const ENGINE = 'https://ttm-translator.example/'
const MODELS = 'https://ttm-models.example/'

let enginePromise = null

function engine() {
    enginePromise ??= (async () => {
        importScripts(`${ENGINE}bergamot-translator.js`)
        const wasmBinary = await (await fetch(`${ENGINE}bergamot-translator.wasm`)).arrayBuffer()
        const bergamot = await new Promise((resolve, reject) => {
            const module = loadBergamot({
                // Firefox starts small and lets the heap grow; that ends up using less memory.
                INITIAL_MEMORY: 41_943_040,
                wasmBinary,
                print: () => {},
                printErr: () => {},
                onAbort: () => reject(new Error('engine failed to load')),
                onRuntimeInitialized: () => Promise.resolve().then(() => resolve(module)),
            })
        })
        return new Engine(bergamot)
    })()
    enginePromise.catch(() => { enginePromise = null })
    return enginePromise
}

class MissingModel extends Error {
    constructor(pair) { super(`language pair ${pair} is not installed`); this.pair = pair }
}

async function ensureModel(e, from, to) {
    if (e.has(from, to)) return
    const pair = `${from}-${to}`
    const info = await fetch(`${MODELS}${pair}/model.json`).catch(() => null)
    if (!info || !info.ok) throw new MissingModel(pair)
    const { name, files } = await info.json()
    const buffers = {}
    for (const [type, file] of Object.entries(files)) {
        const r = await fetch(`${MODELS}${pair}/${file}`)
        if (!r.ok) throw new MissingModel(pair)
        buffers[type] = await r.arrayBuffer()
    }
    e.load(from, to, buffers, name)
}

// Requests run one at a time: the engine call blocks this thread anyway.
let queue = Promise.resolve()

self.onmessage = ({ data }) => {
    const { id, html, from, to } = data
    queue = queue.then(async () => {
        try {
            const e = await engine()
            for (const [f, t] of route(from, to)) await ensureModel(e, f, t)
            self.postMessage({ id, ...e.translate(html, from, to) })
        } catch (err) {
            self.postMessage({ id, error: String(err?.message ?? err), missingPair: err?.pair ?? null })
        }
    })
}
