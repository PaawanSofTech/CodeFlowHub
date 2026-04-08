import React, { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { GitBranch, Plus, GitCommit, Folder, RefreshCw } from 'lucide-react'
import { reposApi } from '../api/client'
import { useAuthStore } from '../store/authStore'
import { Card, Spinner, EmptyState, Badge } from '../components/Layout'

export function DashboardPage() {
  const [repos, setRepos] = useState<any[]>([])
  const [loading, setLoading] = useState(true)
  const [creating, setCreating] = useState(false)
  const [newName, setNewName] = useState('')
  const { user } = useAuthStore()
  const navigate = useNavigate()

  const fetchRepos = () => {
    setLoading(true)
    reposApi.list()
      .then(data => {
        // ✅ Fix: only show repos belonging to the logged-in user
        const mine = data.filter((r: any) => r.owner === user?.username)
        setRepos(mine)
      })
      .catch(() => {})
      .finally(() => setLoading(false))
  }

  useEffect(() => {
    fetchRepos()
  }, [user?.username])

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!newName.trim() || !user) return
    try {
      await reposApi.create(user.username, newName.trim())
      setCreating(false)
      setNewName('')
      fetchRepos()
    } catch (err: any) {
      alert(err.response?.data?.error || 'Failed to create repo')
    }
  }

  if (loading) return <Spinner />

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-white">Repositories</h1>
          <p className="text-gray-500 text-sm mt-0.5">
            {repos.length} {repos.length === 1 ? 'repository' : 'repositories'} for <span className="text-gray-400">{user?.username}</span>
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={fetchRepos}
            className="p-2 text-gray-400 hover:text-white hover:bg-gray-800 rounded-lg transition-colors"
            title="Refresh"
          >
            <RefreshCw size={16} />
          </button>
          <button
            onClick={() => setCreating(!creating)}
            className="flex items-center gap-2 bg-blue-600 hover:bg-blue-500 text-white px-4 py-2 rounded-lg text-sm font-medium transition-colors"
          >
            <Plus size={16} /> New Repository
          </button>
        </div>
      </div>

      {/* Create form */}
      {creating && (
        <Card className="p-5">
          <h2 className="font-semibold text-white mb-4">Create New Repository</h2>
          <form onSubmit={handleCreate} className="flex gap-3 items-center">
            <div className="text-gray-500 text-sm">{user?.username} /</div>
            <input
              autoFocus
              value={newName}
              onChange={e => setNewName(e.target.value)}
              placeholder="repository-name"
              className="flex-1 bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-blue-500"
            />
            <button
              type="submit"
              className="bg-blue-600 hover:bg-blue-500 text-white px-4 py-2 rounded-lg text-sm font-medium"
            >
              Create
            </button>
            <button
              type="button"
              onClick={() => setCreating(false)}
              className="bg-gray-800 hover:bg-gray-700 text-gray-300 px-4 py-2 rounded-lg text-sm"
            >
              Cancel
            </button>
          </form>
        </Card>
      )}

      {/* Repos list */}
      {repos.length === 0 ? (
        <EmptyState
          icon={<Folder size={48} />}
          title="No repositories yet"
          description={`No repositories found for "${user?.username}". Push one from the CLI or create one above.`}
        />
      ) : (
        <div className="grid gap-4">
          {repos.map((repo: any) => (
            <Card key={`${repo.owner}/${repo.name}`} className="p-5 hover:border-gray-600 transition-colors">
              <div className="flex items-start justify-between gap-4">
                <div className="flex-1 min-w-0">
                  <Link
                    to={`/${repo.owner}/${repo.name}`}
                    className="text-blue-400 hover:text-blue-300 font-semibold text-lg"
                  >
                    {repo.owner}/<span className="font-bold">{repo.name}</span>
                  </Link>

                  <div className="flex items-center gap-3 mt-2 text-sm text-gray-500">
                    <span className="flex items-center gap-1">
                      <GitBranch size={13} />
                      {repo.branch || 'main'}
                    </span>
                    {repo.head && (
                      <span className="flex items-center gap-1">
                        <GitCommit size={13} />
                        <code className="text-yellow-500 font-mono text-xs">{repo.head}</code>
                      </span>
                    )}
                  </div>
                </div>

                <div className="flex items-center gap-2 flex-shrink-0">
                  {repo.commitCount !== undefined && (
                    <Badge color="gray">→ {repo.commitCount} commits</Badge>
                  )}
                </div>
              </div>
            </Card>
          ))}
        </div>
      )}
    </div>
  )
}