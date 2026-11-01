export type DeviceKind = 'gamepad' | 'video' | 'audio'
export type Phase = 'disconnected' | 'connecting' | 'connected' | 'disconnecting' | 'error'
export interface DeviceState {
  phase: Phase
  id: string | null
  detail: string
  error: string | null
}
export interface Snapshot {
  gamepad: DeviceState
  video: DeviceState
  audio: DeviceState
  rms: number
  dbfs: number | null
}
export interface DeviceOption { id: string; name: string }
export interface FileEntry { name: string; path: string; directory: boolean }
export interface FileListing { directory: string; parent: string | null; entries: FileEntry[] }
export interface Selection { path: string; preferences: { video: string | null; gamepad: string | null } }

export function emptyDevice(): DeviceState {
  return { phase: 'disconnected', id: null, detail: '', error: null }
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(`/api${path}`, {
    ...init,
    headers: { ...(init.body ? { 'Content-Type': 'application/json' } : {}), ...init.headers },
  })
  if (!response.ok) {
    const text = await response.text()
    let message = text || `请求失败 (${response.status})`
    try { message = JSON.parse(text).error ?? message } catch { /* Non-JSON server errors. */ }
    throw new Error(message)
  }
  return response.status === 204 ? undefined as T : response.json()
}

export function message(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}
