import { useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Box,
  Grid,
  Typography,
  Button,
  LinearProgress,
  Chip,
} from '@mui/material';
import {
  Add as AddIcon,
  TrendingUp as TrendingUpIcon,
  Assessment as AssessmentIcon,
  Speed as SpeedIcon,
  Warning as WarningIcon,
  CheckCircle as CheckCircleIcon,
  Folder as FolderIcon,
} from '@mui/icons-material';
import { motion } from 'framer-motion';
import {
  AreaChart,
  Area,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  PieChart,
  Pie,
  Cell,
} from 'recharts';
import dayjs from 'dayjs';
import { Card, StatusBadge, LoadingSkeleton, EmptyState } from '../../components';
import { useProjects } from '../../hooks';

// Mock chart data
const mockChartData = [
  { date: '2024-12-01', count: 4 },
  { date: '2024-12-02', count: 7 },
  { date: '2024-12-03', count: 5 },
  { date: '2024-12-04', count: 9 },
  { date: '2024-12-05', count: 6 },
  { date: '2024-12-06', count: 11 },
  { date: '2024-12-07', count: 8 },
];

const severityData = [
  { name: 'Critical', value: 2, color: '#d32f2f' },
  { name: 'High', value: 5, color: '#f44336' },
  { name: 'Medium', value: 12, color: '#ff9800' },
  { name: 'Low', value: 25, color: '#4caf50' },
];

/**
 * Dashboard page with metrics and recent projects
 */
