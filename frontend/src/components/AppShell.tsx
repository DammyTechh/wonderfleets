import clsx from 'clsx'
import {
  Bell, ChartNoAxesColumn, CircleAlert, LayoutDashboard, LogOut, MapPinned, Menu, Settings,
  Sprout, Truck, Users, X,
} from 'lucide-react'
import { useEffect, useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '@/lib/auth'
import { useUnreadCount } from '@/lib/queries'
import { Avatar } from './ui'

const navigation = [
  { to: '/', label: 'Dashboard', icon: LayoutDashboard, end: true },
  { to: '/fleet', label: 'Fleet management', icon: Truck },
  { to: '/tracking', label: 'Live tracking', icon: MapPinned },
  { to: '/partners', label: 'Logistics partners', icon: Users },
  { to: '/processors', label: 'Agro-processors', icon: Sprout },
  { to: '/analytics', label: 'Analytics', icon: ChartNoAxesColumn },
  { to: '/alerts', label: 'Alerts', icon: CircleAlert },
]

export function AppShell() {
  const { admin, signOut } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [open, setOpen] = useState(false)
  const unread = useUnreadCount()

  useEffect(() => setOpen(false), [location.pathname])

  return (
    <div className="min-h-screen lg:grid lg:grid-cols-[264px_1fr]">
      <a href="#main" className="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-50 focus:rounded-lg focus:bg-brand-900 focus:px-4 focus:py-2 focus:text-white">
        Skip to content
      </a>

      <aside
        className={clsx(
          'fixed inset-y-0 left-0 z-40 w-[264px] border-r border-line bg-surface transition-transform lg:static lg:translate-x-0',
          open ? 'translate-x-0' : '-translate-x-full',
        )}
      >
        <div className="flex h-16 items-center gap-2.5 border-b border-line px-5">
          <img src="/wonderfleet.svg" alt="" className="h-9 w-9" />
          <div>
            <p className="font-display text-[17px] font-bold leading-none text-brand-900">WonderFleet</p>
            <p className="mt-1 text-[11px] font-medium uppercase tracking-wider text-ink-faint">OfeminiAgricTech</p>
          </div>
          <button className="btn-ghost ml-auto p-1.5 lg:hidden" onClick={() => setOpen(false)} aria-label="Close menu">
            <X size={18} />
          </button>
        </div>

        <nav className="space-y-1 p-3" aria-label="Main">
          {navigation.map(({ to, label, icon: Icon, end }) => (
            <NavLink
              key={to}
              to={to}
              end={end}
              className={({ isActive }) =>
                clsx(
                  'flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition',
                  isActive ? 'bg-brand-50 text-brand-800' : 'text-ink-soft hover:bg-surface-sunken hover:text-ink',
                )
              }
            >
              <Icon size={18} />
              {label}
            </NavLink>
          ))}
        </nav>

        <div className="absolute inset-x-0 bottom-0 border-t border-line p-3">
          <NavLink
            to="/settings"
            className={({ isActive }) =>
              clsx('flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition',
                isActive ? 'bg-brand-50 text-brand-800' : 'text-ink-soft hover:bg-surface-sunken hover:text-ink')
            }
          >
            <Settings size={18} /> Settings
          </NavLink>
          <button
            className="mt-1 flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium text-ink-soft transition hover:bg-critical-50 hover:text-critical-700"
            onClick={async () => {
              await signOut()
              navigate('/sign-in')
            }}
          >
            <LogOut size={18} /> Sign out
          </button>
        </div>
      </aside>

      {open && <div className="fixed inset-0 z-30 bg-ink/20 lg:hidden" onClick={() => setOpen(false)} />}

      <div className="flex min-h-screen flex-col">
        <header className="sticky top-0 z-20 flex h-16 items-center gap-3 border-b border-line bg-surface/90 px-4 backdrop-blur lg:px-8">
          <button className="btn-ghost p-2 lg:hidden" onClick={() => setOpen(true)} aria-label="Open menu">
            <Menu size={20} />
          </button>
          <div className="ml-auto flex items-center gap-2">
            <NavLink to="/notifications" className="btn-ghost relative p-2" aria-label="Notifications">
              <Bell size={20} />
              {(unread.data?.unread ?? 0) > 0 && (
                <span className="tabular absolute -right-0.5 -top-0.5 grid h-5 min-w-5 place-items-center rounded-full bg-critical-600 px-1 text-[11px] font-semibold text-white">
                  {Math.min(unread.data!.unread, 99)}
                </span>
              )}
            </NavLink>
            <NavLink to="/settings" className="flex items-center gap-2.5 rounded-xl px-2 py-1.5 hover:bg-surface-sunken">
              <Avatar initials={admin?.fullName?.slice(0, 2).toUpperCase() ?? 'WF'} photoUrl={admin?.photoUrl} size={34} />
              <span className="hidden text-left sm:block">
                <span className="block text-sm font-semibold leading-tight">{admin?.fullName ?? 'Administrator'}</span>
                <span className="block text-xs text-ink-faint">{admin?.adminCode}</span>
              </span>
            </NavLink>
          </div>
        </header>

        <main id="main" className="flex-1 px-4 py-6 lg:px-8">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
