// =============================================================================
// Type definitions for the AAR Frontend
// =============================================================================

/**
 * User authentication types
 */
export interface User {
  id: string;
  name: string;
  email: string;
  isAdmin: boolean;
  apiKey?: string;
}

export interface AuthState {
  user: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  apiKey: string | null;
}

/**
 * Project types
 */
export type ProjectStatus =
  | 'pending'
  | 'queued'
  | 'filesReady'
  | 'analyzing'
  | 'completed'
  | 'failed'
  | 0 | 1 | 2 | 3 | 4 | 5 | 6;

export interface Project {
  id: string;
  name: string;
  description?: string;
  gitRepoUrl?: string;
  originalFileName?: string;
  status: ProjectStatus;
  statusText: string;
  errorMessage?: string;
  createdAt: string;
  analysisStartedAt?: string;
  analysisCompletedAt?: string;
  fileCount: number;
  totalLinesOfCode: number;
  hasReport: boolean;
  healthScore?: number;
  reportSummary?: string;
}

export interface ProjectListItem {
  id: string;
  name: string;
  description?: string;
  status: ProjectStatus;
  statusText: string;
  createdAt: string;
  analysisCompletedAt?: string;
  fileCount: number;
  totalLinesOfCode: number;
  healthScore?: number;
}

export interface CreateProjectFromZipRequest {
  name: string;
  description?: string;
}

export interface CreateProjectFromGitRequest {
  name: string;
  gitRepoUrl: string;
  description?: string;
}

export interface ProjectCreatedResponse {
  projectId: string;
  name: string;
  status: ProjectStatus;
  createdAt: string;
}

export interface StartAnalysisResponse {
  projectId: string;
  status: ProjectStatus;
  message: string;
}

/**
 * Report types
 */
export type Severity = 'critical' | 'high' | 'medium' | 'low' | 'info' | 0 | 1 | 2 | 3 | 4;

export type FindingCategory =
  | 'architecture'
  | 'security'
  | 'performance'
  | 'maintainability'
  | 'codeQuality'
  | 'documentation'
  | 'testing'
  | 'dependencies';

export interface Finding {
  id: string;
  title: string;
  description: string;
  severity: Severity;
  category: FindingCategory;
  filePath?: string;
  startLine?: number;
  endLine?: number;
  suggestion?: string;
  codeSnippet?: string;
  agentType: string;
}

export interface ReportStatistics {
  totalFiles: number;
  analyzedFiles: number;
  totalLinesOfCode: number;
  highSeverityCount: number;
  mediumSeverityCount: number;
  lowSeverityCount: number;
  totalFindingsCount: number;
  findingsByCategory: Record<string, number>;
}

export interface Report {
  id: string;
  projectId: string;
  projectName: string;
  summary: string;
  recommendations: string[];
  healthScore: number;
  statistics: ReportStatistics;
  findings: Finding[];
  reportVersion: string;
  analysisDurationSeconds: number;
  generatedAt: string;
  pdfDownloadUrl?: string;
  jsonDownloadUrl?: string;
  filesAnalyzed?: number;
  totalTokens?: number;
}

/**
 * Pagination types
 */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export interface PaginationParams {
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDirection?: 'asc' | 'desc';
  search?: string;
  status?: ProjectStatus;
}

/**
 * Upload types
 */
export interface PreflightResponse {
  isValid: boolean;
  fileCount: number;
  totalSize: number;
  estimatedTokens: number;
  estimatedCost: number;
  warnings: string[];
  errors: string[];
}

export interface UploadProgress {
  loaded: number;
  total: number;
  percentage: number;
  partNumber?: number;
  totalParts?: number;
}

/**
 * Real-time types
 */
export interface ProgressUpdate {
  projectId: string;
  phase: string;
  progress: number;
  message: string;
  timestamp: string;
}

export interface PartialFinding {
  projectId: string;
  finding: Finding;
  timestamp: string;
}

export interface LogEntry {
  id: string;
  timestamp: string;
  level: 'info' | 'warning' | 'error' | 'debug';
  message: string;
  source: string;
  projectId?: string;
  metadata?: Record<string, unknown>;
}

/**
 * SignalR connection types
 */
export type ConnectionStatus = 'connecting' | 'connected' | 'disconnected' | 'reconnecting';

export interface SignalRState {
  status: ConnectionStatus;
  error?: string;
  lastConnected?: string;
}

/**
 * Settings types
 */
export interface Settings {
  apiBaseUrl: string;
  mockMode: boolean;
  theme: 'light' | 'dark' | 'system';
  notifications: boolean;
  autoRefresh: boolean;
  refreshInterval: number;
}

/**
 * Dashboard types
 */
export interface DashboardMetrics {
  totalProjects: number;
  projectsToday: number;
  completedToday: number;
  avgAnalysisTime: number;
  totalFindings: number;
  criticalFindings: number;
}

export interface ChartDataPoint {
  date: string;
  count: number;
  label?: string;
}

/**
 * API Error types
 */
export interface ApiError {
  code: string;
  message: string;
  details?: string;
}

export interface ErrorResponse {
  error: ApiError;
  traceId: string;
}
