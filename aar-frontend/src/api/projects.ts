import axiosInstance from './axios';
import {
  Project,
  ProjectListItem,
  PagedResult,
  PaginationParams,
  CreateProjectFromGitRequest,
  ProjectCreatedResponse,
  StartAnalysisResponse,
  PreflightResponse,
  Report,
  UploadProgress,
} from '../types';

// API version prefix
const API_PREFIX = '/api/v1';

/**
 * Projects API client
 */
export const projectsApi = {
  /**
   * List all projects with pagination
   */
  async list(params?: PaginationParams): Promise<PagedResult<ProjectListItem>> {
    const response = await axiosInstance.get<PagedResult<ProjectListItem>>(
      `${API_PREFIX}/projects`,
      { params }
    );
    return response.data;
  },

  /**
   * Get a single project by ID
   */
  async get(projectId: string): Promise<Project> {
    const response = await axiosInstance.get<Project>(
      `${API_PREFIX}/projects/${projectId}`
    );
    return response.data;
  },

  /**
   * Create a project from a Git repository URL
   */
  async createFromGit(data: CreateProjectFromGitRequest): Promise<ProjectCreatedResponse> {
    const response = await axiosInstance.post<ProjectCreatedResponse>(
      `${API_PREFIX}/projects/git`,
      data
    );
    return response.data;
  },

  /**
   * Create a project from a zip file upload
   */
  async createFromZip(
    name: string,
    file: File,
    description?: string,
    onProgress?: (progress: UploadProgress) => void
  ): Promise<ProjectCreatedResponse> {
    const formData = new FormData();
    formData.append('name', name);
    formData.append('file', file);
    if (description) {
      formData.append('description', description);
    }

    const response = await axiosInstance.post<ProjectCreatedResponse>(
      `${API_PREFIX}/projects`,
      formData,
      {
        headers: {
          'Content-Type': 'multipart/form-data',
        },
        onUploadProgress: (progressEvent) => {
          if (onProgress && progressEvent.total) {
            onProgress({
              loaded: progressEvent.loaded,
              total: progressEvent.total,
              percentage: Math.round((progressEvent.loaded * 100) / progressEvent.total),
            });
          }
        },
      }
    );
    return response.data;
  },

  /**
   * Preflight check for a zip file before upload
   * Note: Backend uses /api/preflight/analyze (not versioned)
   */
  async preflight(file: File): Promise<PreflightResponse> {
    const formData = new FormData();
    formData.append('file', file);

    const response = await axiosInstance.post<PreflightResponse>(
      '/api/preflight/analyze',
      formData,
      {
        headers: {
          'Content-Type': 'multipart/form-data',
        },
      }
    );
    return response.data;
  },

  /**
   * Start analysis for a project
   */
  async startAnalysis(projectId: string): Promise<StartAnalysisResponse> {
    const response = await axiosInstance.post<StartAnalysisResponse>(
      `${API_PREFIX}/projects/${projectId}/analyze`
    );
    return response.data;
  },

  /**
   * Delete a project
   */
  async delete(projectId: string): Promise<void> {
    await axiosInstance.delete(`${API_PREFIX}/projects/${projectId}`);
  },
};

/**
 * Reports API client
 */
export const reportsApi = {
  /**
   * Get the report for a project
   */
  async get(projectId: string): Promise<Report> {
    const response = await axiosInstance.get<Report>(
      `${API_PREFIX}/projects/${projectId}/report`
    );
    return response.data;
  },

  /**
   * Get a code chunk by ID
   */
  async getChunk(projectId: string, chunkId: string): Promise<string> {
    const response = await axiosInstance.get<{ content: string }>(
      `${API_PREFIX}/projects/${projectId}/chunks/${chunkId}`
    );
    return response.data.content;
  },

  /**
   * Download report as JSON
   * Note: Backend uses /report/json endpoint
   */
  async downloadJson(projectId: string): Promise<Blob> {
    const response = await axiosInstance.get(
      `${API_PREFIX}/projects/${projectId}/report/json`,
      {
        responseType: 'blob',
      }
    );
    return response.data;
  },

  /**
   * Download report as PDF
   * Note: Backend uses /report/pdf endpoint
   */
  async downloadPdf(projectId: string): Promise<Blob> {
    const response = await axiosInstance.get(
      `${API_PREFIX}/projects/${projectId}/report/pdf`,
      {
        responseType: 'blob',
      }
    );
    return response.data;
  },
};

