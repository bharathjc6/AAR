import { Chip, ChipProps } from '@mui/material';
import {
  CheckCircle as CheckIcon,
  Schedule as ScheduleIcon,
  PlayCircle as PlayIcon,
  Error as ErrorIcon,
  HourglassEmpty as QueuedIcon,
  Analytics as AnalyzingIcon,
} from '@mui/icons-material';
import { ProjectStatus } from '../types';
import { statusColors } from '../theme';

interface StatusBadgeProps extends Omit<ChipProps, 'color' | 'icon' | 'label'> {
  status: ProjectStatus;
  showIcon?: boolean;
}

/**
 * Status badge component for displaying project status
 */
export default function StatusBadge({ status, showIcon = true, ...props }: StatusBadgeProps) {
  const getStatusConfig = (status: ProjectStatus) => {
    switch (status) {
      case 'pending':
      case 0:
        return {
          label: 'Pending',
          color: statusColors.pending.main,
          bgColor: statusColors.pending.light,
          icon: <ScheduleIcon fontSize="small" />,
        };
      case 'queued':
      case 1:
        return {
          label: 'Queued',
          color: statusColors.queued.main,
          bgColor: statusColors.queued.light,
          icon: <QueuedIcon fontSize="small" />,
        };
      case 'filesReady':
      case 2:
        return {
          label: 'Files Ready',
          color: statusColors.processing.main,
          bgColor: statusColors.processing.light,
          icon: <PlayIcon fontSize="small" />,
        };
      case 'analyzing':
      case 3:
      case 4:
        return {
          label: 'Analyzing',
          color: statusColors.analyzing.main,
          bgColor: statusColors.analyzing.light,
          icon: <AnalyzingIcon fontSize="small" />,
        };
      case 'completed':
      case 5:
        return {
          label: 'Completed',
          color: statusColors.completed.main,
          bgColor: statusColors.completed.light,
          icon: <CheckIcon fontSize="small" />,
        };
      case 'failed':
      case 6:
        return {
          label: 'Failed',
          color: statusColors.failed.main,
          bgColor: statusColors.failed.light,
          icon: <ErrorIcon fontSize="small" />,
        };
      default:
        return {
          label: 'Unknown',
          color: '#9e9e9e',
          bgColor: '#f5f5f5',
          icon: <ScheduleIcon fontSize="small" />,
        };
    }
  };

  const config = getStatusConfig(status);

  return (
    <Chip
      label={config.label}
      icon={showIcon ? config.icon : undefined}
      size="small"
      sx={{
        bgcolor: config.bgColor,
        color: config.color,
        fontWeight: 500,
        '& .MuiChip-icon': {
          color: 'inherit',
        },
      }}
      {...props}
    />
  );
}
