import { Skeleton, Box } from '@mui/material';
import { motion } from 'framer-motion';

interface LoadingSkeletonProps {
  variant?: 'card' | 'list' | 'table' | 'chart' | 'text';
  count?: number;
}

/**
 * Reusable loading skeleton components for different content types
 */
export default function LoadingSkeleton({ variant = 'card', count = 1 }: LoadingSkeletonProps) {
  const items = Array.from({ length: count }, (_, i) => i);

  const renderSkeleton = (index: number) => {
    switch (variant) {
      case 'card':
        return (
          <motion.div
            key={index}
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ delay: index * 0.1 }}
          >
            <Box
              sx={{
                p: 2,
                bgcolor: 'background.paper',
                borderRadius: 2,
                mb: 2,
              }}
            >
              <Skeleton variant="rectangular" height={120} sx={{ borderRadius: 1, mb: 2 }} />
              <Skeleton variant="text" width="60%" height={28} />
              <Skeleton variant="text" width="80%" />
              <Skeleton variant="text" width="40%" />
            </Box>
          </motion.div>
        );

      case 'list':
        return (
          <motion.div
            key={index}
            initial={{ opacity: 0, x: -20 }}
            animate={{ opacity: 1, x: 0 }}
            transition={{ delay: index * 0.05 }}
          >
            <Box
              sx={{
                display: 'flex',
                alignItems: 'center',
                gap: 2,
                p: 2,
                bgcolor: 'background.paper',
                borderRadius: 1,
                mb: 1,
              }}
            >
              <Skeleton variant="circular" width={40} height={40} />
              <Box sx={{ flex: 1 }}>
                <Skeleton variant="text" width="40%" height={24} />
                <Skeleton variant="text" width="60%" height={20} />
              </Box>
              <Skeleton variant="rectangular" width={80} height={32} sx={{ borderRadius: 1 }} />
            </Box>
          </motion.div>
        );

      case 'table':
        return (
          <motion.div
            key={index}
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ delay: index * 0.05 }}
          >
            <Box
              sx={{
                display: 'flex',
                alignItems: 'center',
                gap: 2,
                p: 1.5,
                borderBottom: 1,
                borderColor: 'divider',
              }}
            >
              <Skeleton variant="text" width="20%" />
              <Skeleton variant="text" width="30%" />
              <Skeleton variant="text" width="15%" />
              <Skeleton variant="text" width="15%" />
              <Skeleton variant="rectangular" width={60} height={24} sx={{ borderRadius: 1 }} />
            </Box>
          </motion.div>
        );

      case 'chart':
        return (
          <motion.div
            key={index}
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
          >
            <Box sx={{ p: 2, bgcolor: 'background.paper', borderRadius: 2 }}>
              <Skeleton variant="text" width="30%" height={28} sx={{ mb: 2 }} />
              <Skeleton variant="rectangular" height={200} sx={{ borderRadius: 1 }} />
            </Box>
          </motion.div>
        );

      case 'text':
      default:
        return (
          <motion.div
            key={index}
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ delay: index * 0.1 }}
          >
            <Skeleton variant="text" width="100%" sx={{ mb: 1 }} />
          </motion.div>
        );
    }
  };

  return <>{items.map(renderSkeleton)}</>;
}
