import React, { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { FileText, Folder } from 'lucide-react'
import { commitsApi } from '../api/client'
import { Card, Spinner } from '../components/Layout'

export function FileBrowserPage() {
  const { owner, name, hash, '*': filePath } = useParams<{ owner: string; name: string; hash: string; '*': string }>()
  const [files, setFiles] = useState<any[]>([])
  const [content, setContent] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    if (!owner || !name || !hash) return
    if (filePath) {
      commitsApi.file(owner, name, hash, filePath)
        .then(setContent)
        .catch(() => setContent(null))
        .finally(() => setLoading(false))
    } else {
      commitsApi.tree(owner, name, hash)
        .then(setFiles)
        .finally(() => setLoading(false))
    }
  }, [owner, name, hash, filePath])

  if (loading) return <Spinner />

  // Build directory tree from flat file list
  const tree: Record<string, string[]> = {}
  const rootFiles: string[] = []
  files.forEach((f: any) => {
    const parts = f.path.split('/')
    if (parts.length === 1) rootFiles.push(f.path)
    else {
      const dir = parts[0]
      if (!tree[dir]) tree[dir] = []
      tree[dir].push(parts.slice(1).join('/'))
    }
  })

  return (
    <div className="space-y-5">
      <div className="text-sm text-gray-500">
        <Link to={`/${owner}/${name}`} className="hover:text-blue-400">{owner}/{name}</Link>
        {' / '}
        <Link to={`/${owner}/${name}/commits/${hash}`} className="hover:text-blue-400 font-mono">{hash?.substring(0, 8)}</Link>
        {filePath && <> / <span className="text-gray-300 font-mono">{filePath}</span></>}
      </div>

      {filePath ? (
        <Card className="overflow-hidden">
          <div className="bg-gray-800/60 px-4 py-2.5 border-b border-gray-800 flex items-center gap-2">
            <FileText size={14} className="text-gray-500" />
            <span className="text-sm font-mono text-gray-300">{filePath}</span>
          </div>
          <pre className="p-5 text-sm text-gray-300 font-mono overflow-x-auto leading-relaxed whitespace-pre-wrap break-all">
            {content ?? 'Unable to display file content.'}
          </pre>
        </Card>
      ) : (
        <Card>
          <div className="divide-y divide-gray-800">
            {Object.keys(tree).sort().map(dir => (
              <div key={dir} className="flex items-center gap-3 p-3 hover:bg-gray-800/40 transition-colors">
                <Folder size={15} className="text-blue-400 flex-shrink-0" />
                <span className="text-sm font-mono text-blue-300">{dir}/</span>
                <span className="text-xs text-gray-600">{tree[dir].length} files</span>
              </div>
            ))}
            {rootFiles.sort().map(f => (
              <Link
                key={f}
                to={`/${owner}/${name}/tree/${hash}/${f}`}
                className="flex items-center gap-3 p-3 hover:bg-gray-800/40 transition-colors"
              >
                <FileText size={15} className="text-gray-500 flex-shrink-0" />
                <span className="text-sm font-mono text-gray-300 hover:text-blue-400">{f}</span>
              </Link>
            ))}
          </div>
        </Card>
      )}
    </div>
  )
}
