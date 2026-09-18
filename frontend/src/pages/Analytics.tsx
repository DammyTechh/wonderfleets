import { Activity, CloudSun, Download, Droplets, FileText, Thermometer, TriangleAlert } from 'lucide-react'
import { useState } from 'react'
import {
  Bar, BarChart, CartesianGrid, Cell, Line, LineChart, ReferenceArea, ReferenceLine,
  ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import { Card, CardHeader, ErrorNote, Loading, PageHeader, StatCard, StatusChip } from '@/components/ui'
import { download, errorMessage } from '@/lib/api'
import { percent, signed, since } from '@/lib/format'
import { useAnalytics, useReportCatalog } from '@/lib/queries'

const PERIODS = ['week', 'month', 'quarter', 'year'] as const

export default function Analytics() {
  const [period, setPeriod] = useState<(typeof PERIODS)[number]>('month')
  const [deviceId, setDeviceId] = useState('')
  const { data, isLoading, error, refetch } = useAnalytics({ period, deviceId: deviceId || undefined })
  const reports = useReportCatalog()

  if (isLoading) return <Loading rows={6} />
  if (error || !data) return <ErrorNote message={errorMessage(error)} onRetry={() => refetch()} />

  const kpis = data.kpis
  const temperatureData = data.averageTemperature.points.map((point) => ({ label: point.label, value: point.value }))
  const humidityData = data.averageHumidity.points.map((point) => ({ label: point.label, value: point.value }))

  return (
    <>
      <PageHeader
        title="Analytics"
        subtitle="Sensor reliability, cold-chain compliance and spoilage risk."
        action={
          <div className="flex flex-wrap gap-2">
            <select className="input w-auto" value={deviceId} onChange={(event) => setDeviceId(event.target.value)}>
              <option value="">All devices</option>
              {data.deviceHealth.map((device) => (
                <option key={device.deviceId} value={device.deviceId}>
                  {device.serial}
                </option>
              ))}
            </select>
            <div className="flex rounded-xl border border-line bg-surface p-1">
              {PERIODS.map((option) => (
                <button
                  key={option}
                  onClick={() => setPeriod(option)}
                  className={
                    option === period
                      ? 'rounded-lg bg-brand-50 px-3 py-1.5 text-xs font-semibold capitalize text-brand-800'
                      : 'rounded-lg px-3 py-1.5 text-xs font-medium capitalize text-ink-soft hover:bg-surface-muted'
                  }
                >
                  {option}
                </button>
              ))}
            </div>
          </div>
        }
      />

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard
          icon={<Activity size={18} />} tone="brand" label="Sensor uptime"
          value={percent(kpis.sensorUptime.value)}
          delta={kpis.sensorUptime.delta != null ? { value: `${signed(kpis.sensorUptime.delta)}%`, good: kpis.sensorUptime.delta >= 0 } : null}
        />
        <StatCard
          icon={<Thermometer size={18} />} label="Temperature compliance"
          value={percent(kpis.temperatureCompliance.value)}
          delta={kpis.temperatureCompliance.delta != null ? { value: `${signed(kpis.temperatureCompliance.delta)}%`, good: kpis.temperatureCompliance.delta >= 0 } : null}
        />
        <StatCard
          icon={<Droplets size={18} />} label="Humidity compliance"
          value={percent(kpis.humidityCompliance.value)}
          delta={kpis.humidityCompliance.delta != null ? { value: `${signed(kpis.humidityCompliance.delta)}%`, good: kpis.humidityCompliance.delta >= 0 } : null}
        />
        <StatCard
          icon={<TriangleAlert size={18} />} tone="warning" label="Estimated spoilage"
          value={percent(kpis.estimatedSpoilage.value)}
          delta={kpis.estimatedSpoilage.delta != null ? { value: `${signed(kpis.estimatedSpoilage.delta)}%`, good: kpis.estimatedSpoilage.delta <= 0 } : null}
        />
      </div>

      <div className="mt-4 grid gap-4 xl:grid-cols-2">
        <Card>
          <CardHeader title="Average temperature" subtitle={`Threshold ${data.averageTemperature.thresholdC} °C`} />
          <div className="h-64 p-4">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={temperatureData} margin={{ top: 8, right: 12, bottom: 0, left: -18 }}>
                <CartesianGrid stroke="#e6ece9" vertical={false} />
                <XAxis dataKey="label" tick={{ fontSize: 11, fill: '#8b9891' }} tickLine={false} axisLine={false} minTickGap={16} />
                <YAxis tick={{ fontSize: 11, fill: '#8b9891' }} tickLine={false} axisLine={false} unit="°" />
                <Tooltip
                  contentStyle={{ borderRadius: 12, border: '1px solid #e6ece9', fontSize: 12 }}
                  formatter={(value: number) => [`${value?.toFixed?.(1) ?? value} °C`, 'Average']}
                />
                <ReferenceLine y={data.averageTemperature.thresholdC} stroke="#f04438" strokeDasharray="4 4" />
                <Line type="monotone" dataKey="value" stroke="#158554" strokeWidth={2.5} dot={false} connectNulls />
              </LineChart>
            </ResponsiveContainer>
          </div>
        </Card>

        <Card>
          <CardHeader title="Average humidity" subtitle={`Safe band ${data.averageHumidity.safeMin}–${data.averageHumidity.safeMax}% RH`} />
          <div className="h-64 p-4">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={humidityData} margin={{ top: 8, right: 12, bottom: 0, left: -18 }}>
                <CartesianGrid stroke="#e6ece9" vertical={false} />
                <XAxis dataKey="label" tick={{ fontSize: 11, fill: '#8b9891' }} tickLine={false} axisLine={false} minTickGap={16} />
                <YAxis tick={{ fontSize: 11, fill: '#8b9891' }} tickLine={false} axisLine={false} unit="%" domain={[0, 100]} />
                <Tooltip contentStyle={{ borderRadius: 12, border: '1px solid #e6ece9', fontSize: 12 }} />
                <ReferenceArea y1={data.averageHumidity.safeMin} y2={data.averageHumidity.safeMax} fill="#d6f5e2" fillOpacity={0.55} />
                <Bar dataKey="value" radius={[6, 6, 0, 0]}>
                  {humidityData.map((point, index) => (
                    <Cell
                      key={index}
                      fill={
                        point.value == null ? '#d3ded8'
                          : point.value > data.averageHumidity.safeMax || point.value < data.averageHumidity.safeMin
                            ? '#f79009' : '#46bf85'
                      }
                    />
                  ))}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>
        </Card>
      </div>

      <div className="mt-4 grid gap-4 xl:grid-cols-3">
        <Card>
          <CardHeader title="Trips by partner" />
          <ul className="divide-y divide-line">
            {data.tripsByPartners.map((partner) => (
              <li key={partner.partnerId} className="flex items-center gap-3 px-5 py-3">
                <span className="min-w-0 flex-1 truncate text-sm font-medium">{partner.partnerName}</span>
                <span className="h-2 w-24 overflow-hidden rounded-full bg-surface-sunken">
                  <span
                    className="block h-full rounded-full bg-brand-500"
                    style={{ width: `${Math.min(100, (partner.trips / Math.max(...data.tripsByPartners.map((p) => p.trips), 1)) * 100)}%` }}
                  />
                </span>
                <span className="tabular w-8 text-right text-sm font-semibold">{partner.trips}</span>
              </li>
            ))}
            {data.tripsByPartners.length === 0 && <li className="px-5 py-8 text-center text-sm text-ink-faint">No trips in this period.</li>}
          </ul>
        </Card>

        <Card>
          <CardHeader title="Device health" />
          <ul className="divide-y divide-line">
            {data.deviceHealth.slice(0, 6).map((device) => (
              <li key={device.deviceId} className="flex items-center gap-3 px-5 py-3">
                <span className="min-w-0 flex-1">
                  <span className="block font-mono text-sm font-medium">{device.serial}</span>
                  <span className="block truncate text-xs text-ink-faint">{device.route ?? 'Idle'} · {since(device.lastSeenAt)}</span>
                </span>
                {device.batteryLevel != null && <span className="tabular text-xs text-ink-soft">{device.batteryLevel}%</span>}
                <StatusChip status={device.sensorStatus} />
              </li>
            ))}
            {data.deviceHealth.length === 0 && <li className="px-5 py-8 text-center text-sm text-ink-faint">No devices registered.</li>}
          </ul>
        </Card>

        <Card>
          <CardHeader
            title="Weather intelligence"
            subtitle={data.weatherIntelligence?.fleetNumber ? `Along ${data.weatherIntelligence.fleetNumber}'s route` : 'Along the active corridor'}
            action={<CloudSun size={18} className="text-ink-faint" />}
          />
          <ul className="divide-y divide-line">
            {([
              ['Pickup', data.weatherIntelligence?.origin],
              ['Current position', data.weatherIntelligence?.current],
              ['Destination', data.weatherIntelligence?.destination],
            ] as const)
              .filter(([, point]) => Boolean(point))
              .map(([stage, point]) => (
                <li key={stage} className="flex items-center justify-between gap-3 px-5 py-3">
                  <span className="min-w-0">
                    <span className="block text-sm font-medium">{point!.label}</span>
                    <span className="block text-xs text-ink-faint">{stage} · {point!.condition}</span>
                  </span>
                  <span className="tabular whitespace-nowrap text-sm font-semibold">
                    {point!.temperatureC.toFixed(0)}°C · {Math.round(point!.relativeHumidity)}%
                  </span>
                </li>
              ))}
            {!data.weatherIntelligence?.origin && !data.weatherIntelligence?.current && !data.weatherIntelligence?.destination && (
              <li className="px-5 py-8 text-center text-sm text-ink-faint">Weather data appears once a shipment is moving.</li>
            )}
          </ul>
        </Card>
      </div>

      <Card className="mt-4">
        <CardHeader title="Status reports" subtitle={reports.data?.[0]?.period ?? 'Current month'} />
        <ul className="divide-y divide-line">
          {reports.data?.map((report) => (
            <li key={report.type} className="flex flex-wrap items-center gap-3 px-5 py-3.5">
              <FileText size={16} className="text-ink-faint" />
              <span className="min-w-[200px] flex-1">
                <span className="block text-sm font-medium">{report.title}</span>
                <span className="block text-xs text-ink-faint">{report.description}</span>
              </span>
              {report.formats.map((format) => (
                <button
                  key={format}
                  className="btn-secondary px-3 py-1.5 text-xs uppercase"
                  onClick={() => download(`/reports/${report.type}`, { format })}
                >
                  <Download size={14} /> {format}
                </button>
              ))}
            </li>
          ))}
        </ul>
      </Card>
    </>
  )
}
