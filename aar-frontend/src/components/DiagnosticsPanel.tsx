import React, { useState } from 'react';
import {
  Box,
  Paper,
  Typography,
  IconButton,
  Collapse,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Chip,
  Button,
  Switch,
  FormControlLabel,
  Tooltip,
  Divider,
  Alert,
} from '@mui/material';
import {
  BugReport as BugReportIcon,
  ExpandMore as ExpandMoreIcon,
  ExpandLess as ExpandLessIcon,
  Delete as DeleteIcon,
  Download as DownloadIcon,
  Refresh as RefreshIcon,
} from '@mui/icons-material';
import { ApiLogEntry, PerformanceMetrics, calculateMetrics } from '../hooks/useApiLogger';

interface DiagnosticsPanelProps {
  logs: ApiLogEntry[];
  isEnabled: boolean;
  onToggle: () => void;
  onClear: () => void;
  onExport: () => void;
}

/**
 * Diagnostics Panel Component
 * Shows API request logs and performance metrics
 * Toggle visibility with developer tools
 */
export function DiagnosticsPanel({
  logs,
  isEnabled,
  onToggle,
  onClear,
  onExport,
}: DiagnosticsPanelProps) {
  const [isExpanded, setIsExpanded] = useState(false);
  const [showPanel, setShowPanel] = useState(() => {
    return localStorage.getItem('aar-show-diagnostics') === 'true';
  });

  const metrics = calculateMetrics(logs);

  const toggleShowPanel = () => {
    const newValue = !showPanel;
    setShowPanel(newValue);
    localStorage.setItem('aar-show-diagnostics', String(newValue));
  };

  // Developer toggle button (always visible in dev mode)
  if (!showPanel) {
    return (
      <Tooltip title="Show Diagnostics Panel">
        <IconButton
          onClick={toggleShowPanel}
          sx={{
            position: 'fixed',
            bottom: 16,
            right: 16,
            bgcolor: 'background.paper',
            boxShadow: 2,
            '&:hover': { bgcolor: 'action.hover' },
          }}
          size="small"
        >
          <BugReportIcon fontSize="small" />
        </IconButton>
      </Tooltip>
    );
  }

  return (
    <Paper
      elevation={3}
      sx={{
        position: 'fixed',
        bottom: 0,
        left: 0,
        right: 0,
        maxHeight: isExpanded ? '50vh' : 'auto',
        zIndex: 1200,
        overflow: 'hidden',
      }}
    >
      {/* Header */}
      <Box
        sx={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          p: 1,
          bgcolor: 'primary.main',
          color: 'primary.contrastText',
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <BugReportIcon fontSize="small" />
          <Typography variant="subtitle2">API Diagnostics</Typography>
          <Chip
            label={`${logs.length} requests`}
            size="small"
            sx={{ bgcolor: 'rgba(255,255,255,0.2)', color: 'inherit' }}
          />
        </Box>

        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <FormControlLabel
            control={
              <Switch
                checked={isEnabled}
                onChange={onToggle}
                size="small"
                sx={{
                  '& .MuiSwitch-switchBase.Mui-checked': {
                    color: 'white',
                  },
                }}
              />
            }
            label={<Typography variant="caption">Logging</Typography>}
            sx={{ m: 0 }}
          />
          
          <Tooltip title="Export Logs">
            <IconButton size="small" onClick={onExport} sx={{ color: 'inherit' }}>
              <DownloadIcon fontSize="small" />
            </IconButton>
          </Tooltip>
          
          <Tooltip title="Clear Logs">
            <IconButton size="small" onClick={onClear} sx={{ color: 'inherit' }}>
              <DeleteIcon fontSize="small" />
            </IconButton>
          </Tooltip>
          
          <Tooltip title="Hide Panel">
            <IconButton size="small" onClick={toggleShowPanel} sx={{ color: 'inherit' }}>
              <Typography variant="caption">×</Typography>
            </IconButton>
          </Tooltip>
          
          <IconButton
            size="small"
            onClick={() => setIsExpanded(!isExpanded)}
            sx={{ color: 'inherit' }}
          >
            {isExpanded ? <ExpandLessIcon /> : <ExpandMoreIcon />}
          </IconButton>
        </Box>
      </Box>

      {/* Metrics Summary */}
      <Box sx={{ display: 'flex', gap: 2, p: 1, bgcolor: 'background.default' }}>
        <MetricChip label="Avg Response" value={`${metrics.avgResponseTime}ms`} />
        <MetricChip label="Requests" value={String(metrics.totalRequests)} />
        <MetricChip
          label="Error Rate"
          value={`${metrics.errorRate.toFixed(1)}%`}
          color={metrics.errorRate > 5 ? 'error' : 'default'}
        />
        <MetricChip label="Req/min" value={String(metrics.requestsPerMinute)} />
      </Box>

      {/* Expandable Logs Table */}
      <Collapse in={isExpanded}>
        <Divider />
        <TableContainer sx={{ maxHeight: 'calc(50vh - 100px)' }}>
          <Table size="small" stickyHeader>
            <TableHead>
              <TableRow>
                <TableCell width={80}>Time</TableCell>
                <TableCell width={70}>Method</TableCell>
                <TableCell>URL</TableCell>
                <TableCell width={70} align="right">Status</TableCell>
                <TableCell width={80} align="right">Duration</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {logs.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={5} align="center">
                    <Typography variant="body2" color="text.secondary">
                      No API requests logged yet
                    </Typography>
                  </TableCell>
                </TableRow>
              ) : (
                logs.map((log) => (
                  <TableRow
                    key={log.id}
                    sx={{
                      bgcolor: log.error || (log.status && log.status >= 400)
                        ? 'error.light'
                        : 'inherit',
                    }}
                  >
                    <TableCell>
                      <Typography variant="caption" fontFamily="monospace">
                        {formatTime(log.timestamp)}
                      </Typography>
                    </TableCell>
                    <TableCell>
                      <MethodChip method={log.method} />
                    </TableCell>
                    <TableCell>
                      <Typography
                        variant="caption"
                        fontFamily="monospace"
                        sx={{
                          display: 'block',
                          maxWidth: 400,
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                          whiteSpace: 'nowrap',
                        }}
                        title={log.url}
                      >
                        {log.url}
                      </Typography>
                    </TableCell>
                    <TableCell align="right">
                      <StatusChip status={log.status} error={log.error} />
                    </TableCell>
                    <TableCell align="right">
                      <Typography variant="caption" fontFamily="monospace">
                        {log.duration !== undefined ? `${log.duration}ms` : '—'}
                      </Typography>
                    </TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        </TableContainer>
      </Collapse>
    </Paper>
  );
}

// Helper Components
function MetricChip({
  label,
  value,
  color = 'default',
}: {
  label: string;
  value: string;
  color?: 'default' | 'error';
}) {
  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
      <Typography variant="caption" color="text.secondary">
        {label}:
      </Typography>
      <Typography
        variant="caption"
        fontWeight="bold"
        color={color === 'error' ? 'error.main' : 'text.primary'}
      >
        {value}
      </Typography>
    </Box>
  );
}

function MethodChip({ method }: { method: string }) {
  const colorMap: Record<string, 'primary' | 'success' | 'warning' | 'error' | 'info'> = {
    GET: 'success',
    POST: 'primary',
    PUT: 'warning',
    DELETE: 'error',
    PATCH: 'info',
  };

  return (
    <Chip
      label={method}
      size="small"
      color={colorMap[method] || 'default'}
      sx={{ fontSize: '0.65rem', height: 18 }}
    />
  );
}

function StatusChip({ status, error }: { status?: number; error?: string }) {
  if (error) {
    return (
      <Chip
        label="ERR"
        size="small"
        color="error"
        sx={{ fontSize: '0.65rem', height: 18 }}
      />
    );
  }

  if (status === undefined) {
    return (
      <Typography variant="caption" color="text.secondary">
        ...
      </Typography>
    );
  }

  const color = status < 300 ? 'success' : status < 400 ? 'warning' : 'error';

  return (
    <Chip
      label={status}
      size="small"
      color={color}
      sx={{ fontSize: '0.65rem', height: 18 }}
    />
  );
}

function formatTime(date: Date): string {
  return date.toLocaleTimeString('en-US', {
    hour12: false,
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
}

export default DiagnosticsPanel;
