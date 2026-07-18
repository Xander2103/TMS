import { apiClient } from '../../../api/apiClient'
import type { PagedResult } from '../../../api/types'
import type { TankCard, TankCardInput, TankCardStatus } from '../types'

export interface SearchTankCardsParams {
  search?: string
  status?: TankCardStatus
  page: number
  pageSize: number
}

export function searchTankCards(params: SearchTankCardsParams): Promise<PagedResult<TankCard>> {
  const query = new URLSearchParams()
  if (params.search) query.set('search', params.search)
  if (params.status) query.set('status', params.status)
  query.set('page', String(params.page))
  query.set('pageSize', String(params.pageSize))
  return apiClient.getJson<PagedResult<TankCard>>(`/api/tank-cards?${query.toString()}`)
}

export function createTankCard(input: TankCardInput): Promise<TankCard> {
  return apiClient.postJson<TankCard, TankCardInput>('/api/tank-cards', input)
}

export function updateTankCard(id: string, input: TankCardInput): Promise<TankCard> {
  return apiClient.putJson<TankCard, TankCardInput>(`/api/tank-cards/${id}`, input)
}

export function setTankCardBlocked(id: string, isBlocked: boolean, reason: string | null): Promise<TankCard> {
  return apiClient.postJson<TankCard, { isBlocked: boolean; reason: string | null }>(
    `/api/tank-cards/${id}/blocked`,
    { isBlocked, reason },
  )
}

export function deleteTankCard(id: string): Promise<void> {
  return apiClient.deleteRequest(`/api/tank-cards/${id}`)
}
