import { useQueryClient } from '@tanstack/react-query'
import { Camera, Fuel, KeyRound, ShieldAlert, UserRound } from '@/components/icons'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Avatar, Card, CardHeader, ErrorNote, Field, PageHeader, Spinner } from '@/components/ui'
import { api, errorMessage } from '@/lib/api'
import { dt, since } from '@/lib/format'
import { useAuth } from '@/lib/auth'
import { useFuelPrices } from '@/lib/queries'
import type { AdminProfile } from '@/lib/types'

export default function Settings() {
  const { admin, setAdmin } = useAuth()
  const fileInput = useRef<HTMLInputElement>(null)
  const [profile, setProfile] = useState({ fullName: '', email: '', phoneNumber: '', address: '' })
  const [profileState, setProfileState] = useState<{ busy: boolean; error?: string; saved?: boolean }>({ busy: false })
  const [password, setPassword] = useState({ currentPassword: '', newPassword: '', confirmPassword: '' })
  const [passwordState, setPasswordState] = useState<{ busy: boolean; error?: string; saved?: boolean }>({ busy: false })

  useEffect(() => {
    if (!admin) return
    setProfile({
      fullName: admin.fullName,
      email: admin.email,
      phoneNumber: admin.phoneNumber ?? '',
      address: admin.address ?? '',
    })
  }, [admin])

  async function saveProfile(event: FormEvent) {
    event.preventDefault()
    setProfileState({ busy: true })
    try {
      const { data } = await api.put<AdminProfile>('/profile', {
        fullName: profile.fullName.trim(),
        email: profile.email.trim(),
        phoneNumber: profile.phoneNumber.trim() || null,
        address: profile.address.trim() || null,
      })
      setAdmin(data)
      setProfileState({ busy: false, saved: true })
    } catch (caught) {
      setProfileState({ busy: false, error: errorMessage(caught) })
    }
  }

  async function changePassword(event: FormEvent) {
    event.preventDefault()
    if (password.newPassword !== password.confirmPassword) {
      setPasswordState({ busy: false, error: 'The new passwords do not match.' })
      return
    }
    setPasswordState({ busy: true })
    try {
      await api.post('/profile/password', password)
      setPassword({ currentPassword: '', newPassword: '', confirmPassword: '' })
      setPasswordState({ busy: false, saved: true })
    } catch (caught) {
      setPasswordState({ busy: false, error: errorMessage(caught) })
    }
  }

  async function uploadPhoto(file: File) {
    const form = new FormData()
    form.append('file', file)
    try {
      const { data } = await api.post<AdminProfile>('/profile/photo', form)
      setAdmin(data)
    } catch (caught) {
      setProfileState({ busy: false, error: errorMessage(caught) })
    }
  }

  return (
    <>
      <PageHeader title="Settings" subtitle="Your profile, sign-in security and the fuel prices used to cost trips." />

      {admin?.mustChangePassword && (
        <div className="mb-4 flex items-start gap-3 rounded-lg border border-warning-100 bg-warning-50 p-4 text-sm text-warning-700">
          <ShieldAlert size={18} className="mt-0.5 shrink-0" />
          <p>
            <strong>Change your password.</strong> This account still uses the password it was provisioned with.
            Setting a new one signs out every other session.
          </p>
        </div>
      )}

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader title="Profile" subtitle={`Admin ID ${admin?.adminCode ?? '—'} · ${admin?.role ?? ''}`} />
          <form className="space-y-4 p-5" onSubmit={saveProfile}>
            <div className="flex items-center gap-4">
              <Avatar initials={(admin?.fullName ?? 'WF').slice(0, 2).toUpperCase()} photoUrl={admin?.photoUrl} size={64} />
              <div>
                <button type="button" className="btn-secondary" onClick={() => fileInput.current?.click()}>
                  <Camera size={16} /> Change photo
                </button>
                <p className="mt-1.5 text-xs text-ink-faint">PNG or JPG, up to 2 MB.</p>
                <input
                  ref={fileInput}
                  type="file"
                  accept="image/png,image/jpeg"
                  className="hidden"
                  onChange={(event) => {
                    const file = event.target.files?.[0]
                    if (file) void uploadPhoto(file)
                  }}
                />
              </div>
            </div>

            <Field label="Full name">
              <input className="input" value={profile.fullName} onChange={(event) => setProfile({ ...profile, fullName: event.target.value })} />
            </Field>
            <Field label="Email address">
              <input className="input" type="email" value={profile.email} onChange={(event) => setProfile({ ...profile, email: event.target.value })} />
            </Field>
            <Field label="Phone number">
              <input className="input" value={profile.phoneNumber} onChange={(event) => setProfile({ ...profile, phoneNumber: event.target.value })} placeholder="+234…" />
            </Field>
            <Field label="Address">
              <input className="input" value={profile.address} onChange={(event) => setProfile({ ...profile, address: event.target.value })} />
            </Field>

            {profileState.error && <ErrorNote message={profileState.error} />}
            <div className="flex items-center gap-3">
              <button className="btn-primary" disabled={profileState.busy}>
                {profileState.busy ? <Spinner /> : <UserRound size={16} />} Save changes
              </button>
              {profileState.saved && <span className="text-sm text-brand-700">Saved.</span>}
            </div>
            {admin?.lastLoginAt && <p className="text-xs text-ink-faint">Last sign-in {dt(admin.lastLoginAt)}</p>}
          </form>
        </Card>

        <Card className="self-start">
          <CardHeader title="Password" subtitle="Changing it signs out every other session." />
          <form className="space-y-4 p-5" onSubmit={changePassword}>
            <Field label="Current password">
              <input className="input" type="password" autoComplete="current-password" value={password.currentPassword}
                onChange={(event) => setPassword({ ...password, currentPassword: event.target.value })} />
            </Field>
            <Field label="New password" hint="At least 10 characters, with upper and lower case, a digit and a symbol.">
              <input className="input" type="password" autoComplete="new-password" value={password.newPassword}
                onChange={(event) => setPassword({ ...password, newPassword: event.target.value })} />
            </Field>
            <Field label="Confirm new password">
              <input className="input" type="password" autoComplete="new-password" value={password.confirmPassword}
                onChange={(event) => setPassword({ ...password, confirmPassword: event.target.value })} />
            </Field>

            {passwordState.error && <ErrorNote message={passwordState.error} />}
            <div className="flex items-center gap-3">
              <button className="btn-primary" disabled={passwordState.busy}>
                {passwordState.busy ? <Spinner /> : <KeyRound size={16} />} Update password
              </button>
              {passwordState.saved && <span className="text-sm text-brand-700">Password changed.</span>}
            </div>
          </form>
        </Card>
        <FuelPrices />
      </div>
    </>
  )
}

