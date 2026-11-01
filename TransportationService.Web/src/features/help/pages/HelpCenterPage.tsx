import { useMemo, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { useAuth } from '../../auth/authContextValue'
import { useLocale } from '../../../i18n/localeContext'
import { isPortalUser } from '../../../routes/portalUser'
import { PageHeader } from '../../../components/layout/PageHeader'
import { ArticleBody } from '../components/ArticleBody'
import { helpRoleOf, isVisibleTo, orderCategories, primaryArticles, type HelpRole } from '../helpRole'
import { ARTICLES, MANUAL_ASSET_BASE } from '../manualContent'
import type { ManualArticle } from '../manualParser'
import { searchManual } from '../manualSearch'
import '../help.css'

const PDF = {
  manual: `${MANUAL_ASSET_BASE}/TMS-Complete-Gebruikershandleiding.pdf`,
  customer: `${MANUAL_ASSET_BASE}/TMS-Klantportaal-Handleiding.pdf`,
  admin: `${MANUAL_ASSET_BASE}/TMS-Beheerdershandleiding.pdf`,
}

function DownloadLinks({ role }: { role: HelpRole | null }) {
  const { t } = useLocale()
  return (
    <div className="help-downloads">
      {role === 'customer'
        ? <a className="ui-button ui-button-secondary" href={PDF.customer} download>{t('help.downloadCustomer')}</a>
        : <a className="ui-button ui-button-secondary" href={PDF.manual} download>{t('help.downloadManual')}</a>}
      {role === 'admin' && <a className="ui-button ui-button-secondary" href={PDF.admin} download>{t('help.downloadAdmin')}</a>}
    </div>
  )
}

function ArticleCard({ article, basePath, snippet }: { article: ManualArticle; basePath: string; snippet?: string }) {
  return (
    <li>
      <Link to={`${basePath}/${article.id}`} className="help-card">
        <span className="help-card-title">{article.title}</span>
        <span className="help-card-meta">{snippet ?? article.category}</span>
      </Link>
    </li>
  )
}

function HelpHome({ articles, role, basePath }: { articles: ManualArticle[]; role: HelpRole | null; basePath: string }) {
  const { t, locale } = useLocale()
  const [params, setParams] = useSearchParams()
  const [query, setQuery] = useState('')
  const category = params.get('onderwerp')
  const hits = useMemo(() => searchManual(articles, query), [articles, query])
  const categories = useMemo(() => orderCategories([...new Set(articles.map((a) => a.category))], role), [articles, role])
  const quickstarts = articles.filter((a) => a.quickstart && role !== null && a.roles.includes(role))
  const primary = useMemo(() => primaryArticles(articles, role), [articles, role])
  const inCategory = category ? articles.filter((a) => a.category === category) : []
  return (
    <div className="help-center">
      <PageHeader title={t('help.title')} subtitle={t('help.subtitle')} action={<DownloadLinks role={role} />} />
      {locale !== 'nl' && <p className="help-note">{t('help.dutchOnly')}</p>}
      <div className="help-search">
        <label htmlFor="help-search-input">{t('help.searchLabel')}</label>
        <input id="help-search-input" type="search" className="help-search-input" value={query} placeholder={t('help.searchPlaceholder')} onChange={(e) => setQuery(e.target.value)} />
      </div>
      {query.trim() ? (
        <section aria-live="polite">
          <h3>{hits.length ? t('help.searchResults', { count: hits.length, query }) : t('help.searchEmpty', { query })}</h3>
          <ul className="help-card-list">{hits.map((hit) => <ArticleCard key={hit.article.id} article={hit.article} basePath={basePath} snippet={hit.snippet} />)}</ul>
        </section>
      ) : category ? (
        <section>
          <button type="button" className="help-back" onClick={() => setParams({})}>{t('help.allTopics')}</button>
          <h3>{category}</h3>
          <ul className="help-card-list">{inCategory.map((a) => <ArticleCard key={a.id} article={a} basePath={basePath} />)}</ul>
        </section>
      ) : (
        <>
          {(quickstarts.length > 0 || primary.length > 0) && (
            <section aria-labelledby="help-for-you">
              <h3 id="help-for-you">{t('help.forYou')}</h3>
              <ul className="help-card-list">
                {quickstarts.map((a) => <ArticleCard key={a.id} article={a} basePath={basePath} snippet={t('help.quickstart')} />)}
                {primary.map((a) => <ArticleCard key={a.id} article={a} basePath={basePath} />)}
              </ul>
            </section>
          )}
          <section aria-labelledby="help-topics">
            <h3 id="help-topics">{t('help.categories')}</h3>
            <ul className="help-card-list help-card-list-compact">
              {categories.map((c) => {
                const count = articles.filter((a) => a.category === c).length
                return (
                  <li key={c}>
                    <button type="button" className="help-card" onClick={() => setParams({ onderwerp: c })}>
                      <span className="help-card-title">{c}</span>
                      <span className="help-card-meta">{count === 1 ? t('help.articleCountOne') : t('help.articleCount', { count })}</span>
                    </button>
                  </li>
                )
              })}
            </ul>
          </section>
        </>
      )}
    </div>
  )
}

function HelpArticle({ article, articles, basePath }: { article: ManualArticle; articles: ManualArticle[]; basePath: string }) {
  const { t } = useLocale()
  const at = articles.indexOf(article)
  const previous = articles[at - 1]
  const next = articles[at + 1]
  const related = article.related.map((id) => articles.find((a) => a.id === id)).filter((a): a is ManualArticle => Boolean(a))
  return (
    <article className="help-center help-article">
      <Link to={basePath} className="help-back">{t('help.backToHelp')}</Link>
      <PageHeader title={article.title} subtitle={article.category} />
      <ArticleBody blocks={article.blocks} basePath={basePath} visibleIds={new Set(articles.map((a) => a.id))} />
      {related.length > 0 && (
        <aside aria-labelledby="help-related">
          <h3 id="help-related">{t('help.related')}</h3>
          <ul className="help-related-list">{related.map((a) => <li key={a.id}><Link to={`${basePath}/${a.id}`}>{a.title}</Link></li>)}</ul>
        </aside>
      )}
      <nav className="help-pager" aria-label={t('help.title')}>
        {previous ? <Link to={`${basePath}/${previous.id}`} rel="prev"><span>{t('help.previous')}</span>{previous.title}</Link> : <span />}
        {next ? <Link to={`${basePath}/${next.id}`} rel="next" className="help-pager-next"><span>{t('help.next')}</span>{next.title}</Link> : <span />}
      </nav>
    </article>
  )
}

/** The help center. Static content from docs/, so no API calls, no database and no new rights. */
export function HelpCenter({ basePath }: { basePath: string }) {
  const { t } = useLocale()
  const { user, hasPermission } = useAuth()
  const { articleId } = useParams()
  const role = helpRoleOf(hasPermission, isPortalUser(user))
  const articles = useMemo(() => ARTICLES.filter((a) => isVisibleTo(a, role)), [role])
  if (!articleId) return <HelpHome articles={articles} role={role} basePath={basePath} />
  const article = articles.find((a) => a.id === articleId)
  if (!article) {
    return (
      <div className="help-center">
        <Link to={basePath} className="help-back">{t('help.backToHelp')}</Link>
        <PageHeader title={t('help.notFoundTitle')} subtitle={t('help.notFoundText')} />
      </div>
    )
  }
  return <HelpArticle key={article.id} article={article} articles={articles} basePath={basePath} />
}

export function HelpCenterPage() { return <HelpCenter basePath="/help" /> }
export function CustomerHelpPage() { return <HelpCenter basePath="/klantportaal/help" /> }
export function DriverHelpPage() { return <HelpCenter basePath="/driver/help" /> }
