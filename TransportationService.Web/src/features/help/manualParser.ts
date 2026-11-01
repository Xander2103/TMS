/**
 * The manuals have one source: the Markdown files under docs/. This parser turns such a file into
 * plain data. The help center renders that data as React elements (never as raw HTML) and
 * scripts/generate-manuals.mjs renders the same data for the PDFs, so both always say the same.
 * No dependencies and only erasable TypeScript, so Node can import this file directly.
 */
export type Inline =
  | { kind: 'text'; text: string }
  | { kind: 'bold'; text: string }
  | { kind: 'code'; text: string }
  | { kind: 'link'; text: string; articleId?: string; href?: string }

export type Block =
  | { type: 'heading'; level: 2 | 3; text: string }
  | { type: 'paragraph'; inline: Inline[] }
  | { type: 'list'; ordered: boolean; start: number; items: Inline[][] }
  | { type: 'table'; header: Inline[][]; rows: Inline[][][] }
  | { type: 'image'; shot: string; alt: string; caption: string }
  | { type: 'code'; text: string }

export interface ManualArticle {
  id: string
  title: string
  category: string
  /** 'all' = everyone, 'internal' = every employee, otherwise the listed help roles. */
  roles: string[]
  keywords: string[]
  related: string[]
  quickstart: boolean
  order: number
  blocks: Block[]
  /** Searchable plain text of the body. */
  text: string
}

export const CATEGORY_ORDER = [
  'Aan de slag', 'Dossiers', 'Opdrachten', 'Planning', 'Live opvolging', 'Chauffeur', 'Klantportaal',
  'Incidenten & Afwijkingen', 'Wagenpark', 'Personeel', 'Magazijn', 'Boekhouding', 'Documenten',
  'Rollen & rechten', 'Beheer', 'Probleemoplossing',
] as const

