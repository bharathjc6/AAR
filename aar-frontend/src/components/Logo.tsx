import { Box, useTheme } from '@mui/material';

interface LogoProps {
  size?: number;
}

/**
 * AAR Logo component
 */
export default function Logo({ size = 40 }: LogoProps) {
  const theme = useTheme();
  const primaryColor = theme.palette.primary.main;
  const secondaryColor = theme.palette.primary.light;

  return (
    <Box
      component="svg"
      xmlns="http://www.w3.org/2000/svg"
      viewBox="0 0 100 100"
      sx={{ width: size, height: size }}
    >
      <defs>
        <linearGradient id="logoGrad" x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" style={{ stopColor: primaryColor, stopOpacity: 1 }} />
          <stop offset="100%" style={{ stopColor: secondaryColor, stopOpacity: 1 }} />
        </linearGradient>
      </defs>
      <rect width="100" height="100" rx="15" fill="url(#logoGrad)" />
      <text
        x="50"
        y="65"
        fontFamily="Arial, sans-serif"
        fontSize="40"
        fontWeight="bold"
        textAnchor="middle"
        fill="white"
      >
        AAR
      </text>
    </Box>
  );
}
