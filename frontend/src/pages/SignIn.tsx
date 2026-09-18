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
    <div className="min-h-screen lg:grid lg:grid-cols-[1fr_1.05fr]">
      {/* Below lg the panel collapses to a banner above the form, so the
          photograph is still present on phones without crowding the fields. */}
      <div className="relative h-44 w-full overflow-hidden bg-navy sm:h-56 lg:hidden">
        <img
          src="/auth-hero-wide.webp"
          alt=""
          className="absolute inset-0 h-full w-full object-cover"
          fetchPriority="high"
        />
        <div className="absolute inset-0 bg-gradient-to-t from-navy/85 via-navy/25 to-navy/40" />
        <p className="absolute inset-x-5 bottom-4 font-display text-lg font-bold leading-snug text-white sm:text-xl">
          Every truck, every degree, every kilometre — watched in real time.
        </p>
      </div>

      <div className="flex items-center justify-center px-6 py-10 sm:py-12">
        <div className="w-full max-w-sm">
          <div className="mb-8">
            <img src="/wonderfleet-logo.png" alt="WonderFleet" className="h-14 w-auto sm:h-16" />
            <p className="mt-2.5 text-[11px] font-medium uppercase tracking-wider text-ink-faint">OfeminiAgricTech</p>
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

      <aside className="relative hidden overflow-hidden lg:block">
        {/* Only the backdrop is masked, so the panel dissolves into the form
            column on the left instead of meeting it on a hard vertical seam.
            The copy sits outside the mask and keeps full contrast. */}
        <div
          className="absolute inset-0"
          style={{
            maskImage: 'linear-gradient(to right, transparent 0, rgba(0,0,0,.65) 44px, #000 104px)',
            WebkitMaskImage: 'linear-gradient(to right, transparent 0, rgba(0,0,0,.65) 44px, #000 104px)',
          }}
        >
          <div className="absolute inset-0 bg-navy" />
          {/* Cropped, never stretched. */}
          <picture>
            <source srcSet="/auth-hero.webp" type="image/webp" />
            <img
              src="/auth-hero.jpg"
              alt=""
              className="absolute inset-0 h-full w-full object-cover object-[62%_center]"
              fetchPriority="high"
            />
          </picture>
          {/* Darkens the top and bottom just enough for white text to hold
              contrast over the sunset, leaving the truck itself readable. */}
          <div className="absolute inset-0 bg-gradient-to-b from-navy/80 via-navy/15 to-navy/90" />
        </div>

        <div className="relative flex h-full flex-col justify-between py-10 pl-[104px] pr-10 xl:py-12 xl:pr-12">
          <p className="max-w-md font-display text-2xl font-bold leading-snug text-white drop-shadow-[0_1px_8px_rgba(19,26,41,.55)] xl:text-3xl">
            Every truck, every degree, every kilometre — watched in real time.
          </p>
          <dl className="grid grid-cols-3 gap-6 border-t border-white/20 pt-8">
            {[
              ['Temperature & humidity', 'Continuous cargo monitoring with heat-spoilage alerts'],
              ['GPS & route', 'Live position, stoppages and delay detection'],
              ['Email & SMS', 'Stakeholders told the moment conditions slip'],
            ].map(([term, description]) => (
              <div key={term}>
                <dt className="text-sm font-semibold text-white drop-shadow-[0_1px_6px_rgba(19,26,41,.6)]">{term}</dt>
                <dd className="mt-1 text-xs leading-relaxed text-white/80 drop-shadow-[0_1px_6px_rgba(19,26,41,.6)]">
                  {description}
                </dd>
              </div>
            ))}
          </dl>
        </div>
      </aside>
    </div>
  )
}
