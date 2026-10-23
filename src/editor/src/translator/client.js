// The editor's side of the translator: a worker started on first use and stopped again after a while
// without requests, which frees the engine and its models (some 100 MB). The editor page is navigated to
// as a string, so the worker is a blob that loads the real one from the translator host.
const WORKER = 'https://ttm-translator.example/translator-worker.js'
const IDLE_MS = 5 * 60 * 1000

let worker = null
let idleTimer = 0
let nextId = 1
const pending = new Map()

function start() {
    const url = URL.createObjectURL(new Blob([`importScripts(${JSON.stringify(WORKER)})`], { type: 'text/javascript' }))
    worker = new Worker(url)
    URL.revokeObjectURL(url)
    worker.onmessage = ({ data }) => {
        const p = pending.get(data.id)
        if (!p) return
        pending.delete(data.id)
        data.error ? p.reject(Object.assign(new Error(data.error), { missingPair: data.missingPair })) : p.resolve(data)
        scheduleStop()
    }
    // The worker itself failed: fail everything waiting and start over next time.
    worker.onerror = (e) => {
        for (const p of pending.values()) p.reject(new Error(e.message || 'translator failed'))
        pending.clear()
        stop()
    }
}

function stop() {
    clearTimeout(idleTimer)
    worker?.terminate()
    worker = null
}

function scheduleStop() {
    clearTimeout(idleTimer)
    idleTimer = setTimeout(() => { if (pending.size === 0) stop() }, IDLE_MS)
}

// Resolves to { html, missing } (missing = placeholders the model dropped, appended at the end). Rejects
// with error.missingPair set ("en-de") when a needed language pair isn't installed. `formal` asks for the polite
// form (German only).
export function translateHtml(html, from, to, formal = false) {
    return request({ html, from, to, formal })
}

export { FORMAL_LANGUAGES } from './formal.js'

// Resolves to the code among `candidates` the text is written in, or null. The detector lives in the worker so
// its language data (some 300 KB) doesn't load with every template.
export async function detectLanguage(text, candidates) {
    return (await request({ detect: text, candidates })).code ?? null
}

function request(message) {
    if (!worker) start()
    const id = nextId++
    return new Promise((resolve, reject) => {
        pending.set(id, { resolve, reject })
        worker.postMessage({ id, ...message })
        scheduleStop()
    })
}
