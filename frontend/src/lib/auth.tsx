import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, tokens } from './api'
import type { AdminProfile, AuthResult } from './types'

interface AuthState {
  admin: AdminProfile | null
  ready: boolean
  signIn: (email: string, password: string) => Promise<void>
  signOut: () => Promise<void>
  setAdmin: (admin: AdminProfile) => void
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [admin, setAdmin] = useState<AdminProfile | null>(null)
  const [ready, setReady] = useState(false)

  // A refresh cookie survives a page reload, so try to restore the session silently.
  useEffect(() => {
    let cancelled = false
    ;(async () => {
      try {
        const { data } = await api.post<AuthResult>('/auth/refresh', {})
        if (cancelled) return
        tokens.setAccess(data.accessToken)
        setAdmin(data.admin)
      } catch {
        tokens.setAccess(null)
      } finally {
        if (!cancelled) setReady(true)
      }
    })()
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => tokens.onSignedOut(() => setAdmin(null)), [])

  const signIn = useCallback(async (email: string, password: string) => {
    const { data } = await api.post<AuthResult>('/auth/login', { email, password })
    tokens.setAccess(data.accessToken)
    setAdmin(data.admin)
  }, [])

  const signOut = useCallback(async () => {
    try {
      await api.post('/auth/logout', {})
    } finally {
      tokens.setAccess(null)
      setAdmin(null)
    }
  }, [])

  const value = useMemo<AuthState>(
    () => ({ admin, ready, signIn, signOut, setAdmin }),
    [admin, ready, signIn, signOut],
  )
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside AuthProvider')
  return context
}
