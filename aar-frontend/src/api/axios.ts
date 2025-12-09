import axios, { AxiosError, AxiosInstance, InternalAxiosRequestConfig } from 'axios';
import { ErrorResponse } from '../types';

// Get base URL from environment variable
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000';

// Storage key for API key (in-memory by default for security)
let inMemoryApiKey: string | null = null;

/**
 * Set the API key for authentication
 */
export function setApiKey(key: string | null, persist = false) {
  inMemoryApiKey = key;
  if (persist && key) {
    sessionStorage.setItem('aar-api-key', key);
  } else if (!key) {
    sessionStorage.removeItem('aar-api-key');
  }
}

/**
 * Get the current API key
 */
export function getApiKey(): string | null {
  return inMemoryApiKey || sessionStorage.getItem('aar-api-key');
}

/**
 * Create axios instance with default configuration
 */
const axiosInstance: AxiosInstance = axios.create({
  baseURL: API_BASE_URL,
  timeout: 30000,
  headers: {
    'Content-Type': 'application/json',
  },
});

/**
 * Request interceptor for adding auth headers and logging
 */
axiosInstance.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    // Add API key header if available
    const apiKey = getApiKey();
    if (apiKey) {
      config.headers['X-Api-Key'] = apiKey;
    }

    // Development logging (redact sensitive data)
    if (import.meta.env.DEV) {
      const redactedHeaders = { ...config.headers };
      if (redactedHeaders['X-Api-Key']) {
        redactedHeaders['X-Api-Key'] = '***REDACTED***';
      }
      console.log(`[API Request] ${config.method?.toUpperCase()} ${config.url}`, {
        headers: redactedHeaders,
        params: config.params,
      });
    }

    return config;
  },
  (error) => {
    console.error('[API Request Error]', error);
    return Promise.reject(error);
  }
);

/**
 * Response interceptor for error handling and logging
 */
axiosInstance.interceptors.response.use(
  (response) => {
    // Development logging
    if (import.meta.env.DEV) {
      console.log(`[API Response] ${response.status} ${response.config.url}`, {
        data: response.data,
      });
    }
    return response;
  },
  (error: AxiosError<ErrorResponse>) => {
    // Log error
    console.error('[API Error]', {
      url: error.config?.url,
      status: error.response?.status,
      message: error.response?.data?.error?.message || error.message,
    });

    // Extract error message for display
    const errorMessage =
      error.response?.data?.error?.message ||
      error.message ||
      'An unexpected error occurred';

    // Create enhanced error object
    const enhancedError = new Error(errorMessage) as Error & {
      code: string;
      status: number;
      traceId?: string;
    };
    enhancedError.code = error.response?.data?.error?.code || 'UNKNOWN';
    enhancedError.status = error.response?.status || 0;
    enhancedError.traceId = error.response?.data?.traceId;

    return Promise.reject(enhancedError);
  }
);

export default axiosInstance;

// Export the base URL for SignalR connection
export { API_BASE_URL };
