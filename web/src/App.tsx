import React from 'react'
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { useAuthStore } from './store/authStore'
import { Layout } from './components/Layout'
import { LoginPage } from './pages/LoginPage'
import { DashboardPage } from './pages/DashboardPage'
import { RepoPage } from './pages/RepoPage'
import { CommitPage } from './pages/CommitPage'
import { CommitsPage } from './pages/CommitsPage'
import { PullRequestsPage } from './pages/PullRequestsPage'
// import { PullRequestDetailPage } from './pages/PullRequestDetailPage'
import { FileBrowserPage } from './pages/FileBrowserPage'

function PrivateRoute({ children }: { children: React.ReactNode }) {
  const { token } = useAuthStore()
  return token ? <>{children}</> : <Navigate to="/login" replace />
}

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/" element={<PrivateRoute><Layout /></PrivateRoute>}>
          <Route index element={<DashboardPage />} />
          <Route path=":owner/:name" element={<RepoPage />} />
          <Route path=":owner/:name/commits" element={<CommitsPage />} />
          <Route path=":owner/:name/commits/:hash" element={<CommitPage />} />
          <Route path=":owner/:name/tree/:hash/*" element={<FileBrowserPage />} />
          <Route path=":owner/:name/pulls" element={<PullRequestsPage />} />
          {/* <Route path=":owner/:name/pulls/:prId" element={<PullRequestDetailPage />} /> */}
        </Route>
      </Routes>
    </BrowserRouter>
  )
}
