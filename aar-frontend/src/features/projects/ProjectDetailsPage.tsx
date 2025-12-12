import { useState, useMemo } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
  Box,
  Typography,
  Button,
  Tabs,
  Tab,
  Chip,
  CircularProgress,
  Alert,
  Accordion,
  AccordionSummary,
  AccordionDetails,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  IconButton,
  Tooltip,
  LinearProgress,
  Divider,
  List,
  ListItem,
} from '@mui/material';
import Grid from '@mui/material/Grid';
import {
  ArrowBack as BackIcon,
  ExpandMore as ExpandIcon,
  PlayArrow as StartIcon,
  Refresh as RefreshIcon,
  Download as DownloadIcon,
  Code as CodeIcon,
  BugReport as BugIcon,
  Security as SecurityIcon,
  Speed as PerformanceIcon,
  Architecture as ArchitectureIcon,
  Close as CloseIcon,
  FileCopy as CopyIcon,
  CheckCircle as CheckIcon,
  Warning as WarningIcon,
  Error as ErrorIcon,
  Info as InfoIcon,
  RestartAlt as ResetIcon,
} from '@mui/icons-material';
import { motion, AnimatePresence } from 'framer-motion';
import { Card, StatusBadge, SeverityBadge, EmptyState, LoadingScreen } from '../../components';
import { useProject, useReport, useStartAnalysis, useResetProject, useDownloadReport } from '../../hooks';
import { useSignalR } from '../../hooks/useSignalR';
import { Finding, Severity, FindingCategory, ProjectStatus } from '../../types';

/**
 * Normalize status to lowercase string for comparison
 * Handles both string (PascalCase or camelCase) and numeric values
 */
const normalizeStatus = (status: ProjectStatus): string => {
  if (typeof status === 'number') {
    // Map numeric values to status strings (based on backend enum)
    const numericMap: Record<number, string> = {
      1: 'created',
      2: 'filesready',
      3: 'queued',
      4: 'analyzing',
      5: 'completed',
      6: 'failed',
    };
    return numericMap[status] || 'unknown';
  }
  return String(status).toLowerCase();
};

// Status constants for comparison (handles both string and number formats)
const isStatusCompleted = (status: ProjectStatus) => normalizeStatus(status) === 'completed';
const isStatusAnalyzing = (status: ProjectStatus) => normalizeStatus(status) === 'analyzing';
const isStatusQueued = (status: ProjectStatus) => normalizeStatus(status) === 'queued';
const isStatusFilesReady = (status: ProjectStatus) => normalizeStatus(status) === 'filesready';
const isStatusFailed = (status: ProjectStatus) => normalizeStatus(status) === 'failed';

interface TabPanelProps {
  children?: React.ReactNode;
  index: number;
  value: number;
}

function TabPanel({ children, value, index }: TabPanelProps) {
  return (
    <div role="tabpanel" hidden={value !== index}>
      {value === index && <Box sx={{ pt: 3 }}>{children}</Box>}
    </div>
  );
}

/**
 * Project details and report viewer page
 */
