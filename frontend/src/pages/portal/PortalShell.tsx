import { Clock, ShieldCheck } from 'lucide-react'
import type { ReactNode } from 'react'
import { Navigate } from 'react-router-dom'
import { dt } from '@/lib/format'
import { tokens } from '@/lib/api'
import type { PortalSession } from '@/lib/types'

export function usePortalSession(expected: PortalSession['audience']) {
  const token = tokens.restorePortal()
  const raw = sessionStorage.getItem('wf_portal_meta')
  const session = raw ? (JSON.parse(raw) as PortalSession) : null
  return { ok: Boolean(token && session && session.audience === expected), session }
}

export function PortalShell({
  session, subtitle, children,
}: {
  session: PortalSession | null
  subtitle: string
  children: ReactNode
}) {
  if (!session) return <Navigate to="/track" replace />
  return (
    <div className="min-h-screen bg-surface-muted">
      <header className="border-b border-line bg-surface">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-4 px-4 py-4 lg:px-8">
          <img src="/wonderfleet.svg" alt="" className="h-10 w-10" />
          <div className="min-w-0 flex-1">
            <p className="font-display text-lg font-bold leading-tight text-brand-900">WonderFleet</p>
            <p className="truncate text-sm text-ink-soft">
              {session.organisationName} · {subtitle}
            </p>
          </div>
          <span className="chip border-line bg-surface-muted text-ink-soft">
            <Clock size={13} /> Link valid until {dt(session.linkExpiresAt)}
          </span>
        </div>
      </header>

      <main className="mx-auto max-w-6xl px-4 py-6 lg:px-8">{children}</main>

      <footer className="mx-auto max-w-6xl px-4 pb-10 lg:px-8">
        <p className="flex items-start gap-2 text-xs text-ink-faint">
          <ShieldCheck size={14} className="mt-0.5 shrink-0" />
          You are viewing a shipment shared with {session.organisationName} by OfeminiAgricTech. No account is needed,
          and this link stops working once the shipment is delivered.
        </p>
      </footer>
    </div>
  )
}
