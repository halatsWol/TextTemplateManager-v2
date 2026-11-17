import { test } from 'node:test'
import assert from 'node:assert/strict'
import { detectLanguage, guessLanguage } from '../src/translator/detect.js'

test('tells the installed languages apart', () => {
    const installed = ['en', 'de', 'fr']
    assert.equal(detectLanguage('Sehr geehrte Frau Müller, vielen Dank für Ihre Nachricht.', installed), 'de')
    assert.equal(detectLanguage('Thank you for your quick reply, I will call you tomorrow.', installed), 'en')
    assert.equal(detectLanguage('Merci beaucoup pour votre message, je vous rappelle demain.', installed), 'fr')
})

test('a short text gets no guess', () => {
    assert.equal(detectLanguage('Kind regards', ['en', 'de']), null)
})

test('a short text gets a guess that isn\'t sure when no other language comes close', () => {
    const ui = ['en', 'de', 'hu', 'it', 'fr', 'es']
    assert.deepEqual(guessLanguage('this is a test', ui), { code: 'en', sure: false })
    assert.deepEqual(guessLanguage('good morning', ui), { code: 'en', sure: false })
    assert.deepEqual(guessLanguage('Sehr geehrte Frau Müller, vielen Dank.', ui), { code: 'de', sure: true })
})

test('a short text with another language close behind, or very short, gets no guess', () => {
    const ui = ['en', 'de', 'hu', 'it', 'fr', 'es']
    assert.equal(guessLanguage('merci beaucoup', ui), null)
    assert.equal(guessLanguage('muchas gracias', ui), null)
    assert.equal(guessLanguage('Hallo Tom', ui), null)
})

test('placeholders are not text', () => {
    assert.equal(detectLanguage('$[DATE(YYYY-MM-DD)] $[TIME(HH:mm)] ok', ['en', 'de']), null)
})

test('with one candidate there is nothing to tell apart', () => {
    assert.equal(detectLanguage('Some longer text in whatever language it is.', ['de']), 'de')
})

test('languages beyond the translator ones, by their ISO 639-1 code', () => {
    const candidates = ['en', 'de', 'hu', 'it', 'fr', 'es', 'sw', 'mk']
    assert.equal(detectLanguage('Asante sana kwa ujumbe wako, nitakupigia simu kesho asubuhi.', candidates), 'sw')
    assert.equal(detectLanguage('Ви благодарам за пораката, ќе ве повикам утре наутро.', candidates), 'mk')
})

test('a language franc knows in several varieties', () => {
    assert.equal(detectLanguage('Allinllachu kachkanki? Sulpayki kachun, paqarin rimasunchik.', ['en', 'es', 'qu']), 'qu')
})

test('languages franc does not know are left out', () => {
    assert.equal(detectLanguage('Eskerrik asko zure mezuagatik, bihar deituko dizut.', ['eu']), null)
})

test('Simplified and Traditional Chinese', () => {
    const both = ['en', 'zh-Hans', 'zh-Hant']
    assert.equal(detectLanguage('感谢您的来信，我们会尽快回复您的问题。', both), 'zh-Hans')
    assert.equal(detectLanguage('感謝您的來信，我們會盡快回覆您的問題。', both), 'zh-Hant')
})
