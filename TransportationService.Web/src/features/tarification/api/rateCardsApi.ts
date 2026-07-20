import { apiClient } from '../../../api/apiClient'
import type { Quote, QuoteInput, RateCard, RateCardInput } from '../types'

export function listRateCards(customerId?: string): Promise<RateCard[]> {
  const suffix = customerId ? `?customerId=${customerId}` : ''
  return apiClient.getJson<RateCard[]>(`/api/rate-cards${suffix}`)
}

export function createRateCard(input: RateCardInput): Promise<RateCard> {
  return apiClient.postJson<RateCard, RateCardInput>('/api/rate-cards', input)
}

export function updateRateCard(id: string, input: RateCardInput): Promise<RateCard> {
  return apiClient.putJson<RateCard, RateCardInput>(`/api/rate-cards/${id}`, input)
}

export function deleteRateCard(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/rate-cards/${id}`)
}

export function quoteRate(input: QuoteInput): Promise<Quote> {
  return apiClient.postJson<Quote, QuoteInput>('/api/rate-cards/quote', input)
}
