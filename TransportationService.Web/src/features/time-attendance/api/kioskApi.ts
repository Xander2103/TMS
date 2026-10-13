import { apiBaseUrl } from '../../../config/env'
import type { KioskIdentifyResult, KioskPing, KioskPunchAction, KioskPunchResult } from '../types'

/**
 * Kiosk-API: BEWUST los van apiClient. Een prikklok heeft geen gebruikerssessie — het
 * device authenticeert per verzoek met de X-Kiosk-Device-header (provisioning-key uit
 * localStorage). Er is dus geen Bearer-token, geen refresh-flow en geen toegang tot
 * andere ERP-endpoints vanaf dit pad. Netwerk-/serverfouten worden als expliciete
 * outcome teruggegeven: de UI mag nooit doen alsof een punch gelukt is zonder
 * serverbevestiging.
 */

export const KIOSK_DEVICE_KEY_STORAGE = 'ts.kiosk.deviceKey.v1'

export function getStoredDeviceKey(): string | null {
  try {
    return localStorage.getItem(KIOSK_DEVICE_KEY_STORAGE)
  } catch {
    return null
  }
}

export function storeDeviceKey(key: string): void {
  localStorage.setItem(KIOSK_DEVICE_KEY_STORAGE, key)
}

export function clearDeviceKey(): void {
  localStorage.removeItem(KIOSK_DEVICE_KEY_STORAGE)
}

export class KioskUnreachableError extends Error {
  constructor() {
    super('Geen verbinding met de server.')
    this.name = 'KioskUnreachableError'
  }
}

async function kioskRequest<T>(path: string, deviceKey: string, init?: RequestInit): Promise<T> {
  let response: Response
  try {
    response = await fetch(`${apiBaseUrl}${path}`, {
      ...init,
      headers: {
        'Content-Type': 'application/json',
        'X-Kiosk-Device': deviceKey,
        ...init?.headers,
      },
    })
  } catch {
    throw new KioskUnreachableError()
  }

  // Kiosk-endpoints antwoorden ook bij 401/422 met een outcome-body.
  try {
    return (await response.json()) as T
  } catch {
    throw new KioskUnreachableError()
  }
}

export function kioskPing(deviceKey: string): Promise<KioskPing> {
  return kioskRequest<KioskPing>('/api/attendance/kiosk/ping', deviceKey, { method: 'GET' })
}

export function kioskIdentify(deviceKey: string, pin: string): Promise<KioskIdentifyResult> {
  return kioskRequest<KioskIdentifyResult>('/api/attendance/kiosk/identify', deviceKey, {
    method: 'POST',
    body: JSON.stringify({ pin }),
  })
}

export function kioskPunch(
  deviceKey: string, interactionToken: string, action: KioskPunchAction,
): Promise<KioskPunchResult> {
  return kioskRequest<KioskPunchResult>('/api/attendance/kiosk/punch', deviceKey, {
    method: 'POST',
    body: JSON.stringify({ interactionToken, action }),
  })
}
