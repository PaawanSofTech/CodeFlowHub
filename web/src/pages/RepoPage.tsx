import React, { useEffect, useState } from 'react'
import { Link, useParams, useNavigate } from 'react-router-dom'
import { GitBranch, GitCommit, GitMerge, Tag, FileText, Trash2, Plus } from 'lucide-react'
import { reposApi, commitsApi } from '../api/client'
import { Card, Badge, Spinner, HashChip, EmptyState } from '../components/Layout'
import { formatDistanceToNow } from 'date-fns'

export function RepoPage() {
  const { owner, name } = useParams<{ owner: string; name: string }>()
  const [repo, setRepo] = useState<any>(null)
  const [stats, setStats] = useState<any>(null)
  const [commits, setCommits] = useState<any[]>([])
  const [branches, setBranches] = useState<any[]>([])
  const [tags, setTags] = useState<any[]>([])
  const [loading, setLoading] = useState(true)
  const [tab, setTab] = useState<'overview' | 'branches' | 'tags'>('overview')
  const [showBranchForm, setShowBranchForm] = useState(false)
  const [newBranch, setNewBranch] = useState('')
  const navigate = useNavigate()

  useEffect(() => {
    if (!owner || !name) return
    Promise.all([
      reposApi.get(owner, name),
      reposApi.stats(owner, name),
      commitsApi.list(owner, name, { limit: 10 }),
      reposApi.branches(owner, name),
      reposApi.tags(owner, name),
    ]).then(([r, s, c, b, t]) => {
      setRepo(r); setStats(s); setCommits(c); setBranches(b); setTags(t)
    }).catch(() => navigate('/'))
    .finally(() => setLoading(false))
  }, [owner, name])

  const handleCreateBranch = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!owner || !name || !newBranch.trim()) return
    await reposApi.createBranch(owner, name, newBranch.trim())
    const fresh = await reposApi.branches(owner, name)
    setBranches(fresh)
    setNewBranch('')
    setShowBranchForm(false)
  }

  const handleDeleteBranch = async (branch: string) => {
    if (!owner || !name || !confirm(`Delete branch '${branch}'?`)) return
    await reposApi.deleteBranch(owner, name, branch)
    setBranches(branches.filter((b: any) => b.name !== branch))
  }

  if (loading) return <Spinner />
  if (!repo) return null

  const tabs = [
    { id: 'overview', label: 'Overview', icon: <FileText size={14} /> },
    { id: 'branches', label: `Branches (${branches.length})`, icon: <GitBranch size={14} /> },
    { id: 'tags', label: `Tags (${tags.length})`, icon: <Tag size={14} /> },
  ] as const

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <div className="flex items-center gap-2 text-gray-500 text-sm mb-1">
          <Link to="/" className="hover:text-blue-400">{owner}</Link>
          <span>/</span>
          <span className="text-white font-semibold text-xl">{name}</span>
        </div>

        {/* Stats row */}
        <div className="flex items-center gap-4 mt-3 flex-wrap">
          <StatChip icon={<GitCommit size={13} />} value={stats?.totalCommits ?? 0} label="commits" />
          <StatChip icon={<GitBranch size={13} />} value={stats?.totalBranches ?? 0} label="branches" />
          <StatChip icon={<Tag size={13} />} value={tags.length} label="tags" />
          {stats?.currentBranch && <Badge color="green">{stats.currentBranch}</Badge>}
          {stats?.head && <HashChip hash={stats.head} />}
        </div>

        {/* Action buttons */}
        <div className="flex items-center gap-3 mt-4">
          <Link
            to={`/${owner}/${name}/commits`}
            className="flex items-center gap-2 text-sm bg-gray-800 hover:bg-gray-700 text-gray-300 px-3 py-2 rounded-lg border border-gray-700 transition-colors"
          >
            <GitCommit size={14} /> View Commits
          </Link>
          <Link
            to={`/${owner}/${name}/pulls`}
            className="flex items-center gap-2 text-sm bg-gray-800 hover:bg-gray-700 text-gray-300 px-3 py-2 rounded-lg border border-gray-700 transition-colors"
          >
            <GitMerge size={14} /> Pull Requests
          </Link>
        </div>
      </div>

      {/* Tabs */}
      <div className="border-b border-gray-800 flex gap-1">
        {tabs.map(t => (
          <button
            key={t.id}
            onClick={() => setTab(t.id)}
            className={`flex items-center gap-1.5 px-4 py-2.5 text-sm font-medium border-b-2 transition-colors -mb-px ${
              tab === t.id
                ? 'border-blue-500 text-white'
                : 'border-transparent text-gray-500 hover:text-gray-300'
            }`}
          >
            {t.icon} {t.label}
          </button>
        ))}
      </div>

      {/* Tab content */}
      {tab === 'overview' && (
        <div className="space-y-4">
          <h2 className="font-semibold text-gray-300">Recent Commits</h2>
          {commits.length === 0 ? (
            <EmptyState icon={<GitCommit size={40} />} title="No commits yet" description="Start by adding files and committing." />
          ) : (
            <Card>
              <div className="divide-y divide-gray-800">
                {commits.map((c: any) => (
                  <div key={c.hash} className="flex items-start gap-3 p-4 hover:bg-gray-800/50 transition-colors">
                    <div className={`mt-1 w-2 h-2 rounded-full flex-shrink-0 ${c.isMerge ? 'bg-purple-400' : 'bg-blue-400'}`} />
                    <div className="flex-1 min-w-0">
                      <Link
                        to={`/${owner}/${name}/commits/${c.hash}`}
                        className="text-white hover:text-blue-400 font-medium text-sm line-clamp-1"
                      >
                        {c.message}
                      </Link>
                      <div className="flex items-center gap-2 mt-1 text-xs text-gray-500">
                        <span>{c.author}</span>
                        <span>·</span>
                        <span>{formatDistanceToNow(new Date(c.timestamp), { addSuffix: true })}</span>
                        <span>·</span>
                        <HashChip hash={c.hash} />
                        {c.isMerge && <Badge color="purple">merge</Badge>}
                        <Badge color="gray">{c.branch}</Badge>
                      </div>
                    </div>
                    {c.changes?.length > 0 && (
                      <span className="text-xs text-gray-600 flex-shrink-0">{c.changes.length} files</span>
                    )}
                  </div>
                ))}
              </div>
              <div className="p-3 border-t border-gray-800 text-center">
                <Link to={`/${owner}/${name}/commits`} className="text-sm text-blue-400 hover:text-blue-300">
                  View all commits →
                </Link>
              </div>
            </Card>
          )}
        </div>
      )}

      {tab === 'branches' && (
        <div className="space-y-4">
          <div className="flex items-center justify-between">
            <h2 className="font-semibold text-gray-300">Branches</h2>
            <button
              onClick={() => setShowBranchForm(!showBranchForm)}
              className="flex items-center gap-1.5 text-sm bg-blue-600 hover:bg-blue-500 text-white px-3 py-1.5 rounded-lg transition-colors"
            >
              <Plus size={13} /> New Branch
            </button>
          </div>

          {showBranchForm && (
            <Card className="p-4">
              <form onSubmit={handleCreateBranch} className="flex gap-3">
                <input
                  autoFocus value={newBranch} onChange={e => setNewBranch(e.target.value)}
                  placeholder="branch-name"
                  className="flex-1 bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white text-sm focus:outline-none focus:border-blue-500"
                />
                <button type="submit" className="bg-blue-600 text-white px-4 py-2 rounded-lg text-sm">Create</button>
              </form>
            </Card>
          )}

          <Card>
            <div className="divide-y divide-gray-800">
              {branches.map((b: any) => (
                <div key={b.name} className="flex items-center gap-3 p-4">
                  <GitBranch size={14} className="text-gray-500" />
                  <span className={`text-sm font-medium ${b.name === stats?.currentBranch ? 'text-green-400' : 'text-white'}`}>
                    {b.name}
                    {b.name === stats?.currentBranch && <span className="ml-2 text-xs text-gray-500">(default)</span>}
                  </span>
                  <HashChip hash={b.tip} />
                  <div className="flex-1" />
                  {b.name !== stats?.currentBranch && (
                    <button
                      onClick={() => handleDeleteBranch(b.name)}
                      className="p-1.5 text-gray-600 hover:text-red-400 transition-colors rounded"
                    >
                      <Trash2 size={13} />
                    </button>
                  )}
                </div>
              ))}
            </div>
          </Card>
        </div>
      )}

      {tab === 'tags' && (
        <div className="space-y-3">
          {tags.length === 0 ? (
            <EmptyState icon={<Tag size={40} />} title="No tags yet" description="Tag a commit with: codeflow tag v1.0" />
          ) : (
            <Card>
              <div className="divide-y divide-gray-800">
                {tags.map((t: any) => (
                  <div key={t.name} className="flex items-center gap-3 p-4">
                    <Tag size={14} className="text-gray-500" />
                    <span className="text-sm font-medium text-white">{t.name}</span>
                    <HashChip hash={t.commit} />
                  </div>
                ))}
              </div>
            </Card>
          )}
        </div>
      )}
    </div>
  )
}

function StatChip({ icon, value, label }: { icon: React.ReactNode; value: number; label: string }) {
  return (
    <div className="flex items-center gap-1.5 text-sm text-gray-400">
      {icon}
      <span className="font-semibold text-white">{value}</span>
      <span>{label}</span>
    </div>
  )
}
