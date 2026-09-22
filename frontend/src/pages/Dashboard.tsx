import clsx from 'clsx'
import { Activity, Radio, Truck } from '@/components/icons'
import { Link } from 'react-router-dom'
import { FirebaseDevicesPanel } from '@/components/FirebaseDevicesPanel'
import { FleetMap } from '@/components/FleetMap'
import { Card, CardHeader, EmptyState, ErrorNote, Loading, PageHeader, StatCard, StatStrip, StatusChip } from '@/components/ui'
import { errorMessage } from '@/lib/api'
import { humidity as humidityText, since, signed, temperature as temperatureText } from '@/lib/format'
import { useDashboard } from '@/lib/queries'

export default function Dashboard() {
  const { data, isLoading, error, refetch } = useDashboard()

  // Live updates come from the shell's single connection (lib/live.ts); no second socket here.

  if (isLoading) return <Loading rows={6} />
  if (error || !data) return <ErrorNote message={errorMessage(error)} onRetry={() => refetch()} />

  const tempDelta = signed(data.avgTemperature.deltaFromLastHour)
  const co2Delta = signed(data.co2Emission.changePercentThisWeek, 0)

  return (
    <>
      <PageHeader
        title="Dashboard"
        subtitle="Cargo conditions across every shipment on the road."
        action={
          <Link to="/fleet/new" className="btn-primary">
            <Truck size={16} /> Add a fleet
          </Link>
        }
      />

      {/* Renders only when something needs attention: polling off, reads failing, or unregistered units. */}
      <div className="mb-5 empty:hidden">
        <FirebaseDevicesPanel compact />
      </div>

      <StatStrip columns={5}>
        <StatCard
          inStrip label="Active trips"
          value={String(data.activeTrips.count)}
          hint={`${data.activeTrips.newThisWeek} started this week`}
        />
        <StatCard
          inStrip tone={data.alerts.critical > 0 ? 'critical' : 'default'}
          label="Open alerts" value={String(data.alerts.open)}
          hint={`${data.alerts.critical} critical, ${data.alerts.warning} warning`}
        />
        <StatCard
          inStrip label="Average temperature"
          value={temperatureText(data.avgTemperature.average)}
          delta={tempDelta ? { value: `${tempDelta} °C`, good: (data.avgTemperature.deltaFromLastHour ?? 0) <= 0 } : null}
          hint="vs. last hour, trucks in transit"
        />
        <StatCard
          inStrip label="Average humidity"
          value={humidityText(data.humidity.average)}
          hint={`${data.humidity.percentAboveThreshold}% of trucks above their limit`}
        />
        <StatCard
          inStrip label="CO₂ emissions"
          value={data.co2Emission.tonnes.toFixed(2)} unit="t"
          delta={co2Delta ? { value: `${co2Delta}%`, good: (data.co2Emission.changePercentThisWeek ?? 0) <= 0 } : null}
          hint={`last ${data.co2Emission.windowDays} days`}
        />
      </StatStrip>

      {/* Route AI: flat navy panel with its three figures, as in the Figma. */}
      <section className="mt-4 flex flex-col gap-4 rounded-lg bg-navy px-5 py-4 text-white lg:flex-row lg:items-center">
        <div className="lg:w-64">
          <p className="text-[15px] font-semibold">Route AI</p>
          <p className="text-[13px] text-navy-dim">Heat-aware route and departure planning</p>
        </div>
        <dl className="grid flex-1 grid-cols-3 gap-px overflow-hidden rounded-md bg-white/10">
          {[
            ['Routes optimised', String(data.routeAi.optimizationsThisWeek), 'this week'],
            ['Spoilage reduction', `${data.routeAi.avgSpoilageReductionPct.toFixed(1)}%`, 'average this month'],
            ['Critical alerts', String(data.routeAi.criticalAlerts), 'open now'],
          ].map(([term, value, note]) => (
            <div key={term} className="bg-navy px-4 py-2">
              <dt className="text-xs text-navy-dim">{term}</dt>
              <dd className="tabular text-xl font-semibold leading-7">{value}</dd>
              <dd className="text-xs text-navy-label">{note}</dd>
            </div>
          ))}
        </dl>
        <Link to="/analytics" className="btn shrink-0 border border-white/20 text-white hover:bg-white/10">
          View analytics
        </Link>
      </section>

      <div className="mt-4 grid grid-cols-1 gap-4 xl:grid-cols-[1.6fr_1fr]">
        <Card className="overflow-hidden">
          <CardHeader
            title="Live fleet tracking"
            action={<Link to="/tracking" className="btn-secondary h-8 px-3 text-[13px]">Open tracking</Link>}
          />
          <dl className="flex flex-wrap gap-x-5 gap-y-1 border-b border-line px-5 py-2.5 text-[13px]">
            {[
              ['Live', data.statusCounts.live, 'bg-normal-500'],
              ['In transit', data.statusCounts.inTransit, 'bg-info-500'],
              ['Stopped', data.statusCounts.stopped, 'bg-critical-500'],
              ['Delayed', data.statusCounts.delay, 'bg-warning-500'],
            ].map(([term, count, dot]) => (
              <div key={term as string} className="flex items-center gap-1.5">
                <span className={`h-2 w-2 rounded-full ${dot}`} aria-hidden />
                <dt className="text-ink-soft">{term}</dt>
                <dd className="tabular font-medium">{count}</dd>
              </div>
            ))}
          </dl>
          <FleetMap markers={data.markers} height={360} />
          <p className="flex items-center gap-2 border-t border-line px-5 py-2.5 text-[13px] text-ink-soft">
            <Radio size={15} className="text-brand-500" />
            {data.sync.message}
          </p>
        </Card>

        <Card className="flex flex-col overflow-hidden">
          <CardHeader title="Active fleets" subtitle="Worst condition first" />
          {data.activeFleets.length === 0 ? (
            <EmptyState icon={<Activity size={22} />} title="No shipment is moving" description="Trips appear here once they are dispatched." />
          ) : (
            <ul className="flex-1 divide-y divide-line">
              {data.activeFleets.map((fleet) => (
                <li key={fleet.tripId}>
                  <Link to={`/trips/${fleet.tripId}`} className="flex items-center gap-4 px-5 py-3 transition-colors hover:bg-surface-muted">
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm">
                        <span className="code-id">{fleet.fleetNumber}</span>
                        <span className="ml-2 text-ink">{fleet.route}</span>
                      </p>
                      <p className="truncate text-xs text-ink-soft">{fleet.cargoSummary}</p>
                    </div>
                    <div className="tabular shrink-0 text-right">
                      <p className={clsx('text-sm font-medium', fleet.sensorStatus === 'Critical' && 'text-critical-600',
                        fleet.sensorStatus === 'Warning' && 'text-warning-600')}>{temperatureText(fleet.temperature)}</p>
                      <p className="text-xs text-ink-soft">{humidityText(fleet.humidity)}</p>
                    </div>
                    <span className="w-[84px] shrink-0 text-right"><StatusChip status={fleet.sensorStatus} pulse /></span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
          <p className="border-t border-line px-5 py-2.5 text-xs text-ink-soft">
            {data.sync.onlineDevices} of {data.sync.expectedDevices} devices reporting, last reading {since(data.sync.lastSyncAt)}
          </p>
        </Card>
      </div>
    </>
  )
}
