import { apiClient, ApiError } from '../../../api/apiClient'
import { apiBaseUrl } from '../../../config/env'
import { getAccessToken } from '../../auth/authStorage'
import type { CorrectPodInput, FinalizePodInput, PodDetail, PodPhotoCategory } from '../types'

export function finalizePod(tripId: string, stopId: string, input: FinalizePodInput): Promise<PodDetail> {
  return apiClient.postJson<PodDetail, FinalizePodInput>(`/api/trips/${tripId}/stops/${stopId}/pod`, input)
}

export function getPodForStop(tripId: string, stopId: string): Promise<PodDetail> {
  return apiClient.getJson<PodDetail>(`/api/trips/${tripId}/stops/${stopId}/pod`)
}

export function getPod(id: string): Promise<PodDetail> {
  return apiClient.getJson<PodDetail>(`/api/pods/${id}`)
}

export function correctPod(id: string, input: CorrectPodInput): Promise<PodDetail> {
  return apiClient.postJson<PodDetail, CorrectPodInput>(`/api/pods/${id}/corrections`, input)
}

export async function uploadPodPhoto(id: string, category: PodPhotoCategory, file: File): Promise<PodDetail> {
  const form = new FormData()
  form.append('file', file)
  const response = await fetch(`${apiBaseUrl}/api/pods/${id}/photos?category=${category}`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
    body: form,
  })
  if (!response.ok) {
    throw new ApiError('De foto kon niet worden geüpload.', response.status)
  }
  return (await response.json()) as PodDetail
}

async function fetchAuthedBlobUrl(path: string): Promise<string> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    headers: { Authorization: `Bearer ${getAccessToken() ?? ''}` },
  })
  if (!response.ok) {
    throw new ApiError('Het bestand kon niet worden geladen.', response.status)
  }
  return URL.createObjectURL(await response.blob())
}

export function fetchPodPhotoUrl(id: string, photoId: string): Promise<string> {
  return fetchAuthedBlobUrl(`/api/pods/${id}/photos/${photoId}`)
}

export function fetchPodSignatureUrl(id: string): Promise<string> {
  return fetchAuthedBlobUrl(`/api/pods/${id}/signature`)
}
