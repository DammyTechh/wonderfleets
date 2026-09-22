import { useQueryClient } from '@tanstack/react-query'
import { Plus, X } from '@/components/icons'
import { useState } from 'react'
import { api, errorMessage } from '@/lib/api'
import { ErrorNote, Field, Modal, Spinner } from './ui'

interface ContactDraft {
  fullName: string
  phoneNumber: string
  email: string
}

/** Agro-processors must exist before a shipment can name an owner for the produce. */
export function CreateProcessorDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [form, setForm] = useState({ name: '', address: '', city: '', state: '', latitude: '', longitude: '' })
  const [contacts, setContacts] = useState<ContactDraft[]>([{ fullName: '', phoneNumber: '', email: '' }])
  const [primary, setPrimary] = useState(0)

  const set = (key: keyof typeof form, value: string) => setForm((previous) => ({ ...previous, [key]: value }))

  async function submit() {
    setBusy(true)
    setError(null)
    try {
      await api.post('/agro-processors', {
        name: form.name.trim(),
        address: form.address.trim(),
        latitude: form.latitude ? Number(form.latitude) : null,
        longitude: form.longitude ? Number(form.longitude) : null,
        city: form.city.trim() || null,
        state: form.state.trim() || null,
        contacts: contacts
          .filter((contact) => contact.fullName.trim() && contact.email.trim())
          .map((contact, index) => ({
            fullName: contact.fullName.trim(),
            phoneNumber: contact.phoneNumber.trim(),
            email: contact.email.trim(),
            isPrimary: index === primary,
          })),
      })
      await queryClient.invalidateQueries({ queryKey: ['processors'] })
      onClose()
    } catch (caught) {
      setError(errorMessage(caught))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      title="Add an agro-processor"
      description="The produce owner. They receive their own tracking link for every shipment."
      onClose={onClose}
      width="max-w-2xl"
    >
      <div className="space-y-5">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="Processor name">
            <input className="input" value={form.name} onChange={(event) => set('name', event.target.value)} />
          </Field>
          <Field label="Facility address" hint="Located on the map automatically if no pin is given.">
            <input className="input" value={form.address} onChange={(event) => set('address', event.target.value)} />
          </Field>
          <Field label="City">
            <input className="input" value={form.city} onChange={(event) => set('city', event.target.value)} />
          </Field>
          <Field label="State">
            <input className="input" value={form.state} onChange={(event) => set('state', event.target.value)} />
          </Field>
          <Field label="Latitude" hint="Optional map pin.">
            <input className="input tabular" type="number" step="0.000001" value={form.latitude} onChange={(event) => set('latitude', event.target.value)} />
          </Field>
          <Field label="Longitude">
            <input className="input tabular" type="number" step="0.000001" value={form.longitude} onChange={(event) => set('longitude', event.target.value)} />
          </Field>
        </div>

        <section>
          <span className="label">Contact persons</span>
          <div className="space-y-3">
            {contacts.map((contact, index) => (
              <div key={index} className="rounded-md border border-line p-3">
                <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
                  <input className="input" placeholder="Full name" value={contact.fullName}
                    onChange={(event) => setContacts(contacts.map((item, i) => (i === index ? { ...item, fullName: event.target.value } : item)))} />
                  <input className="input" placeholder="+2348031234567" value={contact.phoneNumber}
                    onChange={(event) => setContacts(contacts.map((item, i) => (i === index ? { ...item, phoneNumber: event.target.value } : item)))} />
                  <input className="input" type="email" placeholder="name@company.com" value={contact.email}
                    onChange={(event) => setContacts(contacts.map((item, i) => (i === index ? { ...item, email: event.target.value } : item)))} />
                </div>
                <div className="mt-2 flex items-center justify-between">
                  <label className="flex items-center gap-2 text-xs text-ink-soft">
                    <input type="radio" name="primary-contact" checked={primary === index} onChange={() => setPrimary(index)}
                      className="h-3.5 w-3.5 text-brand-600 focus:ring-brand-500/20" />
                    Primary contact — receives the tracking link and alerts
                  </label>
                  {contacts.length > 1 && (
                    <button className="btn-ghost px-2 py-1 text-xs" onClick={() => {
                      setContacts(contacts.filter((_, i) => i !== index))
                      setPrimary(0)
                    }}>
                      <X size={14} /> Remove
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>
          <button className="btn-secondary mt-2 px-3 py-1.5 text-xs"
            onClick={() => setContacts([...contacts, { fullName: '', phoneNumber: '', email: '' }])}>
            <Plus size={14} /> Add contact person
          </button>
        </section>

        {error && <ErrorNote message={error} />}

        <div className="flex justify-end gap-2">
          <button className="btn-secondary" onClick={onClose}>
            Cancel
          </button>
          <button
            className="btn-primary"
            disabled={busy || !form.name.trim() || !form.address.trim() || !contacts[0]?.email.trim()}
            onClick={submit}
          >
            {busy ? <Spinner /> : <Plus size={16} />} Add processor
          </button>
        </div>
      </div>
    </Modal>
  )
}
