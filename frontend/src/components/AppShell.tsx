import clsx from 'clsx'
import {
  Bell, ChartNoAxesColumn, ChevronDown, CircleAlert, Handshake, LayoutDashboard, LogOut,
  MapPinned, Menu, Settings, Sprout, Truck, Users, X,
} from '@/components/icons'
import { useEffect, useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '@/lib/auth'
import { useLiveUpdates } from '@/lib/live'
import { useUnreadCount } from '@/lib/queries'
import { Avatar } from './ui'

/**
 * Sidebar structure follows the Figma exactly:
 *   Dashboard / Fleet Management / Live Tracking
 *   -- OPERATIONS --
 *   Partners (expands to Logistics partners, Agro-Processors)
 *   Analytics / Alerts
 *   ...pinned to the bottom: Settings, Log out
 */
const primaryNav = [
  { to: '/', label: 'Dashboard', icon: LayoutDashboard, end: true },
  { to: '/fleet', label: 'Fleet Management', icon: Truck },
  { to: '/tracking', label: 'Live Tracking', icon: MapPinned },
]

const partnerNav = [
  { to: '/partners', label: 'Logistics partners', icon: Users },
  { to: '/processors', label: 'Agro-Processors', icon: Sprout },
]

const operationsNav = [
  { to: '/analytics', label: 'Analytics', icon: ChartNoAxesColumn },
  { to: '/alerts', label: 'Alerts', icon: CircleAlert },
]

/** Full-bleed green pill when active -- the Figma has no inset or rounded corners here. */
const itemClass = ({ isActive }: { isActive: boolean }) =>
  clsx(
    'flex items-center gap-3 px-5 py-2.5 text-sm transition-colors',
    isActive ? 'bg-brand-500 font-medium text-white' : 'text-navy-dim hover:bg-white/[.05] hover:text-white',
  )

export function AppShell() {
  const { admin, signOut } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [open, setOpen] = useState(false)
  const unread = useUnreadCount()
  // One live connection for every admin screen; see lib/live.ts.
  const live = useLiveUpdates()

  const inPartners = location.pathname.startsWith('/partners') || location.pathname.startsWith('/processors')
  const [partnersOpen, setPartnersOpen] = useState(inPartners)

  useEffect(() => setOpen(false), [location.pathname])
  useEffect(() => { if (inPartners) setPartnersOpen(true) }, [inPartners])

  return (
    <div className="min-h-screen bg-surface-muted lg:grid lg:grid-cols-[240px_1fr]">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-50 focus:rounded-lg focus:bg-brand-500 focus:px-4 focus:py-2 focus:text-white"
      >
        Skip to content
      </a>

      <aside
        className={clsx(
          'fixed inset-y-0 left-0 z-40 flex w-[240px] flex-col bg-navy transition-transform lg:static lg:translate-x-0',
          open ? 'translate-x-0' : '-translate-x-full',
        )}
      >
        {/* Logo lock-up on navy -- transparent PNG, never the white-background file. */}
        <div className="flex h-[72px] items-center px-5">
          <img src="/wonderfleet-logo.png" alt="WonderFleet" className="h-11 w-auto" />
          <button
            className="ml-auto rounded-lg p-1.5 text-navy-dim hover:bg-white/10 hover:text-white lg:hidden"
            onClick={() => setOpen(false)}
            aria-label="Close menu"
          >
            <X size={18} />
          </button>
        </div>

        <nav className="flex-1 overflow-y-auto pb-4" aria-label="Main">
          {primaryNav.map(({ to, label, icon: Icon, end }) => (
            <NavLink key={to} to={to} end={end} className={itemClass}>
              <Icon size={18} /> {label}
            </NavLink>
          ))}

          <p className="px-5 pb-1.5 pt-6 text-xs text-navy-label">Operations</p>

          <button
            type="button"
            onClick={() => setPartnersOpen((v) => !v)}
            aria-expanded={partnersOpen}
            className={clsx(
              'flex w-full items-center gap-3 px-5 py-2.5 text-sm transition-colors',
              inPartners ? 'bg-brand-500 font-medium text-white' : 'text-navy-dim hover:bg-white/[.05] hover:text-white',
            )}
          >
            <Handshake size={18} /> Partners
            <ChevronDown size={16} className={clsx('ml-auto transition-transform', partnersOpen && 'rotate-180')} />
          </button>

          {partnersOpen &&
            partnerNav.map(({ to, label, icon: Icon }) => (
              <NavLink
                key={to}
                to={to}
                className={({ isActive }) =>
                  clsx(
                    'flex items-center gap-2.5 py-2 pl-11 pr-5 text-[13px] transition-colors',
                    isActive ? 'font-medium text-white' : 'text-navy-dim hover:text-white',
                  )
                }
              >
                <Icon size={15} /> {label}
              </NavLink>
            ))}

          {operationsNav.map(({ to, label, icon: Icon }) => (
            <NavLink key={to} to={to} className={itemClass}>
              <Icon size={18} /> {label}
            </NavLink>
          ))}
        </nav>

        <div className="pb-4">
          <NavLink to="/settings" className={itemClass}>
            <Settings size={18} /> Settings
          </NavLink>
          <button
            className="flex w-full items-center gap-3 px-5 py-2.5 text-sm text-navy-dim transition-colors hover:bg-white/[.05] hover:text-white"
            onClick={async () => {
              await signOut()
              navigate('/sign-in')
            }}
          >
            <LogOut size={18} /> Log out
          </button>
        </div>
      </aside>

      {open && <div className="fixed inset-0 z-30 bg-ink/30 lg:hidden" onClick={() => setOpen(false)} />}

      <div className="flex min-h-screen flex-col">
        {/* The Figma has no full-width chrome bar -- the bell and account sit inline
            with the page heading, so this strip is transparent and borderless. */}
        <header className="sticky top-0 z-20 flex h-[72px] items-center gap-3 bg-surface-muted px-4 lg:px-8">
          <button
            className="rounded-lg p-2 text-ink-soft hover:bg-surface-sunken lg:hidden"
            onClick={() => setOpen(true)}
            aria-label="Open menu"
          >
            <Menu size={20} />
          </button>
          {/* The sidebar (and its logo) is hidden on phones, so the top bar carries the brand. */}
          <img src="/wonderfleet-logo.png" alt="WonderFleet" className="h-8 w-auto lg:hidden" />
          <div className="ml-auto flex items-center gap-3">
            <span
              className="hidden items-center gap-1.5 text-xs text-ink-soft sm:flex"
              title={
                live === 'live'
                  ? 'Receiving readings and alerts as they happen'
                  : 'Live connection interrupted; screens still refresh every minute and will catch up on reconnect'
              }
            >
              <span className="relative flex h-2 w-2">
                {live === 'live' && <span className="absolute inset-0 animate-ring rounded-full bg-live" />}
                <span className={clsx('relative h-2 w-2 rounded-full', live === 'live' ? 'bg-live' : 'bg-warning-500')} />
              </span>
              {live === 'live' ? 'Live' : live === 'connecting' ? 'Connecting…' : 'Reconnecting…'}
            </span>
            <NavLink
              to="/notifications"
              className="relative grid h-9 w-9 place-items-center rounded-md text-ink-soft transition-colors hover:bg-surface-sunken hover:text-ink"
              aria-label="Notifications"
            >
              <Bell size={18} />
              {(unread.data?.unread ?? 0) > 0 && (
                <span className="tabular absolute right-0.5 top-0.5 grid h-4 min-w-4 place-items-center rounded-full bg-critical-500 px-1 text-[10px] font-medium text-white">
                  {Math.min(unread.data!.unread, 99)}
                </span>
              )}
            </NavLink>
            {/* Account block sits over a thin green rule in the Figma. */}
            <NavLink to="/settings" className="flex items-center gap-2.5 rounded-md py-1 pl-1 pr-2 transition-colors hover:bg-surface-sunken">
              <Avatar initials={admin?.fullName?.slice(0, 2).toUpperCase() ?? 'WF'} photoUrl={admin?.photoUrl} size={30} />
              <span className="hidden text-left sm:block">
                <span className="block text-[13px] font-medium leading-tight text-ink">
                  {admin?.fullName ?? 'Administrator'}
                </span>
                <span className="block text-[11px] text-ink-faint">Admin</span>
              </span>
            </NavLink>
          </div>
        </header>

        <main id="main" className="flex-1 px-4 pb-8 lg:px-8">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