export default function DashboardPage() {
  const navigate = useNavigate();
  const { data: projectsData, isLoading } = useProjects({ pageSize: 5 });

  // Calculate metrics from projects
  const metrics = useMemo(() => {
    const projects = projectsData?.items || [];
    const today = dayjs().startOf('day');

    const projectsToday = projects.filter((p) =>
      dayjs(p.createdAt).isAfter(today)
    ).length;

    const completed = projects.filter(
      (p) => p.status === 5 || p.status === 'completed'
    );

    const avgHealthScore =
      completed.length > 0
        ? completed.reduce((sum, p) => sum + (p.healthScore || 0), 0) / completed.length
        : 0;

    return {
      totalProjects: projectsData?.totalCount || 0,
      projectsToday,
      completedToday: completed.filter((p) =>
        dayjs(p.analysisCompletedAt).isAfter(today)
      ).length,
      avgHealthScore: Math.round(avgHealthScore),
    };
  }, [projectsData]);

  // Metric cards data
  const metricCards = [
    {
      title: 'Total Projects',
      value: metrics.totalProjects,
      icon: FolderIcon,
      color: 'primary.main',
      bgColor: 'primary.light',
    },
    {
      title: 'Projects Today',
      value: metrics.projectsToday,
      icon: TrendingUpIcon,
      color: 'success.main',
      bgColor: 'success.light',
    },
    {
      title: 'Completed Today',
      value: metrics.completedToday,
      icon: CheckCircleIcon,
      color: 'info.main',
      bgColor: 'info.light',
    },
    {
      title: 'Avg Health Score',
      value: `${metrics.avgHealthScore}%`,
      icon: SpeedIcon,
      color: 'warning.main',
      bgColor: 'warning.light',
    },
  ];

  return (
    <Box>
      {/* Header */}
      <Box
        sx={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          mb: 4,
        }}
      >
        <Box>
          <Typography variant="h4" fontWeight={600} gutterBottom>
            Dashboard
          </Typography>
          <Typography variant="body2" color="text.secondary">
            Welcome back! Here's an overview of your projects.
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

      {/* Metric cards */}
      <Grid container spacing={3} sx={{ mb: 4 }}>
        {metricCards.map((card, index) => (
          <Grid item xs={12} sm={6} md={3} key={card.title}>
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: index * 0.1 }}
            >
              <Card animateOnMount={false}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
                  <Box
                    sx={{
                      p: 1.5,
                      borderRadius: 2,
                      bgcolor: card.bgColor,
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                    }}
                  >
                    <card.icon sx={{ color: card.color, fontSize: 28 }} />
                  </Box>
                  <Box>
                    <Typography variant="body2" color="text.secondary">
                      {card.title}
                    </Typography>
                    <Typography variant="h5" fontWeight={600}>
                      {isLoading ? '-' : card.value}
                    </Typography>
                  </Box>
                </Box>
              </Card>
            </motion.div>
          </Grid>
        ))}
      </Grid>

      {/* Charts row */}
      <Grid container spacing={3} sx={{ mb: 4 }}>
        {/* Projects over time */}
        <Grid item xs={12} md={8}>
          <Card title="Projects Over Time" subtitle="Last 7 days">
            <Box sx={{ height: 300 }}>
              <ResponsiveContainer width="100%" height="100%">
                <AreaChart data={mockChartData}>
                  <defs>
                    <linearGradient id="colorCount" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="5%" stopColor="#1976d2" stopOpacity={0.3} />
                      <stop offset="95%" stopColor="#1976d2" stopOpacity={0} />
                    </linearGradient>
                  </defs>
                  <CartesianGrid strokeDasharray="3 3" vertical={false} />
                  <XAxis
                    dataKey="date"
                    tickFormatter={(date) => dayjs(date).format('MMM D')}
                    axisLine={false}
                    tickLine={false}
                  />
                  <YAxis axisLine={false} tickLine={false} />
                  <Tooltip
                    labelFormatter={(date) => dayjs(date).format('MMM D, YYYY')}
                  />
                  <Area
                    type="monotone"
                    dataKey="count"
                    stroke="#1976d2"
                    strokeWidth={2}
                    fillOpacity={1}
                    fill="url(#colorCount)"
                  />
                </AreaChart>
              </ResponsiveContainer>
            </Box>
          </Card>
        </Grid>

        {/* Findings by severity */}
        <Grid item xs={12} md={4}>
          <Card title="Findings by Severity" subtitle="All projects">
            <Box sx={{ height: 300, display: 'flex', alignItems: 'center' }}>
              <ResponsiveContainer width="100%" height="100%">
                <PieChart>
                  <Pie
                    data={severityData}
                    cx="50%"
                    cy="50%"
                    innerRadius={60}
                    outerRadius={90}
                    paddingAngle={3}
                    dataKey="value"
                  >
                    {severityData.map((entry, index) => (
                      <Cell key={`cell-${index}`} fill={entry.color} />
                    ))}
                  </Pie>
                  <Tooltip />
                </PieChart>
              </ResponsiveContainer>
            </Box>
            <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, mt: 1 }}>
              {severityData.map((item) => (
                <Chip
                  key={item.name}
                  label={`${item.name}: ${item.value}`}
                  size="small"
                  sx={{
                    bgcolor: item.color + '20',
                    color: item.color,
                    fontWeight: 500,
                  }}
                />
              ))}
            </Box>
          </Card>
        </Grid>
      </Grid>

      {/* Recent projects */}
      <Card
        title="Recent Projects"
        subtitle="Your latest analysis results"
        action={
          <Button size="small" onClick={() => navigate('/projects')}>
            View All
          </Button>
        }
      >
        {isLoading ? (
          <LoadingSkeleton variant="list" count={5} />
        ) : !projectsData?.items?.length ? (
          <EmptyState
            title="No projects yet"
            description="Create your first project to get started"
            action={{
              label: 'New Project',
              onClick: () => navigate('/projects/new'),
            }}
          />
        ) : (
          <Box>
            {projectsData.items.map((project, index) => (
              <motion.div
                key={project.id}
                initial={{ opacity: 0, x: -20 }}
                animate={{ opacity: 1, x: 0 }}
                transition={{ delay: index * 0.05 }}
              >
                <Box
                  onClick={() => navigate(`/projects/${project.id}`)}
                  sx={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: 2,
                    p: 2,
                    borderRadius: 1,
                    cursor: 'pointer',
                    '&:hover': { bgcolor: 'action.hover' },
                    borderBottom:
                      index < projectsData.items.length - 1 ? 1 : 0,
                    borderColor: 'divider',
                  }}
                >
                  <Box
                    sx={{
                      width: 40,
                      height: 40,
                      borderRadius: 1,
                      bgcolor: 'primary.light',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                    }}
                  >
                    <AssessmentIcon sx={{ color: 'primary.main' }} />
                  </Box>

                  <Box sx={{ flex: 1, minWidth: 0 }}>
                    <Typography variant="body1" fontWeight={500} noWrap>
                      {project.name}
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                      {dayjs(project.createdAt).format('MMM D, YYYY h:mm A')}
                    </Typography>
                  </Box>

                  <StatusBadge status={project.status} />

                  {project.healthScore !== undefined && project.healthScore !== null && (
                    <Box sx={{ width: 100 }}>
                      <Typography variant="caption" color="text.secondary">
                        Health: {project.healthScore}%
                      </Typography>
                      <LinearProgress
                        variant="determinate"
                        value={project.healthScore}
                        sx={{
                          height: 6,
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
                  )}
                </Box>
              </motion.div>
            ))}
          </Box>
        )}
      </Card>
    </Box>
  );
}
