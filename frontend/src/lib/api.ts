import axios, { AxiosError, type AxiosInstance } from 'axios'

const baseURL = import.meta.env.VITE_API_BASE_URL ?? ''

/** Tokens live in memory; the refresh token is an HttpOnly cookie set by the API. */
let accessToken: string | null = null
let portalToken: string | null = null
let onSignedOut: (() => void) | null = null

export const tokens = {
  setAccess(token: string | null) {
    accessToken = token
  },
  access: () => accessToken ?? '',
  setPortal(token: string | null) {
    portalToken = token
    if (token) sessionStorage.setItem('wf_portal', token)
    else sessionStorage.removeItem('wf_portal')
  },
  restorePortal() {
    portalToken = sessionStorage.getItem('wf_portal')
    return portalToken
  },
  onSignedOut(handler: () => void) {
    onSignedOut = handler
  },
}

function client(prefix: 'admin' | 'portal'): AxiosInstance {
  const instance = axios.create({ baseURL: `${baseURL}/api/v1`, withCredentials: true, timeout: 30000 })
  instance.interceptors.request.use((config) => {
    const token = prefix === 'admin' ? accessToken : portalToken
    if (token) config.headers.Authorization = `Bearer ${token}`
    return config
  })
  return instance
}

export const api = client('admin')
export const portalApi = client('portal')

// Refresh once per 401, then replay the original request.
let refreshing: Promise<string | null> | null = null

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const original = error.config as (typeof error.config & { _retried?: boolean }) | undefined
    if (error.response?.status !== 401 || !original || original._retried || original.url?.includes('/auth/')) {
      return Promise.reject(error)
    }
    original._retried = true
    refreshing ??= axios
      .post<{ accessToken: string }>(`${baseURL}/api/v1/auth/refresh`, {}, { withCredentials: true })
      .then((r) => r.data.accessToken)
      .catch(() => null)
      .finally(() => {
        setTimeout(() => (refreshing = null), 0)
      })

    const token = await refreshing
    if (!token) {
      tokens.setAccess(null)
      onSignedOut?.()
      return Promise.reject(error)
    }
    tokens.setAccess(token)
    original.headers.set('Authorization', `Bearer ${token}`)
    return api.request(original)
  },
)

export interface ApiProblem {
  title?: string
  detail?: string
  code?: string
  status?: number
  errors?: Record<string, string[]>
}

/** Turns any failure into a sentence a person can act on. */
export function errorMessage(error: unknown): string {
  if (axios.isAxiosError(error)) {
    const problem = error.response?.data as ApiProblem | undefined
    if (problem?.errors) {
      const first = Object.values(problem.errors)[0]
      if (first?.length) return first[0]
    }
    if (problem?.detail) return problem.detail
    if (error.code === 'ECONNABORTED') return 'The request timed out. Please try again.'
    if (!error.response) return 'Cannot reach WonderFleet. Check your connection.'
    return problem?.title ?? `Request failed (${error.response.status}).`
  }
  return error instanceof Error ? error.message : 'Something went wrong.'
}

/** Downloads a file response with the filename the API chose. */
export async function download(path: string, params?: Record<string, unknown>) {
  const response = await api.get(path, { params, responseType: 'blob' })
  const disposition = String(response.headers['content-disposition'] ?? '')
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)
  const url = URL.createObjectURL(response.data as Blob)
  const link = document.createElement('a')
  link.href = url
  link.download = decodeURIComponent(match?.[1] ?? 'wonderfleet-download')
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}
