import { apiClient } from '../../../api/apiClient'
import type {
  AttendanceCredentialResult, AttendanceCredentialStatus, AttendanceHistory, AttendanceOverview,
  AttendanceReport, AttendanceSession, AttendanceSettings, AttendanceStatus, DriverDaySummary,
  KioskDevice, KioskProvisionResult,
} from '../types'

// ── Self-service (api/me/attendance) ────────────────────────────────────────────

export function getMyAttendanceStatus(): Promise<AttendanceStatus> {
  return apiClient.getJson<AttendanceStatus>('/api/me/attendance/status')
}

export function clockIn(): Promise<AttendanceStatus> {
  return apiClient.postJson<AttendanceStatus, Record<string, never>>('/api/me/attendance/clock-in', {})
}

export function clockOut(): Promise<AttendanceStatus> {
  return apiClient.postJson<AttendanceStatus, Record<string, never>>('/api/me/attendance/clock-out', {})
}

export function startBreak(): Promise<AttendanceStatus> {
  return apiClient.postJson<AttendanceStatus, Record<string, never>>('/api/me/attendance/break/start', {})
}

export function endBreak(): Promise<AttendanceStatus> {
  return apiClient.postJson<AttendanceStatus, Record<string, never>>('/api/me/attendance/break/end', {})
}

export function getMyAttendanceHistory(from: string, to: string): Promise<AttendanceHistory> {
  return apiClient.getJson<AttendanceHistory>(`/api/me/attendance/history?from=${from}&to=${to}`)
}

export function getMyDriverDay(): Promise<DriverDaySummary> {
  return apiClient.getJson<DriverDaySummary>('/api/me/attendance/driver-day')
}

// ── HR (attendance.view / attendance.correct) ───────────────────────────────────

export function getAttendanceOverview(params: {
  date?: string
  departmentId?: string
  search?: string
}): Promise<AttendanceOverview> {
  const query = new URLSearchParams()
  if (params.date) query.set('date', params.date)
  if (params.departmentId) query.set('departmentId', params.departmentId)
  if (params.search) query.set('search', params.search)
  const suffix = query.size > 0 ? `?${query.toString()}` : ''
  return apiClient.getJson<AttendanceOverview>(`/api/attendance/overview${suffix}`)
}

export function getEmployeeAttendance(employeeId: string, from: string, to: string): Promise<AttendanceHistory> {
  return apiClient.getJson<AttendanceHistory>(`/api/employees/${employeeId}/attendance?from=${from}&to=${to}`)
}

export interface CorrectSessionRequest {
  clockInAt: string | null
  clockOutAt: string | null
  reason: string
  version: string | null
}

export function correctSession(sessionId: string, request: CorrectSessionRequest): Promise<AttendanceSession> {
  return apiClient.postJson<AttendanceSession, CorrectSessionRequest>(
    `/api/attendance/sessions/${sessionId}/corrections`, request)
}

export interface CorrectBreakRequest {
  startedAt: string | null
  endedAt: string | null
  reason: string
  version: string | null
}

export function correctBreak(
  sessionId: string, breakId: string, request: CorrectBreakRequest,
): Promise<AttendanceSession> {
  return apiClient.postJson<AttendanceSession, CorrectBreakRequest>(
    `/api/attendance/sessions/${sessionId}/breaks/${breakId}/corrections`, request)
}

export function cancelSession(sessionId: string, reason: string, version: string | null): Promise<AttendanceSession> {
  return apiClient.postJson<AttendanceSession, { reason: string; version: string | null }>(
    `/api/attendance/sessions/${sessionId}/cancel`, { reason, version })
}

export interface CreateManualSessionRequest {
  employeeId: string
  clockInAt: string
  clockOutAt: string
  breaks: { startedAt: string; endedAt: string }[] | null
  reason: string
}

export function createManualSession(request: CreateManualSessionRequest): Promise<AttendanceSession> {
  return apiClient.postJson<AttendanceSession, CreateManualSessionRequest>('/api/attendance/sessions', request)
}

// ── Rapportering ─────────────────────────────────────────────────────────────────

export function getAttendanceReport(params: {
  from: string
  to: string
  employeeId?: string
  departmentId?: string
}): Promise<AttendanceReport> {
  const query = new URLSearchParams({ from: params.from, to: params.to })
  if (params.employeeId) query.set('employeeId', params.employeeId)
  if (params.departmentId) query.set('departmentId', params.departmentId)
  return apiClient.getJson<AttendanceReport>(`/api/attendance/report?${query.toString()}`)
}

// ── Instellingen & beheer ────────────────────────────────────────────────────────

export function getAttendanceSettings(): Promise<AttendanceSettings> {
  return apiClient.getJson<AttendanceSettings>('/api/attendance/settings')
}

export function updateAttendanceSettings(settings: Omit<AttendanceSettings, 'kioskConfigured'>): Promise<AttendanceSettings> {
  return apiClient.putJson<AttendanceSettings, Omit<AttendanceSettings, 'kioskConfigured'>>(
    '/api/attendance/settings', settings)
}

export function listKioskDevices(): Promise<KioskDevice[]> {
  return apiClient.getJson<KioskDevice[]>('/api/attendance/kiosks')
}

export interface SaveKioskDeviceRequest {
  name: string
  locationId: string | null
  isActive: boolean
  defaultLanguage: 'nl' | 'fr' | 'en'
}

export function createKioskDevice(request: SaveKioskDeviceRequest): Promise<KioskProvisionResult> {
  return apiClient.postJson<KioskProvisionResult, SaveKioskDeviceRequest>('/api/attendance/kiosks', request)
}

export function updateKioskDevice(id: string, request: SaveKioskDeviceRequest): Promise<KioskDevice> {
  return apiClient.putJson<KioskDevice, SaveKioskDeviceRequest>(`/api/attendance/kiosks/${id}`, request)
}

export function rotateKioskSecret(id: string): Promise<KioskProvisionResult> {
  return apiClient.postJson<KioskProvisionResult, Record<string, never>>(
    `/api/attendance/kiosks/${id}/rotate-secret`, {})
}

export function getAttendanceCredentialStatus(employeeId: string): Promise<AttendanceCredentialStatus> {
  return apiClient.getJson<AttendanceCredentialStatus>(`/api/employees/${employeeId}/attendance-credential`)
}

export function setAttendancePin(employeeId: string, pin: string | null): Promise<AttendanceCredentialResult> {
  return apiClient.putJson<AttendanceCredentialResult, { pin: string | null }>(
    `/api/employees/${employeeId}/attendance-credential`, { pin })
}

export function disableAttendanceCredential(employeeId: string): Promise<AttendanceCredentialResult> {
  return apiClient.deleteJson<AttendanceCredentialResult>(`/api/employees/${employeeId}/attendance-credential`)
}
