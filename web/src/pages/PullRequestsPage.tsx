import React, { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { GitMerge, Plus, MessageSquare, Check, X, Circle } from 'lucide-react'
import { pullsApi, reposApi } from '../api/client'
import { useAuthStore } from '../store/authStore'
import { Card, Badge, Spinner, EmptyState } from '../components/Layout'
import { formatDistanceToNow } from 'date-fns'

// ─── List Page ────────────────────────────────────────────────────────────────

export function PullRequestsPage() {
  const { owner, name } = useParams<{ owner: string; name: string }>()
  const [prs, setPrs] = useState<any[]>([])
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<string>('Open')
  const [showForm, setShowForm] = useState(false)
  const [branches, setBranches] = useState<any[]>([])
  const [form, setForm] = useState({ title: '', description: '', sourceBranch: '', targetBranch: 'main' })
  const { user } = useAuthStore()

  const load = () => {
    if (!owner || !name) return
    Promise.all([
      pullsApi.list(owner, name, status),
      reposApi.branches(owner, name),
    ]).then(([p, b]) => { setPrs(p); setBranches(b) })
    .finally(() => setLoading(false))
  }

  useEffect(() => { load() }, [owner, name, status])

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!owner || !name) return
    await pullsApi.create(owner, name, form)
    setShowForm(false)
    setForm({ title: '', description: '', sourceBranch: '', targetBranch: 'main' })
    load()
  }

  const statusTabs = ['Open', 'Merged', 'Closed'] as const

  return (
    <div className="space-y-5">
      {/* Header */}
      <div className="flex items-center gap-3">
        <Link to={`/${owner}/${name}`} className="text-gray-500 hover:text-gray-300 text-sm">{owner}/{name}</Link>
        <span className="text-gray-700">/</span>
        <h1 className="text-white font-semibold">Pull Requests</h1>
        <div className="flex-1" />
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
          <h2 className="font-semibold text-white mb-5">Open Pull Request</h2>
          <form onSubmit={handleCreate} className="space-y-4">
            <div>
              <label className="text-sm text-gray-400 mb-1.5 block">Title</label>
              <input
                required value={form.title} onChange={e => setForm({ ...form, title: e.target.value })}
                placeholder="Describe what this PR does"
                className="w-full bg-gray-800 border border-gray-700 rounded-lg px-4 py-2.5 text-white text-sm focus:outline-none focus:border-blue-500"
              />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="text-sm text-gray-400 mb-1.5 block">Source branch</label>
                <select
                  required value={form.sourceBranch} onChange={e => setForm({ ...form, sourceBranch: e.target.value })}
                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2.5 text-white text-sm focus:outline-none focus:border-blue-500"
                >
                  <option value="">Select branch...</option>
                  {branches.map((b: any) => <option key={b.name} value={b.name}>{b.name}</option>)}
                </select>
              </div>
              <div>
                <label className="text-sm text-gray-400 mb-1.5 block">Target branch</label>
                <select
                  required value={form.targetBranch} onChange={e => setForm({ ...form, targetBranch: e.target.value })}
                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2.5 text-white text-sm focus:outline-none focus:border-blue-500"
                >
                  {branches.map((b: any) => <option key={b.name} value={b.name}>{b.name}</option>)}
                </select>
              </div>
            </div>
            <div>
              <label className="text-sm text-gray-400 mb-1.5 block">Description</label>
              <textarea
                value={form.description} onChange={e => setForm({ ...form, description: e.target.value })}
                placeholder="Describe the changes in this PR..."
                rows={4}
                className="w-full bg-gray-800 border border-gray-700 rounded-lg px-4 py-2.5 text-white text-sm focus:outline-none focus:border-blue-500 resize-none"
              />
            </div>
            <div className="flex gap-3">
              <button type="submit" className="bg-blue-600 hover:bg-blue-500 text-white px-6 py-2 rounded-lg text-sm font-medium">
                Open Pull Request
              </button>
              <button type="button" onClick={() => setShowForm(false)} className="bg-gray-800 text-gray-300 px-4 py-2 rounded-lg text-sm">
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
            className={`px-4 py-2.5 text-sm font-medium border-b-2 transition-colors -mb-px ${
              status === s ? 'border-blue-500 text-white' : 'border-transparent text-gray-500 hover:text-gray-300'
            }`}
          >
            {s === 'Open' && <Circle size={12} className="inline mr-1.5 text-green-400" />}
            {s === 'Merged' && <GitMerge size={12} className="inline mr-1.5 text-purple-400" />}
            {s === 'Closed' && <X size={12} className="inline mr-1.5 text-red-400" />}
            {s}
          </button>
        ))}
      </div>

      {/* PR list */}
      {loading ? <Spinner /> : prs.length === 0 ? (
        <EmptyState icon={<GitMerge size={40} />} title={`No ${status.toLowerCase()} pull requests`} />
      ) : (
        <Card>
          <div className="divide-y divide-gray-800">
            {prs.map((pr: any) => (
              <Link
                key={pr.id}
                to={`/${owner}/${name}/pulls/${pr.id}`}
                className="flex items-start gap-4 p-4 hover:bg-gray-800/40 transition-colors block"
              >
                <div className="mt-0.5 flex-shrink-0">
                  {pr.status === 'Open' && <Circle size={16} className="text-green-400" />}
                  {pr.status === 'Merged' && <GitMerge size={16} className="text-purple-400" />}
                  {pr.status === 'Closed' && <X size={16} className="text-red-400" />}
                </div>
                <div className="flex-1 min-w-0">
                  <div className="text-white font-medium text-sm hover:text-blue-400 transition-colors">{pr.title}</div>
                  <div className="text-xs text-gray-500 mt-1">
                    #{pr.id.substring(0, 8)} opened {formatDistanceToNow(new Date(pr.createdAt), { addSuffix: true })} ·{' '}
                    <span className="text-blue-300">{pr.sourceBranch}</span>
                    {' → '}
                    <span className="text-gray-400">{pr.targetBranch}</span>
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
  const { user } = useAuthStore()

  useEffect(() => {
    if (!owner || !name || !prId) return
    pullsApi.get(owner, name, prId).then(setPr).finally(() => setLoading(false))
  }, [owner, name, prId])

  const handleComment = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!owner || !name || !prId || !comment.trim()) return
    const updated = await pullsApi.addComment(owner, name, prId, comment)
    setPr(updated)
    setComment('')
  }

  const handleMerge = async () => {
    if (!owner || !name || !prId || !confirm('Merge this pull request?')) return
    const updated = await pullsApi.merge(owner, name, prId)
    setPr(updated)
  }

  const handleClose = async () => {
    if (!owner || !name || !prId || !confirm('Close this pull request?')) return
    const updated = await pullsApi.close(owner, name, prId)
    setPr(updated)
  }

  if (loading) return <Spinner />
  if (!pr) return null

  const statusConfig = {
    Open: { icon: <Circle size={16} className="text-green-400" />, label: 'Open', color: 'green' },
    Merged: { icon: <GitMerge size={16} className="text-purple-400" />, label: 'Merged', color: 'purple' },
    Closed: { icon: <X size={16} className="text-red-400" />, label: 'Closed', color: 'red' },
  } as Record<string, any>

  const s = statusConfig[pr.status] || statusConfig.Open

  return (
    <div className="space-y-6">
      {/* Breadcrumb */}
      <div className="text-sm text-gray-500">
        <Link to={`/${owner}/${name}/pulls`} className="hover:text-blue-400">Pull Requests</Link>
        {' / '}
        <span className="text-gray-300">{pr.title}</span>
      </div>

      {/* PR header */}
      <div>
        <div className="flex items-start gap-3 mb-3">
          {s.icon}
          <h1 className="text-xl font-bold text-white">{pr.title}</h1>
        </div>
        <div className="flex items-center gap-3 flex-wrap text-sm text-gray-500">
          <Badge color={s.color}>{pr.status}</Badge>
          <span>
            <span className="text-blue-400">{pr.sourceBranch}</span>
            {' → '}
            <span className="text-gray-400">{pr.targetBranch}</span>
          </span>
          <span>opened {formatDistanceToNow(new Date(pr.createdAt), { addSuffix: true })}</span>
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

          {/* Comments */}
          <div className="space-y-4">
            {pr.comments?.map((c: any) => (
              <Card key={c.id} className="p-4">
                <div className="flex items-center gap-2 mb-3">
                  <div className="w-7 h-7 bg-blue-700 rounded-full flex items-center justify-center text-xs font-bold text-white">
                    {c.authorPublicKey?.[0] || '?'}
                  </div>
                  <span className="text-sm text-gray-400 font-mono text-xs">{c.authorPublicKey?.substring(0, 12)}...</span>
                  <span className="text-xs text-gray-600">{formatDistanceToNow(new Date(c.createdAt), { addSuffix: true })}</span>
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
                  <MessageSquare size={14} className="text-gray-500" />
                  <span className="text-sm font-medium text-gray-300">Leave a comment</span>
                </div>
                <textarea
                  value={comment} onChange={e => setComment(e.target.value)}
                  placeholder="Write a comment..."
                  rows={3}
                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-4 py-3 text-white text-sm focus:outline-none focus:border-blue-500 resize-none"
                />
                <div className="flex justify-end">
                  <button type="submit" disabled={!comment.trim()} className="bg-blue-600 hover:bg-blue-500 disabled:opacity-50 text-white px-4 py-2 rounded-lg text-sm font-medium">
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
              <button
                onClick={handleMerge}
                className="w-full flex items-center justify-center gap-2 bg-purple-700 hover:bg-purple-600 text-white py-2.5 rounded-lg text-sm font-medium transition-colors"
              >
                <GitMerge size={15} /> Merge Pull Request
              </button>
              <button
                onClick={handleClose}
                className="w-full flex items-center justify-center gap-2 bg-gray-800 hover:bg-gray-700 text-gray-300 py-2.5 rounded-lg text-sm transition-colors"
              >
                <X size={15} /> Close PR
              </button>
            </Card>
          )}

          <Card className="p-4">
            <h3 className="text-sm font-medium text-gray-400 mb-3">Branches</h3>
            <div className="space-y-2 text-sm">
              <div><span className="text-gray-500">Source:</span> <span className="text-blue-400 font-mono">{pr.sourceBranch}</span></div>
              <div><span className="text-gray-500">Target:</span> <span className="text-gray-300 font-mono">{pr.targetBranch}</span></div>
            </div>
          </Card>

          {pr.mergedAt && (
            <Card className="p-4">
              <h3 className="text-sm font-medium text-gray-400 mb-2">Merged</h3>
              <p className="text-xs text-gray-500">{formatDistanceToNow(new Date(pr.mergedAt), { addSuffix: true })}</p>
            </Card>
          )}
        </div>
      </div>
    </div>
  )
}
