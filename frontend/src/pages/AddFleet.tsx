import { useMutation, useQuery } from '@tanstack/react-query'
import clsx from 'clsx'
import { ArrowLeft, ArrowRight, Check, CircleCheck, Cpu, Fuel, MapPin, Package, Plus, RefreshCw, Search, Thermometer, X } from '@/components/icons'
import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { Avatar, Card, ErrorNote, Field, PageHeader, Spinner, StatusChip } from '@/components/ui'
import { api, errorMessage } from '@/lib/api'
import { dt, humidity as humidityText, since, temperature as temperatureText, tonnes } from '@/lib/format'
import { useDevices, useDrivers, usePartners, useProcessors, useProduceTypes } from '@/lib/queries'
import { FuelBreakdown } from '@/components/FuelCard'
import { FirebaseDevicesPanel } from '@/components/FirebaseDevicesPanel'
import type { FuelEstimate, FuelType, Thresholds } from '@/lib/types'

const TABS = ['Vehicle details', 'Shipment info', 'Sensor & docs', 'Review & submit'] as const

interface WizardState {
  // vehicle
  logisticsPartnerId: string
  fleetNumber: string
  licenseNumber: string
  vehicleType: string
  capacityTonnes: string
  yearOfManufacture: string
  fuelType: FuelType
  tankCapacityLitres: string
  baselineLitresPer100Km: string
  // shipment
  agroProcessorId: string
  produceTypeIds: string[]
  newProduce: string[]
  estimatedWeightTonnes: string
  packagingType: string
  unitCount: string
  pickupAddress: string
  destinationAddress: string
  loadingTime: string
  expectedArrival: string
  additionalNotes: string
  // sensor
  driverId: string
  deviceId: string
  thresholds: Thresholds
  startImmediately: boolean
}

const VEHICLE_TYPES = ['Refrigerated truck', 'Insulated truck', 'Open-body truck', 'Van', 'Trailer']
const PACKAGING = ['Crates', 'Sacks', 'Cartons', 'Baskets', 'Pallets', 'ReeferContainer', 'Bulk', 'Other']

const localInput = (date: Date) => date.toISOString().slice(0, 16)

