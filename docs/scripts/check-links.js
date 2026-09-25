#!/usr/bin/env node

const { existsSync, readdirSync, readFileSync, statSync } = require('fs')
const { dirname, extname, resolve } = require('path')

const repositoryRoot = resolve(__dirname, '..', '..')
const roots = ['README.md', 'RELEASE-CHECKLIST.md', 'RELEASE-CYCLES.md', 'docs']
const files = []
const anchors = new Map()

function collect(path) {
  const fullPath = resolve(repositoryRoot, path)
  if (statSync(fullPath).isDirectory()) {
    for (const entry of readdirSync(fullPath)) collect(resolve(path, entry))
  } else if (extname(path).toLowerCase() === '.md') {
    files.push(fullPath)
  }
}

for (const root of roots) collect(root)

function getAnchors(file) {
  if (anchors.has(file)) return anchors.get(file)
  const markdown = readFileSync(file, 'utf8')
  const found = new Set([...markdown.matchAll(/<a\s+(?:name|id)=["']([^"']+)["']/gi)].map(match => match[1]))
  const counts = new Map()
  for (const match of markdown.matchAll(/^#{1,6}\s+(.+)$/gm)) {
    const base = match[1]
      .replace(/<[^>]+>/g, '')
      .replace(/[`*_~]/g, '')
      .trim()
      .toLowerCase()
      .replace(/[^\p{L}\p{N}\s-]/gu, '')
      .replace(/\s+/g, '-')
    const count = counts.get(base) || 0
    counts.set(base, count + 1)
    found.add(count === 0 ? base : `${base}-${count}`)
  }
  anchors.set(file, found)
  return found
}

const errors = []
for (const file of files) {
  const markdown = readFileSync(file, 'utf8')
  const links = [...markdown.matchAll(/(?<!!)\[[^\]]*\]\(([^)]+)\)/g)]
  for (const match of links) {
    const rawTarget = match[1].trim().replace(/^<|>$/g, '')
    if (!rawTarget || /^(https?:|mailto:)/i.test(rawTarget) || rawTarget.startsWith('#')) continue
    const [encodedPath, fragment] = rawTarget.split('#')
    const path = decodeURIComponent(encodedPath)
    if (!path) continue
    const target = resolve(dirname(file), path)
    const candidates = [target, `${target}.md`, resolve(target, 'README.md')]
    const existing = candidates.find(existsSync)
    if (!existing) {
      errors.push(`${file.slice(repositoryRoot.length + 1)}: missing ${rawTarget}`)
    } else if (fragment && extname(existing).toLowerCase() === '.md' && !getAnchors(existing).has(decodeURIComponent(fragment))) {
      errors.push(`${file.slice(repositoryRoot.length + 1)}: missing anchor ${rawTarget}`)
    }
  }
}

if (errors.length > 0) {
  console.error(errors.join('\n'))
  process.exit(1)
}

console.log(`Checked ${files.length} Markdown files.`)
