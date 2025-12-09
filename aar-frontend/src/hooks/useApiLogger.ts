import { useState, useCallback, useRef, useEffect } from 'react';

/**
 * API Request Log entry
 */
export interface ApiLogEntry {
  id: string;
  timestamp: Date;
  method: string;
  url: string;
  status?: number;
  duration?: number;
  error?: string;
  requestSize?: number;
  responseSize?: number;
}

/**
 * API Logger Hook - tracks API requests for diagnostics
 */
export function useApiLogger(maxEntries: number = 100) {
  const [logs, setLogs] = useState<ApiLogEntry[]>([]);
  const [isEnabled, setIsEnabled] = useState(() => {
    // Enable by default in development
    return import.meta.env.DEV || localStorage.getItem('aar-diagnostics-enabled') === 'true';
  });

  const addLog = useCallback((entry: Omit<ApiLogEntry, 'id' | 'timestamp'>) => {
    if (!isEnabled) return;

    const newEntry: ApiLogEntry = {
      ...entry,
      id: `${Date.now()}-${Math.random().toString(36).substr(2, 9)}`,
      timestamp: new Date(),
    };

    setLogs((prev) => {
      const updated = [newEntry, ...prev];
      return updated.slice(0, maxEntries);
    });
  }, [isEnabled, maxEntries]);

  const clearLogs = useCallback(() => {
    setLogs([]);
  }, []);

  const toggleEnabled = useCallback(() => {
    setIsEnabled((prev) => {
      const newValue = !prev;
      localStorage.setItem('aar-diagnostics-enabled', String(newValue));
      return newValue;
    });
  }, []);

  const exportLogs = useCallback(() => {
    const data = JSON.stringify(logs, null, 2);
    const blob = new Blob([data], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `aar-api-logs-${new Date().toISOString()}.json`;
    a.click();
    URL.revokeObjectURL(url);
  }, [logs]);

  return {
    logs,
    isEnabled,
    addLog,
    clearLogs,
    toggleEnabled,
    exportLogs,
  };
}

/**
 * Global API logger instance for axios interceptor integration
 */
let globalLogCallback: ((entry: Omit<ApiLogEntry, 'id' | 'timestamp'>) => void) | null = null;

export function setGlobalApiLogCallback(
  callback: ((entry: Omit<ApiLogEntry, 'id' | 'timestamp'>) => void) | null
) {
  globalLogCallback = callback;
}

export function logApiRequest(entry: Omit<ApiLogEntry, 'id' | 'timestamp'>) {
  if (globalLogCallback) {
    globalLogCallback(entry);
  }
}

/**
 * Performance tracking utilities
 */
export interface PerformanceMetrics {
  avgResponseTime: number;
  totalRequests: number;
  errorRate: number;
  requestsPerMinute: number;
}

export function calculateMetrics(logs: ApiLogEntry[]): PerformanceMetrics {
  if (logs.length === 0) {
    return {
      avgResponseTime: 0,
      totalRequests: 0,
      errorRate: 0,
      requestsPerMinute: 0,
    };
  }

  const completedLogs = logs.filter((log) => log.duration !== undefined);
  const errorLogs = logs.filter((log) => log.error || (log.status && log.status >= 400));
  
  const avgResponseTime = completedLogs.length > 0
    ? completedLogs.reduce((sum, log) => sum + (log.duration || 0), 0) / completedLogs.length
    : 0;

  // Calculate requests per minute based on time span
  const now = new Date();
  const oneMinuteAgo = new Date(now.getTime() - 60000);
  const recentLogs = logs.filter((log) => log.timestamp >= oneMinuteAgo);

  return {
    avgResponseTime: Math.round(avgResponseTime),
    totalRequests: logs.length,
    errorRate: (errorLogs.length / logs.length) * 100,
    requestsPerMinute: recentLogs.length,
  };
}
