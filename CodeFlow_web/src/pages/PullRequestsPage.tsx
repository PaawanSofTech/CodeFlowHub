import React, { useEffect, useState, useCallback } from 'react'
import { Link, useParams } from 'react-router-dom'
import { GitMerge, Plus, MessageSquare, X, Circle, RefreshCw } from 'lucide-react'
import { pullsApi, reposApi } from '../api/client'
import { useAuthStore } from '../store/authStore'
import { Card, Badge, Spinner, EmptyState, Toast } from '../components/Layout'
import { useRepoSocket } from '../hooks/useRepoSocket'
import { formatDistanceToNow, format } from 'date-fns'

// ─── List Page ────────────────────────────────────────────────────────────────

export function PullRequestsPage() {
  const { owner, name } = useParams<{ owner: string; name: string }>()
  const [prs, setPrs] = useState<any[]>([])
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<string>('Open')
  const [showForm, setShowForm] = useState(false)
  const [branches, setBranches] = useState<any[]>([])
  const [form, setForm] = useState({ title: '', description: '', sourceBranch: '', targetBranch: '' })
  const [toast, setToast] = useState<{ message: string; type: 'success' | 'info' | 'error' } | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const load = useCallback(async (silent = false) => {
    if (!owner || !name) return
    if (!silent) setLoading(true)
    try {
      const [p, b] = await Promise.all([
        pullsApi.list(owner, name, status),
        reposApi.branches(owner, name),
      ])
      setPrs(p)
      setBranches(b)
      // Set default target branch to first branch
      if (b.length > 0 && !form.targetBranch) {
        setForm(f => ({ ...f, targetBranch: b[0].name }))
      }
    } finally {
      if (!silent) setLoading(false)
    }
  }, [owner, name, status])

  useEffect(() => { load() }, [load])

  // Real-time: new PR created by someone else
  useRepoSocket({
    owner: owner || '',
    name: name || '',
    onEvent: (event) => {
      if (event.type === 'PullRequestCreated') {
        setToast({ message: `New pull request: "${event.title}"`, type: 'info' })
        load(true)
      }
    },
  })

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!owner || !name) return
    setSubmitting(true)
    try {
      await pullsApi.create(owner, name, form)
      setShowForm(false)
      setForm({ title: '', description: '', sourceBranch: '', targetBranch: branches[0]?.name || '' })
      setToast({ message: 'Pull request opened', type: 'success' })
      load(true)
    } catch (err: any) {
      setToast({ message: err.response?.data?.error || 'Failed to create PR', type: 'error' })
    } finally {
      setSubmitting(false)
    }
  }

  const statusTabs = ['Open', 'Merged', 'Closed'] as const

  const openCount = prs.filter(p => p.status === 'Open').length

  return (
    <div className="space-y-5">
      {toast && <Toast message={toast.message} type={toast.type} onDismiss={() => setToast(null)} />}

      {/* Header */}
      <div className="flex items-center gap-3 flex-wrap">
        <Link to={`/${owner}/${name}`} className="text-gray-500 hover:text-gray-300 text-sm transition-colors">{owner}/{name}</Link>
        <span className="text-gray-700">/</span>
        <h1 className="text-white font-semibold">Pull Requests</h1>
        <div className="flex-1" />
        <button onClick={() => load(true)} className="p-1.5 text-gray-500 hover:text-gray-300 rounded transition-colors">
          <RefreshCw size={14} />
        </button>
        <button
          onClick={() => setShowForm(!showForm)}
          className="flex items-center gap-2 bg-blue-600 hover:bg-blue-500 text-white px-4 py-2 rounded-lg text-sm font-medium transition-colors"
        >
          <Plus size={15} /> New Pull Request
        </button>
      </div>

      {/* Create form */}
      {showForm && (
        <Card className="p-6">
          <h2 className="font-semibold text-white mb-5 flex items-center gap-2">
            <GitMerge size={16} className="text-purple-400" />
            Open Pull Request
          </h2>
          <form onSubmit={handleCreate} className="space-y-4">
            <div>
              <label className="text-sm text-gray-400 mb-1.5 block">Title *</label>
              <input
                required autoFocus
                value={form.title}
                onChange={e => setForm({ ...form, title: e.target.value })}
                placeholder="Describe what this PR does"
                className="w-full bg-gray-800 border border-gray-700 rounded-lg px-4 py-2.5 text-white text-sm focus:outline-none focus:border-blue-500"
              />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="text-sm text-gray-400 mb-1.5 block">Source branch *</label>
                <select
                  required
                  value={form.sourceBranch}
                  onChange={e => setForm({ ...form, sourceBranch: e.target.value })}
                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2.5 text-white text-sm focus:outline-none focus:border-blue-500"
                >
                  <option value="">Select branch...</option>
                  {branches.map((b: any) => <option key={b.name} value={b.name}>{b.name}</option>)}
                </select>
              </div>
              <div>
                <label className="text-sm text-gray-400 mb-1.5 block">Target branch *</label>
                <select
                  required
                  value={form.targetBranch}
                  onChange={e => setForm({ ...form, targetBranch: e.target.value })}
                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2.5 text-white text-sm focus:outline-none focus:border-blue-500"
                >
                  {branches.map((b: any) => <option key={b.name} value={b.name}>{b.name}</option>)}
                </select>
              </div>
            </div>
            {form.sourceBranch && form.targetBranch && form.sourceBranch !== form.targetBranch && (
              <div className="text-xs text-gray-500 bg-gray-800/60 rounded-lg px-3 py-2">
                Merging <span className="text-blue-400 font-mono">{form.sourceBranch}</span> into{' '}
                <span className="text-gray-300 font-mono">{form.targetBranch}</span>
              </div>
            )}
            <div>
              <label className="text-sm text-gray-400 mb-1.5 block">Description</label>
              <textarea
                value={form.description}
                onChange={e => setForm({ ...form, description: e.target.value })}
                placeholder="Describe the changes in this PR..."
                rows={4}
                className="w-full bg-gray-800 border border-gray-700 rounded-lg px-4 py-2.5 text-white text-sm focus:outline-none focus:border-blue-500 resize-none"
              />
            </div>
            <div className="flex gap-3">
              <button
                type="submit"
                disabled={submitting}
                className="bg-blue-600 hover:bg-blue-500 disabled:opacity-50 text-white px-6 py-2 rounded-lg text-sm font-medium transition-colors"
              >
                {submitting ? 'Opening...' : 'Open Pull Request'}
              </button>
              <button
                type="button"
                onClick={() => setShowForm(false)}
                className="bg-gray-800 text-gray-300 px-4 py-2 rounded-lg text-sm hover:bg-gray-700 transition-colors"
              >
                Cancel
              </button>
            </div>
          </form>
        </Card>
      )}

      {/* Status tabs */}
      <div className="flex items-center gap-1 border-b border-gray-800">
        {statusTabs.map(s => (
          <button
            key={s}
            onClick={() => setStatus(s)}
            className={`px-4 py-2.5 text-sm font-medium border-b-2 transition-colors -mb-px flex items-center gap-1.5 ${
              status === s ? 'border-blue-500 text-white' : 'border-transparent text-gray-500 hover:text-gray-300'
            }`}
          >
            {s === 'Open' && <Circle size={12} className="text-green-400" />}
            {s === 'Merged' && <GitMerge size={12} className="text-purple-400" />}
            {s === 'Closed' && <X size={12} className="text-red-400" />}
            {s}
          </button>
        ))}
      </div>

      {/* PR list */}
      {loading ? <Spinner /> : prs.length === 0 ? (
        <EmptyState
          icon={<GitMerge size={40} />}
          title={`No ${status.toLowerCase()} pull requests`}
          description={status === 'Open' ? 'Create a new pull request to start a code review.' : undefined}
        />
      ) : (
        <Card>
          <div className="divide-y divide-gray-800">
            {prs.map((pr: any) => (
              <Link
                key={pr.id}
                to={`/${owner}/${name}/pulls/${pr.id}`}
                className="flex items-start gap-4 p-4 hover:bg-gray-800/40 transition-colors"
              >
                <div className="mt-0.5 flex-shrink-0">
                  {pr.status === 'Open' && <Circle size={16} className="text-green-400" />}
                  {pr.status === 'Merged' && <GitMerge size={16} className="text-purple-400" />}
                  {pr.status === 'Closed' && <X size={16} className="text-red-400" />}
                </div>
                <div className="flex-1 min-w-0">
                  <div className="text-white font-medium text-sm hover:text-blue-400 transition-colors">
                    {pr.title}
                  </div>
                  <div className="text-xs text-gray-500 mt-1">
                    #{pr.id?.substring(0, 8)} · opened {formatDistanceToNow(new Date(pr.createdAt), { addSuffix: true })} ·{' '}
                    <span className="text-blue-300 font-mono">{pr.sourceBranch}</span>
                    {' → '}
                    <span className="text-gray-400 font-mono">{pr.targetBranch}</span>
                  </div>
                </div>
                {pr.comments?.length > 0 && (
                  <div className="flex items-center gap-1 text-xs text-gray-500 flex-shrink-0">
                    <MessageSquare size={12} /> {pr.comments.length}
                  </div>
                )}
              </Link>
            ))}
          </div>
        </Card>
      )}
    </div>
  )
}