/** Pump prices drive every fuel estimate; estimates keep the price they were made with. */
function FuelPrices() {
  const queryClient = useQueryClient()
  const prices = useFuelPrices()
  const [drafts, setDrafts] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function save(fuelType: string) {
    const value = Number(drafts[fuelType])
    if (!(value > 0)) return
    setBusy(fuelType)
    setError(null)
    try {
      await api.put(`/fuel/prices/${fuelType}`, { pricePerLitre: value, source: 'Updated in Settings' })
      await queryClient.invalidateQueries({ queryKey: ['fuel'] })
      setDrafts((previous) => ({ ...previous, [fuelType]: '' }))
    } catch (caught) {
      setError(errorMessage(caught))
    } finally {
      setBusy(null)
    }
  }

  return (
    <Card className="lg:col-span-2">
      <CardHeader
        title="Fuel prices"
        subtitle="Used to cost every trip. Shipments keep the price they were planned with."
        action={<Fuel size={18} className="text-ink-faint" />}
      />
      <div className="grid grid-cols-1 gap-4 p-5 sm:grid-cols-2">
        {prices.data?.length === 0 && (
          <p className="text-sm text-ink-soft sm:col-span-2">
            No fuel prices are set, so trip fuel costs can't be estimated. The database migrations add them; restart the API to apply them.
          </p>
        )}
        {prices.data?.map((price) => (
          <div key={price.fuelType} className="rounded-md border border-line p-4">
            <div className="flex items-baseline justify-between">
              <p className="font-semibold">{price.fuelType}</p>
              <p className="tabular text-lg font-semibold">
                ₦{price.pricePerLitre.toLocaleString()}
                <span className="ml-1 text-xs font-medium text-ink-faint">per litre</span>
              </p>
            </div>
            <p className="mt-1 text-xs text-ink-faint">
              {price.source ?? 'No source recorded'} · updated {since(price.updatedAt)}
            </p>
            <div className="mt-3 flex gap-2">
              <input
                className="input tabular"
                type="number"
                min="1"
                step="1"
                placeholder="New price"
                value={drafts[price.fuelType] ?? ''}
                onChange={(event) => setDrafts({ ...drafts, [price.fuelType]: event.target.value })}
              />
              <button
                className="btn-secondary shrink-0"
                disabled={busy === price.fuelType || !(Number(drafts[price.fuelType]) > 0)}
                onClick={() => save(price.fuelType)}
              >
                {busy === price.fuelType ? <Spinner /> : null} Update
              </button>
            </div>
          </div>
        ))}
      </div>
      {error && <ErrorNote message={error} />}
    </Card>
  )
}
