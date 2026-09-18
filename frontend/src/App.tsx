import { Navigate, Route, Routes } from 'react-router-dom'
import { AppShell } from './components/AppShell'
import { Spinner } from './components/ui'
import { useAuth } from './lib/auth'
import AddFleet from './pages/AddFleet'
import Alerts from './pages/Alerts'
import Analytics from './pages/Analytics'
import Dashboard from './pages/Dashboard'
import Fleet from './pages/Fleet'
import Notifications from './pages/Notifications'
import PartnerProfile from './pages/PartnerProfile'
import Partners from './pages/Partners'
import Processors from './pages/Processors'
import Settings from './pages/Settings'
import SignIn from './pages/SignIn'
import Tracking from './pages/Tracking'
import TripDetailPage from './pages/TripDetail'
import AgroPortal from './pages/portal/AgroPortal'
import LogisticsPortal from './pages/portal/LogisticsPortal'
import PortalEntry from './pages/portal/PortalEntry'

function RequireAdmin({ children }: { children: JSX.Element }) {
  const { admin, ready } = useAuth()
  if (!ready) {
    return (
      <div className="grid min-h-screen place-items-center text-ink-soft">
        <span className="flex items-center gap-2 text-sm">
          <Spinner /> Restoring your session…
        </span>
      </div>
    )
  }
  return admin ? children : <Navigate to="/sign-in" replace />
}

export default function App() {
  return (
    <Routes>
      <Route path="/sign-in" element={<SignIn />} />

      {/* Login-free partner portals, reached through a share link. */}
      <Route path="/track" element={<PortalEntry />} />
      <Route path="/portal/logistics" element={<LogisticsPortal />} />
      <Route path="/portal/agro" element={<AgroPortal />} />

      <Route
        element={
          <RequireAdmin>
            <AppShell />
          </RequireAdmin>
        }
      >
        <Route index element={<Dashboard />} />
        <Route path="fleet" element={<Fleet />} />
        <Route path="fleet/new" element={<AddFleet />} />
        <Route path="trips/:tripId" element={<TripDetailPage />} />
        <Route path="tracking" element={<Tracking />} />
        <Route path="partners" element={<Partners />} />
        <Route path="partners/:partnerId" element={<PartnerProfile />} />
        <Route path="processors" element={<Processors />} />
        <Route path="analytics" element={<Analytics />} />
        <Route path="alerts" element={<Alerts />} />
        <Route path="notifications" element={<Notifications />} />
        <Route path="settings" element={<Settings />} />
      </Route>

      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
