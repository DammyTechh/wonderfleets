import clsx from 'clsx'
import { CircleAlert, Cpu, PlugZap, Radio } from '@/components/icons'
import { type ReactNode, useState } from 'react'
import { errorMessage } from '@/lib/api'
import { since } from '@/lib/format'
import { registerDevice, useApiMutation, useFirebaseStatus } from '@/lib/queries'
import { Spinner } from './ui'

/**
 * Answers "why is my hardware data not showing?" in the place you would look for it.
 *
 * Three things must be true for a Firebase node to reach the app: polling is running,
 * the node is registered as a device, and the node holds real readings. This panel
 * surfaces the first two directly and lets you fix the second with one click.
 *
 * `compact` renders nothing when all is well, so it can sit on the dashboard without
 * adding permanent chrome to the design.
 */
export function FirebaseDevicesPanel({ compact = false }: { compact?: boolean }) {
  const status = useFirebaseStatus()
  const [registering, setRegistering] = useState<string | null>(null)
  const [failure, setFailure] = useState<string | null>(null)
  // Invalidating the 'devices' prefix refreshes the device chips and this panel together.
  const register = useApiMutation(registerDevice, [['devices']])

  if (status.isLoading || !status.data) return null
  const data = status.data

  const onRegister = async (key: string) => {
    setRegistering(key)
    setFailure(null)
    try {
      await register.mutateAsync(key)
    } catch (error) {
      setFailure(errorMessage(error))
    } finally {
      setRegistering(null)
    }
  }

  // Polling is off: say why, in the terms of the setting that controls it.
  if (!data.pollingActive && data.disabledReason) {
    return (
      <Notice tone="warning" icon={<PlugZap size={18} />} title="Device data is not being read from Firebase">
        <p>{data.disabledReason}</p>
        <p className="mt-1 text-ink-faint">Change it in <code className="font-mono">.env</code> and restart the API. The README section “Firebase: the hardware feed” walks through it.</p>
      </Notice>
    )
  }

  // Polling runs but the reads fail (rules, credential, network).
  if (data.pollingActive && data.lastError) {
    return (
      <Notice tone="critical" icon={<CircleAlert size={18} />} title="Reading from Firebase is failing">
        <p className="break-words font-mono text-xs">{data.lastError}</p>
        <p className="mt-1 text-ink-faint">Last successful read {since(data.lastSuccessfulPollAt) || 'never'}.</p>
      </Notice>
    )
  }

  const pending = data.unregisteredKeys
  if (pending.length > 0) {
    return (
      <Notice
        tone="info"
        icon={<Radio size={18} />}
        title={pending.length === 1 ? '1 unit is transmitting but not registered' : `${pending.length} units are transmitting but not registered`}
      >
        <p className="text-ink-faint">
          WonderFleet never adds a Firebase node on its own, so a test node cannot become a device by accident. Register the
          ones that are real hardware.
        </p>
        <ul className="mt-3 divide-y divide-line overflow-hidden rounded-md border border-line bg-surface">
          {pending.map((unit) => (
            <li key={unit.firebaseKey} className="flex items-center gap-3 px-3 py-2.5">
              <Cpu size={16} className="shrink-0 text-ink-soft" />
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm font-medium text-ink">{unit.firebaseKey}</p>
                <p className="text-xs text-ink-faint">Seen {since(unit.lastSeenAt)}</p>
              </div>
              <button
                type="button"
                className="btn-primary shrink-0 px-3 py-1.5 text-xs"
                disabled={registering !== null}
                onClick={() => onRegister(unit.firebaseKey)}
              >
                {registering === unit.firebaseKey ? <Spinner size={14} /> : 'Register'}
              </button>
            </li>
          ))}
        </ul>
        {failure && <p className="mt-2 text-xs text-critical-600">{failure}</p>}
      </Notice>
    )
  }

  if (compact) return null

  // Healthy: a quiet one-liner so you can see the connection is alive.
  return (
    <p className="flex items-center gap-2 text-xs text-ink-faint">
      <span className="h-2 w-2 rounded-full bg-live" aria-hidden />
      {data.lastPollAt
        ? `Connected to Firebase · ${data.nodesInLastPoll} node${data.nodesInLastPoll === 1 ? '' : 's'} · read ${since(data.lastPollAt)}`
        : 'Connecting to Firebase…'}
    </p>
  )
}

function Notice({ tone, icon, title, children }: {
  tone: 'warning' | 'critical' | 'info'
  icon: ReactNode
  title: string
  children: ReactNode
}) {
  return (
    <div
      role="status"
      className={clsx(
        'rounded-lg border p-4 text-sm text-ink-soft',
        tone === 'warning' && 'border-warning-100 bg-warning-50',
        tone === 'critical' && 'border-critical-100 bg-critical-50',
        tone === 'info' && 'border-info-100 bg-info-50',
      )}
    >
      <div className="flex gap-3">
        <span
          className={clsx(
            'mt-0.5 shrink-0',
            tone === 'warning' && 'text-warning-600',
            tone === 'critical' && 'text-critical-600',
            tone === 'info' && 'text-info-600',
          )}
        >
          {icon}
        </span>
        <div className="min-w-0 flex-1">
          <p className="font-semibold text-ink">{title}</p>
          <div className="mt-1">{children}</div>
        </div>
      </div>
    </div>
  )
}
