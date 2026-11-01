import { CATEGORY_ORDER, parseArticle, type ManualArticle } from './manualParser'

// The Markdown under docs/ is the one source of the help center and the PDFs. This module only
// loads with the help pages (see the lazy route), so it does not weigh on the main bundle.
const sources = import.meta.glob(['../../../../docs/user-manual/*.md', '../../../../docs/admin-manual/*.md'], {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>

const categoryRank = (category: string) => {
  const at = (CATEGORY_ORDER as readonly string[]).indexOf(category)
  return at < 0 ? CATEGORY_ORDER.length : at
}

export const ARTICLES: ManualArticle[] = Object.entries(sources)
  .map(([path, source]) => parseArticle(source, Number(/(\d+)-[^/]*\.md$/.exec(path)?.[1] ?? 0)))
  .sort((a, b) => categoryRank(a.category) - categoryRank(b.category) || a.order - b.order)

export const MANUAL_ASSET_BASE = '/manual-assets'
export const imageUrl = (shot: string) => `${MANUAL_ASSET_BASE}/img/${shot}.png`
