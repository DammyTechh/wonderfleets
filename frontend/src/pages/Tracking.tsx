import { Satellite } from '@/components/icons'
import { FleetMap } from '@/components/FleetMap'
import { Card, CardHeader, ErrorNote, Loading, PageHeader, StatCard, StatStrip, StatusChip } from '@/components/ui'
import { errorMessage } from '@/lib/api'
import { since, temperature as temperatureText, watLabel } from '@/lib/format'
import { useLiveTracking } from '@/lib/queries'

export default function Tracking() {
  const { data, isLoading, error, refetch } = useLiveTracking()

  if (isLoading) return <Loading rows={6} />
  if (error || !data) return <ErrorNote message={errorMessage(error)} onRetry={() => refetch()} />

  const { markers, statusCounts, deviceSummary, gpsMonitor, alertTriggers, climate, sync } = data

  return (
    <>
      <PageHeader title="Live tracking" subtitle="Positions, signals and device health across the fleet." />

      <StatStrip columns={4}>
        <StatCard inStrip label="Live" value={String(statusCounts.live)} hint="reporting now" />
        <StatCard inStrip label="In transit" value={String(statusCounts.inTransit)} hint="moving" />
        <StatCard inStrip label="Stopped" value={String(statusCounts.stopped)} tone={statusCounts.stopped > 0 ? 'warning' : 'default'} hint="stationary" />
        <StatCard inStrip label="Delayed" value={String(statusCounts.delay)} tone={statusCounts.delay > 0 ? 'warning' : 'default'} hint="behind schedule" />
      </StatStrip>

      <div className="mt-4 grid grid-cols-1 gap-4 xl:grid-cols-[1.6fr_1fr]">
        <Card className="self-start overflow-hidden">
          <CardHeader
            title="Fleet positions"
            subtitle={`${markers.length} vehicle${markers.length === 1 ? '' : 's'} reporting`}
            action={<span className="tabular text-[13px] text-ink-soft">{watLabel()}</span>}
          />
          <FleetMap markers={markers} height={520} />
        </Card>

        <div className="space-y-4">
          <Card className="overflow-hidden">
            <CardHeader
              title="Device sensor summary"
              subtitle={`${temperatureText(climate.avgTemperature)} average · ${climate.temperatureState}`}
            />
            <div className="grid grid-cols-4 divide-x divide-line">
              {([
                ['Normal', deviceSummary.normal],
                ['Warning', deviceSummary.warning],
                ['Critical', deviceSummary.critical],
                ['Offline', deviceSummary.offline],
              ] as const).map(([label, value]) => (
                <div key={label} className="px-3 py-4 text-center">
                  <p className="text-xs font-medium text-ink-faint">{label}</p>
                  <p className="tabular mt-1 text-xl font-semibold">{value}</p>
                </div>
              ))}
            </div>
            <p className="flex items-center gap-2 border-t border-line px-5 py-3 text-xs text-ink-soft">
              <Satellite size={14} /> {sync.message}
            </p>
          </Card>

          <Card className="overflow-hidden">
            <CardHeader
              title="GPS & time monitor"
              subtitle={`${gpsMonitor.activeSignals} active signals · ${gpsMonitor.localTime} ${gpsMonitor.timeZone}`}
            />
            <ul className="divide-y divide-line text-sm">
              {gpsMonitor.recentPositions.slice(0, 6).map((position) => (
                <li key={`${position.vehicleCode}-${position.recordedAt}`} className="flex items-center justify-between gap-3 px-5 py-2.5">
                  <span className="tabular text-[13px] font-medium">{position.vehicleCode}</span>
                  <span className="min-w-0 flex-1 truncate text-xs text-ink-faint">{position.route}</span>
                  <span className="tabular whitespace-nowrap text-sm font-medium">
                    {position.speedKmh == null ? '—' : `${position.speedKmh.toFixed(0)} km/h`}
                  </span>
                </li>
              ))}
              {gpsMonitor.recentPositions.length === 0 && (
                <li className="px-5 py-8 text-center text-ink-faint">No fixes in the last hour.</li>
              )}
            </ul>
          </Card>

          <Card className="overflow-hidden">
            <CardHeader title="Alert triggers" subtitle="Conditions raised on the road" />
            <ul className="divide-y divide-line">
              {alertTriggers.slice(0, 5).map((alert) => (
                <li key={alert.id} className="flex items-start gap-3 px-5 py-3">
                  <StatusChip status={alert.severity} pulse />
                  <span className="min-w-0 flex-1">
                    <span className="block text-sm font-medium">{alert.title}</span>
                    <span className="block truncate text-xs text-ink-faint">
                      {alert.vehicleCode ?? alert.deviceSerial ?? '—'} · {alert.route} · {since(alert.at)}
                    </span>
                  </span>
                </li>
              ))}
              {alertTriggers.length === 0 && (
                <li className="px-5 py-8 text-center text-sm text-ink-faint">Nothing has tripped a threshold.</li>
              )}
            </ul>
          </Card>
        </div>
      </div>
    </>
  )
}
