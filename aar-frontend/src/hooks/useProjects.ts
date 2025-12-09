import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useSnackbar } from 'notistack';
import { projectsApi, reportsApi } from '../api';
import {
  PaginationParams,
  CreateProjectFromGitRequest,
  UploadProgress,
} from '../types';

// Query keys for cache management
export const projectKeys = {
  all: ['projects'] as const,
  lists: () => [...projectKeys.all, 'list'] as const,
  list: (params: PaginationParams) => [...projectKeys.lists(), params] as const,
  details: () => [...projectKeys.all, 'detail'] as const,
  detail: (id: string) => [...projectKeys.details(), id] as const,
  reports: () => [...projectKeys.all, 'report'] as const,
  report: (id: string) => [...projectKeys.reports(), id] as const,
};

/**
 * Hook for fetching paginated project list
 */
export function useProjects(params?: PaginationParams) {
  return useQuery({
    queryKey: projectKeys.list(params || {}),
    queryFn: () => projectsApi.list(params),
    staleTime: 1000 * 60, // 1 minute
  });
}

/**
 * Hook for fetching a single project
 */
export function useProject(projectId: string, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: projectKeys.detail(projectId),
    queryFn: () => projectsApi.get(projectId),
    enabled: options?.enabled !== false && !!projectId,
    staleTime: 1000 * 30, // 30 seconds
    refetchInterval: (query) => {
      // Auto-refetch while analyzing
      const status = query.state.data?.status;
      if (status === 3 || status === 4 || status === 'analyzing') {
        return 3000; // 3 seconds
      }
      return false;
    },
  });
}

/**
 * Hook for fetching a project report
 */
export function useReport(projectId: string, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: projectKeys.report(projectId),
    queryFn: () => reportsApi.get(projectId),
    enabled: options?.enabled !== false && !!projectId,
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
}

/**
 * Hook for creating a project from Git URL
 */
export function useCreateProjectFromGit() {
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();

  return useMutation({
    mutationFn: (data: CreateProjectFromGitRequest) => projectsApi.createFromGit(data),
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: projectKeys.lists() });
      enqueueSnackbar(`Project "${data.name}" created successfully`, { variant: 'success' });
    },
    onError: (error: Error) => {
      enqueueSnackbar(error.message || 'Failed to create project', { variant: 'error' });
    },
  });
}

/**
 * Hook for creating a project from zip file
 */
export function useCreateProjectFromZip() {
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();

  return useMutation({
    mutationFn: ({
      name,
      file,
      description,
      onProgress,
    }: {
      name: string;
      file: File;
      description?: string;
      onProgress?: (progress: UploadProgress) => void;
    }) => projectsApi.createFromZip(name, file, description, onProgress),
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: projectKeys.lists() });
      enqueueSnackbar(`Project "${data.name}" created successfully`, { variant: 'success' });
    },
    onError: (error: Error) => {
      enqueueSnackbar(error.message || 'Failed to upload project', { variant: 'error' });
    },
  });
}

/**
 * Hook for starting project analysis
 */
export function useStartAnalysis() {
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();

  return useMutation({
    mutationFn: (projectId: string) => projectsApi.startAnalysis(projectId),
    onSuccess: (data, projectId) => {
      // Update the project in cache
      queryClient.invalidateQueries({ queryKey: projectKeys.detail(projectId) });
      queryClient.invalidateQueries({ queryKey: projectKeys.lists() });
      enqueueSnackbar('Analysis started successfully', { variant: 'success' });
    },
    onError: (error: Error) => {
      enqueueSnackbar(error.message || 'Failed to start analysis', { variant: 'error' });
    },
  });
}

/**
 * Hook for resetting a stuck project
 */
export function useResetProject() {
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();

  return useMutation({
    mutationFn: (projectId: string) => projectsApi.reset(projectId),
    onSuccess: (data, projectId) => {
      queryClient.invalidateQueries({ queryKey: projectKeys.detail(projectId) });
      queryClient.invalidateQueries({ queryKey: projectKeys.lists() });
      enqueueSnackbar('Project reset successfully. You can start analysis again.', { variant: 'success' });
    },
    onError: (error: Error) => {
      enqueueSnackbar(error.message || 'Failed to reset project', { variant: 'error' });
    },
  });
}

/**
 * Hook for deleting a project
 */
export function useDeleteProject() {
  const queryClient = useQueryClient();
  const { enqueueSnackbar } = useSnackbar();

  return useMutation({
    mutationFn: (projectId: string) => projectsApi.delete(projectId),
    onSuccess: (_, projectId) => {
      queryClient.removeQueries({ queryKey: projectKeys.detail(projectId) });
      queryClient.invalidateQueries({ queryKey: projectKeys.lists() });
      enqueueSnackbar('Project deleted successfully', { variant: 'success' });
    },
    onError: (error: Error) => {
      enqueueSnackbar(error.message || 'Failed to delete project', { variant: 'error' });
    },
  });
}

/**
 * Hook for preflight check
 */
export function usePreflight() {
  return useMutation({
    mutationFn: (file: File) => projectsApi.preflight(file),
  });
}

/**
 * Hook for downloading report
 */
export function useDownloadReport() {
  const { enqueueSnackbar } = useSnackbar();

  return useMutation({
    mutationFn: async ({
      projectId,
      format,
      projectName,
    }: {
      projectId: string;
      format: 'json' | 'pdf';
      projectName: string;
    }) => {
      const blob =
        format === 'json'
          ? await reportsApi.downloadJson(projectId)
          : await reportsApi.downloadPdf(projectId);

      // Create download link
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `${projectName}-report.${format}`;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      window.URL.revokeObjectURL(url);
    },
    onSuccess: () => {
      enqueueSnackbar('Report downloaded successfully', { variant: 'success' });
    },
    onError: (error: Error) => {
      enqueueSnackbar(error.message || 'Failed to download report', { variant: 'error' });
    },
  });
}

/**
 * Hook for prefetching project details on hover
 */
export function usePrefetchProject() {
  const queryClient = useQueryClient();

  return (projectId: string) => {
    queryClient.prefetchQuery({
      queryKey: projectKeys.detail(projectId),
      queryFn: () => projectsApi.get(projectId),
      staleTime: 1000 * 60, // 1 minute
    });
  };
}
