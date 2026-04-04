import type {
  ApiValidationProblem,
  AppConfig,
  AppStateResponse,
  CreateSessionRequest,
  SessionSummary,
} from '../types'
import { normalizeSession } from './config'

export class ApiError extends Error {
  readonly status: number
  readonly details?: ApiValidationProblem

  constructor(message: string, status: number, details?: ApiValidationProblem) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.details = details
  }
}

export async function loadAppState(signal?: AbortSignal): Promise<AppStateResponse> {
  const response = await requestJson<{
    config: AppConfig
    sessions: Array<Omit<SessionSummary, 'state'> & { state: number | string }>
  }>('/api/app-state', {
    signal,
  })

  return {
    config: response.config,
    sessions: response.sessions.map(normalizeSession),
  }
}

export async function loadSessions(signal?: AbortSignal): Promise<SessionSummary[]> {
  const response = await requestJson<Array<Omit<SessionSummary, 'state'> & { state: number | string }>>('/api/sessions', {
    signal,
  })

  return response.map(normalizeSession)
}

export async function createSession(request: CreateSessionRequest): Promise<SessionSummary> {
  const response = await requestJson<Omit<SessionSummary, 'state'> & { state: number | string }>('/api/sessions', {
    method: 'POST',
    body: JSON.stringify(request),
  })

  return normalizeSession(response)
}

export async function stopSession(sessionId: string): Promise<void> {
  await requestJson(`/api/sessions/${sessionId}/stop`, {
    method: 'POST',
  })
}

export async function saveConfig(config: AppConfig): Promise<AppConfig> {
  return requestJson<AppConfig>('/api/config', {
    method: 'PUT',
    body: JSON.stringify(config),
  })
}

async function requestJson<T = void>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(init?.headers ?? {}),
    },
  })

  if (!response.ok) {
    const errorPayload = await readError(response)
    throw new ApiError(
      errorPayload?.title ?? `Request failed with status ${response.status}.`,
      response.status,
      errorPayload,
    )
  }

  return (await readJson(response)) as T
}

async function readError(response: Response): Promise<ApiValidationProblem | undefined> {
  const payload = await response.text()
  if (!payload.trim()) {
    return undefined
  }

  const contentType = response.headers.get('content-type') ?? ''
  if (!isJsonContentType(contentType)) {
    return { title: payload }
  }

  return JSON.parse(payload) as ApiValidationProblem
}

async function readJson<T>(response: Response): Promise<T | undefined> {
  if (response.status === 204 || response.status === 205) {
    return undefined
  }

  const payload = await response.text()
  if (!payload.trim()) {
    return undefined
  }

  const contentType = response.headers.get('content-type') ?? ''
  if (!isJsonContentType(contentType)) {
    return undefined
  }

  return JSON.parse(payload) as T
}

function isJsonContentType(contentType: string): boolean {
  return /\bjson\b/i.test(contentType)
}
