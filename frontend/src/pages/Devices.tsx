import clsx from 'clsx'
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { Cpu, RefreshCw, Search } from '@/components/icons'
import { FirebaseDevicesPanel } from '@/components/FirebaseDevicesPanel'
import {
  Card, EmptyState, ErrorNote, Field, Loading, Modal, PageHeader, Pagination, StatCard, StatStrip, StatusChip, Spinner,
} from '@/components/ui'
import { errorMessage } from '@/lib/api'
import { humidity as humidityText, since, temperature as temperatureText } from '@/lib/format'
import { syncDeviceThresholds, updateDevice, useApiMutation, useDevices } from '@/lib/queries'
import type { Device } from '@/lib/types'

/**
 * Every registered unit with what it is reporting right now, and the limits it alarms on.
 *
 * A unit reports whether or not it is carrying a shipment, so this is where its readings
 * appear before it is assigned to a trip. While a trip is running the trip's limits take
 * over, so the limits here are shown as read-only until it finishes.
 */
export default function Devices() {
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [editing, setEditing] = useState<Device | null>(null)
  const [syncing, setSyncing] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const devices = useDevices({ search: search || undefined, page, pageSize: 20 })
  const sync = useApiMutation(syncDeviceThresholds, [['devices']])

  const rows = devices.data?.items ?? []
  const counts = useMemo(() => ({
    online: rows.filter((d) => d.isOnline).length,
    offline: rows.filter((d) => !d.isOnline).length,
    onTrip: rows.filter((d) => d.currentTripId).length,
  }), [rows])

  async function send(device: Device) {
    setSyncing(device.id)
    setActionError(null)
    try {
      await sync.mutateAsync(device.id)
    } catch (error) {
      setActionError(errorMessage(error))
    } finally {
      setSyncing(null)
    }
  }

  return (
    <>
      <PageHeader title="Devices" subtitle="Monitoring units, what they are reporting and the limits they alarm on." />

      <div className="mb-4">
        <FirebaseDevicesPanel />
      </div>

      <StatStrip columns={3}>
        <StatCard inStrip label="Transmitting" value={String(counts.online)} hint="reporting now" />
        <StatCard inStrip label="Silent" value={String(counts.offline)} tone={counts.offline > 0 ? 'warning' : 'default'} hint="no recent write" />
        <StatCard inStrip label="On a shipment" value={String(counts.onTrip)} hint="trip limits apply" />
      </StatStrip>

      {actionError && <div className="mt-4"><ErrorNote message={actionError} /></div>}

      <Card className="mt-4 self-start overflow-hidden">
        <div className="border-b border-line p-4">
          <label className="relative block">
            <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-ink-faint" />
            <input
              className="input pl-9" placeholder="Search serial, Firebase key or vehicle"
              value={search} onChange={(event) => { setSearch(event.target.value); setPage(1) }}
            />
          </label>
        </div>

        {devices.isLoading ? (
          <Loading rows={4} />
        ) : devices.isError ? (
          <ErrorNote message={errorMessage(devices.error)} onRetry={() => devices.refetch()} />
        ) : rows.length === 0 ? (
          <EmptyState
            icon={<Cpu size={22} />}
            title={search ? 'No unit matches that search' : 'No devices registered yet'}
            description={search ? undefined : 'A unit transmitting in Firebase can be registered from the panel above.'}
          />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[820px]">
              <thead>
                <tr>
                  <th className="th">Unit</th>
                  <th className="th">Temperature</th>
                  <th className="th">Humidity</th>
                  <th className="th">Limits</th>
                  <th className="th">Position</th>
                  <th className="th">Last reading</th>
                  <th className="th">Status</th>
                  <th className="th relative text-right"><span className="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {rows.map((device) => {
                  const hot = device.maxTemperature != null && device.lastTemperature != null && device.lastTemperature > device.maxTemperature
                  const cold = device.minTemperature != null && device.lastTemperature != null && device.lastTemperature < device.minTemperature
                  const damp = device.maxHumidity != null && device.lastHumidity != null && device.lastHumidity > device.maxHumidity
                  const onTrip = Boolean(device.currentTripId)
                  return (
                    <tr key={device.id} className="hover:bg-surface-muted">
                      <td className="td">
                        <span className="block font-medium">{device.serial}</span>
                        <span className="block text-xs text-ink-soft">
                          {onTrip ? <Link to={`/trips/${device.currentTripId}`} className="code-id">{device.currentTripCode}</Link> : 'Not on a shipment'}
                        </span>
                      </td>
                      <td className={clsx('td tabular font-medium', (hot || cold) && 'text-critical-600')}>
                        {temperatureText(device.lastTemperature)}
                      </td>
                      <td className={clsx('td tabular font-medium', damp && 'text-warning-600')}>
                        {humidityText(device.lastHumidity)}
                      </td>
                      <td className="td tabular whitespace-nowrap text-[13px] text-ink-soft">
                        {device.minTemperature != null && device.maxTemperature != null
                          ? `${device.minTemperature}–${device.maxTemperature} °C`
                          : 'Not set'}
                        <span className="block">
                          {device.minHumidity != null && device.maxHumidity != null
                            ? `${device.minHumidity}–${device.maxHumidity}% RH`
                            : ''}
                        </span>
                      </td>
                      <td className="td tabular whitespace-nowrap text-[13px] text-ink-soft">
                        {device.lastLatitude != null && device.lastLongitude != null
                          ? `${device.lastLatitude.toFixed(3)}, ${device.lastLongitude.toFixed(3)}`
                          : 'No GPS fix'}
                      </td>
                      <td className="td whitespace-nowrap text-[13px] text-ink-soft">
                        {device.lastChangedAt ? since(device.lastChangedAt) : 'Never'}
                      </td>
                      <td className="td"><StatusChip status={device.sensorStatus} pulse /></td>
                      <td className="td">
                        <div className="flex justify-end gap-1.5">
                          <button className="btn-secondary h-8 px-2.5 text-xs" onClick={() => setEditing(device)}>
                            {onTrip ? 'View' : 'Set'}
                          </button>
                          <button
                            className="btn-secondary h-8 px-2.5 text-xs" disabled={syncing !== null}
                            title="Send the limits to the unit again"
                            onClick={() => send(device)}
                          >
                            {syncing === device.id ? <Spinner size={14} /> : <RefreshCw size={14} />}
                            <span className="hidden sm:inline">Send</span>
                          </button>
                        </div>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}

        {devices.data && devices.data.totalPages > 0 && (
          <Pagination page={devices.data.page} totalPages={devices.data.totalPages} totalCount={devices.data.totalCount} onChange={setPage} />
        )}
      </Card>

      {editing && <LimitsDialog device={editing} onClose={() => setEditing(null)} />}
    </>
  )
}

/** The four numbers the firmware alarms on, written to the unit as soon as they are saved. */
function LimitsDialog({ device, onClose }: { device: Device; onClose: () => void }) {
  const onTrip = Boolean(device.currentTripId)
  const [form, setForm] = useState({
    minTemperature: device.minTemperature?.toString() ?? '',
    maxTemperature: device.maxTemperature?.toString() ?? '',
    minHumidity: device.minHumidity?.toString() ?? '',
    maxHumidity: device.maxHumidity?.toString() ?? '',
  })
  const [error, setError] = useState<string | null>(null)
  const save = useApiMutation(
    (body: Parameters<typeof updateDevice>[1]) => updateDevice(device.id, body),
    [['devices']],
  )

  const number = (value: string) => (value.trim() === '' ? null : Number(value))
  const set = (key: keyof typeof form) => (event: React.ChangeEvent<HTMLInputElement>) =>
    setForm((previous) => ({ ...previous, [key]: event.target.value }))

  async function submit() {
    setError(null)
    try {
      await save.mutateAsync({
        kind: device.kind,
        parentDeviceId: device.parentDeviceId ?? null,
        vehicleId: device.vehicleId ?? null,
        firmwareVersion: device.firmwareVersion ?? null,
        minTemperature: number(form.minTemperature),
        maxTemperature: number(form.maxTemperature),
        minHumidity: number(form.minHumidity),
        maxHumidity: number(form.maxHumidity),
      })
      onClose()
    } catch (caught) {
      setError(errorMessage(caught))
    }
  }

  return (
    <Modal
      title={`Limits for ${device.serial}`}
      description={onTrip
        ? `${device.currentTripCode} is running, and a shipment's limits take over while it is on the road. Complete the trip to change these.`
        : 'The unit alarms outside this range. Saving writes the values to the hardware.'}
      open
      onClose={onClose}
    >
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field label="Minimum temperature (°C)">
          <input className="input tabular" inputMode="decimal" disabled={onTrip} value={form.minTemperature} onChange={set('minTemperature')} />
        </Field>
        <Field label="Maximum temperature (°C)">
          <input className="input tabular" inputMode="decimal" disabled={onTrip} value={form.maxTemperature} onChange={set('maxTemperature')} />
        </Field>
        <Field label="Minimum humidity (%)">
          <input className="input tabular" inputMode="decimal" disabled={onTrip} value={form.minHumidity} onChange={set('minHumidity')} />
        </Field>
        <Field label="Maximum humidity (%)">
          <input className="input tabular" inputMode="decimal" disabled={onTrip} value={form.maxHumidity} onChange={set('maxHumidity')} />
        </Field>
      </div>
      <p className="mt-3 text-[13px] text-ink-soft">
        Written to <span className="font-mono text-xs">settings/{device.firebaseKey}</span> in Firebase, which the firmware reads.
      </p>
      {error && <div className="mt-3"><ErrorNote message={error} /></div>}
      <div className="mt-5 flex justify-end gap-2">
        <button className="btn-secondary" onClick={onClose}>{onTrip ? 'Close' : 'Cancel'}</button>
        {!onTrip && (
          <button className="btn-primary" disabled={save.isPending} onClick={submit}>
            {save.isPending && <Spinner />} Save and send
          </button>
        )}
      </div>
    </Modal>
  )
}
