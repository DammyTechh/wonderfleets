import { useQueryClient } from '@tanstack/react-query'
import { Plus, X } from 'lucide-react'
import { useState } from 'react'
import { api, errorMessage } from '@/lib/api'
import { ErrorNote, Field, Modal, Spinner } from './ui'

const AVAILABILITY = ['AvailableNow', 'AvailableSoon', 'FullyBooked', 'Unavailable'] as const
const INSURANCE = ['GoodsInTransit', 'ComprehensiveFleet', 'ThirdParty', 'MarineCargo', 'Other'] as const
const TRUCK_TYPES = ['Refrigerated truck', 'Insulated truck', 'Open-body truck', 'Van', 'Trailer']

const spaced = (value: string) => value.replace(/([a-z])([A-Z])/g, '$1 $2')

/** Mirrors the four Add a Partner tabs: company, fleet, operations and compliance. */
export function CreatePartnerDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [corridorDraft, setCorridorDraft] = useState('')
  const [form, setForm] = useState({
    companyName: '', contactPerson: '', cacNumber: '', phoneNumber: '', email: '',
    fleetSize: '0', driverPoolSize: '0', yearsOfOperation: '1',
    availabilityStatus: 'AvailableNow' as (typeof AVAILABILITY)[number],
    insuranceCoverageType: '' as '' | (typeof INSURANCE)[number],
    insuranceExpiryDate: '', officeAddress: '', city: '', state: '',
  })
  const [corridors, setCorridors] = useState<string[]>([])
  const [truckTypes, setTruckTypes] = useState<{ truckType: string; maxTonnage: string; quantity: string }[]>([
    { truckType: TRUCK_TYPES[0], maxTonnage: '7', quantity: '1' },
  ])

  const set = (key: keyof typeof form, value: string) => setForm((previous) => ({ ...previous, [key]: value }))

  function close() {
    setError(null)
    onClose()
  }

  async function submit() {
    setBusy(true)
    setError(null)
    try {
      await api.post('/logistics-partners', {
        companyName: form.companyName.trim(),
        contactPerson: form.contactPerson.trim(),
        cacNumber: form.cacNumber.trim(),
        phoneNumber: form.phoneNumber.trim(),
        email: form.email.trim(),
        fleetSize: Number(form.fleetSize) || 0,
        driverPoolSize: Number(form.driverPoolSize) || 0,
        yearsOfOperation: Number(form.yearsOfOperation) || 0,
        truckTypes: truckTypes
          .filter((truck) => truck.truckType && Number(truck.maxTonnage) > 0)
          .map((truck) => ({
            truckType: truck.truckType,
            maxTonnage: Number(truck.maxTonnage),
            quantity: Number(truck.quantity) || 1,
          })),
        corridors,
        availabilityStatus: form.availabilityStatus,
        insuranceCoverageType: form.insuranceCoverageType || null,
        insuranceExpiryDate: form.insuranceExpiryDate || null,
        officeAddress: form.officeAddress.trim() || null,
        city: form.city.trim() || null,
        state: form.state.trim() || null,
      })
      await queryClient.invalidateQueries({ queryKey: ['partners'] })
      close()
    } catch (caught) {
      setError(errorMessage(caught))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal open={open} title="Add a logistics partner" description="Company, fleet, operations and compliance." onClose={close} width="max-w-2xl">
      <div className="space-y-5">
        <section className="grid gap-4 sm:grid-cols-2">
          <Field label="Company name">
            <input className="input" value={form.companyName} onChange={(event) => set('companyName', event.target.value)} />
          </Field>
          <Field label="CAC number" hint="Corporate Affairs Commission registration.">
            <input className="input" placeholder="RC-1849213" value={form.cacNumber} onChange={(event) => set('cacNumber', event.target.value)} />
          </Field>
          <Field label="Contact person">
            <input className="input" value={form.contactPerson} onChange={(event) => set('contactPerson', event.target.value)} />
          </Field>
          <Field label="Phone number">
            <input className="input" placeholder="+2348031234567" value={form.phoneNumber} onChange={(event) => set('phoneNumber', event.target.value)} />
          </Field>
          <Field label="Email">
            <input className="input" type="email" value={form.email} onChange={(event) => set('email', event.target.value)} />
          </Field>
          <Field label="Office address">
            <input className="input" value={form.officeAddress} onChange={(event) => set('officeAddress', event.target.value)} />
          </Field>
          <Field label="City">
            <input className="input" value={form.city} onChange={(event) => set('city', event.target.value)} />
          </Field>
          <Field label="State">
            <input className="input" value={form.state} onChange={(event) => set('state', event.target.value)} />
          </Field>
        </section>

        <section className="grid gap-4 sm:grid-cols-3">
          <Field label="Fleet size">
            <input className="input tabular" type="number" min="0" value={form.fleetSize} onChange={(event) => set('fleetSize', event.target.value)} />
          </Field>
          <Field label="Driver pool">
            <input className="input tabular" type="number" min="0" value={form.driverPoolSize} onChange={(event) => set('driverPoolSize', event.target.value)} />
          </Field>
          <Field label="Years of operation">
            <input className="input tabular" type="number" min="0" value={form.yearsOfOperation} onChange={(event) => set('yearsOfOperation', event.target.value)} />
          </Field>
        </section>

        <section>
          <span className="label">Truck types</span>
          <div className="space-y-2">
            {truckTypes.map((truck, index) => (
              <div key={index} className="flex flex-wrap gap-2">
                <select
                  className="input flex-1"
                  value={truck.truckType}
                  onChange={(event) =>
                    setTruckTypes(truckTypes.map((item, i) => (i === index ? { ...item, truckType: event.target.value } : item)))
                  }
                >
                  {TRUCK_TYPES.map((type) => (
                    <option key={type}>{type}</option>
                  ))}
                </select>
                <input
                  className="input tabular w-28" type="number" min="0.5" step="0.5" aria-label="Max tonnage"
                  value={truck.maxTonnage}
                  onChange={(event) =>
                    setTruckTypes(truckTypes.map((item, i) => (i === index ? { ...item, maxTonnage: event.target.value } : item)))
                  }
                />
                <input
                  className="input tabular w-24" type="number" min="1" aria-label="Quantity"
                  value={truck.quantity}
                  onChange={(event) =>
                    setTruckTypes(truckTypes.map((item, i) => (i === index ? { ...item, quantity: event.target.value } : item)))
                  }
                />
                <button className="btn-ghost p-2" aria-label="Remove truck type"
                  onClick={() => setTruckTypes(truckTypes.filter((_, i) => i !== index))}>
                  <X size={16} />
                </button>
              </div>
            ))}
          </div>
          <button
            className="btn-secondary mt-2 px-3 py-1.5 text-xs"
            onClick={() => setTruckTypes([...truckTypes, { truckType: TRUCK_TYPES[0], maxTonnage: '7', quantity: '1' }])}
          >
            <Plus size={14} /> Add truck type
          </button>
        </section>

        <section>
          <span className="label">Operating corridors</span>
          <div className="mb-2 flex flex-wrap gap-1.5">
            {corridors.map((corridor) => (
              <span key={corridor} className="chip border-brand-200 bg-brand-50 text-brand-800">
                {corridor}
                <button onClick={() => setCorridors(corridors.filter((item) => item !== corridor))} aria-label={`Remove ${corridor}`}>
                  <X size={12} />
                </button>
              </span>
            ))}
          </div>
          <div className="flex gap-2">
            <input
              className="input max-w-xs" placeholder="Lagos to Kano" value={corridorDraft}
              onChange={(event) => setCorridorDraft(event.target.value)}
              onKeyDown={(event) => {
                if (event.key !== 'Enter') return
                event.preventDefault()
                const value = corridorDraft.trim()
                if (value && !corridors.includes(value)) setCorridors([...corridors, value])
                setCorridorDraft('')
              }}
            />
            <button
              className="btn-secondary"
              onClick={() => {
                const value = corridorDraft.trim()
                if (value && !corridors.includes(value)) setCorridors([...corridors, value])
                setCorridorDraft('')
              }}
            >
              <Plus size={15} /> Add
            </button>
          </div>
        </section>

        <section className="grid gap-4 sm:grid-cols-3">
          <Field label="Availability">
            <select className="input" value={form.availabilityStatus} onChange={(event) => set('availabilityStatus', event.target.value)}>
              {AVAILABILITY.map((option) => (
                <option key={option} value={option}>
                  {spaced(option)}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Insurance cover">
            <select className="input" value={form.insuranceCoverageType} onChange={(event) => set('insuranceCoverageType', event.target.value)}>
              <option value="">Not recorded</option>
              {INSURANCE.map((option) => (
                <option key={option} value={option}>
                  {spaced(option)}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Insurance expiry">
            <input className="input" type="date" value={form.insuranceExpiryDate} onChange={(event) => set('insuranceExpiryDate', event.target.value)} />
          </Field>
        </section>

        {error && <ErrorNote message={error} />}

        <div className="flex justify-end gap-2">
          <button className="btn-secondary" onClick={close}>
            Cancel
          </button>
          <button
            className="btn-primary"
            disabled={busy || !form.companyName.trim() || !form.contactPerson.trim() || !form.email.trim()}
            onClick={submit}
          >
            {busy ? <Spinner /> : <Plus size={16} />} Add partner
          </button>
        </div>
        <p className="text-xs text-ink-faint">
          The partner starts as Pending. Activate them from the partner list once documents are verified;
          only active partners can take shipments.
        </p>
      </div>
    </Modal>
  )
}
