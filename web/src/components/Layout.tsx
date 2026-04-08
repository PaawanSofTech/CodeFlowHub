import React, { useState } from 'react'
import { Outlet, Link, useNavigate, useLocation } from 'react-router-dom'
import { useAuthStore } from '../store/authStore'
import {
  GitBranch, Home, LogOut, User, Moon, Sun, Menu, X, Code2, GitCommit
} from 'lucide-react'

export function Layout() {
  const { user, logout } = useAuthStore()
  const navigate = useNavigate()
  const location = useLocation()
  const [dark, setDark] = useState(true)
  const [sidebarOpen, setSidebarOpen] = useState(false)

  const handleLogout = () => { logout(); navigate('/login') }

  const navItems = [
    { to: '/', icon: <Home size={16} />, label: 'Dashboard' },
  ]

  return (
    <div className={`min-h-screen flex flex-col ${dark ? 'bg-gray-950 text-gray-100' : 'bg-gray-50 text-gray-900'}`}>
      {/* Top nav */}
      <header className={`h-14 border-b flex items-center px-4 gap-4 sticky top-0 z-50 ${dark ? 'bg-gray-900 border-gray-800' : 'bg-white border-gray-200'}`}>
        <button className="md:hidden" onClick={() => setSidebarOpen(!sidebarOpen)}>
          {sidebarOpen ? <X size={20} /> : <Menu size={20} />}
        </button>

        <Link to="/" className="flex items-center gap-2 font-bold text-lg text-blue-400">
          <GitBranch size={22} />
          <span>CodeFlow</span>
        </Link>

        <div className="flex-1" />

        <nav className="hidden md:flex items-center gap-1">
          {navItems.map(item => (
            <Link
              key={item.to}
              to={item.to}
              className={`flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm transition-colors ${
                location.pathname === item.to
                  ? 'bg-blue-600 text-white'
                  : dark ? 'hover:bg-gray-800 text-gray-300' : 'hover:bg-gray-100 text-gray-700'
              }`}
            >
              {item.icon} {item.label}
            </Link>
          ))}
        </nav>

        <div className="flex items-center gap-2">
          <button
            onClick={() => setDark(!dark)}
            className={`p-2 rounded-md transition-colors ${dark ? 'hover:bg-gray-800' : 'hover:bg-gray-100'}`}
          >
            {dark ? <Sun size={16} /> : <Moon size={16} />}
          </button>

          <div className="flex items-center gap-2 px-3 py-1.5 rounded-md border text-sm"
            style={{ borderColor: dark ? '#374151' : '#e5e7eb' }}>
            <User size={14} />
            <span className="hidden sm:block">{user?.username || 'Anonymous'}</span>
          </div>

          <button
            onClick={handleLogout}
            className="p-2 rounded-md text-red-400 hover:bg-red-950 transition-colors"
            title="Logout"
          >
            <LogOut size={16} />
          </button>
        </div>
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
