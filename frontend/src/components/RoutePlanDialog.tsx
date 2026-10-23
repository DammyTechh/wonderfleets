import { useState } from 'react'
import { Bot, Thermometer } from '@/components/icons'
import { ErrorNote, Modal, Spinner, StatusChip } from '@/components/ui'
import { api, errorMessage } from '@/lib/api'
import { dt } from '@/lib/format'
import { keys, useApiMutation } from '@/lib/queries'
import type { RouteRecommendation } from '@/lib/types'

/**
 * Asks Route AI to plan this trip, and shows what came back.
 *
 * This is the one action that exercises the whole Google chain in a single call: the
 * pickup and destination are geocoded if the trip has no coordinates yet, the Routes API
 * returns the road alternatives, and each is sampled against the hourly forecast to pick
 * the coolest departure. Without a Google key the API still answers, using its own
 * distance heuristic and the keyless forecast, so the button is never a dead end — the
 * model name in the result says which path produced it.
 */
export function RoutePlanDialog({ tripId }: { tripId: string }) {
  const [open, setOpen] = useState(false)
  const [plan, setPlan] = useState<RouteRecommendation | null>(null)
  const [failure, setFailure] = useState<string | null>(null)

  const optimize = useApiMutation(
    () => api.post<RouteRecommendation>('/route-ai/optimize', { tripId }).then((r) => r.data),
    [keys.routeAi, keys.trip(tripId), keys.dashboard],
  )

  async function run() {
    setFailure(null)
    setPlan(null)
    setOpen(true)
    try {
      setPlan(await optimize.mutateAsync(undefined))
    } catch (error) {
      setFailure(errorMessage(error))
    }
  }

  const chosen = plan?.routes.find((route) => route.selected) ?? plan?.routes[0]

  return (
    <>
      <button className="btn-secondary" disabled={optimize.isPending} onClick={run}>
        {optimize.isPending ? <Spinner size={16} /> : <Bot size={16} />} Plan route
      </button>

      <Modal
        open={open}
        title="Route AI"
        description="Heat-aware route and departure planning for this shipment."
        onClose={() => setOpen(false)}
        width="max-w-2xl"
      >
        {optimize.isPending ? (
          <p className="flex items-center gap-2 py-6 text-sm text-ink-soft">
            <Spinner /> Reading the road alternatives and the forecast along each one…
          </p>
        ) : failure ? (
          <ErrorNote message={failure} onRetry={run} />
        ) : plan ? (
          <div className="space-y-4">
            <div className="flex flex-wrap items-center gap-3">
              <StatusChip status={plan.heatRiskLevel === 'Low' ? 'Normal' : plan.heatRiskLevel === 'Moderate' ? 'Warning' : 'Critical'} />
              <span className="text-sm text-ink-soft">
                Heat risk {plan.heatRiskLevel.toLowerCase()} · spoilage down an estimated{' '}
                <span className="tabular font-medium text-ink">{plan.estimatedSpoilageReductionPct.toFixed(1)}%</span>
              </span>
            </div>

            <p className="text-sm text-ink">{plan.summary}</p>

            <dl className="grid grid-cols-1 gap-3 rounded-lg border border-line bg-surface-muted p-4 text-sm sm:grid-cols-2">
              <div>
                <dt className="text-xs text-ink-faint">Leave at</dt>
                <dd className="font-medium">{plan.recommendedDeparture ? dt(plan.recommendedDeparture) : dt(plan.requestedDeparture)}</dd>
              </div>
              <div>
                <dt className="text-xs text-ink-faint">Chosen route</dt>
                <dd className="font-medium">
                  {chosen ? `${chosen.distanceKm.toFixed(0)} km · ${Math.round(chosen.durationMinutes / 60)} h ${chosen.durationMinutes % 60} min` : '—'}
                </dd>
              </div>
              <div>
                <dt className="text-xs text-ink-faint">Hottest point on the way</dt>
                <dd className="tabular flex items-center gap-1.5 font-medium">
                  <Thermometer size={14} className="text-ink-faint" />
                  {chosen?.maxForecastTempC != null ? `${chosen.maxForecastTempC.toFixed(1)} °C` : 'No forecast'}
                </dd>
              </div>
              <div>
                <dt className="text-xs text-ink-faint">Planned by</dt>
                <dd className="font-mono text-xs">{plan.model}</dd>
              </div>
            </dl>

            {plan.driverTips.length > 0 && (
              <div>
                <p className="mb-1.5 text-xs font-semibold uppercase tracking-wide text-ink-faint">For the driver</p>
                <ul className="list-disc space-y-1 pl-5 text-sm text-ink-soft">
                  {plan.driverTips.map((tip) => <li key={tip}>{tip}</li>)}
                </ul>
              </div>
            )}

            {plan.routes.length > 1 && (
              <div>
                <p className="mb-1.5 text-xs font-semibold uppercase tracking-wide text-ink-faint">Alternatives considered</p>
                <ul className="divide-y divide-line overflow-hidden rounded-md border border-line">
                  {plan.routes.map((route) => (
                    <li key={route.index} className="flex items-center justify-between gap-3 px-3 py-2 text-sm">
                      <span className="min-w-0 flex-1 truncate text-ink-soft">{route.description}</span>
                      <span className="tabular shrink-0 text-xs text-ink-faint">
                        {route.distanceKm.toFixed(0)} km
                        {route.maxForecastTempC != null && ` · peak ${route.maxForecastTempC.toFixed(0)} °C`}
                      </span>
                      {route.selected && <span className="shrink-0 text-xs font-medium text-brand-600">chosen</span>}
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        ) : null}

        <div className="mt-5 flex justify-end">
          <button className="btn-secondary" onClick={() => setOpen(false)}>Close</button>
        </div>
      </Modal>
    </>
  )
}
