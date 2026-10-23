import { test } from 'node:test'
import assert from 'node:assert/strict'
import { repairInline } from '../src/translator/inline.js'

test('a link the engine put on two words stays on the one in the original place', () => {
    const source = '<p>You can also find the contact information for your location <a href="u">here</a>.</p>'
    const out = '<p><a href="u">Die</a> Kontaktdaten zu Ihrem Standort finden Sie auch <a href="u">hier</a>.</p>'
    assert.equal(repairInline(source, out), '<p>Die Kontaktdaten zu Ihrem Standort finden Sie auch <a href="u">hier</a>.</p>')
})

test('copies right next to each other become one', () => {
    const source = '<p>Thank you for your <strong>very quick</strong> reply.</p>'
    const out = '<p>Vielen Dank für Ihre <strong>sehr</strong> <strong>schnelle</strong> Antwort.</p>'
    assert.equal(repairInline(source, out), '<p>Vielen Dank für Ihre <strong>sehr schnelle</strong> Antwort.</p>')
})

test('as many copies as in the original are left alone', () => {
    const source = '<p><a href="u">one</a> and <a href="u">two</a></p>'
    const out = '<p><a href="u">eins</a> und <a href="u">zwei</a></p>'
    assert.equal(repairInline(source, out), out)
})

test('elements with other attributes are counted apart', () => {
    const source = '<p><a href="x">here</a> or <a href="y">there</a></p>'
    const out = '<p><a href="x">hier</a> <a href="y">oder</a> <a href="y">dort</a></p>'
    assert.equal(repairInline(source, out), '<p><a href="x">hier</a> <a href="y">oder dort</a></p>')
})

test('an element the original never had is unwrapped', () => {
    assert.equal(repairInline('<p>plain</p>', '<p><em>schlicht</em></p>'), '<p>schlicht</p>')
})

test('nested formatting inside a link survives', () => {
    const source = '<p>See <a href="u"><strong>this page</strong></a> now.</p>'
    const out = '<p><a href="u">Siehe</a> <a href="u"><strong>diese Seite</strong></a> jetzt.</p>'
    assert.equal(repairInline(source, out), '<p><a href="u">Siehe <strong>diese Seite</strong></a> jetzt.</p>')
})
