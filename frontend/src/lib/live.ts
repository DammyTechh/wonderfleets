import { type QueryClient, type QueryKey, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { freshAccessToken, tokens } from './api'
import { keys } from './queries'
import { connectHub, type LiveState } from './realtime'

/**
 * Batches refreshes: a fleet can report several readings a second, so however many
 * events arrive, each data set is refetched at most once per second.
 */
function coalescer(queryClient: QueryClient) {
  const pending = new Map<string, QueryKey>()
  let timer: ReturnType<typeof setTimeout> | undefined
  const flush = () => {
    timer = undefined
    const batch = [...pending.values()]
    pending.clear()
    for (const queryKey of batch) void queryClient.invalidateQueries({ queryKey })
  }
  return {
    stale: (...queryKeys: QueryKey[]) => {
      for (const key of queryKeys) pending.set(JSON.stringify(key), key)
      timer ??= setTimeout(flush, 1000)
    },
    dispose: () => clearTimeout(timer),
  }
}

const tripOf = (payload: unknown) =>
  typeof payload === 'object' && payload !== null && typeof (payload as { tripId?: unknown }).tripId === 'string'
    ? (payload as { tripId: string }).tripId
    : null

/**
 * The single live connection for the admin app, mounted once in the shell.
 *
 * The API pushes an event the moment a reading or alert is stored. Each event marks
 * the data it affects as stale, and React Query refetches only what is currently on
 * screen — so Dashboard, Live Tracking, Fleet, a trip's detail page, Alerts and the
 * bell all update within about a second, with no reload.
 */
export function useLiveUpdates(): LiveState {
  const queryClient = useQueryClient()
  const [state, setState] = useState<LiveState>('connecting')

  useEffect(() => {
    const { stale, dispose } = coalescer(queryClient)
    // Catch-up is only needed after a gap. On the first connect the page has just loaded.
    let everLive = false
    const disconnect = connectHub(freshAccessToken, {
      onTelemetry: (payload) => {
        const tripId = tripOf(payload)
        // Prefix keys: ['fleet'] refreshes every page and filter of the fleet list, and so on.
        stale(keys.dashboard, keys.tracking, ['fleet'], ['trips'], ['devices'])
        if (tripId) stale(keys.trip(tripId))
      },
      onAlert: (payload) => {
        const tripId = tripOf(payload)
        stale(keys.dashboard, keys.tracking, ['fleet'], ['alerts'], ['notifications'], keys.routeAi)
        if (tripId) stale(keys.trip(tripId))
      },
      onNotification: () => stale(['notifications']),
      onStateChange: (next) => {
        setState(next)
        if (next !== 'live') return
        // Coming back after a gap: catch up on anything pushed while disconnected.
        if (everLive) stale(keys.dashboard, keys.tracking, ['fleet'], ['alerts'], ['notifications'], ['devices'])
        everLive = true
      },
    })
    return () => {
      dispose()
      disconnect()
    }
  }, [queryClient])

  return state
}

/**
 * The same for a partner's tracking link. The hub admits a partner only to the trips on
 * their link, and only while the link is valid, so every pushed event is theirs to see.
 */
export function usePortalLiveUpdates(enabled: boolean): LiveState {
  const queryClient = useQueryClient()
  const [state, setState] = useState<LiveState>('connecting')

  useEffect(() => {
    if (!enabled) return
    const { stale, dispose } = coalescer(queryClient)
    let everLive = false
    const disconnect = connectHub(tokens.portal, {
      onTelemetry: () => stale(['portal']),
      onAlert: () => stale(['portal']),
      onStateChange: (next) => {
        setState(next)
        if (next !== 'live') return
        if (everLive) stale(['portal'])
        everLive = true
      },
    })
    return () => {
      dispose()
      disconnect()
    }
  }, [enabled, queryClient])

  return state
}
