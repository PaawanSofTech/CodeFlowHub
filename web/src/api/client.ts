import axios from 'axios'
import { useAuthStore } from '../store/authStore'

const BASE = import.meta.env.VITE_API_URL || 'http://localhost:5000'

export const api = axios.create({ baseURL: BASE })

api.interceptors.request.use((config) => {
  const token = useAuthStore.getState().token
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

api.interceptors.response.use(
  (r) => r,
  (err) => {
    if (err.response?.status === 401) useAuthStore.getState().logout()
    return Promise.reject(err)
  }
)

// ─── Auth ────────────────────────────────────────────────────────────────────
export const authApi = {
  login: (publicKeyBase64: string, username: string, email: string) =>
    api.post('/api/auth/login', { publicKeyBase64, signedChallenge: '', username, email }).then(r => r.data),
  me: () => api.get('/api/auth/me').then(r => r.data),
}

// ─── Repos ───────────────────────────────────────────────────────────────────
export const reposApi = {
  list: () => api.get('/api/repos').then(r => r.data),
  get: (owner: string, name: string) => api.get(`/api/repos/${owner}/${name}`).then(r => r.data),
  create: (owner: string, name: string, description = '') =>
    api.post(`/api/repos/${owner}`, { name, description }).then(r => r.data),
  delete: (owner: string, name: string) => api.delete(`/api/repos/${owner}/${name}`),
  stats: (owner: string, name: string) => api.get(`/api/repos/${owner}/${name}/stats`).then(r => r.data),
  branches: (owner: string, name: string) => api.get(`/api/repos/${owner}/${name}/branches`).then(r => r.data),
  tags: (owner: string, name: string) => api.get(`/api/repos/${owner}/${name}/tags`).then(r => r.data),
  createBranch: (owner: string, name: string, branchName: string, fromHash?: string) =>
    api.post(`/api/repos/${owner}/${name}/branches`, { name: branchName, fromHash }).then(r => r.data),
  deleteBranch: (owner: string, name: string, branch: string) =>
    api.delete(`/api/repos/${owner}/${name}/branches/${branch}`),
}

// ─── Commits ──────────────────────────────────────────────────────────────────
export const commitsApi = {
  list: (owner: string, name: string, opts?: { branch?: string; author?: string; limit?: number }) =>
    api.get(`/api/repos/${owner}/${name}/commits`, { params: opts }).then(r => r.data),
  get: (owner: string, name: string, hash: string) =>
    api.get(`/api/repos/${owner}/${name}/commits/${hash}`).then(r => r.data),
  diff: (owner: string, name: string, hash: string) =>
    api.get(`/api/repos/${owner}/${name}/commits/${hash}/diff`).then(r => r.data),
  tree: (owner: string, name: string, hash: string) =>
    api.get(`/api/repos/${owner}/${name}/commits/${hash}/tree`).then(r => r.data),
  file: (owner: string, name: string, hash: string, filePath: string) =>
    api.get(`/api/repos/${owner}/${name}/commits/${hash}/files/${filePath}`, { responseType: 'text' }).then(r => r.data),
}

// ─── Pull Requests ────────────────────────────────────────────────────────────
export const pullsApi = {
  list: (owner: string, name: string, status?: string) =>
    api.get(`/api/repos/${owner}/${name}/pulls`, { params: { status } }).then(r => r.data),
  get: (owner: string, name: string, prId: string) =>
    api.get(`/api/repos/${owner}/${name}/pulls/${prId}`).then(r => r.data),
  create: (owner: string, name: string, body: { title: string; description: string; sourceBranch: string; targetBranch: string }) =>
    api.post(`/api/repos/${owner}/${name}/pulls`, body).then(r => r.data),
  addComment: (owner: string, name: string, prId: string, body: string, filePath?: string, lineNumber?: number) =>
    api.post(`/api/repos/${owner}/${name}/pulls/${prId}/comments`, { body, filePath, lineNumber }).then(r => r.data),
  merge: (owner: string, name: string, prId: string, message?: string) =>
    api.post(`/api/repos/${owner}/${name}/pulls/${prId}/merge`, { message }).then(r => r.data),
  close: (owner: string, name: string, prId: string) =>
    api.post(`/api/repos/${owner}/${name}/pulls/${prId}/close`).then(r => r.data),
}