export default function ProjectDetailsPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  // State
  const [tabValue, setTabValue] = useState(0);
  const [codeDialogOpen, setCodeDialogOpen] = useState(false);
  const [selectedFinding, setSelectedFinding] = useState<Finding | null>(null);
  const [expandedCategory, setExpandedCategory] = useState<string | null>(null);

  // Data hooks
  const { data: project, isLoading: isLoadingProject, error: projectError, refetch: refetchProject } = useProject(id || '');
  const { data: report, isLoading: isLoadingReport, refetch: refetchReport } = useReport(id || '', { enabled: project?.status ? isStatusCompleted(project.status) : false });
  const { mutate: startAnalysis, isPending: isStarting } = useStartAnalysis();
  const { mutate: resetProject, isPending: isResetting } = useResetProject();
  const { mutate: downloadReport, isPending: isDownloading } = useDownloadReport();

  // SignalR real-time updates
  const { progress: analysisProgress, isConnected } = useSignalR({
    projectId: id,
    onStatusChange: (status) => {
      if (status === 'completed') {
        refetchProject();
        refetchReport();
      }
    },
  });

  // Computed values
  const isAnalyzing = project?.status ? isStatusAnalyzing(project.status) : false;
  const isQueued = project?.status ? isStatusQueued(project.status) : false;
  const isCompleted = project?.status ? isStatusCompleted(project.status) : false;
  const isFailed = project?.status ? isStatusFailed(project.status) : false;
  const canStartAnalysis = project?.status ? isStatusFilesReady(project.status) : false;
  const canReset = isAnalyzing || isQueued || isFailed;

  // Helper to convert numeric category to string (backend may send numeric enum values)
  const getCategoryName = (category: FindingCategory | number): string => {
    if (typeof category === 'string') return category;
    // Map numeric values to category names (based on backend enum)
    const categoryMap: Record<number, string> = {
      1: 'performance',
      2: 'security',
      3: 'architecture',
      4: 'codeQuality',
      5: 'structure',
      6: 'complexity',
      7: 'maintainability',
      8: 'bestPractice',
      99: 'other',
    };
    return categoryMap[category] || `category-${category}`;
  };

  // Group findings by category
  const findingsByCategory = useMemo(() => {
    if (!report?.findings) return {};
    return report.findings.reduce((acc, finding) => {
      const category = getCategoryName(finding.category);
      if (!acc[category]) acc[category] = [];
      acc[category].push(finding);
      return acc;
    }, {} as Record<string, Finding[]>);
  }, [report?.findings]);

  // Count findings by severity
  const severityCounts = useMemo(() => {
    const counts = { critical: 0, high: 0, medium: 0, low: 0, info: 0 };
    if (!report?.findings) return counts;
    return report.findings.reduce(
      (acc, f) => {
        const severity = typeof f.severity === 'number' 
          ? (['critical', 'high', 'medium', 'low', 'info'] as const)[f.severity] 
          : f.severity;
        if (severity in acc) acc[severity as keyof typeof acc]++;
        return acc;
      },
      counts
    );
  }, [report?.findings]);

  // Category icons - accepts both string and normalized category name
  const getCategoryIcon = (category: string) => {
    const normalizedCategory = category.toLowerCase();
    if (normalizedCategory.includes('security')) return <SecurityIcon />;
    if (normalizedCategory.includes('performance')) return <PerformanceIcon />;
    if (normalizedCategory.includes('architecture')) return <ArchitectureIcon />;
    if (normalizedCategory.includes('codequality') || normalizedCategory.includes('quality')) return <BugIcon />;
    if (normalizedCategory.includes('structure')) return <CodeIcon />;
    if (normalizedCategory.includes('complexity')) return <CodeIcon />;
    if (normalizedCategory.includes('maintainability')) return <CodeIcon />;
    if (normalizedCategory.includes('bestpractice')) return <CodeIcon />;
    return <CodeIcon />;
  };

  // Format category name for display (capitalize and add spaces)
  const formatCategoryName = (category: string): string => {
    // Handle camelCase: insert space before capital letters
    const spaced = category.replace(/([a-z])([A-Z])/g, '$1 $2');
    // Capitalize first letter
    return spaced.charAt(0).toUpperCase() + spaced.slice(1);
  };

  // Severity icon
  const getSeverityIcon = (severity: Severity) => {
    switch (severity) {
      case 'critical':
      case 'high':
      case 0:
      case 1:
        return <ErrorIcon color="error" />;
      case 'medium':
      case 2:
        return <WarningIcon color="warning" />;
      case 'low':
      case 3:
        return <InfoIcon color="info" />;
      default:
        return <CheckIcon color="success" />;
    }
  };

  // Handle start analysis
  const handleStartAnalysis = () => {
    if (!id) return;
    startAnalysis(id, {
      onSuccess: () => {
        refetchProject();
      },
    });
  };

  // Handle view code
  const handleViewCode = (finding: Finding) => {
    setSelectedFinding(finding);
    setCodeDialogOpen(true);
  };

  // Handle copy code
  const handleCopyCode = () => {
    if (selectedFinding?.codeSnippet) {
      navigator.clipboard.writeText(selectedFinding.codeSnippet);
    }
  };

  // Handle download report as PDF
  const handleDownloadReport = (format: 'pdf' | 'json' = 'pdf') => {
    if (!id || !project) return;
    downloadReport({
      projectId: id,
      format,
      projectName: project.name || 'report',
    });
  };

  // Handle reset project
  const handleResetProject = () => {
    if (!id) return;
    resetProject(id);
  };

  // Loading state
  if (isLoadingProject) {
    return <LoadingScreen message="Loading project..." />;
  }

  // Error state
  if (projectError || !project) {
    return (
      <EmptyState
        variant="error"
        title="Project not found"
        description="The project you're looking for doesn't exist or you don't have access to it."
        action={
          <Button startIcon={<BackIcon />} onClick={() => navigate('/projects')}>
            Back to Projects
          </Button>
        }
      />
    );
  }

  // Health score color
  const getHealthColor = (score: number): string => {
    if (score >= 80) return 'success.main';
    if (score >= 60) return 'warning.main';
    return 'error.main';
  };

  return (
    <Box>
      {/* Header */}
      <Box sx={{ display: 'flex', alignItems: 'flex-start', gap: 2, mb: 3 }}>
        <Button
          startIcon={<BackIcon />}
          onClick={() => navigate('/projects')}
          sx={{ mt: 0.5 }}
        >
          Back
        </Button>
        <Box sx={{ flex: 1 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 1 }}>
            <Typography variant="h4" fontWeight={600}>
              {project.name}
            </Typography>
            <StatusBadge status={project.status} />
            {isConnected && isAnalyzing && (
              <Chip
                size="small"
                label="Live"
                color="success"
                variant="outlined"
                sx={{ animation: 'pulse 2s infinite' }}
              />
            )}
          </Box>
          {project.description && (
            <Typography variant="body2" color="text.secondary">
              {project.description}
            </Typography>
          )}
          <Typography variant="caption" color="text.secondary">
            Created: {new Date(project.createdAt).toLocaleString()}
          </Typography>
        </Box>
        <Box sx={{ display: 'flex', gap: 1 }}>
          {canStartAnalysis && (
            <Button
              variant="contained"
              startIcon={<StartIcon />}
              onClick={handleStartAnalysis}
              disabled={isStarting}
            >
              {isStarting ? 'Starting...' : 'Start Analysis'}
            </Button>
          )}
          {canReset && (
            <Tooltip title="Reset project to allow re-analysis">
              <Button
                variant="outlined"
                color="warning"
                startIcon={<ResetIcon />}
                onClick={handleResetProject}
                disabled={isResetting}
              >
                {isResetting ? 'Resetting...' : 'Reset'}
              </Button>
            </Tooltip>
          )}
          {isCompleted && (
            <>
              <Button
                variant="outlined"
                startIcon={<RefreshIcon />}
                onClick={() => {
                  refetchProject();
                  refetchReport();
                }}
              >
                Refresh
              </Button>
              <Button
                variant="contained"
                startIcon={<DownloadIcon />}
                onClick={() => handleDownloadReport('pdf')}
                disabled={isDownloading}
              >
                {isDownloading ? 'Downloading...' : 'Download PDF'}
              </Button>
            </>
          )}
        </Box>
      </Box>

      {/* Analysis Progress */}
      <AnimatePresence>
        {isAnalyzing && (
          <motion.div
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: 'auto' }}
            exit={{ opacity: 0, height: 0 }}
          >
            <Card sx={{ mb: 3 }}>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 2 }}>
                <CircularProgress size={24} />
                <Typography variant="h6">Analysis in Progress</Typography>
              </Box>
              {analysisProgress ? (
                <>
                  <LinearProgress
                    variant="determinate"
                    value={analysisProgress.progressPercent ?? analysisProgress.progress ?? 0}
                    sx={{ height: 8, borderRadius: 4, mb: 1 }}
                  />
                  <Typography variant="body2" color="text.secondary">
                    {analysisProgress.phase}: {analysisProgress.currentFile || analysisProgress.message || 'Processing...'}
                  </Typography>
                  <Typography variant="caption" color="text.secondary">
                    {analysisProgress.progressPercent ?? analysisProgress.progress ?? 0}% complete
                    {analysisProgress.filesProcessed !== undefined && analysisProgress.totalFiles !== undefined && 
                      ` (${analysisProgress.filesProcessed}/${analysisProgress.totalFiles} files)`}
                  </Typography>
                </>
              ) : (
                <Typography variant="body2" color="text.secondary">
                  Waiting for progress updates...
                </Typography>
              )}
            </Card>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Report Content */}
      {isCompleted && (
        <>
          {isLoadingReport ? (
            <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
              <CircularProgress />
            </Box>
          ) : report ? (
            <>
              {/* Summary Cards */}
              <Grid container spacing={3} sx={{ mb: 3 }}>
                <Grid item xs={12} md={4}>
                  <Card>
                    <Typography variant="overline" color="text.secondary">
                      Health Score
                    </Typography>
                    <Box sx={{ display: 'flex', alignItems: 'baseline', gap: 1 }}>
                      <Typography
                        variant="h2"
                        fontWeight={700}
                        sx={{ color: getHealthColor(report.healthScore) }}
                      >
                        {report.healthScore}
                      </Typography>
                      <Typography variant="h5" color="text.secondary">
                        / 100
                      </Typography>
                    </Box>
                  </Card>
                </Grid>
                <Grid item xs={12} md={4}>
                  <Card>
                    <Typography variant="overline" color="text.secondary">
                      Total Findings
                    </Typography>
                    <Typography variant="h2" fontWeight={700}>
                      {report.findings.length}
                    </Typography>
                    <Box sx={{ display: 'flex', gap: 1, mt: 1 }}>
                      {severityCounts.critical > 0 && (
                        <Chip
                          size="small"
                          label={`${severityCounts.critical} Critical`}
                          sx={{ bgcolor: 'error.main', color: 'white' }}
                        />
                      )}
                      {severityCounts.high > 0 && (
                        <Chip
                          size="small"
                          label={`${severityCounts.high} High`}
                          sx={{ bgcolor: 'error.light', color: 'white' }}
                        />
                      )}
                    </Box>
                  </Card>
                </Grid>
                <Grid item xs={12} md={4}>
                  <Card>
                    <Typography variant="overline" color="text.secondary">
                      Files Analyzed
                    </Typography>
                    <Typography variant="h2" fontWeight={700}>
                      {report.filesAnalyzed ?? report.statistics?.analyzedFiles ?? 0}
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                      {report.totalTokens?.toLocaleString() || 0} tokens processed
                    </Typography>
                  </Card>
                </Grid>
              </Grid>

              {/* Tabs */}
              <Card>
                <Tabs
                  value={tabValue}
                  onChange={(_, newValue) => setTabValue(newValue)}
                  sx={{ borderBottom: 1, borderColor: 'divider' }}
                >
                  <Tab label="Findings" />
                  <Tab label="Summary" />
                  <Tab label="Recommendations" />
                </Tabs>

                {/* Findings Tab */}
                <TabPanel value={tabValue} index={0}>
                  {report.findings.length === 0 ? (
                    <EmptyState
                      variant="success"
                      title="No issues found"
                      description="The analysis didn't find any significant issues in your codebase."
                    />
                  ) : (
                    Object.entries(findingsByCategory).map(([category, categoryFindings]) => {
                      const findings = categoryFindings as Finding[];
                      return (
                      <Accordion
                        key={category}
                        expanded={expandedCategory === category}
                        onChange={(_, expanded) =>
                          setExpandedCategory(expanded ? category : null)
                        }
                        sx={{ mb: 1 }}
                      >
                        <AccordionSummary expandIcon={<ExpandIcon />}>
                          <Box
                            sx={{
                              display: 'flex',
                              alignItems: 'center',
                              gap: 2,
                              width: '100%',
                            }}
                          >
                            {getCategoryIcon(category)}
                            <Typography fontWeight={500}>{formatCategoryName(category)}</Typography>
                            <Chip
                              size="small"
                              label={findings.length}
                              color="primary"
                              variant="outlined"
                            />
                          </Box>
                        </AccordionSummary>
                        <AccordionDetails>
                          <List disablePadding>
                            {findings.map((finding, index) => (
                              <ListItem
                                key={finding.id || index}
                                sx={{
                                  border: 1,
                                  borderColor: 'divider',
                                  borderRadius: 1,
                                  mb: 1,
                                  flexDirection: 'column',
                                  alignItems: 'stretch',
                                }}
                              >
                                <Box
                                  sx={{
                                    display: 'flex',
                                    alignItems: 'flex-start',
                                    gap: 2,
                                    width: '100%',
                                  }}
                                >
                                  {getSeverityIcon(finding.severity)}
                                  <Box sx={{ flex: 1 }}>
                                    <Box
                                      sx={{
                                        display: 'flex',
                                        alignItems: 'center',
                                        gap: 1,
                                        mb: 0.5,
                                      }}
                                    >
                                      <Typography fontWeight={500}>
                                        {finding.title}
                                      </Typography>
                                      <SeverityBadge severity={finding.severity} />
                                    </Box>
                                    <Typography
                                      variant="body2"
                                      color="text.secondary"
                                      sx={{ mb: 1 }}
                                    >
                                      {finding.description}
                                    </Typography>
                                    <Typography
                                      variant="caption"
                                      color="text.secondary"
                                      sx={{ fontFamily: 'monospace' }}
                                    >
                                      {finding.filePath}
                                      {finding.startLine &&
                                        `:${finding.startLine}${finding.endLine ? `-${finding.endLine}` : ''}`}
                                    </Typography>
                                  </Box>
                                  {finding.codeSnippet && (
                                    <Tooltip title="View Code">
                                      <IconButton
                                        size="small"
                                        onClick={() => handleViewCode(finding)}
                                      >
                                        <CodeIcon />
                                      </IconButton>
                                    </Tooltip>
                                  )}
                                </Box>
                                {finding.suggestion && (
                                  <Alert severity="info" sx={{ mt: 2 }}>
                                    <Typography variant="body2">
                                      <strong>Suggestion:</strong>{' '}
                                      {finding.suggestion}
                                    </Typography>
                                  </Alert>
                                )}
                              </ListItem>
                            ))}
                          </List>
                        </AccordionDetails>
                      </Accordion>
                    );
                    })
                  )}
                </TabPanel>

                {/* Summary Tab */}
                <TabPanel value={tabValue} index={1}>
                  <Typography variant="body1" sx={{ whiteSpace: 'pre-wrap' }}>
                    {report.summary || 'No summary available.'}
                  </Typography>
                </TabPanel>

                {/* Recommendations Tab */}
                <TabPanel value={tabValue} index={2}>
                  {report.recommendations && report.recommendations.length > 0 ? (
                    <Box component="ul" sx={{ pl: 2, m: 0 }}>
                      {report.recommendations.map((recommendation, index) => (
                        <Typography
                          key={index}
                          component="li"
                          variant="body1"
                          sx={{ mb: 1.5 }}
                        >
                          {recommendation}
                        </Typography>
                      ))}
                    </Box>
                  ) : (
                    <Typography variant="body1" color="text.secondary">
                      No recommendations available.
                    </Typography>
                  )}
                </TabPanel>
              </Card>
            </>
          ) : (
            <Alert severity="warning">Report not available yet.</Alert>
          )}
        </>
      )}

      {/* Not completed yet message */}
      {!isCompleted && !isAnalyzing && (
        <Card>
          <EmptyState
            variant="empty"
            title={canStartAnalysis ? 'Ready for analysis' : 'Project not ready'}
            description={
              canStartAnalysis
                ? 'Click "Start Analysis" to begin analyzing your codebase.'
                : `Current status: ${project.statusText || project.status}`
            }
            action={
              canStartAnalysis ? (
                <Button
                  variant="contained"
                  startIcon={<StartIcon />}
                  onClick={handleStartAnalysis}
                  disabled={isStarting}
                >
                  Start Analysis
                </Button>
              ) : undefined
            }
          />
        </Card>
      )}

      {/* Code Snippet Dialog */}
      <Dialog
        open={codeDialogOpen}
        onClose={() => setCodeDialogOpen(false)}
        maxWidth="md"
        fullWidth
      >
        <DialogTitle>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
            <CodeIcon />
            <Box sx={{ flex: 1 }}>
              <Typography variant="h6">{selectedFinding?.title}</Typography>
              <Typography
                variant="caption"
                color="text.secondary"
                sx={{ fontFamily: 'monospace' }}
              >
                {selectedFinding?.filePath}
                {selectedFinding?.startLine &&
                  `:${selectedFinding.startLine}${selectedFinding.endLine ? `-${selectedFinding.endLine}` : ''}`}
              </Typography>
            </Box>
            <IconButton onClick={() => setCodeDialogOpen(false)}>
              <CloseIcon />
            </IconButton>
          </Box>
        </DialogTitle>
        <Divider />
        <DialogContent>
          <Box
            sx={{
              position: 'relative',
              bgcolor: 'grey.900',
              borderRadius: 1,
              p: 2,
              overflow: 'auto',
            }}
          >
            <Tooltip title="Copy code">
              <IconButton
                size="small"
                onClick={handleCopyCode}
                sx={{
                  position: 'absolute',
                  top: 8,
                  right: 8,
                  color: 'grey.400',
                }}
              >
                <CopyIcon />
              </IconButton>
            </Tooltip>
            <pre
              style={{
                margin: 0,
                fontFamily: 'Consolas, Monaco, monospace',
                fontSize: 14,
                color: '#e0e0e0',
                whiteSpace: 'pre-wrap',
                wordBreak: 'break-word',
              }}
            >
              {selectedFinding?.codeSnippet}
            </pre>
          </Box>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setCodeDialogOpen(false)}>Close</Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}
