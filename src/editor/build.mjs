import * as esbuild from 'esbuild'
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
// Output goes into the app's Assets so it ships with the build.
const outDir = resolve(here, '..', 'TextTemplateManager', 'Assets', 'editor')
mkdirSync(outDir, { recursive: true })

const css = readFileSync(resolve(here, 'src', 'editor.css'), 'utf8')

async function bundle(entry) {
  const result = await esbuild.build({
    entryPoints: [resolve(here, 'src', entry)],
    bundle: true, format: 'iife', minify: true, target: ['chrome110'], logLevel: 'info', write: false,
  })
  return result.outputFiles[0].text
}

// One self-contained page per WebView: the app navigates to it as a string, so there is nothing to
// fetch and no cache that could serve a stale bundle. Function replacers keep `$` in the content as is.
function page(shell, bundleFile, js) {
  let html = readFileSync(resolve(here, 'src', shell), 'utf8')
  const link = /<link[^>]*editor\.css[^>]*>/
  const script = new RegExp(`<script[^>]*${bundleFile.replaceAll('.', '\\.')}[^>]*></script>`)
  if (!link.test(html) || !script.test(html)) throw new Error(`${shell}: stylesheet or script tag not found`)
  html = html.replace(link, () => `<style>${css}</style>`)
  // An accidental </script> in the bundle would close the inline tag.
  html = html.replace(script, () => `<script>${js.replaceAll('</script', '<\\/script')}</script>`)
  writeFileSync(resolve(outDir, shell), html)
}

page('editor.html', 'editor.bundle.js', await bundle('main.js'))
// The read-only preview highlights code blocks with the same grammars (highlight.js core).
page('preview.html', 'preview.bundle.js', await bundle('preview.js'))
console.log('Editor pages written to', outDir)