export function parseInline(source: string): Inline[] {
  const out: Inline[] = []
  const pattern = /\*\*([^*]+)\*\*|`([^`]+)`|\[([^\]]+)\]\(([^)\s]+)\)/g
  let last = 0
  for (let m = pattern.exec(source); m; m = pattern.exec(source)) {
    if (m.index > last) out.push({ kind: 'text', text: source.slice(last, m.index) })
    if (m[1] !== undefined) out.push({ kind: 'bold', text: m[1] })
    else if (m[2] !== undefined) out.push({ kind: 'code', text: m[2] })
    else if (m[4].startsWith('help:')) out.push({ kind: 'link', text: m[3], articleId: m[4].slice(5) })
    else out.push({ kind: 'link', text: m[3], href: m[4] })
    last = m.index + m[0].length
  }
  if (last < source.length) out.push({ kind: 'text', text: source.slice(last) })
  return out
}

const cells = (line: string) => line.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map((c) => parseInline(c.trim()))
const IMAGE = /^!\[([^\]]*)\]\(shot:([\w-]+)(?:\s+"([^"]*)")?\)$/

export function parseBlocks(body: string): Block[] {
  const lines = body.split('\n')
  const blocks: Block[] = []
  let i = 0
  while (i < lines.length) {
    const line = lines[i]
    const trimmed = line.trim()
    if (!trimmed) { i++; continue }
    if (trimmed.startsWith('```')) {
      const code: string[] = []
      i++
      while (i < lines.length && !lines[i].trim().startsWith('```')) code.push(lines[i++])
      i++
      blocks.push({ type: 'code', text: code.join('\n') })
      continue
    }
    const heading = /^(#{2,3})\s+(.*)$/.exec(trimmed)
    if (heading) { blocks.push({ type: 'heading', level: heading[1].length as 2 | 3, text: heading[2] }); i++; continue }
    const image = IMAGE.exec(trimmed)
    if (image) { blocks.push({ type: 'image', alt: image[1], shot: image[2], caption: image[3] ?? image[1] }); i++; continue }
    if (trimmed.startsWith('|')) {
      const rows: Inline[][][] = []
      while (i < lines.length && lines[i].trim().startsWith('|')) {
        if (!/^\|[\s:|-]+\|$/.test(lines[i].trim())) rows.push(cells(lines[i]))
        i++
      }
      blocks.push({ type: 'table', header: rows[0], rows: rows.slice(1) })
      continue
    }
    const ordered = /^(\d+)\.\s+/.exec(trimmed)
    if (ordered || trimmed.startsWith('- ')) {
      const items: Inline[][] = []
      const marker = ordered ? /^\d+\.\s+/ : /^-\s+/
      while (i < lines.length && marker.test(lines[i].trim())) items.push(parseInline(lines[i++].trim().replace(marker, '')))
      blocks.push({ type: 'list', ordered: Boolean(ordered), start: ordered ? Number(ordered[1]) : 1, items })
      continue
    }
    const paragraph: string[] = []
    while (i < lines.length && lines[i].trim() && !/^(#{2,3}\s|\||!\[|```|-\s|\d+\.\s)/.test(lines[i].trim())) paragraph.push(lines[i++].trim())
    blocks.push({ type: 'paragraph', inline: parseInline(paragraph.join(' ')) })
  }
  return blocks
}

const list = (value: string | undefined) => (value ?? '').split(',').map((v) => v.trim()).filter(Boolean)
const inlineText = (inline: Inline[]) => inline.map((n) => n.text).join('')

function blockText(block: Block): string {
  switch (block.type) {
    case 'heading': return block.text
    case 'paragraph': return inlineText(block.inline)
    case 'list': return block.items.map(inlineText).join(' ')
    case 'table': return [block.header, ...block.rows].flat().map(inlineText).join(' ')
    case 'image': return `${block.alt} ${block.caption}`
    case 'code': return block.text
  }
}

/** Parses one Markdown file with frontmatter. `order` comes from the file name's number. */
export function parseArticle(source: string, order = 0): ManualArticle {
  const normalized = source.replace(/\r\n/g, '\n')
  const end = normalized.indexOf('\n---', 3)
  if (!normalized.startsWith('---') || end < 0) throw new Error('Manual article without frontmatter')
  const meta: Record<string, string> = {}
  for (const line of normalized.slice(3, end).split('\n')) {
    const at = line.indexOf(':')
    if (at > 0) meta[line.slice(0, at).trim()] = line.slice(at + 1).trim()
  }
  if (!meta.id || !meta.title || !meta.category) throw new Error(`Manual article incomplete: ${meta.id ?? meta.title ?? source.slice(0, 40)}`)
  const blocks = parseBlocks(normalized.slice(end + 4))
  return {
    id: meta.id,
    title: meta.title,
    category: meta.category,
    roles: list(meta.roles),
    keywords: list(meta.keywords),
    related: list(meta.related),
    quickstart: meta.quickstart === 'true',
    order: meta.order ? Number(meta.order) : order,
    blocks,
    text: blocks.map(blockText).join(' '),
  }
}

/** Every help link and screenshot in a set of articles must resolve; used by the tests and the generator. */
export function validateArticles(articles: ManualArticle[], knownShots?: Set<string>): string[] {
  const ids = new Set(articles.map((a) => a.id))
  const problems: string[] = []
  if (ids.size !== articles.length) problems.push('duplicate article id')
  const visitInline = (a: ManualArticle, inline: Inline[]) => {
    for (const n of inline) if (n.kind === 'link' && n.articleId && !ids.has(n.articleId)) problems.push(`${a.id}: broken link to ${n.articleId}`)
  }
  for (const a of articles) {
    for (const r of a.related) if (!ids.has(r)) problems.push(`${a.id}: broken related ${r}`)
    for (const b of a.blocks) {
      if (b.type === 'paragraph') visitInline(a, b.inline)
      if (b.type === 'list') b.items.forEach((i) => visitInline(a, i))
      if (b.type === 'table') [b.header, ...b.rows].flat().forEach((c) => visitInline(a, c))
      if (b.type === 'image' && knownShots && !knownShots.has(b.shot)) problems.push(`${a.id}: missing screenshot ${b.shot}`)
      if (b.type === 'image' && !b.alt) problems.push(`${a.id}: image ${b.shot} without alt text`)
    }
  }
  return problems
}
