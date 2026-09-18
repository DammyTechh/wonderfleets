import { useQuery } from '@tanstack/react-query'
import { Droplets, PackageCheck, Thermometer } from 'lucide-react'
import { CartesianGrid, Line, LineChart, ReferenceArea, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { useState } from 'react'
import { FleetMap } from '@/components/FleetMap'
import { Card, CardHeader, EmptyState, ErrorNote, Loading, StatusChip } from '@/components/ui'
import { errorMessage, portalApi } from '@/lib/api'
import { dt, humidity as humidityText, temperature as temperatureText } from '@/lib/format'
import type { AgroShipment, AgroTracking, TrackPoint } from '@/lib/types'
import { PortalShell, usePortalSession } from './PortalShell'

/** Produce-owner view: where my goods are, and the conditions they are travelling in. */
export default function AgroPortal() {
  const { session } = usePortalSession('AgroProcessor')
  const [selected, setSelected] = useState<string | null>(null)

  const shipments = useQuery({
    queryKey: ['portal', 'agro', 'shipments'],
    queryFn: async () => (await portalApi.get<AgroShipment[]>('/portal/agro/shipments')).data,
    refetchInterval: 30_000,
    enabled: Boolean(session),
  })

  const tracking = useQuery({
    queryKey: ['portal', 'agro', 'tracking'],
    queryFn: async () => (await portalApi.get<AgroTracking>('/portal/agro/tracking')).data,
    refetchInterval: 20_000,
    enabled: Boolean(session),
  })

  const activeTripId = selected ?? shipments.data?.[0]?.tripId ?? null
  const active = shipments.data?.find((shipment) => shipment.tripId === activeTripId)

  const readings = useQuery({
    queryKey: ['portal', 'agro', 'readings', activeTripId],
    enabled: Boolean(activeTripId),
    refetchInterval: 60_000,
    queryFn: async () =>
      (await portalApi.get<TrackPoint[]>(`/portal/agro/shipments/${activeTripId}/readings`, { params: { hours: 12 } })).data,
  })

  const chartData = (readings.data ?? []).map((reading) => ({
    time: dt(reading.recordedAt, 'HH:mm'),
    temperature: reading.temperature ?? null,
    humidity: reading.humidity ?? null,
  }))

  return (
    <PortalShell session={session} subtitle="Produce in transit">
      {shipments.isLoading ? (
        <Loading rows={4} />
      ) : shipments.error ? (
        <ErrorNote message={errorMessage(shipments.error)} onRetry={() => shipments.refetch()} />
      ) : shipments.data && shipments.data.length > 0 ? (
        <div className="grid gap-4">
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {shipments.data.map((shipment) => (
              <button
                key={shipment.tripId}
                onClick={() => setSelected(shipment.tripId)}
                className={
                  shipment.tripId === activeTripId
                    ? 'card border-brand-300 p-4 text-left ring-4 ring-brand-500/10'
                    : 'card p-4 text-left transition hover:border-line-strong'
                }
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="font-mono text-xs text-ink-faint">{shipment.tripCode}</p>
                    <p className="truncate text-sm font-semibold">{shipment.produce.join(', ') || 'Produce'}</p>
                    <p className="truncate text-xs text-ink-soft">{shipment.route}</p>
                  </div>
                  <StatusChip status={shipment.sensorStatus} pulse />
                </div>
                <dl className="tabular mt-3 grid grid-cols-2 gap-2 text-sm">
                  <div className="rounded-lg bg-surface-sunken px-2.5 py-2">
                    <dt className="flex items-center gap-1 text-[11px] text-ink-faint">
                      <Thermometer size={12} /> Temp
                    </dt>
                    <dd className="font-semibold">{temperatureText(shipment.temperature)}</dd>
                  </div>
                  <div className="rounded-lg bg-surface-sunken px-2.5 py-2">
                    <dt className="flex items-center gap-1 text-[11px] text-ink-faint">
                      <Droplets size={12} /> Humidity
                    </dt>
                    <dd className="font-semibold">{humidityText(shipment.humidity)}</dd>
                  </div>
                </dl>
                <p className="mt-2 text-xs text-ink-faint">
                  Expected {dt(shipment.expectedArrival)}
                  {shipment.progressPercent != null && ` · ${shipment.progressPercent}% of the way`}
                </p>
                {shipment.progressPercent != null && (
                  <span className="mt-1.5 block h-1.5 w-full overflow-hidden rounded-full bg-surface-sunken">
                    <span className="block h-full rounded-full bg-brand-500" style={{ width: `${Math.min(100, shipment.progressPercent)}%` }} />
                  </span>
                )}
              </button>
            ))}
          </div>

          <div className="grid gap-4 xl:grid-cols-[1.4fr_1fr]">
            <Card className="overflow-hidden">
              <CardHeader
                title="Where your produce is"
                subtitle={
                  tracking.data
                    ? `${tracking.data.gpsMonitor.activeSignals} active signals · ${tracking.data.gpsMonitor.localTime} ${tracking.data.gpsMonitor.timeZone}`
                    : 'Waiting for the first fix'
                }
              />
              <FleetMap
                markers={(tracking.data?.markers ?? []).filter((marker) => !activeTripId || marker.tripId === activeTripId)}
                height={340}
              />
            </Card>

            <Card>
              <CardHeader
                title="Conditions, last 12 hours"
                subtitle={active ? `Safe range ${active.minTemperature}–${active.maxTemperature} °C` : undefined}
              />
              <div className="h-[280px] p-4">
                {readings.isLoading ? (
                  <Loading rows={2} />
                ) : chartData.length === 0 ? (
                  <p className="grid h-full place-items-center text-sm text-ink-faint">No readings yet.</p>
                ) : (
                  <ResponsiveContainer width="100%" height="100%">
                    <LineChart data={chartData} margin={{ top: 8, right: 8, bottom: 0, left: -20 }}>
                      <CartesianGrid stroke="#e6ece9" vertical={false} />
                      <XAxis dataKey="time" tick={{ fontSize: 11, fill: '#8b9891' }} tickLine={false} axisLine={false} minTickGap={24} />
                      <YAxis tick={{ fontSize: 11, fill: '#8b9891' }} tickLine={false} axisLine={false} />
                      <Tooltip contentStyle={{ borderRadius: 12, border: '1px solid #e6ece9', fontSize: 12 }} />
                      {active && (
                        <ReferenceArea
                          y1={active.minTemperature}
                          y2={active.maxTemperature}
                          fill="#d6f5e2"
                          fillOpacity={0.6}
                        />
                      )}
                      <Line type="monotone" dataKey="temperature" name="Temperature (°C)" stroke="#158554" strokeWidth={2.5} dot={false} connectNulls />
                      <Line type="monotone" dataKey="humidity" name="Humidity (%)" stroke="#2e90fa" strokeWidth={2} dot={false} strokeDasharray="4 3" connectNulls />
                    </LineChart>
                  </ResponsiveContainer>
                )}
              </div>
            </Card>
          </div>
        </div>
      ) : (
        <EmptyState
          icon={<PackageCheck size={20} />}
          title="No shipment in transit"
          description="Everything shared through this link has been delivered."
        />
      )}
    </PortalShell>
  )
}
