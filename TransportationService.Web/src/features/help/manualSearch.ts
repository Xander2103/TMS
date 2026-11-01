import type { ManualArticle } from './manualParser'

const normalize = (value: string) => value.toLocaleLowerCase('nl-BE').normalize('NFD').replace(/\p{M}/gu, '')

export interface SearchHit {
  article: ManualArticle
  snippet: string
}

/** Client-side search over title, keywords and text. Every word must appear; the title counts most. */
export function searchManual(articles: ManualArticle[], query: string): SearchHit[] {
  const words = normalize(query).split(/\s+/).filter(Boolean)
  if (words.length === 0) return []
  const scored: { hit: SearchHit; score: number }[] = []
  for (const article of articles) {
    const title = normalize(article.title)
    const keywords = normalize(article.keywords.join(' '))
    const text = normalize(article.text)
    let score = 0
    let matched = true
    for (const word of words) {
      const inTitle = title.includes(word)
      const inKeywords = keywords.includes(word)
      const inText = text.includes(word)
      if (!inTitle && !inKeywords && !inText) { matched = false; break }
      score += (inTitle ? 10 : 0) + (inKeywords ? 5 : 0) + (inText ? 1 : 0)
    }
    if (matched) scored.push({ score, hit: { article, snippet: snippetOf(article.text, text, words[0]) } })
  }
  return scored.sort((a, b) => b.score - a.score).map((s) => s.hit)
}

function snippetOf(original: string, normalized: string, word: string): string {
  // Normalizing can shift positions slightly on accents; good enough for a preview.
  const at = normalized.indexOf(word)
  if (at < 0) return original.slice(0, 120).trim()
  const from = Math.max(0, at - 50)
  return `${from > 0 ? '…' : ''}${original.slice(from, at + 90).trim()}…`
}
