import { useEffect, useRef, useCallback, useState } from 'react';
import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { useSnackbar } from 'notistack';
import { API_BASE_URL, getApiKey } from '../api';
import { ConnectionStatus, ProgressUpdate, PartialFinding, LogEntry, Finding } from '../types';
import { projectKeys } from './useProjects';

// SignalR hub URL
const HUB_URL = import.meta.env.VITE_SIGNALR_HUB_URL || `${API_BASE_URL}/hubs/analysis`;

interface UseSignalROptions {
  projectId?: string;
  autoConnect?: boolean;
  onProgress?: (update: ProgressUpdate) => void;
  onFinding?: (finding: PartialFinding) => void;
  onLog?: (log: LogEntry) => void;
  onStatusChange?: (status: string) => void;
}

interface SignalRState {
  status: ConnectionStatus;
  error: string | null;
  progress: ProgressUpdate | null;
  logs: LogEntry[];
  findings: Finding[];
}

/**
 * Hook for managing SignalR connection and real-time updates
 */
export function useSignalR(options: UseSignalROptions = {}) {
  const {
    projectId,
    autoConnect = true,
    onProgress,
    onFinding,
    onLog,
    onStatusChange,
  } = options;

  const connectionRef = useRef<HubConnection | null>(null);
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();

  const [state, setState] = useState<SignalRState>({
    status: 'disconnected',
    error: null,
    progress: null,
    logs: [],
    findings: [],
  });

  // Track if we should be connected
  const shouldConnectRef = useRef(autoConnect);

  /**
   * Create and configure the SignalR connection
   */
  const createConnection = useCallback(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL, {
        // Get fresh API key on each connection/reconnection attempt
        accessTokenFactory: () => getApiKey() || '',
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          // Exponential backoff: 0, 1s, 2s, 4s, 8s, max 30s
          const delay = Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000);
          return delay;
        },
      })
      .configureLogging(import.meta.env.DEV ? LogLevel.Information : LogLevel.Warning)
      .build();

    return connection;
  }, []);

  /**
   * Connect to the SignalR hub
   */
  const connect = useCallback(async () => {
    if (connectionRef.current?.state === 'Connected') {
      return;
    }

    try {
      setState((s) => ({ ...s, status: 'connecting', error: null }));

      const connection = createConnection();
      connectionRef.current = connection;

      // Set up event handlers
      connection.on('ProgressUpdate', (update: ProgressUpdate) => {
        setState((s) => ({ ...s, progress: update }));
        onProgress?.(update);

        // Update project in cache if it's the one we're watching
        if (update.projectId === projectId) {
          queryClient.invalidateQueries({
            queryKey: projectKeys.detail(update.projectId),
          });
        }
      });

      connection.on('PartialFinding', (data: PartialFinding) => {
        setState((s) => ({
          ...s,
          findings: [...s.findings, data.finding],
        }));
        onFinding?.(data);
      });

      connection.on('LogMessage', (log: LogEntry) => {
        setState((s) => ({
          ...s,
          logs: [...s.logs.slice(-99), log], // Keep last 100 logs
        }));
        onLog?.(log);
      });

      connection.on('StatusChanged', (data: { projectId: string; status: string }) => {
        onStatusChange?.(data.status);

        // Refresh project data
        queryClient.invalidateQueries({
          queryKey: projectKeys.detail(data.projectId),
        });
        queryClient.invalidateQueries({
          queryKey: projectKeys.lists(),
        });

        // Show notification for completion/failure
        if (data.status === 'completed') {
          enqueueSnackbar('Analysis completed!', { variant: 'success' });
        } else if (data.status === 'failed') {
          enqueueSnackbar('Analysis failed', { variant: 'error' });
        }
      });

      connection.onreconnecting(() => {
        setState((s) => ({ ...s, status: 'reconnecting' }));
      });

      connection.onreconnected(() => {
        setState((s) => ({ ...s, status: 'connected' }));
        // Re-subscribe to project if we were watching one
        if (projectId) {
          connection.invoke('SubscribeToProject', projectId).catch(console.error);
        }
      });

      connection.onclose((error) => {
        setState((s) => ({
          ...s,
          status: 'disconnected',
          error: error?.message || null,
        }));
      });

      // Start connection
      await connection.start();
      setState((s) => ({ ...s, status: 'connected' }));

      // Subscribe to project if provided
      if (projectId) {
        await connection.invoke('SubscribeToProject', projectId);
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Failed to connect';
      setState((s) => ({
        ...s,
        status: 'disconnected',
        error: message,
      }));
      console.error('SignalR connection error:', error);
    }
  }, [createConnection, projectId, queryClient, onProgress, onFinding, onLog, onStatusChange, enqueueSnackbar]);

  /**
   * Disconnect from the SignalR hub
   */
  const disconnect = useCallback(async () => {
    if (connectionRef.current) {
      try {
        await connectionRef.current.stop();
      } catch (error) {
        console.error('Error disconnecting:', error);
      }
      connectionRef.current = null;
      setState((s) => ({ ...s, status: 'disconnected' }));
    }
  }, []);

  /**
   * Subscribe to a specific project
   */
  const subscribeToProject = useCallback(async (id: string) => {
    if (connectionRef.current?.state === 'Connected') {
      try {
        await connectionRef.current.invoke('SubscribeToProject', id);
      } catch (error) {
        console.error('Error subscribing to project:', error);
      }
    }
  }, []);

  /**
   * Unsubscribe from a specific project
   */
  const unsubscribeFromProject = useCallback(async (id: string) => {
    if (connectionRef.current?.state === 'Connected') {
      try {
        await connectionRef.current.invoke('UnsubscribeFromProject', id);
      } catch (error) {
        console.error('Error unsubscribing from project:', error);
      }
    }
  }, []);

  /**
   * Clear accumulated state
   */
  const clearState = useCallback(() => {
    setState((s) => ({
      ...s,
      progress: null,
      logs: [],
      findings: [],
    }));
  }, []);

  // Auto-connect on mount - use refs to avoid infinite loops
  useEffect(() => {
    shouldConnectRef.current = autoConnect;
  }, [autoConnect]);

  useEffect(() => {
    let mounted = true;

    const connectIfNeeded = async () => {
      if (shouldConnectRef.current && mounted) {
        await connect();
      }
    };

    connectIfNeeded();

    return () => {
      mounted = false;
      disconnect();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []); // Only run on mount/unmount

  // Re-subscribe when project changes
  useEffect(() => {
    if (projectId && connectionRef.current?.state === 'Connected') {
      subscribeToProject(projectId);

      return () => {
        unsubscribeFromProject(projectId);
      };
    }
  }, [projectId, subscribeToProject, unsubscribeFromProject]);

  return {
    ...state,
    connect,
    disconnect,
    subscribeToProject,
    unsubscribeFromProject,
    clearState,
    isConnected: state.status === 'connected',
  };
}
