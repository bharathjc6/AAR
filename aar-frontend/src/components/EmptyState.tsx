import { Box, Typography, Button } from '@mui/material';
import { motion } from 'framer-motion';
import {
  Inbox as InboxIcon,
  SearchOff as SearchOffIcon,
  Error as ErrorIcon,
  Refresh as RefreshIcon,
  CheckCircle as CheckIcon,
} from '@mui/icons-material';
import { ReactNode } from 'react';

type EmptyStateVariant = 'empty' | 'no-results' | 'error' | 'success';

interface EmptyStateProps {
  /** @deprecated Use variant instead */
  type?: EmptyStateVariant;
  variant?: EmptyStateVariant;
  title?: string;
  description?: string;
  action?: {
    label: string;
    onClick: () => void;
  } | ReactNode;
  icon?: React.ReactNode;
}

/**
 * Empty state component for showing when there's no data
 */
export default function EmptyState({
  type,
  variant,
  title,
  description,
  action,
  icon,
}: EmptyStateProps) {
  const effectiveVariant = variant || type || 'empty';

  const getDefaultContent = () => {
    switch (effectiveVariant) {
      case 'no-results':
        return {
          icon: <SearchOffIcon sx={{ fontSize: 64 }} />,
          title: 'No results found',
          description: 'Try adjusting your search or filter criteria',
        };
      case 'error':
        return {
          icon: <ErrorIcon sx={{ fontSize: 64 }} />,
          title: 'Something went wrong',
          description: 'An error occurred while loading the data',
        };
      case 'success':
        return {
          icon: <CheckIcon sx={{ fontSize: 64 }} />,
          title: 'All good!',
          description: 'No issues found',
        };
      case 'empty':
      default:
        return {
          icon: <InboxIcon sx={{ fontSize: 64 }} />,
          title: 'No data yet',
          description: 'Get started by creating your first item',
        };
    }
  };

  const defaultContent = getDefaultContent();

  // Determine icon color based on variant
  const getIconColor = () => {
    switch (effectiveVariant) {
      case 'error':
        return 'error.main';
      case 'success':
        return 'success.main';
      default:
        return 'text.secondary';
    }
  };

  // Render action - support both object and ReactNode
  const renderAction = () => {
    if (!action) return null;
    
    // If it's a ReactNode (JSX element), render it directly
    if (typeof action !== 'object' || !('label' in action)) {
      return action;
    }
    
    // If it's an action object with label/onClick
    return (
      <Button
        variant="contained"
        onClick={action.onClick}
        startIcon={effectiveVariant === 'error' ? <RefreshIcon /> : undefined}
      >
        {action.label}
      </Button>
    );
  };

  return (
    <Box
      component={motion.div}
      initial={{ opacity: 0, y: 20 }}
      animate={{ opacity: 1, y: 0 }}
      sx={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        py: 8,
        px: 2,
        textAlign: 'center',
      }}
    >
      <Box
        sx={{
          color: getIconColor(),
          mb: 2,
          opacity: 0.7,
        }}
      >
        {icon || defaultContent.icon}
      </Box>

      <Typography
        variant="h6"
        color={effectiveVariant === 'error' ? 'error.main' : effectiveVariant === 'success' ? 'success.main' : 'text.primary'}
        gutterBottom
      >
        {title || defaultContent.title}
      </Typography>

      <Typography variant="body2" color="text.secondary" sx={{ mb: 3, maxWidth: 400 }}>
        {description || defaultContent.description}
      </Typography>

      {renderAction()}
    </Box>
  );
}

// Named export for convenience
export { EmptyState };
