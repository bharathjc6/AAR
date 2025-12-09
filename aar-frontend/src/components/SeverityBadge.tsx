import { Chip, ChipProps } from '@mui/material';
import {
  Warning as WarningIcon,
  Error as ErrorIcon,
  Info as InfoIcon,
  ReportProblem as CriticalIcon,
} from '@mui/icons-material';
import { Severity } from '../types';
import { severityColors } from '../theme';

interface SeverityBadgeProps extends Omit<ChipProps, 'color' | 'icon' | 'label'> {
  severity: Severity;
  showIcon?: boolean;
  count?: number;
}

/**
 * Severity badge component for displaying finding severity
 */
export default function SeverityBadge({
  severity,
  showIcon = true,
  count,
  ...props
}: SeverityBadgeProps) {
  const getSeverityConfig = (severity: Severity) => {
    switch (severity) {
      case 'critical':
      case 0:
        return {
          label: 'Critical',
          color: severityColors.critical.main,
          bgColor: severityColors.critical.light,
          icon: <CriticalIcon fontSize="small" />,
        };
      case 'high':
      case 1:
        return {
          label: 'High',
          color: severityColors.high.main,
          bgColor: severityColors.high.light,
          icon: <ErrorIcon fontSize="small" />,
        };
      case 'medium':
      case 2:
        return {
          label: 'Medium',
          color: severityColors.medium.main,
          bgColor: severityColors.medium.light,
          icon: <WarningIcon fontSize="small" />,
        };
      case 'low':
      case 3:
        return {
          label: 'Low',
          color: severityColors.low.main,
          bgColor: severityColors.low.light,
          icon: <InfoIcon fontSize="small" />,
        };
      case 'info':
      case 4:
      default:
        return {
          label: 'Info',
          color: severityColors.info.main,
          bgColor: severityColors.info.light,
          icon: <InfoIcon fontSize="small" />,
        };
    }
  };

  const config = getSeverityConfig(severity);
  const displayLabel = count !== undefined ? `${config.label} (${count})` : config.label;

  return (
    <Chip
      label={displayLabel}
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