export default function AddFleet() {
  const navigate = useNavigate()
  const [tab, setTab] = useState(0)
  const [errors, setErrors] = useState<string[]>([])
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [produceDraft, setProduceDraft] = useState('')
  const [driverSearch, setDriverSearch] = useState('')

  const [state, setState] = useState<WizardState>(() => {
    const now = new Date()
    return {
      logisticsPartnerId: '', fleetNumber: '', licenseNumber: '', vehicleType: VEHICLE_TYPES[0],
      capacityTonnes: '7', yearOfManufacture: '',
      fuelType: 'Diesel', tankCapacityLitres: '', baselineLitresPer100Km: '',
      agroProcessorId: '', produceTypeIds: [], newProduce: [], estimatedWeightTonnes: '',
      packagingType: 'Crates', unitCount: '', pickupAddress: '', destinationAddress: '',
      loadingTime: localInput(new Date(now.getTime() + 60 * 60 * 1000)),
      expectedArrival: localInput(new Date(now.getTime() + 14 * 60 * 60 * 1000)),
      additionalNotes: '',
      driverId: '', deviceId: '',
      thresholds: { minTemperature: 2, maxTemperature: 8, minHumidity: 40, maxHumidity: 75 },
      startImmediately: false,
    }
  })

  const set = <K extends keyof WizardState>(key: K, value: WizardState[K]) =>
    setState((previous) => ({ ...previous, [key]: value }))

  const partners = usePartners({ pageSize: 100, status: 'Active' })
  const processors = useProcessors({ pageSize: 100, status: 'Active' })
  const produceTypes = useProduceTypes()
  const drivers = useDrivers({ pageSize: 50, availableOnly: true, partnerId: state.logisticsPartnerId || undefined, search: driverSearch || undefined })
  const devices = useDevices({ pageSize: 50, availableOnly: true })

  const nextCode = useQuery({
    queryKey: ['next-vehicle-code'],
    queryFn: async () => (await api.get<{ vehicleCode: string }>('/fleet/next-vehicle-code')).data,
  })

  // Suggested limits follow the produce selection, as long as the operator has not overridden them.
  const [thresholdsTouched, setThresholdsTouched] = useState(false)
  const suggestion = useQuery({
    queryKey: ['suggested-thresholds', state.produceTypeIds],
    enabled: state.produceTypeIds.length > 0,
    queryFn: async () =>
      (
        await api.get<{ thresholds: Thresholds | null; source: string; notes: string[] }>('/fleet/suggested-thresholds', {
          params: { produceTypeId: state.produceTypeIds },
          paramsSerializer: { indexes: null },
        })
      ).data,
  })

  useEffect(() => {
    if (!thresholdsTouched && suggestion.data?.thresholds) set('thresholds', suggestion.data.thresholds)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [suggestion.data, thresholdsTouched])

  const selectedPartner = partners.data?.items.find((partner) => partner.id === state.logisticsPartnerId)
  const selectedProcessor = processors.data?.items.find((processor) => processor.id === state.agroProcessorId)
  const selectedDriver = drivers.data?.items.find((driver) => driver.id === state.driverId)
  const selectedDevice = devices.data?.items.find((device) => device.id === state.deviceId)
  const produceNames = useMemo(
    () => [
      ...(produceTypes.data ?? []).filter((produce) => state.produceTypeIds.includes(produce.id)).map((produce) => produce.name),
      ...state.newProduce,
    ],
    [produceTypes.data, state.produceTypeIds, state.newProduce],
  )

  function validate(step: number): string[] {
    const found: string[] = []
    if (step === 0) {
      if (!state.logisticsPartnerId) found.push('Choose the logistics partner that owns this vehicle.')
      if (!state.fleetNumber.trim()) found.push('Fleet number is required.')
      if (!state.licenseNumber.trim()) found.push('Plate number is required.')
      if (!(Number(state.capacityTonnes) > 0)) found.push('Capacity must be greater than zero.')
    }
    if (step === 1) {
      if (!state.agroProcessorId) found.push('Choose the agro-processor that owns the produce.')
      if (produceNames.length === 0) found.push('Select at least one produce.')
      const weight = Number(state.estimatedWeightTonnes)
      if (!(weight > 0)) found.push('Enter the estimated weight.')
      if (weight > Number(state.capacityTonnes)) found.push('Estimated weight exceeds the vehicle capacity.')
      if (!state.pickupAddress.trim()) found.push('Pickup location is required.')
      if (!state.destinationAddress.trim()) found.push('Destination is required.')
      if (new Date(state.expectedArrival) <= new Date(state.loadingTime))
        found.push('Expected arrival must be after the loading time.')
    }
    if (step === 2) {
      const { minTemperature, maxTemperature, minHumidity, maxHumidity } = state.thresholds
      if (maxTemperature <= minTemperature) found.push('Maximum temperature must be above the minimum.')
      if (maxHumidity <= minHumidity) found.push('Maximum humidity must be above the minimum.')
      if (state.startImmediately && !state.deviceId) found.push('A device is required to dispatch immediately.')
    }
    return found
  }

  function goTo(step: number) {
    if (step > tab) {
      const found = validate(tab)
      setErrors(found)
      if (found.length > 0) return
    }
    setErrors([])
    setTab(step)
  }

  // Review tab shows what dispatch should send with the truck, using the live route and forecast.
  const [fuelPreview, setFuelPreview] = useState<FuelEstimate | null>(null)
  const [fuelError, setFuelError] = useState<string | null>(null)
  const fuelEstimate = useMutation({
    mutationFn: async () => {
      setFuelError(null)
      const body = {
        fuelType: state.fuelType,
        capacityTonnes: Number(state.capacityTonnes),
        payloadTonnes: Number(state.estimatedWeightTonnes) || undefined,
        origin: state.pickupAddress.trim(),
        destination: state.destinationAddress.trim(),
        departure: new Date(state.loadingTime).toISOString(),
        cargoMaxTemperature: state.thresholds.maxTemperature,
        baselineLitresPer100Km: state.baselineLitresPer100Km ? Number(state.baselineLitresPer100Km) : undefined,
      }
      return (await api.post<FuelEstimate>('/fuel/estimate', body)).data
    },
    onSuccess: (result) => setFuelPreview(result),
    onError: (caught) => setFuelError(errorMessage(caught)),
  })

  useEffect(() => {
    if (tab === 3 && !fuelPreview && !fuelEstimate.isPending) fuelEstimate.mutate()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab])

  const submit = useMutation({
    mutationFn: async () => {
      const body = {
        vehicle: {
          fleetNumber: state.fleetNumber.trim(),
          licenseNumber: state.licenseNumber.trim(),
          vehicleType: state.vehicleType,
          capacityTonnes: Number(state.capacityTonnes),
          yearOfManufacture: state.yearOfManufacture ? Number(state.yearOfManufacture) : null,
          logisticsPartnerId: state.logisticsPartnerId,
          fuelType: state.fuelType,
          tankCapacityLitres: state.tankCapacityLitres ? Number(state.tankCapacityLitres) : null,
          baselineLitresPer100Km: state.baselineLitresPer100Km ? Number(state.baselineLitresPer100Km) : null,
        },
        shipment: {
          agroProcessorId: state.agroProcessorId,
          produceTypeIds: state.produceTypeIds,
          newProduce: state.newProduce,
          estimatedWeightTonnes: Number(state.estimatedWeightTonnes),
          packagingType: state.packagingType,
          unitCount: state.unitCount ? Number(state.unitCount) : null,
          pickup: { address: state.pickupAddress.trim() },
          destination: { address: state.destinationAddress.trim() },
          loadingTime: new Date(state.loadingTime).toISOString(),
          expectedArrival: new Date(state.expectedArrival).toISOString(),
          additionalNotes: state.additionalNotes.trim() || null,
        },
        driverId: state.driverId || null,
        deviceId: state.deviceId || null,
        thresholds: state.thresholds,
        startImmediately: state.startImmediately,
      }
      return (await api.post<{ tripId: string; tripCode: string; thresholdsPushed: boolean }>('/fleet', body)).data
    },
    onSuccess: (result) => navigate(`/trips/${result.tripId}`),
    onError: (caught) => setSubmitError(errorMessage(caught)),
  })

  return (
    <>
      <PageHeader
        title="Add a fleet"
        subtitle="Register the vehicle, describe the shipment, attach the sensor, then review."
        action={
          <Link to="/fleet" className="btn-secondary">
            <ArrowLeft size={16} /> Back to fleet
          </Link>
        }
      />

      <Card className="overflow-hidden">
        <ol className="flex flex-wrap border-b border-line bg-surface-muted">
          {TABS.map((label, index) => (
            <li key={label} className="flex-1">
              <button
                onClick={() => goTo(index)}
                className={clsx(
                  'flex w-full items-center justify-center gap-2 px-4 py-3.5 text-sm font-medium transition',
                  index === tab ? 'bg-surface text-brand-800 shadow-[inset_0_-2px_0_0_#63883C]'
                    : index < tab ? 'text-brand-700 hover:bg-surface' : 'text-ink-faint hover:bg-surface',
                )}
              >
                <span
                  className={clsx(
                    'grid h-5 w-5 place-items-center rounded-full text-[11px] font-semibold',
                    index < tab ? 'bg-brand-600 text-white' : index === tab ? 'bg-brand-100 text-brand-800' : 'bg-line text-ink-faint',
                  )}
                >
                  {index < tab ? <Check size={12} /> : index + 1}
                </span>
                <span className="hidden sm:inline">{label}</span>
              </button>
            </li>
          ))}
        </ol>

        {errors.length > 0 && (
          <ul className="m-5 space-y-1 rounded-md border border-critical-100 bg-critical-50 p-4 text-sm text-critical-700">
            {errors.map((message) => (
              <li key={message}>• {message}</li>
            ))}
          </ul>
        )}

        <div className="p-5">
          {/* ---------------------------------------------------------- tab 1 */}
          {tab === 0 && (
            <div className="grid grid-cols-1 gap-5 md:grid-cols-2">
              <Field label="Vehicle ID" hint="Generated automatically when you submit.">
                <input className="input tabular" disabled value={nextCode.data?.vehicleCode ?? 'TRK-…'} />
              </Field>
              <Field label="Logistics partner">
                <select className="input" value={state.logisticsPartnerId} onChange={(event) => set('logisticsPartnerId', event.target.value)}>
                  <option value="">Select a partner</option>
                  {partners.data?.items.map((partner) => (
                    <option key={partner.id} value={partner.id}>
                      {partner.companyName} ({partner.partnerCode})
                    </option>
                  ))}
                </select>
              </Field>
              <Field label="Fleet number">
                <input className="input" placeholder="WF-1234" value={state.fleetNumber} onChange={(event) => set('fleetNumber', event.target.value)} />
              </Field>
              <Field label="Plate number">
                <input className="input" placeholder="LAG-234-XY" value={state.licenseNumber} onChange={(event) => set('licenseNumber', event.target.value)} />
              </Field>
              <Field label="Vehicle type">
                <select className="input" value={state.vehicleType} onChange={(event) => set('vehicleType', event.target.value)}>
                  {VEHICLE_TYPES.map((type) => (
                    <option key={type}>{type}</option>
                  ))}
                </select>
              </Field>
              <Field label="Capacity (tonnes)">
                <input className="input tabular" type="number" min="0.5" step="0.5" value={state.capacityTonnes} onChange={(event) => set('capacityTonnes', event.target.value)} />
              </Field>
              <Field label="Year of manufacture" hint="Optional.">
                <input className="input tabular" type="number" min="1970" max={new Date().getFullYear() + 1} value={state.yearOfManufacture} onChange={(event) => set('yearOfManufacture', event.target.value)} />
              </Field>
              <Field label="Fuel" hint="Drives the fuel estimate and the emission figure.">
                <select className="input" value={state.fuelType} onChange={(event) => set('fuelType', event.target.value as FuelType)}>
                  <option value="Diesel">Diesel</option>
                  <option value="Petrol">Petrol</option>
                </select>
              </Field>
              <Field label="Tank capacity (litres)" hint="Optional. Shows how many tank fills a trip needs.">
                <input className="input tabular" type="number" min="10" max="2000" value={state.tankCapacityLitres} onChange={(event) => set('tankCapacityLitres', event.target.value)} />
              </Field>
              <Field label="Known consumption (L/100 km)" hint="Optional. From the partner's own fuel logs; overrides the modelled baseline.">
                <input className="input tabular" type="number" min="3" max="120" step="0.1" value={state.baselineLitresPer100Km} onChange={(event) => set('baselineLitresPer100Km', event.target.value)} />
              </Field>
            </div>
          )}

          {/* ---------------------------------------------------------- tab 2 */}
          {tab === 1 && (
            <div className="space-y-6">
              <div className="grid grid-cols-1 gap-5 md:grid-cols-2">
                <Field label="Agro-processor" hint="Who owns the produce — they receive their own tracking link.">
                  <select className="input" value={state.agroProcessorId} onChange={(event) => set('agroProcessorId', event.target.value)}>
                    <option value="">Select an agro-processor</option>
                    {processors.data?.items.map((processor) => (
                      <option key={processor.id} value={processor.id}>
                        {processor.name} ({processor.processorCode})
                      </option>
                    ))}
                  </select>
                </Field>
                <Field label="Estimated weight (tonnes)">
                  <input className="input tabular" type="number" min="0.1" step="0.1" value={state.estimatedWeightTonnes} onChange={(event) => set('estimatedWeightTonnes', event.target.value)} />
                </Field>
              </div>

              <div>
                <span className="label">Produce</span>
                <div className="flex flex-wrap gap-2">
                  {produceTypes.data?.map((produce) => {
                    const selected = state.produceTypeIds.includes(produce.id)
                    return (
                      <button
                        key={produce.id}
                        onClick={() => {
                          setThresholdsTouched(false)
                          set('produceTypeIds', selected
                            ? state.produceTypeIds.filter((id) => id !== produce.id)
                            : [...state.produceTypeIds, produce.id])
                        }}
                        className={clsx('chip', selected ? 'border-brand-300 bg-brand-50 text-brand-800' : 'border-line bg-surface text-ink-soft hover:bg-surface-muted')}
                      >
                        {selected && <Check size={13} />}
                        {produce.name}
                      </button>
                    )
                  })}
                  {state.newProduce.map((name) => (
                    <span key={name} className="chip border-brand-300 bg-brand-50 text-brand-800">
                      {name}
                      <button onClick={() => set('newProduce', state.newProduce.filter((item) => item !== name))} aria-label={`Remove ${name}`}>
                        <X size={12} />
                      </button>
                    </span>
                  ))}
                </div>
                <div className="mt-3 flex gap-2">
                  <input
                    className="input max-w-xs"
                    placeholder="Add a produce"
                    value={produceDraft}
                    onChange={(event) => setProduceDraft(event.target.value)}
                    onKeyDown={(event) => {
                      if (event.key !== 'Enter') return
                      event.preventDefault()
                      const value = produceDraft.trim()
                      if (value && !state.newProduce.includes(value)) set('newProduce', [...state.newProduce, value])
                      setProduceDraft('')
                    }}
                  />
                  <button
                    className="btn-secondary"
                    onClick={() => {
                      const value = produceDraft.trim()
                      if (value && !state.newProduce.includes(value)) set('newProduce', [...state.newProduce, value])
                      setProduceDraft('')
                    }}
                  >
                    <Plus size={15} /> Add
                  </button>
                </div>
              </div>

              <div className="grid grid-cols-1 gap-5 md:grid-cols-2">
                <Field label="Packaging">
                  <select className="input" value={state.packagingType} onChange={(event) => set('packagingType', event.target.value)}>
                    {PACKAGING.map((option) => (
                      <option key={option}>{option}</option>
                    ))}
                  </select>
                </Field>
                <Field label="Unit count" hint="Number of crates, sacks or pallets. Optional.">
                  <input className="input tabular" type="number" min="0" value={state.unitCount} onChange={(event) => set('unitCount', event.target.value)} />
                </Field>
                <Field label="Pickup location">
                  <input className="input" placeholder="Mile 12 Market, Lagos" value={state.pickupAddress} onChange={(event) => set('pickupAddress', event.target.value)} />
                </Field>
                <Field label="Destination">
                  <input className="input" placeholder="Kano Central Market, Kano" value={state.destinationAddress} onChange={(event) => set('destinationAddress', event.target.value)} />
                </Field>
                <Field label="Loading time">
                  <input className="input" type="datetime-local" value={state.loadingTime} onChange={(event) => set('loadingTime', event.target.value)} />
                </Field>
                <Field label="Expected arrival">
                  <input className="input" type="datetime-local" value={state.expectedArrival} onChange={(event) => set('expectedArrival', event.target.value)} />
                </Field>
              </div>

              <Field label="Additional notes" hint="Handling instructions for the driver. Optional.">
                <textarea className="input min-h-[90px]" value={state.additionalNotes} onChange={(event) => set('additionalNotes', event.target.value)} />
              </Field>
            </div>
          )}

          {/* ---------------------------------------------------------- tab 3 */}
          {tab === 2 && (
            <div className="space-y-6">
              <div>
                <span className="label">Driver assignment</span>
                <div className="relative mb-3 max-w-sm">
                  <Search size={16} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-faint" />
                  <input className="input pl-9" placeholder="Search available drivers" value={driverSearch} onChange={(event) => setDriverSearch(event.target.value)} />
                </div>
                <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                  {drivers.data?.items.map((driver) => (
                    <button
                      key={driver.id}
                      onClick={() => set('driverId', state.driverId === driver.id ? '' : driver.id)}
                      className={clsx(
                        'flex items-center gap-3 rounded-md border p-3 text-left transition',
                        state.driverId === driver.id ? 'border-brand-400 bg-brand-50' : 'border-line hover:bg-surface-muted',
                      )}
                    >
                      <Avatar initials={driver.initials} size={38} />
                      <span className="min-w-0 flex-1">
                        <span className="block truncate text-sm font-semibold">{driver.fullName}</span>
                        <span className="block truncate text-xs text-ink-soft">
                          {driver.phoneNumber} · {driver.completedTrips} trips
                        </span>
                      </span>
                      {state.driverId === driver.id && <CircleCheck size={18} className="text-brand-600" />}
                    </button>
                  ))}
                  {drivers.data?.items.length === 0 && (
                    <p className="text-sm text-ink-faint">
                      No free driver for this partner. You can assign one later from the shipment page.
                    </p>
                  )}
                </div>
              </div>

              <div>
                <span className="label">Monitoring device</span>
                <div className="mb-3">
                  <FirebaseDevicesPanel />
                </div>
                <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
                  {devices.data?.items.map((device) => (
                    <button
                      key={device.id}
                      onClick={() => set('deviceId', state.deviceId === device.id ? '' : device.id)}
                      className={clsx(
                        'rounded-md border p-3 text-left transition',
                        state.deviceId === device.id ? 'border-brand-400 bg-brand-50' : 'border-line hover:bg-surface-muted',
                      )}
                    >
                      <span className="flex items-center justify-between">
                        <span className="flex items-center gap-2 tabular text-sm font-semibold">
                          <Cpu size={15} /> {device.serial}
                        </span>
                        <StatusChip status={device.isOnline ? 'Normal' : 'Offline'} />
                      </span>
                      <span className="mt-1 block text-xs text-ink-faint">
                        Key {device.firebaseKey}
                        {device.batteryLevel != null && ` · battery ${device.batteryLevel}%`}
                      </span>
                      {/* What the unit is actually reporting, so a silent sensor is obvious before dispatch. */}
                      <span className="mt-1 block text-xs text-ink-soft">
                        {device.lastTemperature == null && device.lastHumidity == null
                          ? 'No sensor reading yet'
                          : `${temperatureText(device.lastTemperature)} · ${humidityText(device.lastHumidity)}`}
                        {device.lastLatitude == null && ' · no GPS fix'}
                      </span>
                      {device.lastChangedAt && (
                        <span className="block text-xs text-ink-faint">Last wrote {since(device.lastChangedAt)}</span>
                      )}
                    </button>
                  ))}
                  {devices.data?.items.length === 0 && (
                    <p className="text-sm text-ink-faint sm:col-span-3">
                      No free devices. Register a transmitting unit above, or complete a shipment to free one.
                    </p>
                  )}
                </div>
              </div>

              <div>
                <span className="label flex items-center gap-2">
                  <Thermometer size={15} /> Temperature & humidity thresholds
                </span>
                {suggestion.data?.thresholds && !thresholdsTouched && (
                  <p className="mb-3 rounded-md border border-brand-100 bg-brand-50 px-3.5 py-2.5 text-xs text-brand-800">
                    Suggested from the selected produce ({suggestion.data.source.replace('_', ' ')}). {suggestion.data.notes[0]}
                  </p>
                )}
                {suggestion.data && !suggestion.data.thresholds && (
                  <p className="mb-3 rounded-md border border-warning-100 bg-warning-50 px-3.5 py-2.5 text-xs text-warning-700">
                    {suggestion.data.notes[0]}
                  </p>
                )}
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-4">
                  {([
                    ['minTemperature', 'Min temp (°C)'],
                    ['maxTemperature', 'Max temp (°C)'],
                    ['minHumidity', 'Min humidity (%)'],
                    ['maxHumidity', 'Max humidity (%)'],
                  ] as const).map(([key, label]) => (
                    <Field key={key} label={label}>
                      <input
                        className="input tabular"
                        type="number"
                        step="0.5"
                        value={state.thresholds[key]}
                        onChange={(event) => {
                          setThresholdsTouched(true)
                          set('thresholds', { ...state.thresholds, [key]: Number(event.target.value) })
                        }}
                      />
                    </Field>
                  ))}
                </div>
                <p className="mt-2 text-xs text-ink-faint">
                  Limits are written to the device so it can alarm locally even without GSM coverage.
                </p>
              </div>
            </div>
          )}

          {/* ---------------------------------------------------------- tab 4 */}
          {tab === 3 && (
            <div className="space-y-5">
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                <ReviewBlock
                  icon={<MapPin size={15} />}
                  title="Vehicle"
                  rows={[
                    ['Vehicle ID', nextCode.data?.vehicleCode ?? 'auto'],
                    ['Fleet number', state.fleetNumber],
                    ['Plate', state.licenseNumber],
                    ['Type', `${state.vehicleType} · ${tonnes(Number(state.capacityTonnes))}`],
                    ['Partner', selectedPartner?.companyName ?? '—'],
                  ]}
                />
                <ReviewBlock
                  icon={<Package size={15} />}
                  title="Shipment"
                  rows={[
                    ['Agro-processor', selectedProcessor?.name ?? '—'],
                    ['Produce', produceNames.join(', ') || '—'],
                    ['Weight', tonnes(Number(state.estimatedWeightTonnes))],
                    ['Route', `${state.pickupAddress} → ${state.destinationAddress}`],
                    ['Loading', dt(new Date(state.loadingTime).toISOString())],
                    ['Expected arrival', dt(new Date(state.expectedArrival).toISOString())],
                  ]}
                />
                <ReviewBlock
                  icon={<Cpu size={15} />}
                  title="Sensor & driver"
                  rows={[
                    ['Driver', selectedDriver?.fullName ?? 'To be assigned'],
                    ['Device', selectedDevice?.serial ?? 'Not assigned'],
                    ['Temperature', `${state.thresholds.minTemperature}–${state.thresholds.maxTemperature} °C`],
                    ['Humidity', `${state.thresholds.minHumidity}–${state.thresholds.maxHumidity} % RH`],
                  ]}
                />
                <div className="rounded-md border border-line p-4">
                  <p className="text-sm font-semibold">Dispatch</p>
                  <label className="mt-3 flex items-start gap-3 text-sm">
                    <input
                      type="checkbox"
                      className="mt-0.5 h-4 w-4 rounded border-line-strong text-brand-600 focus:ring-brand-500/20"
                      checked={state.startImmediately}
                      onChange={(event) => set('startImmediately', event.target.checked)}
                    />
                    <span>
                      Start the shipment now
                      <span className="block text-xs text-ink-faint">
                        Notifies the transporter and the produce owner by email. Otherwise the trip stays scheduled.
                      </span>
                    </span>
                  </label>
                </div>
              </div>

              <div className="rounded-md border border-line p-4">
                <div className="mb-3 flex items-center justify-between gap-3">
                  <p className="flex items-center gap-2 text-sm font-semibold">
                    <Fuel size={15} /> Fuel for this trip
                  </p>
                  <button className="btn-secondary px-3 py-1.5 text-xs" disabled={fuelEstimate.isPending} onClick={() => fuelEstimate.mutate()}>
                    {fuelEstimate.isPending ? <Spinner size={14} /> : <RefreshCw size={14} />} Recalculate
                  </button>
                </div>
                {fuelEstimate.isPending && !fuelPreview ? (
                  <p className="py-6 text-center text-sm text-ink-faint">Checking the route, traffic and forecast…</p>
                ) : fuelPreview ? (
                  <FuelBreakdown estimate={fuelPreview} />
                ) : (
                  <p className="py-4 text-sm text-ink-faint">
                    {fuelError ?? 'A fuel estimate will be created with the shipment.'}
                  </p>
                )}
              </div>

              {submitError && <ErrorNote message={submitError} />}
            </div>
          )}
        </div>

        <footer className="flex items-center justify-between gap-3 border-t border-line bg-surface-muted px-5 py-4">
          <button className="btn-secondary" disabled={tab === 0} onClick={() => goTo(tab - 1)}>
            <ArrowLeft size={16} /> Back
          </button>
          {tab < TABS.length - 1 ? (
            <button className="btn-primary" onClick={() => goTo(tab + 1)}>
              Continue <ArrowRight size={16} />
            </button>
          ) : (
            <button className="btn-primary" disabled={submit.isPending} onClick={() => submit.mutate()}>
              {submit.isPending ? <Spinner /> : <Check size={16} />}
              {submit.isPending ? 'Creating…' : 'Create shipment'}
            </button>
          )}
        </footer>
      </Card>
    </>
  )
}

function ReviewBlock({ icon, title, rows }: { icon: React.ReactNode; title: string; rows: [string, string][] }) {
  return (
    <div className="rounded-md border border-line p-4">
      <p className="flex items-center gap-2 text-sm font-semibold">
        {icon} {title}
      </p>
      <dl className="mt-3 space-y-1.5 text-sm">
        {rows.map(([term, value]) => (
          <div key={term} className="flex justify-between gap-4">
            <dt className="text-ink-faint">{term}</dt>
            <dd className="text-right font-medium">{value || '—'}</dd>
          </div>
        ))}
      </dl>
    </div>
  )
}
