import { test } from 'node:test'
import assert from 'node:assert/strict'
import { detectLanguage } from '../src/translator/detect.js'

test('tells the installed languages apart', () => {
    const installed = ['en', 'de', 'fr']
    assert.equal(detectLanguage('Sehr geehrte Frau Müller, vielen Dank für Ihre Nachricht.', installed), 'de')
    assert.equal(detectLanguage('Thank you for your quick reply, I will call you tomorrow.', installed), 'en')
    assert.equal(detectLanguage('Merci beaucoup pour votre message, je vous rappelle demain.', installed), 'fr')
})

test('a short text gets no guess', () => {
    assert.equal(detectLanguage('Kind regards', ['en', 'de']), null)
})

test('placeholders are not text', () => {
    assert.equal(detectLanguage('$[DATE(YYYY-MM-DD)] $[TIME(HH:mm)] ok', ['en', 'de']), null)
})

test('with one candidate there is nothing to tell apart', () => {
    assert.equal(detectLanguage('Some longer text in whatever language it is.', ['de']), 'de')
})

test('languages franc does not know are left out', () => {
    assert.equal(detectLanguage('Eskerrik asko zure mezuagatik, bihar deituko dizut.', ['eu']), null)
})

test('Simplified and Traditional Chinese', () => {
    const both = ['en', 'zh-Hans', 'zh-Hant']
    assert.equal(detectLanguage('感谢您的来信，我们会尽快回复您的问题。', both), 'zh-Hans')
    assert.equal(detectLanguage('感謝您的來信，我們會盡快回覆您的問題。', both), 'zh-Hant')
})
