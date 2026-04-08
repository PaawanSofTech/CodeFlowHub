import React, { useState, useEffect } from 'react'
import { Outlet, Link, useNavigate, useLocation } from 'react-router-dom'
import { useAuthStore } from '../store/authStore'
import {
  GitBranch, Home, LogOut, User, Wifi, WifiOff
} from 'lucide-react'
import { api } from '../api/client'

export function Layout() {
  const { user, logout } = useAuthStore()
  const navigate = useNavigate()
  const location = useLocation()
  const [connected, setConnected] = useState<boolean | null>(null)

  // Ping the API to show live connection status
  useEffect(() => {
    const check = () => {
      api.get('/api/repos', { timeout: 3000 })
        .then(() => setConnected(true))
        .catch(() => setConnected(false))
    }
    check()
    const id = setInterval(check, 15000)
    return () => clearInterval(id)
  }, [])

  const handleLogout = () => { logout(); navigate('/login') }

  const navItems = [
    { to: '/', icon: <Home size={16} />, label: 'Dashboard' },
  ]

  return (
    <div className="min-h-screen flex flex-col bg-gray-950 text-gray-100">
      {/* Top nav */}
      <header className="h-14 border-b border-gray-800 flex items-center px-5 gap-4 sticky top-0 z-50 bg-gray-900">
        <Link to="/" className="flex items-center gap-2 font-bold text-lg text-blue-400">
          <GitBranch size={22} />
          <span>CodeFlowHub</span>
        </Link>

        <div className="flex-1" />

        {/* Connection status badge */}
        <div className={`flex items-center gap-1.5 px-2.5 py-1 rounded-full border text-xs font-medium ${
          connected === true
            ? 'bg-green-950 border-green-800 text-green-400'
            : connected === false
            ? 'bg-red-950 border-red-900 text-red-400'
            : 'bg-gray-800 border-gray-700 text-gray-500'
        }`}>
          {connected === true ? <Wifi size={11} /> : <WifiOff size={11} />}
          {connected === true ? 'connected' : connected === false ? 'offline' : '...'}
        </div>

        <nav className="flex items-center gap-1">
          {navItems.map(item => (
            <Link
              key={item.to}
              to={item.to}
              className={`flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm transition-colors font-medium ${
                location.pathname === item.to
                  ? 'bg-blue-600 text-white'
                  : 'hover:bg-gray-800 text-gray-300'
              }`}
            >
              {item.icon} {item.label}
            </Link>
          ))}
        </nav>

        {/* User pill */}
        <div className="flex items-center gap-2 px-3 py-1.5 rounded-lg border border-gray-700 text-sm bg-gray-800">
          <div className="w-5 h-5 bg-blue-600 rounded-full flex items-center justify-center text-xs font-bold text-white">
            {user?.username?.[0]?.toUpperCase() ?? 'P'}
          </div>
          <span className="text-gray-200 hidden sm:block">{user?.username ?? 'Unknown'}</span>
        </div>

        <button
          onClick={handleLogout}
          className="p-2 rounded-md text-gray-500 hover:text-red-400 hover:bg-red-950 transition-colors"
          title="Logout"
        >
          <LogOut size={16} />
        </button>
      </header>

      <main className="flex-1 max-w-7xl mx-auto w-full px-4 py-6">
        <Outlet />
      </main>
    </div>
  )
}

// ─── Reusable UI atoms ────────────────────────────────────────────────────────

export function Card({ children, className = '' }: { children: React.ReactNode; className?: string }) {
  return (
    <div className={`bg-gray-900 border border-gray-800 rounded-lg ${className}`}>
      {children}
    </div>
  )
}

export function Badge({ children, color = 'blue' }: { children: React.ReactNode; color?: string }) {
  const colors: Record<string, string> = {
    blue: 'bg-blue-900/60 text-blue-300 border-blue-800',
    green: 'bg-green-900/60 text-green-300 border-green-800',
    red: 'bg-red-900/60 text-red-300 border-red-800',
    yellow: 'bg-yellow-900/60 text-yellow-300 border-yellow-800',
    purple: 'bg-purple-900/60 text-purple-300 border-purple-800',
    gray: 'bg-gray-800 text-gray-400 border-gray-700',
  }
  return (
    <span className={`text-xs px-2 py-0.5 rounded-full border font-mono ${colors[color] || colors.blue}`}>
      {children}
    </span>
  )
}

export function Spinner() {
  return (
    <div className="flex items-center justify-center py-12">
      <div className="w-8 h-8 border-2 border-blue-500 border-t-transparent rounded-full animate-spin" />
    </div>
  )
}

export function EmptyState({ icon, title, description }: { icon: React.ReactNode; title: string; description?: string }) {
  return (
    <div className="flex flex-col items-center justify-center py-16 text-center">
      <div className="text-gray-600 mb-4">{icon}</div>
      <h3 className="text-lg font-medium text-gray-400 mb-1">{title}</h3>
      {description && <p className="text-sm text-gray-600 max-w-sm">{description}</p>}
    </div>
  )
}

export function HashChip({ hash }: { hash?: string | null }) {
  if (!hash) return null
  return (
    <code className="text-xs bg-gray-800 text-yellow-400 px-1.5 py-0.5 rounded font-mono border border-gray-700">
      {hash.substring(0, 8)}
    </code>
  )
}

export function LiveBadge() {
  return (
    <span className="flex items-center gap-1 text-xs px-2 py-0.5 rounded-full border bg-green-950 border-green-800 text-green-400">
      <span className="w-1.5 h-1.5 bg-green-400 rounded-full animate-pulse" />
      live
    </span>
  )
}

export function Toast({
  message,
  type = 'info',
  onDismiss,
}: {
  message: string
  type?: 'success' | 'info' | 'error'
  onDismiss?: () => void
}) {
  const colors = {
    success: 'bg-green-900/80 border-green-700 text-green-300',
    error: 'bg-red-900/80 border-red-700 text-red-300',
    info: 'bg-blue-900/80 border-blue-700 text-blue-300',
  }

  return (
    <div className="fixed top-5 right-5 z-50">
      <div className={`px-4 py-2 rounded-lg border shadow-lg text-sm ${colors[type]}`}>
        <div className="flex items-center gap-3">
          <span>{message}</span>
          {onDismiss && (
            <button
              onClick={onDismiss}
              className="text-xs opacity-70 hover:opacity-100"
            >
              ✕
            </button>
          )}
        </div>
      </div>
    </div>
  )
}