import { Clock, ShieldCheck } from 'lucide-react'
import type { ReactNode } from 'react'
import { Navigate } from 'react-router-dom'
import { dt } from '@/lib/format'
import { tokens } from '@/lib/api'
import type { PortalSession } from '@/lib/types'
import { usePortalLiveUpdates } from '@/lib/live'

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
  // Before the early return: hooks must run in the same order on every render.
  const live = usePortalLiveUpdates(Boolean(session))
  if (!session) return <Navigate to="/track" replace />
  return (
    <div className="min-h-screen bg-surface-muted">
      <header className="bg-navy">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-4 px-4 py-4 lg:px-8">
          <img src="/wonderfleet-logo.png" alt="WonderFleet" className="h-8 w-auto" />
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm text-navy-dim">
              {session.organisationName} · {subtitle}
            </p>
          </div>
          <span className="chip border-transparent bg-white/10 text-navy-dim">
            <span className={live === 'live' ? 'h-2 w-2 rounded-full bg-live' : 'h-2 w-2 rounded-full bg-warning-500'} />
            {live === 'live' ? 'Live' : 'Reconnecting…'}
          </span>
          <span className="chip border-transparent bg-white/10 text-navy-dim">
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
