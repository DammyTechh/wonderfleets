import {
  Activity, Bot, CircleAlert, Droplets, Leaf, Radio, Thermometer, Truck,
} from 'lucide-react'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { Link } from 'react-router-dom'
import { FleetMap } from '@/components/FleetMap'
import { Card, CardHeader, EmptyState, ErrorNote, Loading, PageHeader, StatCard, StatusChip } from '@/components/ui'
import { errorMessage } from '@/lib/api'
import { humidity as humidityText, since, signed, temperature as temperatureText } from '@/lib/format'
import { tokens } from '@/lib/api'
import { keys, useDashboard } from '@/lib/queries'
import { connectHub } from '@/lib/realtime'

export default function Dashboard() {
  const { data, isLoading, error, refetch } = useDashboard()
  const queryClient = useQueryClient()

  // Live telemetry and alerts refresh the dashboard between polls.
  useEffect(
    () =>
      connectHub(tokens.access, {
        onTelemetry: () => queryClient.invalidateQueries({ queryKey: keys.dashboard }),
        onAlert: () => {
          void queryClient.invalidateQueries({ queryKey: keys.dashboard })
          void queryClient.invalidateQueries({ queryKey: ['alerts'] })
        },
        onNotification: () => queryClient.invalidateQueries({ queryKey: keys.unread }),
      }),
    [queryClient],
  )

  if (isLoading) return <Loading rows={6} />
  if (error || !data) return <ErrorNote message={errorMessage(error)} onRetry={() => refetch()} />

  const tempDelta = signed(data.avgTemperature.deltaFromLastHour)
  const co2Delta = signed(data.co2Emission.changePercentThisWeek, 0)

  return (
    <>
      <PageHeader
        title="Dashboard"
        subtitle="Live condition of every shipment on the road right now."
        action={
          <Link to="/fleet/new" className="btn-primary">
            <Truck size={16} /> Add a fleet
          </Link>
        }
      />

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">
        <StatCard
          icon={<Truck size={18} />} tone="brand" label="Active trips"
          value={String(data.activeTrips.count)}
          hint={`${data.activeTrips.newThisWeek} started this week`}
        />
        <StatCard
          icon={<CircleAlert size={18} />} tone={data.alerts.critical > 0 ? 'critical' : 'default'}
          label="Open alerts" value={String(data.alerts.open)}
          hint={`${data.alerts.critical} critical · ${data.alerts.warning} warning`}
        />
        <StatCard
          icon={<Thermometer size={18} />} tone="warning" label="Average temperature"
          value={temperatureText(data.avgTemperature.average)}
          delta={tempDelta ? { value: `${tempDelta}°C`, good: (data.avgTemperature.deltaFromLastHour ?? 0) <= 0 } : null}
          hint="Across shipments in transit, versus the previous hour"
        />
        <StatCard
          icon={<Droplets size={18} />} label="Average humidity"
          value={humidityText(data.humidity.average)}
          hint={`${data.humidity.percentAboveThreshold}% of trucks above their limit`}
        />
        <StatCard
          icon={<Leaf size={18} />} tone="brand" label="CO₂ emissions"
          value={data.co2Emission.tonnes.toFixed(2)} unit="t"
          delta={co2Delta ? { value: `${co2Delta}%`, good: (data.co2Emission.changePercentThisWeek ?? 0) <= 0 } : null}
          hint={`Last ${data.co2Emission.windowDays} days`}
        />
      </div>

      <Card className="mt-4 overflow-hidden">
        <div className="flex flex-wrap items-center gap-4 bg-gradient-to-r from-brand-900 to-brand-700 px-5 py-4 text-white">
          <span className="grid h-10 w-10 place-items-center rounded-xl bg-white/15">
            <Bot size={20} />
          </span>
          <div className="min-w-[220px] flex-1">
            <p className="font-display text-base font-bold">WonderFleet Route AI</p>
            <p className="text-sm text-brand-100">
              Heat-aware routing cut estimated spoilage by{' '}
              <strong className="text-white">{data.routeAi.avgSpoilageReductionPct.toFixed(1)}%</strong> on average this month
              across {data.routeAi.optimizationsThisWeek} optimisation{data.routeAi.optimizationsThisWeek === 1 ? '' : 's'} this week.
            </p>
          </div>
          <Link to="/analytics" className="btn bg-white/15 text-white hover:bg-white/25">
            View analytics
          </Link>
        </div>
      </Card>

      <div className="mt-4 grid gap-4 xl:grid-cols-[1.6fr_1fr]">
        <Card className="overflow-hidden">
          <CardHeader
            title="Live fleet tracking"
            subtitle={`${data.statusCounts.live} live · ${data.statusCounts.inTransit} in transit · ${data.statusCounts.stopped} stopped · ${data.statusCounts.delay} delayed`}
            action={<Link to="/tracking" className="btn-secondary px-3 py-1.5 text-xs">Open tracking</Link>}
          />
          <FleetMap markers={data.markers} height={360} />
          <div className="flex items-center gap-2 border-t border-line px-5 py-3 text-sm text-ink-soft">
            <Radio size={15} className="text-brand-600" />
            {data.sync.message}
          </div>
        </Card>

        <Card className="overflow-hidden">
          <CardHeader title="Active fleets" subtitle="Sorted by condition, worst first" />
          {data.activeFleets.length === 0 ? (
            <EmptyState icon={<Activity size={20} />} title="No shipment is moving" description="Trips appear here once they are dispatched." />
          ) : (
            <ul className="divide-y divide-line">
              {data.activeFleets.map((fleet) => (
                <li key={fleet.tripId}>
                  <Link to={`/trips/${fleet.tripId}`} className="flex items-center gap-3 px-5 py-3.5 transition hover:bg-surface-muted">
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm font-semibold">
                        {fleet.fleetNumber} <span className="font-normal text-ink-faint">· {fleet.route}</span>
                      </p>
                      <p className="truncate text-xs text-ink-soft">{fleet.cargoSummary}</p>
                    </div>
                    <div className="tabular shrink-0 text-right text-xs">
                      <p className="font-semibold">{temperatureText(fleet.temperature)}</p>
                      <p className="text-ink-faint">{humidityText(fleet.humidity)}</p>
                    </div>
                    <StatusChip status={fleet.sensorStatus} pulse />
                  </Link>
                </li>
              ))}
            </ul>
          )}
          <p className="border-t border-line px-5 py-3 text-xs text-ink-faint">
            Last sensor sync {since(data.sync.lastSyncAt)} · {data.sync.onlineDevices}/{data.sync.expectedDevices} devices reporting
          </p>
        </Card>
      </div>
    </>
  )
}
