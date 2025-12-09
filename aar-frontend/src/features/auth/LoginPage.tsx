import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Box,
  Card,
  CardContent,
  TextField,
  Button,
  Typography,
  Alert,
  InputAdornment,
  IconButton,
  Checkbox,
  FormControlLabel,
  Link,
  Divider,
} from '@mui/material';
import {
  Visibility,
  VisibilityOff,
  Key as KeyIcon,
} from '@mui/icons-material';
import { motion } from 'framer-motion';
import { useAuth } from '../../hooks/useAuth';
import Logo from '../../components/Logo';

/**
 * Login page component with API key authentication
 */
export default function LoginPage() {
  const navigate = useNavigate();
  const { login, isLoading } = useAuth();

  const [apiKey, setApiKey] = useState('');
  const [showKey, setShowKey] = useState(false);
  const [rememberKey, setRememberKey] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    if (!apiKey.trim()) {
      setError('Please enter your API key');
      return;
    }

    try {
      await login(apiKey.trim(), rememberKey);
      navigate('/dashboard');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Authentication failed');
    }
  };

  // Demo key for easy testing
  const handleUseDemoKey = () => {
    setApiKey('aar_1kToNBn9uKzHic2HNWyZZi0yZurtRsJI');
  };

  return (
    <Box
      sx={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        bgcolor: 'background.default',
        p: 2,
      }}
    >
      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.4 }}
      >
        <Card sx={{ maxWidth: 440, width: '100%' }}>
          <CardContent sx={{ p: 4 }}>
            {/* Logo and title */}
            <Box sx={{ textAlign: 'center', mb: 4 }}>
              <Box sx={{ display: 'flex', justifyContent: 'center', mb: 2 }}>
                <Logo size={64} />
              </Box>
              <Typography variant="h4" fontWeight={700} gutterBottom>
                Welcome to AAR
              </Typography>
              <Typography variant="body2" color="text.secondary">
                Automated Architecture Review
              </Typography>
            </Box>

            {/* Error alert */}
            {error && (
              <Alert severity="error" sx={{ mb: 3 }} onClose={() => setError(null)}>
                {error}
              </Alert>
            )}

            {/* Login form */}
            <form onSubmit={handleSubmit}>
              <TextField
                fullWidth
                label="API Key"
                placeholder="aar_xxxxxxxxxx..."
                value={apiKey}
                onChange={(e) => setApiKey(e.target.value)}
                type={showKey ? 'text' : 'password'}
                InputProps={{
                  startAdornment: (
                    <InputAdornment position="start">
                      <KeyIcon color="action" />
                    </InputAdornment>
                  ),
                  endAdornment: (
                    <InputAdornment position="end">
                      <IconButton
                        onClick={() => setShowKey(!showKey)}
                        edge="end"
                        aria-label={showKey ? 'Hide API key' : 'Show API key'}
                      >
                        {showKey ? <VisibilityOff /> : <Visibility />}
                      </IconButton>
                    </InputAdornment>
                  ),
                }}
                sx={{ mb: 2 }}
              />

              <FormControlLabel
                control={
                  <Checkbox
                    checked={rememberKey}
                    onChange={(e) => setRememberKey(e.target.checked)}
                    size="small"
                  />
                }
                label={
                  <Typography variant="body2">
                    Remember for this session
                  </Typography>
                }
                sx={{ mb: 3 }}
              />

              <Button
                type="submit"
                fullWidth
                variant="contained"
                size="large"
                disabled={isLoading}
                sx={{ mb: 2 }}
              >
                {isLoading ? 'Signing in...' : 'Sign In'}
              </Button>
            </form>

            <Divider sx={{ my: 2 }}>
              <Typography variant="caption" color="text.secondary">
                OR
              </Typography>
            </Divider>

            {/* Demo key button */}
            <Button
              fullWidth
              variant="outlined"
              onClick={handleUseDemoKey}
              sx={{ mb: 3 }}
            >
              Use Demo API Key
            </Button>

            {/* Help text */}
            <Typography variant="body2" color="text.secondary" textAlign="center">
              Don't have an API key?{' '}
              <Link href="#" underline="hover">
                Contact your administrator
              </Link>
            </Typography>
          </CardContent>
        </Card>

        {/* Footer */}
        <Typography
          variant="caption"
          color="text.secondary"
          sx={{ display: 'block', textAlign: 'center', mt: 3 }}
        >
          AAR v{import.meta.env.VITE_APP_VERSION || '1.0.0'} • Powered by AI
        </Typography>
      </motion.div>
    </Box>
  );
}
