import { Card as MuiCard, CardProps, CardContent, CardHeader, Box } from '@mui/material';
import { motion } from 'framer-motion';
import { ReactNode } from 'react';

interface CustomCardProps extends Omit<CardProps, 'title'> {
  title?: ReactNode;
  subtitle?: ReactNode;
  action?: ReactNode;
  children: ReactNode;
  noPadding?: boolean;
  hoverable?: boolean;
  animateOnMount?: boolean;
}

/**
 * Enhanced Card component with animations and consistent styling
 */
export default function Card({
  title,
  subtitle,
  action,
  children,
  noPadding = false,
  hoverable = false,
  animateOnMount = true,
  sx,
  ...props
}: CustomCardProps) {
  const cardContent = (
    <MuiCard
      sx={{
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        transition: 'box-shadow 0.2s, transform 0.2s',
        ...(hoverable && {
          cursor: 'pointer',
          '&:hover': {
            boxShadow: 4,
            transform: 'translateY(-2px)',
          },
        }),
        ...sx,
      }}
      {...props}
    >
      {(title || subtitle || action) && (
        <CardHeader
          title={title}
          subheader={subtitle}
          action={action}
          titleTypographyProps={{ variant: 'h6', fontWeight: 600 }}
          subheaderTypographyProps={{ variant: 'body2' }}
          sx={{ pb: subtitle ? 1 : 2 }}
        />
      )}
      {noPadding ? (
        <Box sx={{ flex: 1 }}>{children}</Box>
      ) : (
        <CardContent sx={{ flex: 1, pt: title ? 0 : undefined }}>{children}</CardContent>
      )}
    </MuiCard>
  );

  if (animateOnMount) {
    return (
      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.3 }}
        style={{ height: '100%' }}
      >
        {cardContent}
      </motion.div>
    );
  }

  return cardContent;
}
