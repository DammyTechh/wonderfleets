import { useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { Spinner } from '@/components/ui'
import { errorMessage, portalApi, tokens } from '@/lib/api'
import type { PortalSession } from '@/lib/types'

/**
 * Landing page for a share link (/track?t=…). It exchanges the link token for a short-lived
 * portal session — no account, no password — and routes to the dashboard for that audience.
 */
export default function PortalEntry() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const token = params.get('t') ?? params.get('token')
    if (!token) {
      setError('This link is incomplete. Please use the full link from your email or SMS.')
      return
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
        <img src="/wonderfleet.svg" alt="" className="mx-auto h-12 w-12" />
        <p className="mt-3 font-display text-xl font-bold text-brand-900">WonderFleet</p>
        {error ? (
          <div className="mt-6 rounded-2xl border border-critical-100 bg-critical-50 p-5 text-sm text-critical-700">
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
