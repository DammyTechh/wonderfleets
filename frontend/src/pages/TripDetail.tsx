import { useQueryClient } from '@tanstack/react-query'
import {
  ArrowLeft, Battery, CheckCircle2, Cpu, Fuel, Gauge, Leaf, Link2, MapPin, Play, RefreshCw, Thermometer, Truck, XCircle,
} from 'lucide-react'
import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { FleetMap } from '@/components/FleetMap'
import { FuelBreakdown, formatMoney } from '@/components/FuelCard'
import { Card, CardHeader, ErrorNote, Field, Loading, Modal, PageHeader, Spinner, StatusChip } from '@/components/ui'
import { api, errorMessage } from '@/lib/api'
import { dt, humidity as humidityText, km, since, temperature as temperatureText, tonnes } from '@/lib/format'
import { keys, useApiMutation, useTrip, useTripFuel } from '@/lib/queries'

export default function TripDetailPage() {
  const { tripId = '' } = useParams()
  const queryClient = useQueryClient()
  const { data: trip, isLoading, error, refetch } = useTrip(tripId)
  const [cancelling, setCancelling] = useState(false)
  const [reason, setReason] = useState('')
  const [actionError, setActionError] = useState<string | null>(null)
  const [recordingFuel, setRecordingFuel] = useState(false)
  const [fuelLitres, setFuelLitres] = useState('')
  const [fuelCost, setFuelCost] = useState('')
  const fuel = useTripFuel(tripId)
  const fuelKey = ['fuel', 'trip', tripId] as const
  const replanFuel = useApiMutation(() => api.post(`/trips/${tripId}/fuel/estimate`), [fuelKey, keys.trip(tripId)])
  const recordFuel = useApiMutation(
    (body: { litres: number; cost?: number }) => api.post(`/trips/${tripId}/fuel/actual`, body),
    [fuelKey, keys.trip(tripId)],
  )

  const invalidate = [keys.trip(tripId), keys.dashboard, ['fleet'], ['trips']] as const

  const start = useApiMutation(() => api.post(`/trips/${tripId}/start`), invalidate)
  const complete = useApiMutation(() => api.post(`/trips/${tripId}/complete`), invalidate)
  const cancel = useApiMutation(
    (body: { reason: string }) => api.post(`/trips/${tripId}/cancel`, body),
    invalidate,
  )

  async function run(action: () => Promise<unknown>) {
    setActionError(null)
    try {
      await action()
      await queryClient.invalidateQueries({ queryKey: keys.trip(tripId) })
    } catch (caught) {
      setActionError(errorMessage(caught))
    }
  }

  if (isLoading) return <Loading rows={6} />
  if (error || !trip) return <ErrorNote message={errorMessage(error)} onRetry={() => refetch()} />

  const ended = trip.status === 'Completed' || trip.status === 'Cancelled'

  return (
    <>
      <PageHeader
        title={`${trip.tripCode} · ${trip.route}`}
        subtitle={`${trip.fleetNumber} · ${trip.partnerName} → ${trip.processorName}`}
        action={
          <div className="flex flex-wrap gap-2">
            <Link to="/fleet" className="btn-secondary">
              <ArrowLeft size={16} /> Fleet
            </Link>
            {trip.status === 'Scheduled' && (
              <button className="btn-primary" disabled={start.isPending} onClick={() => run(() => start.mutateAsync(undefined))}>
                {start.isPending ? <Spinner /> : <Play size={16} />} Dispatch
              </button>
            )}
            {!ended && trip.status !== 'Scheduled' && (
              <button className="btn-primary" disabled={complete.isPending} onClick={() => run(() => complete.mutateAsync(undefined))}>
                {complete.isPending ? <Spinner /> : <CheckCircle2 size={16} />} Mark delivered
              </button>
            )}
            {!ended && (
              <button className="btn-secondary" onClick={() => setCancelling(true)}>
                <XCircle size={16} /> Cancel
              </button>
            )}
          </div>
        }
      />

      {actionError && <ErrorNote message={actionError} />}

      <div className="grid gap-4 lg:grid-cols-4">
        <Card className="p-4">
          <p className="flex items-center gap-2 text-xs font-medium uppercase tracking-wide text-ink-faint">
            <Thermometer size={14} /> Temperature
          </p>
          <p className="tabular mt-2 text-2xl font-bold">{temperatureText(trip.temperature)}</p>
          <p className="mt-1 text-xs text-ink-soft">
            Limits {trip.thresholds.minTemperature}–{trip.thresholds.maxTemperature} °C
          </p>
        </Card>
        <Card className="p-4">
          <p className="flex items-center gap-2 text-xs font-medium uppercase tracking-wide text-ink-faint">
            <Gauge size={14} /> Humidity
          </p>
          <p className="tabular mt-2 text-2xl font-bold">{humidityText(trip.humidity)}</p>
          <p className="mt-1 text-xs text-ink-soft">
            Limits {trip.thresholds.minHumidity}–{trip.thresholds.maxHumidity} % RH
          </p>
        </Card>
        <Card className="p-4">
          <p className="flex items-center gap-2 text-xs font-medium uppercase tracking-wide text-ink-faint">
            <Truck size={14} /> Distance
          </p>
          <p className="tabular mt-2 text-2xl font-bold">{km(trip.distanceTravelledKm)}</p>
          <p className="mt-1 text-xs text-ink-soft">
            {trip.plannedDistanceKm ? `Planned ${km(trip.plannedDistanceKm)}` : 'No planned distance'}
          </p>
        </Card>
        <Card className="p-4">
          <p className="flex items-center gap-2 text-xs font-medium uppercase tracking-wide text-ink-faint">
            <Leaf size={14} /> CO₂
          </p>
          <p className="tabular mt-2 text-2xl font-bold">{trip.co2EmissionKg.toFixed(1)} kg</p>
          <p className="mt-1 text-xs text-ink-soft">
            {fuel.data?.plannedLitres != null
              ? `Fuel ${(fuel.data.actualLitres ?? fuel.data.plannedLitres).toFixed(0)} L${fuel.data.actualLitres != null ? ' used' : ' planned'}`
              : `${trip.openAlerts} open alert${trip.openAlerts === 1 ? '' : 's'}`}
          </p>
        </Card>
      </div>

      <div className="mt-4 grid gap-4 lg:grid-cols-[1.5fr_1fr]">
        <Card className="overflow-hidden">
          <CardHeader
            title="Position"
            subtitle={trip.lastPositionAt ? `Last fix ${since(trip.lastPositionAt)}` : 'Awaiting the first fix'}
            action={<StatusChip status={trip.sensorStatus} pulse />}
          />
          <FleetMap
            height={320}
            markers={
              trip.latitude != null && trip.longitude != null
                ? [
                    {
                      tripId: trip.id, tripCode: trip.tripCode, fleetNumber: trip.fleetNumber,
                      vehicleCode: trip.vehicleCode, latitude: trip.latitude, longitude: trip.longitude,
                      tripStatus: trip.status, sensorStatus: trip.sensorStatus, driverName: trip.driverName,
                      route: trip.route, temperature: trip.temperature, humidity: trip.humidity,
                      speedKmh: trip.speedKmh, lastPositionAt: trip.lastPositionAt,
                    },
                  ]
                : []
            }
          />
        </Card>

        <Card>
          <CardHeader title="Shipment" action={<StatusChip status={trip.status} />} />
          <dl className="divide-y divide-line text-sm">
            {[
              ['Produce', trip.produce.join(', ') || '—'],
              ['Weight', tonnes(trip.estimatedWeightTonnes)],
              ['Packaging', trip.packagingType ?? '—'],
              ['Pickup', trip.pickupAddress],
              ['Destination', trip.destinationAddress],
              ['Loading time', dt(trip.loadingTime)],
              ['Expected arrival', dt(trip.expectedArrival)],
              ['Dispatched', dt(trip.startedAt)],
              ['Delivered', dt(trip.completedAt)],
              ['Driver', trip.driverName ? `${trip.driverName} · ${trip.driverPhone ?? ''}` : 'Unassigned'],
              ['Device', trip.deviceSerial ?? 'Unassigned'],
            ].map(([term, value]) => (
              <div key={term} className="flex justify-between gap-4 px-5 py-2.5">
                <dt className="text-ink-faint">{term}</dt>
                <dd className="text-right font-medium">{value}</dd>
              </div>
            ))}
          </dl>
          <div className="flex flex-wrap items-center gap-3 border-t border-line px-5 py-3 text-xs text-ink-soft">
            <span className="flex items-center gap-1.5">
              <Cpu size={14} /> {trip.deviceOnline ? 'Device online' : 'Device offline'}
            </span>
            {trip.deviceBattery != null && (
              <span className="flex items-center gap-1.5">
                <Battery size={14} /> {trip.deviceBattery}%
              </span>
            )}
            <span className="flex items-center gap-1.5">
              <Link2 size={14} /> {trip.activeShareLinks} live tracking link{trip.activeShareLinks === 1 ? '' : 's'}
            </span>
            <span className="flex items-center gap-1.5">
              <MapPin size={14} /> {trip.originLabel} → {trip.destinationLabel}
            </span>
          </div>
        </Card>
      </div>

      <Card className="mt-4">
        <CardHeader
          title="Fuel"
          subtitle={
            fuel.data?.recordedAt
              ? `Reconciled ${dt(fuel.data.recordedAt)}`
              : 'Planned from the route, traffic, forecast and the cooling load'
          }
          action={
            <div className="flex gap-2">
              <button className="btn-secondary px-3 py-1.5 text-xs" disabled={replanFuel.isPending}
                onClick={() => run(() => replanFuel.mutateAsync(undefined))}>
                {replanFuel.isPending ? <Spinner size={14} /> : <RefreshCw size={14} />} Re-plan
              </button>
              {!ended || fuel.data?.actualLitres == null ? (
                <button className="btn-primary px-3 py-1.5 text-xs" onClick={() => setRecordingFuel(true)}>
                  <Fuel size={14} /> Record fuel used
                </button>
              ) : null}
            </div>
          }
        />
        <div className="p-5">
          {fuel.isLoading ? (
            <Loading rows={2} />
          ) : (
            <>
              {fuel.data?.actualLitres != null && (
                <div className="mb-4 grid gap-3 sm:grid-cols-3">
                  <div className="rounded-xl border border-line p-4">
                    <p className="text-xs uppercase tracking-wide text-ink-faint">Planned</p>
                    <p className="tabular mt-1 text-xl font-bold">{fuel.data.plannedLitres?.toFixed(0) ?? '—'} L</p>
                    <p className="text-xs text-ink-soft">
                      {fuel.data.plannedCost ? formatMoney(fuel.data.plannedCost, fuel.data.currency) : '—'}
                    </p>
                  </div>
                  <div className="rounded-xl border border-line p-4">
                    <p className="text-xs uppercase tracking-wide text-ink-faint">Actually used</p>
                    <p className="tabular mt-1 text-xl font-bold">{fuel.data.actualLitres.toFixed(0)} L</p>
                    <p className="text-xs text-ink-soft">
                      {fuel.data.actualCost ? formatMoney(fuel.data.actualCost, fuel.data.currency) : '—'}
                    </p>
                  </div>
                  <div className={fuel.data.varianceLitres && fuel.data.varianceLitres > 0
                    ? 'rounded-xl border border-warning-100 bg-warning-50 p-4'
                    : 'rounded-xl border border-brand-100 bg-brand-50 p-4'}>
                    <p className="text-xs uppercase tracking-wide text-ink-faint">Variance</p>
                    <p className="tabular mt-1 text-xl font-bold">
                      {fuel.data.varianceLitres == null ? '—' : `${fuel.data.varianceLitres > 0 ? '+' : ''}${fuel.data.varianceLitres.toFixed(0)} L`}
                    </p>
                    <p className="text-xs text-ink-soft">
                      {fuel.data.variancePercent == null ? 'No plan to compare' : `${fuel.data.variancePercent.toFixed(1)}% against plan`}
                    </p>
                  </div>
                </div>
              )}
              {fuel.data?.estimate ? (
                <FuelBreakdown estimate={fuel.data.estimate} />
              ) : (
                <p className="py-4 text-sm text-ink-faint">
                  No fuel plan yet. Use “Re-plan” to build one from the live route and forecast.
                </p>
              )}
            </>
          )}
        </div>
      </Card>

      <Modal open={recordingFuel} title="Record fuel used" description="Emissions for this shipment are recomputed from the litres burnt." onClose={() => setRecordingFuel(false)}>
        <div className="space-y-4">
          <Field label="Litres used">
            <input className="input tabular" type="number" min="0.1" max="5000" step="0.1" value={fuelLitres}
              onChange={(event) => setFuelLitres(event.target.value)} />
          </Field>
          <Field label="Cost" hint="Leave blank to price it at the current pump price.">
            <input className="input tabular" type="number" min="0" step="1" value={fuelCost}
              onChange={(event) => setFuelCost(event.target.value)} />
          </Field>
          <div className="flex justify-end gap-2">
            <button className="btn-secondary" onClick={() => setRecordingFuel(false)}>Cancel</button>
            <button
              className="btn-primary"
              disabled={!(Number(fuelLitres) > 0) || recordFuel.isPending}
              onClick={async () => {
                await run(() => recordFuel.mutateAsync({
                  litres: Number(fuelLitres),
                  cost: fuelCost ? Number(fuelCost) : undefined,
                }))
                setRecordingFuel(false)
                setFuelLitres('')
                setFuelCost('')
              }}
            >
              {recordFuel.isPending ? <Spinner /> : <Fuel size={16} />} Save
            </button>
          </div>
        </div>
      </Modal>

      <Modal open={cancelling} title="Cancel this shipment" description="Everyone tracking it will be notified." onClose={() => setCancelling(false)}>
        <Field label="Reason" hint="Recorded in the audit trail and shown to the partners.">
          <textarea className="input min-h-[90px]" value={reason} onChange={(event) => setReason(event.target.value)} />
        </Field>
        <div className="mt-4 flex justify-end gap-2">
          <button className="btn-secondary" onClick={() => setCancelling(false)}>
            Keep shipment
          </button>
          <button
            className="btn-danger"
            disabled={reason.trim().length < 3 || cancel.isPending}
            onClick={async () => {
              await run(() => cancel.mutateAsync({ reason: reason.trim() }))
              setCancelling(false)
            }}
          >
            {cancel.isPending ? <Spinner /> : null} Cancel shipment
          </button>
        </div>
      </Modal>
    </>
  )
}
