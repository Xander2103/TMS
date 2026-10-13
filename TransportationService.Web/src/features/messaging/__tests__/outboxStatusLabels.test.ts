import { describe, expect, it } from 'vitest'
import { translate } from '../../../i18n/translations'

/**
 * B02 regression: the outbox page crashed on a status the label map did not know (the server's
 * "AwaitingReview"). Every backend OutboxStatus must have a label in every language, and a
 * missing key must never throw.
 */
const BACKEND_OUTBOX_STATUSES = ['Pending', 'Sent', 'Failed', 'Suppressed', 'AwaitingReview'] as const

describe('outbox status labels', () => {
  it('has a human label for every backend status in nl, fr and en', () => {
    for (const locale of ['nl', 'fr', 'en'] as const) {
      for (const status of BACKEND_OUTBOX_STATUSES) {
        const label = translate(locale, `messaging.status.${status}`)
        expect(label, `${locale} ${status}`).not.toBe(`messaging.status.${status}`)
      }
    }
  })

  it('renders an empty string instead of throwing for an undefined key', () => {
    expect(translate('nl', undefined as unknown as string)).toBe('')
  })
})
