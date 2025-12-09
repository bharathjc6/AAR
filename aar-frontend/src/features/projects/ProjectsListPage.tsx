import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Box,
  Typography,
  Button,
  TextField,
  InputAdornment,
  Menu,
  MenuItem,
  Chip,
  IconButton,
  Tooltip,
  TableContainer,
  Table,
  TableHead,
  TableBody,
  TableRow,
  TableCell,
  TableSortLabel,
  TablePagination,
  LinearProgress,
} from '@mui/material';
import {
  Add as AddIcon,
  Search as SearchIcon,
  FilterList as FilterIcon,
  Refresh as RefreshIcon,
  MoreVert as MoreVertIcon,
  Delete as DeleteIcon,
  PlayArrow as PlayIcon,
  Visibility as ViewIcon,
} from '@mui/icons-material';
import { motion, AnimatePresence } from 'framer-motion';
import dayjs from 'dayjs';
import { Card, StatusBadge, LoadingSkeleton, EmptyState } from '../../components';
import { useProjects, usePrefetchProject, useStartAnalysis, useDeleteProject } from '../../hooks';
import { ProjectListItem, ProjectStatus } from '../../types';

type SortDirection = 'asc' | 'desc';
type SortField = 'name' | 'createdAt' | 'status' | 'healthScore';

/**
 * Projects list page with filtering, sorting, and pagination
 */
