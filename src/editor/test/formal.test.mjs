import { test } from 'node:test'
import assert from 'node:assert/strict'
import { addGreetings, removeGreetings, translateFormal } from '../src/translator/formal.js'

test('every sentence gets the greeting, in text only', () => {
    const r = addGreetings('Thanks for your <strong>quick</strong> reply. Can you call me? I will wait.')
    assert.equal(r.count, 3)
    assert.equal(r.html, 'Dear Sir or Madam, thanks for your <strong>quick</strong> reply. Dear Sir or Madam, can you call me? Dear Sir or Madam, I will wait.')
})

test('a sentence starting inside a tag or with a placeholder gets it too', () => {
    assert.equal(addGreetings('<a href="u">Click</a> here.').html, '<a href="u">Dear Sir or Madam, click</a> here.')
    assert.equal(addGreetings('{0} is your date.').html, 'Dear Sir or Madam, {0} is your date.')
})

test('the greeting comes off, once per sentence, and the sentence is capitalized again', () => {
    const r = removeGreetings('Sehr geehrte Damen und Herren, ich rufe Sie an. Sehr geehrte Frau oder Madam, <strong>können</strong> Sie?')
    assert.deepEqual(r, { html: 'Ich rufe Sie an. <strong>Können</strong> Sie?', removed: 2 })
    assert.equal(removeGreetings('Sehr geehrte Damen und Herren, sehr geehrte Damen und Herren, hallo').html, 'Sehr geehrte Damen und Herren, hallo')
})

// A stand-in engine: "you" becomes "du" unless the sentence starts with the greeting.
function fakeTranslate(html) {
    return html.replace(/(Dear Sir or Madam, )?([^.?!<>]*?)\byou\b/g, (m, greeting, before) =>
        greeting ? `Sehr geehrte Damen und Herren, ${before}Sie` : `${before}du`)
}

test('only paragraphs that came out with du are translated again', () => {
    let calls = 0
    const translate = (h) => { calls++; return fakeTranslate(h) }
    const out = translateFormal('<p>Hello team.</p><p>Thank you.</p>', translate)
    assert.equal(out, '<p>Hello team.</p><p>Thank Sie.</p>')
    assert.equal(calls, 2)
})

test('without du nothing is translated twice', () => {
    let calls = 0
    translateFormal('<p>Hello team.</p>', (h) => { calls++; return h })
    assert.equal(calls, 1)
})

test('a formal paragraph that lost text is not used', () => {
    const translate = (h) => h.includes('Dear Sir') ? '<p>Sehr geehrte Damen und Herren, kurz.</p>' : '<p>Du hast recht, das war unser Fehler.</p>'
    assert.equal(translateFormal('<p>You are right, that was our mistake.</p>', translate), '<p>Du hast recht, das war unser Fehler.</p>')
})

test('a greeting that does not come back keeps the first translation', () => {
    const translate = (h) => h.includes('Dear Sir') ? '<p>Hallo, bitte rufen Sie mich an, wenn Sie Zeit haben.</p>' : '<p>Bitte ruf mich an, wenn du Zeit hast.</p>'
    assert.equal(translateFormal('<p>Please call me when you have time.</p>', translate), '<p>Bitte ruf mich an, wenn du Zeit hast.</p>')
})

test('a line break starts a sentence for the greeting', () => {
    assert.deepEqual(addGreetings('Dear Ms/Mr @<br>Unfortunately we could not reach you.'),
        { html: 'Dear Sir or Madam, dear Ms/Mr @<br>Dear Sir or Madam, unfortunately we could not reach you.', count: 2 })
    assert.deepEqual(removeGreetings('Sehr geehrte Damen und Herren, hallo<br>Sehr geehrte Damen und Herren, leider nicht.'),
        { html: 'Hallo<br>Leider nicht.', removed: 2 })
})

test('only the lines that came out with du are translated again', () => {
    const seen = []
    const translate = (h) => { seen.push(h); return fakeTranslate(h).replace('Dear Ms/Mr', 'Sehr geehrte Frau/Herr') }
    const out = translateFormal('<p>Dear Ms/Mr @<br>We could not reach you.</p>', translate)
    assert.equal(out, '<p>Sehr geehrte Frau/Herr @<br>We could not reach Sie.</p>')
    assert.deepEqual(seen, ['<p>Dear Ms/Mr @<br>We could not reach you.</p>', '<p>Dear Sir or Madam, we could not reach you.</p>'])
})

test('lines that split a tag go as one paragraph', () => {
    const seen = []
    const translate = (h) => { seen.push(h); return fakeTranslate(h) }
    translateFormal('<p><strong>Hello<br>Thank you.</strong></p>', translate)
    assert.equal(seen[1], '<p><strong>Dear Sir or Madam, hello<br>Dear Sir or Madam, thank you.</strong></p>')
})
