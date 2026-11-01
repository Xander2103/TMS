import { Link } from 'react-router-dom'
import { useLocale } from '../../../i18n/localeContext'
import { imageUrl } from '../manualContent'
import type { Block, Inline } from '../manualParser'

function InlineNodes({ inline, basePath, visibleIds }: { inline: Inline[]; basePath: string; visibleIds: Set<string> }) {
  return (
    <>
      {inline.map((node, i) => {
        switch (node.kind) {
          case 'bold': return <strong key={i}>{node.text}</strong>
          case 'code': return <code key={i}>{node.text}</code>
          case 'link':
            // An article this reader cannot open stays plain text instead of a dead end.
            if (node.articleId && !visibleIds.has(node.articleId)) return <span key={i}>{node.text}</span>
            return node.articleId
              ? <Link key={i} to={`${basePath}/${node.articleId}`}>{node.text}</Link>
              : <a key={i} href={node.href} rel="noreferrer">{node.text}</a>
          default: return <span key={i}>{node.text}</span>
        }
      })}
    </>
  )
}

/** Renders parsed manual blocks with the design system's elements; no raw HTML is ever injected. */
export function ArticleBody({ blocks, basePath, visibleIds }: { blocks: Block[]; basePath: string; visibleIds: Set<string> }) {
  const { t } = useLocale()
  return (
    <div className="help-article-body">
      {blocks.map((block, i) => {
        switch (block.type) {
          case 'heading':
            return block.level === 2 ? <h3 key={i}>{block.text}</h3> : <h4 key={i}>{block.text}</h4>
          case 'paragraph':
            return <p key={i}><InlineNodes inline={block.inline} basePath={basePath} visibleIds={visibleIds} /></p>
          case 'list': {
            const items = block.items.map((item, j) => <li key={j}><InlineNodes inline={item} basePath={basePath} visibleIds={visibleIds} /></li>)
            return block.ordered ? <ol key={i} start={block.start}>{items}</ol> : <ul key={i}>{items}</ul>
          }
          case 'table':
            return (
              <div className="help-table-scroll" key={i} tabIndex={0} role="region" aria-label={block.header.map((c) => c.map((n) => n.text).join('')).join(', ')}>
                <table>
                  <thead><tr>{block.header.map((c, j) => <th key={j} scope="col"><InlineNodes inline={c} basePath={basePath} visibleIds={visibleIds} /></th>)}</tr></thead>
                  <tbody>{block.rows.map((row, j) => <tr key={j}>{row.map((c, k) => <td key={k}><InlineNodes inline={c} basePath={basePath} visibleIds={visibleIds} /></td>)}</tr>)}</tbody>
                </table>
              </div>
            )
          case 'image':
            return (
              <figure key={i} className="help-figure">
                <a href={imageUrl(block.shot)} target="_blank" rel="noreferrer" title={t('help.openImage')}>
                  <img src={imageUrl(block.shot)} alt={block.alt} loading="lazy" />
                </a>
                <figcaption>{block.caption}</figcaption>
              </figure>
            )
          case 'code':
            return <pre key={i} tabIndex={0}><code>{block.text}</code></pre>
        }
      })}
    </div>
  )
}
