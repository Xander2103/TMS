import { Outlet } from 'react-router-dom'
import { Sidebar } from './Sidebar'
import './AppLayout.css'

export function AppLayout() {
  return (
    <div className="app-shell">
      <Sidebar />
      <main className="content">
        <Outlet />
      </main>
    </div>
  )
}
