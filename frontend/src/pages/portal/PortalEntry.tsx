import { useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { Spinner } from '@/components/ui'
import { errorMessage, portalApi, tokens } from '@/lib/api'
import type { PortalSession } from '@/lib/types'

/**
 * Reads the token out of a share link, wherever it was put.
 *
 * The API issues links as `/share#<token>`: a URL fragment is never sent to a server, so the
 * token stays out of access logs and out of the Referer header when the partner follows a
 * link onward. Older links carry it as `?t=` instead, and both must keep working — a partner
 * holding a link that no longer opens has no way to ask for a new one.
 */
function readToken(search: URLSearchParams, hash: string): string | null {
  const fromQuery = search.get('t') ?? search.get('token')
  if (fromQuery) return fromQuery

  const raw = hash.replace(/^#/, '')
  if (!raw) return null
  // Either "#<token>" or "#t=<token>".
  if (raw.includes('=')) return new URLSearchParams(raw).get('t') ?? new URLSearchParams(raw).get('token')
  return decodeURIComponent(raw)
}

/**
 * Landing page for a share link. It exchanges the link token for a short-lived portal
 * session — no account, no password — and routes to the dashboard for that audience.
 */
export default function PortalEntry() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const token = readToken(params, window.location.hash)
    if (!token) {
      setError('This link is incomplete. Please use the full link from your email or message.')
      return
    }
    // Take the token out of the address bar once it has been read, so it is not left in
    // view, in the browser history, or in anything the partner later copies and pastes.
    if (window.location.hash) {
      window.history.replaceState(null, '', window.location.pathname + window.location.search)
    }
    let cancelled = false
    ;(async () => {
      try {
        const { data } = await portalApi.post<PortalSession>('/portal/session', { token })
        if (cancelled) return
        tokens.setPortal(data.accessToken)
        sessionStorage.setItem('wf_portal_meta', JSON.stringify(data))
        navigate(data.audience === 'LogisticsPartner' ? '/portal/logistics' : '/portal/agro', { replace: true })
      } catch (caught) {
        if (!cancelled) setError(errorMessage(caught))
      }
    })()
    return () => {
      cancelled = true
    }
  }, [params, navigate])

  return (
    <div className="grid min-h-screen place-items-center bg-surface-muted px-6">
      <div className="w-full max-w-md text-center">
        <img src="/wonderfleet-logo.png" alt="WonderFleet" className="mx-auto h-11 w-auto" />
        <p className="mt-3 font-display text-xl font-semibold text-brand-900">WonderFleet</p>
        {error ? (
          <div className="mt-6 rounded-lg border border-critical-100 bg-critical-50 p-5 text-sm text-critical-700">
            <p className="font-semibold">This tracking link is not working</p>
            <p className="mt-1">{error}</p>
            <p className="mt-3 text-xs text-critical-600">
              Links expire when the shipment is delivered. Ask your WonderFleet contact for a fresh link.
            </p>
          </div>
        ) : (
          <p className="mt-6 flex items-center justify-center gap-2 text-sm text-ink-soft">
            <Spinner /> Opening your shipment dashboard…
          </p>
        )}
      </div>
    </div>
  )
}
