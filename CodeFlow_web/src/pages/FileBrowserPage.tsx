import React, { useEffect, useState } from 'react'
import { Link, useParams, useNavigate } from 'react-router-dom'
import { FileText, Folder, ChevronRight, ArrowLeft, Copy, Check } from 'lucide-react'
import { commitsApi } from '../api/client'
import { Card, Spinner } from '../components/Layout'

interface TreeNode {
  name: string
  path: string
  type: 'file' | 'dir'
  children?: Record<string, TreeNode>
}

function buildTree(files: { path: string }[], prefix: string): TreeNode[] {
  const nodes: Record<string, TreeNode> = {}

  files.forEach(f => {
    const rel = prefix ? f.path.slice(prefix.length) : f.path
    const parts = rel.split('/').filter(Boolean)
    if (parts.length === 0) return

    const topName = parts[0]
    const fullPath = prefix ? `${prefix}${topName}` : topName

    if (parts.length === 1) {
      // File
      if (!nodes[topName]) {
        nodes[topName] = { name: topName, path: f.path, type: 'file' }
      }
    } else {
      // Directory
      if (!nodes[topName]) {
        nodes[topName] = { name: topName, path: fullPath + '/', type: 'dir', children: {} }
      }
    }
  })

  // Sort: dirs first, then files alphabetically
  return Object.values(nodes).sort((a, b) => {
    if (a.type !== b.type) return a.type === 'dir' ? -1 : 1
    return a.name.localeCompare(b.name)
  })
}

function getFilesUnderDir(files: { path: string }[], dirPath: string) {
  return files.filter(f => f.path.startsWith(dirPath))
}

function getExtension(filename: string): string {
  return filename.split('.').pop()?.toLowerCase() || ''
}

function getLanguageClass(ext: string): string {
  const map: Record<string, string> = {
    ts: 'TypeScript', tsx: 'TSX', js: 'JavaScript', jsx: 'JSX',
    cs: 'C#', py: 'Python', rs: 'Rust', go: 'Go',
    json: 'JSON', md: 'Markdown', yml: 'YAML', yaml: 'YAML',
    html: 'HTML', css: 'CSS', sh: 'Shell', txt: 'Text',
  }
  return map[ext] || ext.toUpperCase() || 'Plain'
}