/**
 * Resumable upload API client
 */
export const uploadApi = {
  /**
   * Initialize a resumable upload
   * Note: Backend uses /api/uploads (not versioned, no projectId in URL)
   */
  async initResumable(
    _projectId: string, // unused - included for API compatibility
    fileName: string,
    fileSize: number,
    projectName?: string
  ): Promise<{ uploadId: string; chunkSize: number; sessionId: string }> {
    const response = await axiosInstance.post(
      '/api/uploads',
      { 
        name: projectName || fileName.replace('.zip', ''),
        fileName, 
        totalSizeBytes: fileSize,
        totalParts: Math.ceil(fileSize / (5 * 1024 * 1024)) // 5MB chunks
      }
    );
    // Map backend response to expected format
    return {
      uploadId: response.data.sessionId,
      sessionId: response.data.sessionId,
      chunkSize: response.data.chunkSizeBytes || 5 * 1024 * 1024
    };
  },

  /**
   * Upload a single chunk
   * Note: Backend uses PUT /api/uploads/{sessionId}/parts/{partNumber}
   */
  async uploadChunk(
    _projectId: string, // unused - kept for compatibility
    uploadId: string, // this is actually sessionId
    partNumber: number,
    chunk: Blob,
    onProgress?: (progress: UploadProgress) => void
  ): Promise<{ etag: string }> {
    const response = await axiosInstance.put(
      `/api/uploads/${uploadId}/parts/${partNumber}`,
      chunk,
      {
        headers: {
          'Content-Type': 'application/octet-stream',
        },
        onUploadProgress: (progressEvent) => {
          if (onProgress && progressEvent.total) {
            onProgress({
              loaded: progressEvent.loaded,
              total: progressEvent.total,
              percentage: Math.round((progressEvent.loaded * 100) / progressEvent.total),
              partNumber,
            });
          }
        },
      }
    );
    return { etag: response.data?.etag || `part-${partNumber}` };
  },

  /**
   * Finalize a resumable upload
   * Note: Backend uses POST /api/uploads/{sessionId}/complete
   */
  async finalizeUpload(
    _projectId: string, // unused - kept for compatibility
    uploadId: string, // this is actually sessionId
    parts: { partNumber: number; etag: string }[]
  ): Promise<{ projectId: string }> {
    const response = await axiosInstance.post(`/api/uploads/${uploadId}/complete`, {
      parts,
    });
    return { projectId: response.data?.projectId || uploadId };
  },
};

/**
 * Admin API client
 */
export const adminApi = {
  /**
   * Get pending job approvals
   */
  async getPendingApprovals(): Promise<{ id: string; projectName: string; estimatedCost: number }[]> {
    const response = await axiosInstance.get(`${API_PREFIX}/admin/approvals`);
    return response.data;
  },

  /**
   * Approve a job
   */
  async approveJob(jobId: string): Promise<void> {
    await axiosInstance.post(`${API_PREFIX}/admin/approvals/${jobId}/approve`);
  },

  /**
   * Reject a job
   */
  async rejectJob(jobId: string, reason?: string): Promise<void> {
    await axiosInstance.post(`${API_PREFIX}/admin/approvals/${jobId}/reject`, { reason });
  },
};

/**
 * Dashboard/metrics API client
 */
export const metricsApi = {
  /**
   * Get dashboard metrics
   */
  async getDashboardMetrics(): Promise<{
    totalProjects: number;
    projectsToday: number;
    completedToday: number;
    avgAnalysisTime: number;
    totalFindings: number;
    criticalFindings: number;
  }> {
    // This endpoint might not exist - return mock data for now
    try {
      const response = await axiosInstance.get(`${API_PREFIX}/metrics/dashboard`);
      return response.data;
    } catch {
      // Return mock data if endpoint doesn't exist
      return {
        totalProjects: 0,
        projectsToday: 0,
        completedToday: 0,
        avgAnalysisTime: 0,
        totalFindings: 0,
        criticalFindings: 0,
      };
    }
  },

  /**
   * Get projects over time chart data
   */
  async getProjectsChart(): Promise<{ date: string; count: number }[]> {
    try {
      const response = await axiosInstance.get(`${API_PREFIX}/metrics/projects-chart`);
      return response.data;
    } catch {
      return [];
    }
  },
};
