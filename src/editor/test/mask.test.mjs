import { test } from 'node:test'
import assert from 'node:assert/strict'
import { maskPlaceholders, unmaskPlaceholders } from '../src/translator/mask.js'

test('placeholders become numbered braces and come back', () => {
    const html = '<p>Call on $[DATE(YYYY-MM-DD)] at $[TIME(HH:mm)], $[YEAR].</p>'
    const masked = maskPlaceholders(html)
    assert.equal(masked.html, '<p>Call on {0} at {1}, {2}.</p>')
    const back = unmaskPlaceholders('<p>Anruf am {0} um {1}, {2}.</p>', masked)
    assert.deepEqual(back, { html: '<p>Anruf am $[DATE(YYYY-MM-DD)] um $[TIME(HH:mm)], $[YEAR].</p>', missing: 0 })
})

test('the order may change in translation', () => {
    const masked = maskPlaceholders('<p>$[DATE] $[TIME]</p>')
    assert.equal(unmaskPlaceholders('<p>{1} {0}</p>', masked).html, '<p>$[TIME] $[DATE]</p>')
})

test('braces already in the text are left alone', () => {
    const masked = maskPlaceholders('<p>Use {0} and {3} on $[DATE]</p>')
    assert.equal(masked.html, '<p>Use {0} and {3} on {4}</p>')
    assert.equal(unmaskPlaceholders('<p>Nutze {0} und {3} am {4}</p>', masked).html, '<p>Nutze {0} und {3} am $[DATE]</p>')
})

test('a dropped placeholder is appended to the last paragraph', () => {
    const masked = maskPlaceholders('<p>Hi</p><p>On $[DATE] at $[TIME]</p>')
    assert.deepEqual(unmaskPlaceholders('<p>Hallo</p><p>Am {0}</p>', masked), { html: '<p>Hallo</p><p>Am $[DATE] $[TIME]</p>', missing: 1 })
})

test('a duplicated placeholder is kept once', () => {
    const masked = maskPlaceholders('<p>$[DATE]</p>')
    assert.equal(unmaskPlaceholders('<p>{0} {0}</p>', masked).html, '<p>$[DATE] </p>')
})

test('text without placeholders passes through', () => {
    const masked = maskPlaceholders('<p>Kind regards</p>')
    assert.equal(masked.placeholders.length, 0)
    assert.deepEqual(unmaskPlaceholders('<p>Freundliche Grüße</p>', masked), { html: '<p>Freundliche Grüße</p>', missing: 0 })
})
