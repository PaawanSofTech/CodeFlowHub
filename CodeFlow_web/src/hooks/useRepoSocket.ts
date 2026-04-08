import { useEffect, useRef, useCallback } from 'react'
import * as signalR from '@microsoft/signalr'
import { BASE } from '../api/client'
import { useAuthStore } from '../store/authStore'

export type RepoEvent =
  | { type: 'PushReceived'; owner: string; name: string; commitHash: string; branch: string; timestamp: string }
  | { type: 'PullRequestCreated'; owner: string; name: string; prId: string; title: string; timestamp: string }

interface UseRepoSocketOptions {
  owner: string
  name: string
  onEvent: (event: RepoEvent) => void
}

export function useRepoSocket({ owner, name, onEvent }: UseRepoSocketOptions) {
  const connectionRef = useRef<signalR.HubConnection | null>(null)
  const onEventRef = useRef(onEvent)
  onEventRef.current = onEvent

  useEffect(() => {
    const token = useAuthStore.getState().token
    if (!token || !owner || !name) return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${BASE}/hubs/repo`, {
        accessTokenFactory: () => useAuthStore.getState().token || '',
        transport: signalR.HttpTransportType.WebSockets,
        skipNegotiation: true,
      })
      .withAutomaticReconnect([1000, 2000, 5000, 10000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    connection.on('PushReceived', (data) => {
      onEventRef.current({ type: 'PushReceived', ...data })
    })

    connection.on('PullRequestCreated', (data) => {
      onEventRef.current({ type: 'PullRequestCreated', ...data })
    })

    connection.on('Joined', () => {
      // Successfully joined repo group
    })

    connectionRef.current = connection

    connection.start()
      .then(() => connection.invoke('JoinRepo', owner, name))
      .catch(() => {
        // SignalR unavailable — graceful degradation, polling still works
      })

    return () => {
      connection.invoke('LeaveRepo', owner, name).catch(() => {})
      connection.stop()
    }
  }, [owner, name])
}

/** Lightweight polling fallback — used on pages without WebSocket needs */
export function usePolling(fn: () => void, intervalMs = 15000) {
  const fnRef = useRef(fn)
  fnRef.current = fn

  useEffect(() => {
    const id = setInterval(() => fnRef.current(), intervalMs)
    return () => clearInterval(id)
  }, [intervalMs])
}
