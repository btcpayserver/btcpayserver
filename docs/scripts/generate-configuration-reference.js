#!/usr/bin/env node

const { spawnSync } = require('child_process')
const { readFileSync, writeFileSync } = require('fs')
const { resolve } = require('path')

const repositoryRoot = resolve(__dirname, '..', '..')
const outputPath = resolve(repositoryRoot, 'docs', 'operators', 'configuration-reference.md')

const categories = [
  ['Process and network', new Set(['help', 'network', 'chains', 'nodefaultchain', 'conf', 'port', 'bind', 'datadir'])],
  ['Database', new Set(['postgres', 'explorerpostgres'])],
  ['HTTP and security', new Set(['nocsp', 'rootpath', 'xforwardedproto', 'disable-registration'])],
  ['Host and external services', new Set(['externalservices', 'btcpayhostenabled', 'btcpayhostexecutable', 'torrcfile', 'torservices', 'socksendpoint', 'updateurl'])],
  ['Logging and diagnostics', new Set(['debuglog', 'debugloglevel'])],
  ['Development', new Set(['cheatmode'])],
  ['Chain services', new Set(['btcexplorerurl', 'btcexplorercookiefile', 'btclightning', 'btcexternallndgrpc', 'btcexternallndrest', 'btcexternalrtl', 'btcexternalspark', 'btcexternalcharge'])]
]

const legacyOptions = new Set(['testnet', 'regtest', 'signet', 'deprecated', 'recommended-plugins'])

const configurationKeys = {
  btcexplorerurl: 'btc.explorer.url',
  btcexplorercookiefile: 'btc.explorer.cookiefile',
  btclightning: 'btc.lightning',
  btcexternallndgrpc: 'btc.external.lndgrpc',
  btcexternallndrest: 'btc.external.lndrest',
  btcexternalrtl: 'btc.external.rtl',
  btcexternalspark: 'btc.external.spark',
  btcexternalcharge: 'btc.external.charge'
}

const result = spawnSync(
  'dotnet',
  ['run', '--no-launch-profile', '--project', 'BTCPayServer/BTCPayServer.csproj', '--', '--help'],
  { cwd: repositoryRoot, encoding: 'utf8' }
)

const help = `${result.stdout || ''}\n${result.stderr || ''}`.replace(/\x1b\[[0-9;]*m/g, '')
if (!help.includes('Usage: BTCPay [options]')) {
  process.stderr.write(help)
  throw new Error('Could not read BTCPay Server command-line help')
}

const options = []
let inOptions = false
for (const line of help.split(/\r?\n/)) {
  if (line === 'Options:') {
    inOptions = true
    continue
  }
  if (!inOptions) continue
  if (!line.startsWith('  ')) {
    if (options.length > 0) break
    continue
  }

  const match = line.match(/^\s{2}(.+?)\s{2,}(.+)$/)
  if (!match) continue
  const syntax = match[1].trim()
  const description = match[2].trim()
  const longOptions = [...syntax.matchAll(/--([a-z0-9-]+)/gi)].map(value => value[1])
  if (longOptions.length === 0) continue
  const name = longOptions[0] === 'help' ? 'help' : longOptions[0]
  options.push({ name, syntax, description })
}

const assigned = new Set(categories.flatMap(([, names]) => [...names]))
const unknown = options.filter(option => !assigned.has(option.name) && !legacyOptions.has(option.name))
const missing = [...assigned].filter(name => !options.some(option => option.name === name))
if (unknown.length > 0 || missing.length > 0) {
  if (unknown.length > 0) console.error(`Uncategorized options: ${unknown.map(option => option.name).join(', ')}`)
  if (missing.length > 0) console.error(`Missing options: ${missing.join(', ')}`)
  process.exit(1)
}

const escapeCell = value => value.replace(/\|/g, '\\|')
const rows = option => {
  const key = configurationKeys[option.name] || option.name
  const config = option.name === 'help' ? 'N/A' : `\`${key}\``
  const environment = option.name === 'help' ? 'N/A' : `\`BTCPAY_${option.name.toUpperCase()}\``
  const description = option.name === 'btcexplorercookiefile'
    ? 'Path to the NBXplorer cookie file (default: the network data directory)'
    : option.description
  return `| \`${escapeCell(option.syntax)}\` | ${config} | ${environment} | ${escapeCell(description)} |`
}

const sectionIntroductions = {
  'Chain services': `The generated options use Bitcoin (\`BTC\`) as the chain prefix. Builds that
include other chains use the same setting names with \`btc\` replaced by the
lowercase crypto code in command-line and configuration-file keys, and by the
uppercase crypto code in environment variables. For example, Litecoin's
explorer URL is \`--ltcexplorerurl\`, \`ltc.explorer.url\`, or
\`BTCPAY_LTCEXPLORERURL\`.

| Chain | Crypto code |
|---|---|
| Bitcoin | \`BTC\` |
| Bitcoin Gold | \`BTG\` |
| Dash | \`DASH\` |
| Dogecoin | \`DOGE\` |
| Groestlcoin | \`GRS\` |
| Liquid Bitcoin | \`LBTC\` |
| Litecoin | \`LTC\` |
| Monacoin | \`MONA\` |

Only configure chains included in the deployed build and its NBXplorer
instance. Not every chain supports every Lightning-specific setting.`
}

const sections = categories.map(([title, names]) => {
  const categoryOptions = options.filter(option => names.has(option.name))
  const introduction = sectionIntroductions[title]
  return `## ${title}\n\n${introduction ? `${introduction}\n\n` : ''}| Command line | Configuration file | Environment | Description |\n|---|---|---|---|\n${categoryOptions.map(rows).join('\n')}`
})

const markdown = `# Configuration Reference

<!-- Generated by docs/scripts/generate-configuration-reference.js. Do not edit manually. -->

This reference is generated from every command-line option registered by the
standard Bitcoin build of BTCPay Server. Use the same application version that
you deploy because options and defaults can change between releases. Some
advanced settings are available only through configuration providers and are
documented with the feature that consumes them.

Configuration-file keys, environment variables, and command-line options feed
the same configuration system. Environment variables use the \`BTCPAY_\`
prefix. Legacy compatibility options are intentionally omitted.

Regenerate this page from the repository root:

\`\`\`bash
node docs/scripts/generate-configuration-reference.js
\`\`\`

${sections.join('\n\n')}
`

if (process.argv.includes('--check')) {
  const current = readFileSync(outputPath, 'utf8')
  if (current !== markdown) {
    console.error('Configuration reference is stale. Run: node docs/scripts/generate-configuration-reference.js')
    process.exit(1)
  }
} else {
  writeFileSync(outputPath, markdown)
}