// ─── Detail Page ──────────────────────────────────────────────────────────────

export function PullRequestDetailPage() {
  const { owner, name, prId } = useParams<{ owner: string; name: string; prId: string }>()
  const [pr, setPr] = useState<any>(null)
  const [comment, setComment] = useState('')
  const [loading, setLoading] = useState(true)
  const [toast, setToast] = useState<{ message: string; type: 'success' | 'info' | 'error' } | null>(null)
  const [actionLoading, setActionLoading] = useState(false)
  const { user } = useAuthStore()

  const reload = useCallback(async () => {
    if (!owner || !name || !prId) return
    const updated = await pullsApi.get(owner, name, prId)
    setPr(updated)
  }, [owner, name, prId])

  useEffect(() => {
    if (!owner || !name || !prId) return
    pullsApi.get(owner, name, prId).then(setPr).finally(() => setLoading(false))
  }, [owner, name, prId])

  const handleComment = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!owner || !name || !prId || !comment.trim()) return
    try {
      const updated = await pullsApi.addComment(owner, name, prId, comment)
      setPr(updated)
      setComment('')
    } catch {
      setToast({ message: 'Failed to add comment', type: 'error' })
    }
  }

  const handleMerge = async () => {
    if (!owner || !name || !prId || !confirm('Merge this pull request?')) return
    setActionLoading(true)
    try {
      const updated = await pullsApi.merge(owner, name, prId)
      setPr(updated)
      setToast({ message: 'Pull request merged', type: 'success' })
    } catch (err: any) {
      setToast({ message: err.response?.data?.error || 'Merge failed', type: 'error' })
    } finally {
      setActionLoading(false)
    }
  }

  const handleClose = async () => {
    if (!owner || !name || !prId || !confirm('Close this pull request?')) return
    setActionLoading(true)
    try {
      const updated = await pullsApi.close(owner, name, prId)
      setPr(updated)
      setToast({ message: 'Pull request closed', type: 'info' })
    } catch {
      setToast({ message: 'Failed to close PR', type: 'error' })
    } finally {
      setActionLoading(false)
    }
  }

  if (loading) return <Spinner />
  if (!pr) return <div className="text-gray-500 text-center py-16">Pull request not found</div>

  const statusConfig: Record<string, { icon: React.ReactNode; label: string; color: string }> = {
    Open: { icon: <Circle size={16} className="text-green-400" />, label: 'Open', color: 'green' },
    Merged: { icon: <GitMerge size={16} className="text-purple-400" />, label: 'Merged', color: 'purple' },
    Closed: { icon: <X size={16} className="text-red-400" />, label: 'Closed', color: 'red' },
  }
  const s = statusConfig[pr.status] || statusConfig.Open

  return (
    <div className="space-y-6">
      {toast && <Toast message={toast.message} type={toast.type} onDismiss={() => setToast(null)} />}

      {/* Breadcrumb */}
      <div className="text-sm text-gray-500 flex items-center gap-1">
        <Link to={`/${owner}/${name}`} className="hover:text-blue-400 transition-colors">{owner}/{name}</Link>
        <span>/</span>
        <Link to={`/${owner}/${name}/pulls`} className="hover:text-blue-400 transition-colors">Pull Requests</Link>
        <span>/</span>
        <span className="text-gray-300 font-mono">#{pr.id?.substring(0, 8)}</span>
      </div>

      {/* PR header */}
      <div>
        <div className="flex items-start gap-3 mb-3">
          {s.icon}
          <h1 className="text-xl font-bold text-white leading-snug">{pr.title}</h1>
        </div>
        <div className="flex items-center gap-3 flex-wrap text-sm text-gray-500">
          <Badge color={s.color}>{pr.status}</Badge>
          <span>
            <span className="text-blue-400 font-mono">{pr.sourceBranch}</span>
            {' → '}
            <span className="text-gray-400 font-mono">{pr.targetBranch}</span>
          </span>
          <span>opened {formatDistanceToNow(new Date(pr.createdAt), { addSuffix: true })}</span>
          {pr.comments?.length > 0 && (
            <span className="flex items-center gap-1">
              <MessageSquare size={12} /> {pr.comments.length} comment{pr.comments.length !== 1 ? 's' : ''}
            </span>
          )}
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Main content */}
        <div className="lg:col-span-2 space-y-5">
          {/* Description */}
          {pr.description && (
            <Card className="p-5">
              <p className="text-gray-300 text-sm leading-relaxed whitespace-pre-wrap">{pr.description}</p>
            </Card>
          )}

          {/* Timeline / Comments */}
          <div className="space-y-3">
            {pr.comments?.length === 0 && !pr.description && (
              <div className="text-gray-600 text-sm text-center py-6">No comments yet — be the first!</div>
            )}

            {pr.comments?.map((c: any, i: number) => (
              <Card key={c.id || i} className="p-4">
                <div className="flex items-center gap-2 mb-3">
                  <div className="w-7 h-7 bg-gradient-to-br from-blue-600 to-purple-600 rounded-full flex items-center justify-center text-xs font-bold text-white flex-shrink-0">
                    {c.authorPublicKey?.substring(0, 1)?.toUpperCase() || '?'}
                  </div>
                  <span className="text-sm text-gray-400 font-mono text-xs">
                    {c.authorPublicKey ? c.authorPublicKey.substring(0, 16) + '...' : 'Unknown'}
                  </span>
                  <span className="text-xs text-gray-600">
                    {formatDistanceToNow(new Date(c.createdAt), { addSuffix: true })}
                  </span>
                </div>
                {c.filePath && (
                  <div className="text-xs text-gray-500 mb-2 font-mono bg-gray-800 px-2 py-1 rounded">
                    {c.filePath}{c.lineNumber ? `:${c.lineNumber}` : ''}
                  </div>
                )}
                <p className="text-gray-300 text-sm whitespace-pre-wrap">{c.body}</p>
              </Card>
            ))}

            {/* Add comment */}
            <Card className="p-4">
              <form onSubmit={handleComment} className="space-y-3">
                <div className="flex items-center gap-2 mb-2">
                  <div className="w-7 h-7 bg-blue-600 rounded-full flex items-center justify-center text-xs font-bold text-white">
                    {user?.username?.[0]?.toUpperCase() || '?'}
                  </div>
                  <span className="text-sm font-medium text-gray-300">Leave a comment</span>
                </div>
                <textarea
                  value={comment}
                  onChange={e => setComment(e.target.value)}
                  placeholder="Write a comment..."
                  rows={3}
                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-4 py-3 text-white text-sm focus:outline-none focus:border-blue-500 resize-none"
                />
                <div className="flex justify-end">
                  <button
                    type="submit"
                    disabled={!comment.trim()}
                    className="bg-blue-600 hover:bg-blue-500 disabled:opacity-40 text-white px-4 py-2 rounded-lg text-sm font-medium transition-colors"
                  >
                    Comment
                  </button>
                </div>
              </form>
            </Card>
          </div>
        </div>

        {/* Sidebar */}
        <div className="space-y-4">
          {pr.status === 'Open' && (
            <Card className="p-4 space-y-3">
              <h3 className="text-sm font-medium text-gray-400 mb-1">Actions</h3>
              <button
                onClick={handleMerge}
                disabled={actionLoading}
                className="w-full flex items-center justify-center gap-2 bg-purple-700 hover:bg-purple-600 disabled:opacity-50 text-white py-2.5 rounded-lg text-sm font-medium transition-colors"
              >
                <GitMerge size={15} />
                {actionLoading ? 'Processing...' : 'Merge Pull Request'}
              </button>
              <button
                onClick={handleClose}
                disabled={actionLoading}
                className="w-full flex items-center justify-center gap-2 bg-gray-800 hover:bg-gray-700 disabled:opacity-50 text-gray-300 py-2.5 rounded-lg text-sm transition-colors"
              >
                <X size={15} /> Close PR
              </button>
            </Card>
          )}

          <Card className="p-4">
            <h3 className="text-sm font-medium text-gray-400 mb-3">Branches</h3>
            <div className="space-y-2 text-sm">
              <div className="flex items-center gap-2">
                <span className="text-gray-500 w-14">Source</span>
                <span className="text-blue-400 font-mono text-xs">{pr.sourceBranch}</span>
              </div>
              <div className="flex items-center gap-2">
                <span className="text-gray-500 w-14">Target</span>
                <span className="text-gray-300 font-mono text-xs">{pr.targetBranch}</span>
              </div>
            </div>
          </Card>

          <Card className="p-4">
            <h3 className="text-sm font-medium text-gray-400 mb-3">Timeline</h3>
            <div className="space-y-2 text-xs text-gray-500">
              <div>Opened {format(new Date(pr.createdAt), 'MMM d, yyyy')}</div>
              {pr.mergedAt && (
                <div className="text-purple-400">Merged {format(new Date(pr.mergedAt), 'MMM d, yyyy')}</div>
              )}
              {pr.mergeCommitHash && pr.mergeCommitHash !== 'server-merge-not-signed' && (
                <div className="font-mono text-yellow-500">{pr.mergeCommitHash.substring(0, 8)}</div>
              )}
            </div>
          </Card>
        </div>
      </div>
    </div>
  )
}
