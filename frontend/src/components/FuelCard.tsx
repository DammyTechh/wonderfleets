import clsx from 'clsx'
import { CloudSun, Fuel, Gauge, Info, Snowflake, TrafficCone } from '@/components/icons'
import type { FuelEstimate } from '@/lib/types'

const money = (amount: number, currency: string) =>
  currency === 'NGN' ? `₦${Math.round(amount).toLocaleString()}` : `${amount.toLocaleString()} ${currency}`

const componentColour: Record<string, string> = {
  base: 'bg-brand-600',
  payload: 'bg-brand-400',
  traffic: 'bg-warning-500',
  road: 'bg-ink-faint',
  heat: 'bg-critical-500',
  rain: 'bg-info-500',
  reefer: 'bg-info-700',
  idle: 'bg-line-strong',
}

const confidenceStyle: Record<string, string> = {
  High: 'border-brand-100 bg-brand-50 text-brand-700',
  Medium: 'border-warning-100 bg-warning-50 text-warning-700',
  Low: 'border-line-strong bg-surface-sunken text-ink-soft',
}

/**
 * The dispatch figure with its reasoning: where every litre goes, and what the estimate
 * assumed when the route or the forecast could not be reached.
 */
export function FuelBreakdown({ estimate, compact }: { estimate: FuelEstimate; compact?: boolean }) {
  return (
    <div className="space-y-4">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <div className="rounded-md border border-brand-200 bg-brand-50 p-4">
          <p className="flex items-center gap-1.5 text-xs font-medium text-brand-700">
            <Fuel size={13} /> Send with the truck
          </p>
          <p className="tabular mt-1 text-2xl font-semibold text-brand-900">{estimate.recommendedLitres.toFixed(0)} L</p>
          <p className="mt-0.5 text-sm font-semibold text-brand-800">{money(estimate.recommendedCost, estimate.currency)}</p>
          <p className="mt-1 text-[11px] text-brand-700">
            Estimate {estimate.totalLitres.toFixed(0)} L plus a 10% margin
            {estimate.tankFills != null && ` · ${estimate.tankFills.toFixed(2)} tank fills`}
          </p>
        </div>
        <div className="rounded-md border border-line p-4">
          <p className="flex items-center gap-1.5 text-xs font-medium text-ink-faint">
            <Gauge size={13} /> Consumption
          </p>
          <p className="tabular mt-1 text-2xl font-semibold">{estimate.litresPer100Km.toFixed(1)}</p>
          <p className="text-xs text-ink-soft">L per 100 km over {estimate.distanceKm.toFixed(0)} km</p>
          <p className="mt-1 text-[11px] text-ink-faint">
            {estimate.fuelType} at {money(estimate.pricePerLitre, estimate.currency)}/L
          </p>
        </div>
        <div className="rounded-md border border-line p-4">
          <p className="flex items-center gap-1.5 text-xs font-medium text-ink-faint">
            <TrafficCone size={13} /> Conditions
          </p>
          <p className="mt-1 text-sm font-semibold">{estimate.trafficLabel}</p>
          <p className="mt-1 flex items-center gap-1.5 text-xs text-ink-soft">
            <CloudSun size={13} />
            {estimate.avgAmbientC == null ? 'No forecast' : `${estimate.avgAmbientC.toFixed(0)}°C average, peak ${estimate.peakAmbientC?.toFixed(0) ?? '—'}°C`}
          </p>
          {estimate.refrigerated && (
            <p className="mt-1 flex items-center gap-1.5 text-xs text-info-700">
              <Snowflake size={13} /> Cooling unit running
            </p>
          )}
        </div>
      </div>

      <div>
        <div className="flex h-2.5 w-full overflow-hidden rounded-full bg-surface-sunken">
          {estimate.components
            .filter((component) => component.litres > 0)
            .map((component) => (
              <span
                key={component.key}
                title={`${component.label}: ${component.litres.toFixed(1)} L`}
                className={clsx('h-full', componentColour[component.key] ?? 'bg-ink-faint')}
                style={{ width: `${component.percent}%` }}
              />
            ))}
        </div>

        <ul className="mt-3 space-y-1.5">
          {estimate.components
            .filter((component) => component.litres > 0)
            .slice(0, compact ? 5 : undefined)
            .map((component) => (
              <li key={component.key} className="flex items-center gap-3 text-sm">
                <span className={clsx('h-2.5 w-2.5 shrink-0 rounded-full', componentColour[component.key] ?? 'bg-ink-faint')} />
                <span className="min-w-0 flex-1">
                  <span className="font-medium">{component.label}</span>
                  {!compact && <span className="block text-xs text-ink-faint">{component.detail}</span>}
                </span>
                <span className="tabular shrink-0 text-right">
                  <span className="font-semibold">{component.litres.toFixed(1)} L</span>
                  <span className="ml-2 text-xs text-ink-faint">{component.percent.toFixed(0)}%</span>
                </span>
              </li>
            ))}
        </ul>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <span className={clsx('chip', confidenceStyle[estimate.confidence])}>{estimate.confidence} confidence</span>
        <span className="chip border-line bg-surface-muted text-ink-soft">CO₂ {estimate.co2Kg.toFixed(0)} kg</span>
        <span className="chip border-line bg-surface-muted text-ink-soft">
          {Math.floor(estimate.durationMinutes / 60)}h {estimate.durationMinutes % 60}m driving
        </span>
      </div>

      {!compact && estimate.assumptions.length > 0 && (
        <ul className="space-y-1 rounded-md bg-surface-sunken p-3.5 text-xs text-ink-soft">
          {estimate.assumptions.map((assumption) => (
            <li key={assumption} className="flex items-start gap-2">
              <Info size={13} className="mt-0.5 shrink-0" />
              {assumption}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

export { money as formatMoney }