export function FileBrowserPage() {
  const { owner, name, hash, '*': filePath } = useParams<{
    owner: string; name: string; hash: string; '*': string
  }>()
  const navigate = useNavigate()
  const [allFiles, setAllFiles] = useState<{ path: string }[]>([])
  const [content, setContent] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [copied, setCopied] = useState(false)

  const isFile = filePath && !filePath.endsWith('/')
  const currentDir = !isFile ? (filePath || '') : ''

  useEffect(() => {
    if (!owner || !name || !hash) return
    setLoading(true)

    if (isFile) {
      // Viewing a file
      commitsApi.file(owner, name, hash, filePath!)
        .then(setContent)
        .catch(() => setContent(null))
        .finally(() => setLoading(false))
    } else {
      // Browsing directory — always load full tree
      commitsApi.tree(owner, name, hash)
        .then(setAllFiles)
        .finally(() => setLoading(false))
    }
  }, [owner, name, hash, filePath, isFile])

  const handleCopy = async () => {
    if (!content) return
    await navigator.clipboard.writeText(content)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }

  if (loading) return <Spinner />

  const filesInDir = currentDir
    ? getFilesUnderDir(allFiles, currentDir)
    : allFiles

  const treeNodes = isFile ? [] : buildTree(filesInDir, currentDir)

  // Build breadcrumb parts
  const pathParts = filePath ? filePath.split('/').filter(Boolean) : []

  return (
    <div className="space-y-4">
      {/* Breadcrumb */}
      <div className="text-sm text-gray-500 flex items-center gap-1 flex-wrap">
        <Link to={`/${owner}/${name}`} className="hover:text-blue-400 transition-colors">{owner}/{name}</Link>
        <ChevronRight size={12} className="text-gray-700" />
        <Link to={`/${owner}/${name}/tree/${hash}`} className="hover:text-blue-400 transition-colors font-mono">
          {hash!.substring(0, 8)}
        </Link>
        {pathParts.map((part, i) => {
          const partPath = pathParts.slice(0, i + 1).join('/')
          const isLast = i === pathParts.length - 1
          return (
            <React.Fragment key={partPath}>
              <ChevronRight size={12} className="text-gray-700" />
              {isLast ? (
                <span className="text-gray-300 font-mono">{part}</span>
              ) : (
                <Link
                  to={`/${owner}/${name}/tree/${hash}/${partPath}/`}
                  className="hover:text-blue-400 transition-colors font-mono"
                >
                  {part}
                </Link>
              )}
            </React.Fragment>
          )
        })}
      </div>

      {isFile ? (
        /* ── File viewer ─────────────────────────────────────────────── */
        <Card className="overflow-hidden">
          <div className="bg-gray-800/60 px-4 py-2.5 border-b border-gray-800 flex items-center gap-3">
            <FileText size={14} className="text-gray-500" />
            <span className="text-sm font-mono text-gray-300 flex-1">{filePath}</span>
            <span className="text-xs text-gray-600 bg-gray-800 px-2 py-0.5 rounded">
              {getLanguageClass(getExtension(filePath!))}
            </span>
            {content && (
              <>
                <span className="text-xs text-gray-600">
                  {content.split('\n').length} lines
                </span>
                <button
                  onClick={handleCopy}
                  className="flex items-center gap-1 text-xs text-gray-500 hover:text-gray-300 transition-colors"
                >
                  {copied ? <Check size={12} className="text-green-400" /> : <Copy size={12} />}
                  {copied ? 'Copied' : 'Copy'}
                </button>
              </>
            )}
            <Link
              to={`/${owner}/${name}/tree/${hash}${currentDir ? `/${currentDir}` : ''}`}
              className="flex items-center gap-1 text-xs text-gray-500 hover:text-gray-300 transition-colors"
            >
              <ArrowLeft size={12} /> Back
            </Link>
          </div>
          <pre className="p-5 text-sm text-gray-300 font-mono overflow-x-auto leading-relaxed whitespace-pre-wrap break-all max-h-[70vh] overflow-y-auto">
            {content ?? (
              <span className="text-gray-600 italic">Unable to display file content (binary or too large)</span>
            )}
          </pre>
        </Card>
      ) : (
        /* ── Directory browser ──────────────────────────────────────── */
        <Card>
          {/* Back button when in subdir */}
          {currentDir && (
            <div
              className="flex items-center gap-3 p-3 border-b border-gray-800 hover:bg-gray-800/40 transition-colors cursor-pointer"
              onClick={() => {
                const parts = currentDir.split('/').filter(Boolean)
                parts.pop()
                const parent = parts.length ? parts.join('/') + '/' : ''
                navigate(`/${owner}/${name}/tree/${hash}${parent ? `/${parent}` : ''}`)
              }}
            >
              <ArrowLeft size={14} className="text-gray-500" />
              <span className="text-sm text-gray-400">..</span>
            </div>
          )}

          <div className="divide-y divide-gray-800">
            {treeNodes.length === 0 ? (
              <div className="p-8 text-center text-gray-600 text-sm">Empty directory</div>
            ) : treeNodes.map(node => (
              node.type === 'dir' ? (
                <Link
                  key={node.path}
                  to={`/${owner}/${name}/tree/${hash}/${node.path}`}
                  className="flex items-center gap-3 p-3 hover:bg-gray-800/40 transition-colors group"
                >
                  <Folder size={15} className="text-blue-400 flex-shrink-0" />
                  <span className="text-sm font-mono text-blue-300 group-hover:text-blue-200 flex-1">
                    {node.name}/
                  </span>
                  <span className="text-xs text-gray-700">
                    {getFilesUnderDir(allFiles, node.path).length} files
                  </span>
                  <ChevronRight size={13} className="text-gray-700 group-hover:text-gray-500" />
                </Link>
              ) : (
                <Link
                  key={node.path}
                  to={`/${owner}/${name}/tree/${hash}/${node.path}`}
                  className="flex items-center gap-3 p-3 hover:bg-gray-800/40 transition-colors group"
                >
                  <FileText size={15} className="text-gray-500 flex-shrink-0" />
                  <span className="text-sm font-mono text-gray-300 group-hover:text-blue-400 flex-1 transition-colors">
                    {node.name}
                  </span>
                  <span className="text-xs text-gray-700 opacity-0 group-hover:opacity-100 transition-opacity">
                    {getLanguageClass(getExtension(node.name))}
                  </span>
                </Link>
              )
            ))}
          </div>
        </Card>
      )}
    </div>
  )
}