export default function ProjectsListPage() {
  const navigate = useNavigate();
  const prefetchProject = usePrefetchProject();
  const { mutate: startAnalysis, isPending: isStarting } = useStartAnalysis();
  const { mutate: deleteProject, isPending: isDeleting } = useDeleteProject();

  // Pagination state
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(10);

  // Sorting state
  const [sortField, setSortField] = useState<SortField>('createdAt');
  const [sortDirection, setSortDirection] = useState<SortDirection>('desc');

  // Filter state
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<ProjectStatus | 'all'>('all');
  const [filterAnchorEl, setFilterAnchorEl] = useState<null | HTMLElement>(null);

  // Action menu state
  const [actionAnchorEl, setActionAnchorEl] = useState<null | HTMLElement>(null);
  const [selectedProject, setSelectedProject] = useState<ProjectListItem | null>(null);

  // Fetch projects
  const { data, isLoading, refetch, isFetching } = useProjects({
    page: page + 1,
    pageSize,
    sortBy: sortField,
    sortDirection,
    search: search || undefined,
    status: statusFilter !== 'all' ? statusFilter : undefined,
  });

  // Handle sort
  const handleSort = (field: SortField) => {
    if (sortField === field) {
      setSortDirection(sortDirection === 'asc' ? 'desc' : 'asc');
    } else {
      setSortField(field);
      setSortDirection('desc');
    }
  };

  // Handle action menu
  const handleActionClick = (event: React.MouseEvent<HTMLElement>, project: ProjectListItem) => {
    event.stopPropagation();
    setSelectedProject(project);
    setActionAnchorEl(event.currentTarget);
  };

  const handleActionClose = () => {
    setActionAnchorEl(null);
    setSelectedProject(null);
  };

  // Actions
  const handleViewProject = () => {
    if (selectedProject) {
      navigate(`/projects/${selectedProject.id}`);
    }
    handleActionClose();
  };

  const handleStartAnalysis = () => {
    if (selectedProject) {
      startAnalysis(selectedProject.id);
    }
    handleActionClose();
  };

  const handleDeleteProject = () => {
    if (selectedProject && window.confirm('Are you sure you want to delete this project?')) {
      deleteProject(selectedProject.id);
    }
    handleActionClose();
  };

  // Status filter options
  const statusOptions: { value: ProjectStatus | 'all'; label: string }[] = [
    { value: 'all', label: 'All Status' },
    { value: 2, label: 'Files Ready' },
    { value: 3, label: 'Analyzing' },
    { value: 5, label: 'Completed' },
    { value: 6, label: 'Failed' },
  ];

  return (
    <Box>
      {/* Header */}
      <Box
        sx={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          mb: 3,
        }}
      >
        <Box>
          <Typography variant="h4" fontWeight={600} gutterBottom>
            Projects
          </Typography>
          <Typography variant="body2" color="text.secondary">
            Manage and view all your architecture review projects
          </Typography>
        </Box>
        <Button
          variant="contained"
          startIcon={<AddIcon />}
          onClick={() => navigate('/projects/new')}
        >
          New Project
        </Button>
      </Box>

      {/* Filters and search */}
      <Card animateOnMount={false} sx={{ mb: 3 }}>
        <Box sx={{ display: 'flex', gap: 2, alignItems: 'center', flexWrap: 'wrap' }}>
          <TextField
            placeholder="Search projects..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            size="small"
            sx={{ minWidth: 250 }}
            InputProps={{
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon color="action" />
                </InputAdornment>
              ),
            }}
          />

          <Button
            variant="outlined"
            startIcon={<FilterIcon />}
            onClick={(e) => setFilterAnchorEl(e.currentTarget)}
          >
            {statusFilter === 'all' ? 'Filter' : 'Filtered'}
          </Button>

          <Menu
            anchorEl={filterAnchorEl}
            open={Boolean(filterAnchorEl)}
            onClose={() => setFilterAnchorEl(null)}
          >
            {statusOptions.map((option) => (
              <MenuItem
                key={option.value}
                selected={statusFilter === option.value}
                onClick={() => {
                  setStatusFilter(option.value);
                  setFilterAnchorEl(null);
                }}
              >
                {option.label}
              </MenuItem>
            ))}
          </Menu>

          {statusFilter !== 'all' && (
            <Chip
              label={statusOptions.find((o) => o.value === statusFilter)?.label}
              onDelete={() => setStatusFilter('all')}
              size="small"
            />
          )}

          <Box sx={{ flex: 1 }} />

          <Tooltip title="Refresh">
            <IconButton onClick={() => refetch()} disabled={isFetching}>
              <RefreshIcon className={isFetching ? 'animate-spin' : ''} />
            </IconButton>
          </Tooltip>
        </Box>
      </Card>

      {/* Loading bar */}
      {isFetching && (
        <LinearProgress sx={{ mb: 2, borderRadius: 1 }} />
      )}

      {/* Projects table */}
      <Card noPadding animateOnMount={false}>
        {isLoading ? (
          <Box sx={{ p: 2 }}>
            <LoadingSkeleton variant="table" count={5} />
          </Box>
        ) : !data?.items?.length ? (
          <EmptyState
            type={search ? 'no-results' : 'empty'}
            title={search ? 'No projects found' : 'No projects yet'}
            description={
              search
                ? 'Try adjusting your search criteria'
                : 'Create your first project to get started'
            }
            action={
              search
                ? { label: 'Clear Search', onClick: () => setSearch('') }
                : { label: 'New Project', onClick: () => navigate('/projects/new') }
            }
          />
        ) : (
          <>
            <TableContainer>
              <Table>
                <TableHead>
                  <TableRow>
                    <TableCell>
                      <TableSortLabel
                        active={sortField === 'name'}
                        direction={sortField === 'name' ? sortDirection : 'asc'}
                        onClick={() => handleSort('name')}
                      >
                        Project Name
                      </TableSortLabel>
                    </TableCell>
                    <TableCell>
                      <TableSortLabel
                        active={sortField === 'status'}
                        direction={sortField === 'status' ? sortDirection : 'asc'}
                        onClick={() => handleSort('status')}
                      >
                        Status
                      </TableSortLabel>
                    </TableCell>
                    <TableCell>
                      <TableSortLabel
                        active={sortField === 'createdAt'}
                        direction={sortField === 'createdAt' ? sortDirection : 'asc'}
                        onClick={() => handleSort('createdAt')}
                      >
                        Created
                      </TableSortLabel>
                    </TableCell>
                    <TableCell align="right">Files</TableCell>
                    <TableCell align="right">
                      <TableSortLabel
                        active={sortField === 'healthScore'}
                        direction={sortField === 'healthScore' ? sortDirection : 'asc'}
                        onClick={() => handleSort('healthScore')}
                      >
                        Health Score
                      </TableSortLabel>
                    </TableCell>
                    <TableCell align="right">Actions</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  <AnimatePresence>
                    {data.items.map((project, index) => (
                      <TableRow
                        key={project.id}
                        component={motion.tr}
                        initial={{ opacity: 0, y: 10 }}
                        animate={{ opacity: 1, y: 0 }}
                        exit={{ opacity: 0 }}
                        transition={{ delay: index * 0.03 }}
                        onClick={() => navigate(`/projects/${project.id}`)}
                        onMouseEnter={() => prefetchProject(project.id)}
                        sx={{
                          cursor: 'pointer',
                          '&:hover': { bgcolor: 'action.hover' },
                        }}
                      >
                        <TableCell>
                          <Typography variant="body2" fontWeight={500}>
                            {project.name}
                          </Typography>
                          {project.description && (
                            <Typography variant="caption" color="text.secondary" className="truncate">
                              {project.description}
                            </Typography>
                          )}
                        </TableCell>
                        <TableCell>
                          <StatusBadge status={project.status} />
                        </TableCell>
                        <TableCell>
                          <Typography variant="body2">
                            {dayjs(project.createdAt).format('MMM D, YYYY')}
                          </Typography>
                          <Typography variant="caption" color="text.secondary">
                            {dayjs(project.createdAt).format('h:mm A')}
                          </Typography>
                        </TableCell>
                        <TableCell align="right">
                          <Typography variant="body2">{project.fileCount}</Typography>
                        </TableCell>
                        <TableCell align="right">
                          {project.healthScore !== undefined && project.healthScore !== null ? (
                            <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: 1 }}>
                              <Typography variant="body2" fontWeight={500}>
                                {project.healthScore}%
                              </Typography>
                              <Box sx={{ width: 60 }}>
                                <LinearProgress
                                  variant="determinate"
                                  value={project.healthScore}
                                  sx={{
                                    height: 6,
                                    borderRadius: 3,
                                    bgcolor: 'action.hover',
                                    '& .MuiLinearProgress-bar': {
                                      bgcolor:
                                        project.healthScore >= 70
                                          ? 'success.main'
                                          : project.healthScore >= 40
                                          ? 'warning.main'
                                          : 'error.main',
                                    },
                                  }}
                                />
                              </Box>
                            </Box>
                          ) : (
                            <Typography variant="body2" color="text.secondary">
                              -
                            </Typography>
                          )}
                        </TableCell>
                        <TableCell align="right">
                          <IconButton
                            size="small"
                            onClick={(e) => handleActionClick(e, project)}
                          >
                            <MoreVertIcon />
                          </IconButton>
                        </TableCell>
                      </TableRow>
                    ))}
                  </AnimatePresence>
                </TableBody>
              </Table>
            </TableContainer>

            <TablePagination
              component="div"
              count={data.totalCount}
              page={page}
              onPageChange={(_, newPage) => setPage(newPage)}
              rowsPerPage={pageSize}
              onRowsPerPageChange={(e) => {
                setPageSize(parseInt(e.target.value, 10));
                setPage(0);
              }}
              rowsPerPageOptions={[5, 10, 25, 50]}
            />
          </>
        )}
      </Card>

      {/* Action menu */}
      <Menu
        anchorEl={actionAnchorEl}
        open={Boolean(actionAnchorEl)}
        onClose={handleActionClose}
      >
        <MenuItem onClick={handleViewProject}>
          <ViewIcon fontSize="small" sx={{ mr: 1 }} />
          View Details
        </MenuItem>
        {selectedProject && (selectedProject.status === 2 || selectedProject.status === 'filesReady') && (
          <MenuItem onClick={handleStartAnalysis} disabled={isStarting}>
            <PlayIcon fontSize="small" sx={{ mr: 1 }} />
            Start Analysis
          </MenuItem>
        )}
        <MenuItem onClick={handleDeleteProject} disabled={isDeleting} sx={{ color: 'error.main' }}>
          <DeleteIcon fontSize="small" sx={{ mr: 1 }} />
          Delete
        </MenuItem>
      </Menu>
    </Box>
  );
}
