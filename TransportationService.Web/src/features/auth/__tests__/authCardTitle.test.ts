import { describe, expect, it } from 'vitest'
import css from '../LoginPage.css?raw'

// jsdom does no layout, so the wrapping itself is verified in the browser. This guards the cause:
// the card title must carry its OWN unitless line-height instead of inheriting the root's computed
// one (a fixed ~26px), which made a title that wraps print its lines on top of each other.

function block(selector: string): string {
  const start = css.indexOf(`${selector} {`)
  expect(start, `${selector} rule`).toBeGreaterThanOrEqual(0)
  return css.slice(start, css.indexOf('}', start))
}

describe('auth card title (login, reset, forced password change)', () => {
  it('sets a unitless line-height that scales with the title', () => {
    const lineHeight = /line-height:\s*([^;]+);/.exec(block('.login-card h1'))?.[1].trim()
    expect(lineHeight).toMatch(/^\d+(\.\d+)?$/)
    expect(Number(lineHeight)).toBeGreaterThanOrEqual(1.15)
  })

  it('may wrap, and is not scaled artificially', () => {
    const rule = block('.login-card h1')
    expect(rule).not.toMatch(/white-space:\s*nowrap/)
    expect(css).not.toMatch(/\bzoom\s*:|transform:\s*scale/)
  })
})
