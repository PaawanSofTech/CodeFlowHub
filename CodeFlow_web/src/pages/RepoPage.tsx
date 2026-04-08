import React, { useEffect, useState, useCallback } from 'react'
import { Link, useParams, useNavigate } from 'react-router-dom'
import { GitBranch, GitCommit, GitMerge, Tag, FileText, Trash2, Plus, RefreshCw } from 'lucide-react'
import { reposApi, commitsApi } from '../api/client'
import { Card, Badge, Spinner, HashChip, EmptyState, LiveBadge, Toast } from '../components/Layout'
import { useRepoSocket } from '../hooks/useRepoSocket'
import { formatDistanceToNow } from 'date-fns'
import { getBranchesWithCommits } from '../api/repo'
import { Clipboard, Check } from "lucide-react";

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
  const [toast, setToast] = useState<{ message: string; type: 'success' | 'info' | 'error' } | null>(null)
  const navigate = useNavigate()
  const [selectedBranch, setSelectedBranch] = useState<string>()
  const [branchCommits, setBranchCommits] = useState<any[]>([])
  const cloneText = `codeflow clone ${window.location.host}/${owner}/${name}`;
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(cloneText);
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    } catch (err) {
      console.error("Copy failed", err);
    }
  };

  const load = useCallback(async () => {
    if (!owner || !name) return
    try {
      const [r, s, b, t] = await Promise.all([
        reposApi.get(owner, name),
        reposApi.stats(owner, name),
        reposApi.branches(owner, name),
        reposApi.tags(owner, name),
      ])

      setRepo(r)
      setStats(s)
      setBranches(b)
      setTags(t)
    } catch {
      navigate('/')
    } finally {
      setLoading(false)
    }
  }, [owner, name, navigate])

  useEffect(() => { load() }, [load])
  useEffect(() => {
    if (!branchCommits || branchCommits.length === 0) return;

    const mainBranch = branchCommits.find(b => b.branch === "main");

    if (mainBranch) {
      setSelectedBranch("main");
    } else {
      // fallback (in case repo uses "master" or something else)
      setSelectedBranch(branchCommits[0].branch);
    }
  }, [branchCommits]);

  useEffect(() => {
    if (!owner || !name) return

    getBranchesWithCommits(owner, name)
      .then(data => setBranchCommits(data))
      .catch(() => { })
  }, [owner, name])

  const selectedBranchData = branchCommits?.find(
    (b: any) => b.branch === selectedBranch
  )

  const selectedHeadHash =
    branchCommits.find(b => b.branch === selectedBranch)?.head


  useEffect(() => {
    if (!owner || !name || !selectedBranch || branchCommits.length === 0) return;

    commitsApi.list(owner, name, {
      limit: 15,
      branch: selectedBranch
    })
      .then(setCommits)
      .catch(() => { });
  }, [owner, name, selectedBranch, branchCommits]);

  // ── Real-time: listen for CLI pushes ──────────────────────────────────────
  useRepoSocket({
    owner: owner || '',
    name: name || '',
    onEvent: (event) => {
      if (event.type === 'PushReceived') {
        setToast({ message: `New push on "${event.branch}" — ${event.commitHash.substring(0, 8)}`, type: 'success' })
        // Silently refresh commits + stats
        Promise.all([
          commitsApi.list(owner!, name!, { limit: 15 }),
          reposApi.stats(owner!, name!),
        ]).then(([c, s]) => { setCommits(c); setStats(s) })
      }
    },
  })

  const handleCreateBranch = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!owner || !name || !newBranch.trim()) return
    try {
      await reposApi.createBranch(owner, name, newBranch.trim())
      const fresh = await reposApi.branches(owner, name)
      setBranches(fresh)
      setNewBranch('')
      setShowBranchForm(false)
      setToast({ message: `Branch "${newBranch}" created`, type: 'success' })
    } catch (err: any) {
      setToast({ message: err.response?.data?.error || 'Failed to create branch', type: 'error' })
    }
  }

  const handleDeleteBranch = async (branch: string) => {
    if (!owner || !name || !confirm(`Delete branch "${branch}"?`)) return
    try {
      await reposApi.deleteBranch(owner, name, branch)
      setBranches(branches.filter((b: any) => b.name !== branch))
      setToast({ message: `Branch "${branch}" deleted`, type: 'info' })
    } catch (err: any) {
      setToast({ message: err.response?.data?.error || 'Failed to delete branch', type: 'error' })
    }
  }

  if (loading) return <Spinner />
  if (!repo) return null

  const tabs = [
    { id: 'overview', label: 'Code', icon: <FileText size={14} /> },
    { id: 'branches', label: `Branches`, count: branches.length, icon: <GitBranch size={14} /> },
    { id: 'tags', label: 'Tags', count: tags.length, icon: <Tag size={14} /> },
  ] as const

  // Build file tree from head commit if available
  const headHash = stats?.head

  return (
    <div className="space-y-6">
      {toast && (
        <Toast message={toast.message} type={toast.type} onDismiss={() => setToast(null)} />
      )}

      {/* ================= HEADER ================= */}
      <div className="bg-gray-900/40 border border-gray-800 rounded-xl p-5 space-y-4">

        {/* Top Row */}
        <div className="flex items-center justify-between flex-wrap gap-3">
          <div className="flex items-center gap-2 text-sm">
            <Link to="/" className="text-gray-500 hover:text-blue-400 transition">
              {owner}
            </Link>
            <span className="text-gray-600">/</span>
            <span className="text-white font-semibold text-lg">{name}</span>
            <Badge color="gray">public</Badge>
          </div>

          {stats?.currentBranch && (
            <Badge color="green">{stats.currentBranch}</Badge>
          )}
        </div>

        {/* Stats */}
        <div className="flex items-center gap-4 flex-wrap">
          <StatChip icon={<GitCommit size={13} />} value={stats?.totalCommits ?? 0} label="commits" />
          <StatChip icon={<GitBranch size={13} />} value={stats?.totalBranches ?? 0} label="branches" />
          <StatChip icon={<Tag size={13} />} value={tags.length} label="tags" />
          {stats?.head && <HashChip hash={stats.head} />}
          <LiveBadge />
        </div>

        {/* Clone + Branch + Actions */}
        <div className="flex items-center gap-3 flex-wrap">

          {/* Clone */}
          <div className="bg-gray-800/60 border border-gray-700 rounded-lg px-3 py-2 flex items-center justify-between gap-2 text-sm font-mono">
            <div className="flex items-center gap-2 overflow-hidden">
              <span className="text-gray-500 text-xs">clone</span>
              <code className="text-blue-300 truncate max-w-xs">
                {cloneText}
              </code>
            </div>

            <button
              onClick={handleCopy}
              className="text-gray-400 hover:text-white transition"
              title="Copy"
            >
              {copied ? <Check size={16} /> : <Clipboard size={16} />}
            </button>
          </div>

          {/* Branch Selector */}
          <div className="relative">
            <select
              value={selectedBranch || ''}
              onChange={(e) => setSelectedBranch(e.target.value)}
              className="
              appearance-none
              bg-gray-800
              border border-gray-700
              text-gray-200 text-sm font-medium
              pl-3 pr-8 py-1.5
              rounded-md
              hover:bg-gray-700
              focus:outline-none focus:ring-1 focus:ring-blue-500
              transition
              cursor-pointer
            "
            >
              {branchCommits.map((b: any) => (
                <option key={b.branch} value={b.branch}>
                  {b.branch}
                </option>
              ))}
            </select>

            <div className="absolute inset-y-0 right-2 flex items-center text-gray-500 text-xs pointer-events-none">
              ▼
            </div>
          </div>

          {/* Actions */}
          <div className="flex items-center gap-2">
            <Link
              to={`/${owner}/${name}/commits`}
              className="flex items-center gap-2 text-sm bg-gray-800 hover:bg-gray-700 text-gray-300 px-3 py-1.5 rounded-md border border-gray-700 transition"
            >
              <GitCommit size={14} /> Commits
            </Link>

            <Link
              to={`/${owner}/${name}/pulls`}
              className="flex items-center gap-2 text-sm bg-gray-800 hover:bg-gray-700 text-gray-300 px-3 py-1.5 rounded-md border border-gray-700 transition"
            >
              <GitMerge size={14} /> Pull Requests
            </Link>

            {headHash && (
              <Link
                to={`/${owner}/${name}/tree/${selectedHeadHash}`}
                className="flex items-center gap-2 text-sm bg-gray-800 hover:bg-gray-700 text-gray-300 px-3 py-1.5 rounded-md border border-gray-700 transition"
              >
                <FileText size={14} /> Browse Files
              </Link>
            )}
          </div>
        </div>
      </div>

      {/* ================= TABS ================= */}
      <div className="border-b border-gray-800 flex gap-1">
        {tabs.map(t => (
          <button
            key={t.id}
            onClick={() => setTab(t.id)}
            className={`flex items-center gap-1.5 px-4 py-2.5 text-sm font-medium border-b-2 transition -mb-px ${tab === t.id
              ? 'border-blue-500 text-white'
              : 'border-transparent text-gray-500 hover:text-gray-300'
              }`}
          >
            {t.icon} {t.label}
            {'count' in t && t.count !== undefined && (
              <span className="text-xs bg-gray-800 text-gray-500 px-1.5 py-0.5 rounded-full ml-1">
                {t.count}
              </span>
            )}
          </button>
        ))}
      </div>

      {/* Tab: Code / Recent commits */}
      {tab === 'overview' && (
        <div className="space-y-4">
          <div className="flex items-center justify-between">
            <h2 className="font-semibold text-gray-300 text-sm">Recent Commits</h2>
            <button
              onClick={() => load()}
              className="flex items-center gap-1 text-xs text-gray-500 hover:text-gray-300 transition-colors"
            >
              <RefreshCw size={11} /> refresh
            </button>
          </div>

          {commits.length === 0 ? (
            <EmptyState
              icon={<GitCommit size={40} />}
              title="No commits yet"
              description="Push your first commit: codeflow push"
            />
          ) : (
            <Card>
              <div className="divide-y divide-gray-800">
                {commits.map((c: any) => (
                  <div key={c.hash} className="flex items-start gap-3 p-4 hover:bg-gray-800/50 transition-colors">
                    <div className={`mt-1.5 w-2 h-2 rounded-full flex-shrink-0 ${c.isMerge ? 'bg-purple-400' : 'bg-blue-400'}`} />
                    <div className="flex-1 min-w-0">
                      <Link
                        to={`/${owner}/${name}/commits/${c.hash}`}
                        className="text-white hover:text-blue-400 font-medium text-sm line-clamp-1 transition-colors"
                      >
                        {c.message}
                      </Link>
                      <div className="flex items-center gap-2 mt-1 text-xs text-gray-500 flex-wrap">
                        <span className="text-gray-400">{c.author}</span>
                        <span>·</span>
                        <span>{formatDistanceToNow(new Date(c.timestamp), { addSuffix: true })}</span>
                        <span>·</span>
                        <HashChip hash={c.hash} />
                        {c.isMerge && <Badge color="purple">merge</Badge>}
                        {c.branch && <Badge color="gray">{c.branch}</Badge>}
                      </div>
                    </div>
                    {c.changes?.length > 0 && (
                      <span className="text-xs text-gray-600 flex-shrink-0">{c.changes.length} files</span>
                    )}
                  </div>
                ))}
              </div>
              <div className="p-3 border-t border-gray-800 text-center">
                <Link to={`/${owner}/${name}/commits`} className="text-sm text-blue-400 hover:text-blue-300 transition-colors">
                  View all {stats?.totalCommits} commits →
                </Link>
              </div>
            </Card>
          )}
        </div>
      )}

      {/* Tab: Branches */}
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
                <button type="button" onClick={() => setShowBranchForm(false)} className="bg-gray-800 text-gray-400 px-4 py-2 rounded-lg text-sm">Cancel</button>
              </form>
            </Card>
          )}

          <Card>
            <div className="divide-y divide-gray-800">
              {branches.length === 0 ? (
                <div className="p-8 text-center text-gray-600 text-sm">No branches yet</div>
              ) : branches.map((b: any) => (
                <div key={b.name} className="flex items-center gap-3 p-4 group">
                  <GitBranch size={14} className="text-gray-500 flex-shrink-0" />
                  <span className={`text-sm font-medium ${b.name === stats?.currentBranch ? 'text-green-400' : 'text-white'}`}>
                    {b.name}
                    {b.name === stats?.currentBranch && <span className="ml-2 text-xs text-gray-500">(default)</span>}
                  </span>
                  {b.tip && <HashChip hash={b.tip} />}
                  <div className="flex-1" />
                  {b.name !== stats?.currentBranch && (
                    <button
                      onClick={() => handleDeleteBranch(b.name)}
                      className="opacity-0 group-hover:opacity-100 p-1.5 text-gray-600 hover:text-red-400 transition-all rounded"
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

      {/* Tab: Tags */}
      {tab === 'tags' && (
        <div className="space-y-3">
          {tags.length === 0 ? (
            <EmptyState icon={<Tag size={40} />} title="No tags yet" description="Tag a commit: codeflow tag v1.0" />
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
