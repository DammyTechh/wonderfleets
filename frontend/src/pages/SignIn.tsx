import { Eye, EyeOff, Lock, Mail, ShieldCheck } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Navigate, useNavigate } from 'react-router-dom'
import { Spinner } from '@/components/ui'
import { errorMessage } from '@/lib/api'
import { useAuth } from '@/lib/auth'

export default function SignIn() {
  const { admin, ready, signIn } = useAuth()
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  if (ready && admin) return <Navigate to="/" replace />

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await signIn(email.trim(), password)
      navigate('/', { replace: true })
    } catch (caught) {
      setError(errorMessage(caught))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="grid min-h-screen lg:grid-cols-2">
      <div className="flex items-center justify-center px-6 py-12">
        <div className="w-full max-w-sm">
          <div className="mb-8 flex items-center gap-2.5">
            <img src="/wonderfleet.svg" alt="" className="h-11 w-11" />
            <div>
              <p className="font-display text-xl font-bold leading-none text-brand-900">WonderFleet</p>
              <p className="mt-1 text-[11px] font-medium uppercase tracking-wider text-ink-faint">OfeminiAgricTech</p>
            </div>
          </div>

          <h1 className="text-2xl font-bold">Sign in</h1>
          <p className="mt-1 text-sm text-ink-soft">Administrator access to fleet monitoring and alerts.</p>

          <form className="mt-7 space-y-4" onSubmit={submit}>
            <label className="block">
              <span className="label">Email address</span>
              <div className="relative">
                <Mail size={16} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-faint" />
                <input
                  className="input pl-9"
                  type="email"
                  autoComplete="username"
                  required
                  value={email}
                  onChange={(event) => setEmail(event.target.value)}
                  placeholder="you@ofeminiagrictech.com"
                />
              </div>
            </label>

            <label className="block">
              <span className="label">Password</span>
              <div className="relative">
                <Lock size={16} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-faint" />
                <input
                  className="input pl-9 pr-10"
                  type={showPassword ? 'text' : 'password'}
                  autoComplete="current-password"
                  required
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                  placeholder="••••••••"
                />
                <button
                  type="button"
                  className="absolute right-2 top-1/2 -translate-y-1/2 rounded-lg p-1.5 text-ink-faint hover:bg-surface-sunken"
                  onClick={() => setShowPassword((value) => !value)}
                  aria-label={showPassword ? 'Hide password' : 'Show password'}
                >
                  {showPassword ? <EyeOff size={16} /> : <Eye size={16} />}
                </button>
              </div>
            </label>

            {error && (
              <p role="alert" className="rounded-xl border border-critical-100 bg-critical-50 px-3.5 py-2.5 text-sm text-critical-700">
                {error}
              </p>
            )}

            <button className="btn-primary w-full" disabled={busy}>
              {busy ? <Spinner /> : null}
              {busy ? 'Signing in…' : 'Sign in'}
            </button>
          </form>

          <p className="mt-6 flex items-start gap-2 text-xs text-ink-faint">
            <ShieldCheck size={14} className="mt-0.5 shrink-0" />
            WonderFleet has no public sign-up. Accounts are provisioned by Ofemini Global Limited.
          </p>
        </div>
      </div>

      <aside className="relative hidden overflow-hidden bg-brand-900 lg:block">
        <div className="absolute inset-0 bg-[radial-gradient(ellipse_at_top_right,rgba(70,191,133,.35),transparent_55%)]" />
        <div className="relative flex h-full flex-col justify-between p-12 text-brand-50">
          <p className="max-w-md font-display text-3xl font-bold leading-snug text-white">
            Every truck, every degree, every kilometre — watched in real time.
          </p>
          <dl className="grid grid-cols-3 gap-6 border-t border-white/15 pt-8">
            {[
              ['Temperature & humidity', 'Continuous cargo monitoring with heat-spoilage alerts'],
              ['GPS & route', 'Live position, stoppages and delay detection'],
              ['Email & SMS', 'Stakeholders told the moment conditions slip'],
            ].map(([term, description]) => (
              <div key={term}>
                <dt className="text-sm font-semibold text-white">{term}</dt>
                <dd className="mt-1 text-xs leading-relaxed text-brand-200">{description}</dd>
              </div>
            ))}
          </dl>
        </div>
      </aside>
    </div>
  )
}
