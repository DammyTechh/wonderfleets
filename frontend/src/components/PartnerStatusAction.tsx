import { useState } from 'react'
import { BadgeCheck, ShieldAlert } from '@/components/icons'
import { ErrorNote, Field, Modal, Spinner } from '@/components/ui'
import { errorMessage } from '@/lib/api'
import { changePartnerStatus, changeProcessorStatus, useApiMutation } from '@/lib/queries'
import type { PartnerStatus } from '@/lib/types'

/**
 * Approves a partner or processor, or takes one out of service.
 *
 * Both are created Pending on purpose — a party is only cleared for work once their
 * documents have been checked. Nothing can be dispatched to or by a party that is not
 * Active, so this button is what unblocks trip creation, and it is the counterpart of
 * the line in the onboarding dialog that says to activate them from this list.
 *
 * Activation is a single click because it is routine and reversible. Suspension asks for
 * a reason, because it stops work and someone will later want to know why.
 */
export function PartnerStatusAction({ kind, id, name, status }: {
  kind: 'partner' | 'processor'
  id: string
  name: string
  status: PartnerStatus | string
}) {
  const [suspending, setSuspending] = useState(false)
  const [reason, setReason] = useState('')
  const [failure, setFailure] = useState<string | null>(null)

  const change = useApiMutation(
    kind === 'partner' ? changePartnerStatus : changeProcessorStatus,
    [['partners'], ['processors'], ['notifications'], ['fleet'], ['dashboard']],
  )

  async function apply(next: PartnerStatus, note?: string) {
    setFailure(null)
    try {
      await change.mutateAsync({ id, status: next, reason: note?.trim() || null })
      setSuspending(false)
      setReason('')
    } catch (error) {
      setFailure(errorMessage(error))
    }
  }

  const busy = change.isPending

  return (
    <>
      {status === 'Active' ? (
        <button
          className="btn-secondary px-3 py-1.5 text-xs"
          disabled={busy}
          title={`Stop new shipments for ${name}`}
          onClick={() => { setFailure(null); setSuspending(true) }}
        >
          <ShieldAlert size={14} /> Suspend
        </button>
      ) : (
        <button
          className="btn-primary px-3 py-1.5 text-xs"
          disabled={busy}
          title={`Clear ${name} for shipments`}
          onClick={() => apply('Active')}
        >
          {busy ? <Spinner size={14} /> : <BadgeCheck size={14} />}
          {status === 'Suspended' ? 'Reinstate' : 'Activate'}
        </button>
      )}

      {/* An activation that fails has nowhere to show itself in a table row. */}
      <Modal
        open={Boolean(failure) && !suspending}
        title="That did not go through"
        onClose={() => setFailure(null)}
      >
        <ErrorNote message={failure ?? ''} />
        <div className="mt-5 flex justify-end">
          <button className="btn-secondary" onClick={() => setFailure(null)}>Close</button>
        </div>
      </Modal>

      <Modal
        open={suspending}
        title={`Suspend ${name}`}
        description="Shipments already on the road continue. No new trip can be created for them until they are reinstated."
        onClose={() => { setSuspending(false); setFailure(null) }}
      >
        <Field label="Reason" hint="Kept on the record, and shown to whoever reinstates them.">
          <input
            className="input"
            placeholder="Insurance certificate expired"
            value={reason}
            onChange={(event) => setReason(event.target.value)}
          />
        </Field>
        {failure && <div className="mt-3"><ErrorNote message={failure} /></div>}
        <div className="mt-5 flex justify-end gap-2">
          <button className="btn-secondary" onClick={() => { setSuspending(false); setFailure(null) }}>Cancel</button>
          <button className="btn-danger" disabled={busy} onClick={() => apply('Suspended', reason)}>
            {busy && <Spinner />} Suspend
          </button>
        </div>
      </Modal>
    </>
  )
}
