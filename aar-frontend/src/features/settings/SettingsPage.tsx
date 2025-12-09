import { useState } from 'react';
import {
  Box,
  Typography,
  Switch,
  TextField,
  Button,
  Divider,
  Alert,
  FormControlLabel,
  InputAdornment,
  IconButton,
} from '@mui/material';
import {
  Visibility as VisibilityIcon,
  VisibilityOff as VisibilityOffIcon,
  Save as SaveIcon,
  Logout as LogoutIcon,
} from '@mui/icons-material';
import { useNavigate } from 'react-router-dom';
import { Card } from '../../components';
import { useThemeContext } from '../../theme/ThemeContext';
import { useAuthStore } from '../../hooks';

/**
 * Settings page for theme, API configuration, and logout
 */
export default function SettingsPage() {
  const navigate = useNavigate();
  const { mode, toggleColorMode } = useThemeContext();
  const { apiKey, logout } = useAuthStore();

  // State
  const [showApiKey, setShowApiKey] = useState(false);
  const [apiUrl, setApiUrl] = useState(
    import.meta.env.VITE_API_URL || 'http://localhost:5000/api/v1'
  );
  const [saved, setSaved] = useState(false);

  // Handle logout
  const handleLogout = () => {
    logout();
    navigate('/login');
  };

  // Handle save settings (for demo - would persist to localStorage in real app)
  const handleSave = () => {
    // In a real app, you would save the API URL to localStorage or a settings store
    localStorage.setItem('aar_api_url', apiUrl);
    setSaved(true);
    setTimeout(() => setSaved(false), 3000);
  };

  // Mask API key for display
  const maskedApiKey = apiKey
    ? `${apiKey.substring(0, 8)}${'*'.repeat(24)}${apiKey.substring(apiKey.length - 4)}`
    : 'Not configured';

  return (
    <Box sx={{ maxWidth: 800, mx: 'auto' }}>
      {/* Header */}
      <Box sx={{ mb: 4 }}>
        <Typography variant="h4" fontWeight={600} gutterBottom>
          Settings
        </Typography>
        <Typography variant="body2" color="text.secondary">
          Configure application preferences and API settings
        </Typography>
      </Box>

      {/* Appearance */}
      <Card sx={{ mb: 3 }}>
        <Typography variant="h6" gutterBottom>
          Appearance
        </Typography>
        <Divider sx={{ mb: 2 }} />
        
        <FormControlLabel
          control={
            <Switch
              checked={mode === 'dark'}
              onChange={toggleColorMode}
              color="primary"
            />
          }
          label="Dark Mode"
        />
        <Typography variant="body2" color="text.secondary" sx={{ mt: 1, ml: 4 }}>
          Switch between light and dark themes
        </Typography>
      </Card>

      {/* API Configuration */}
      <Card sx={{ mb: 3 }}>
        <Typography variant="h6" gutterBottom>
          API Configuration
        </Typography>
        <Divider sx={{ mb: 2 }} />

        <TextField
          fullWidth
          label="API Base URL"
          value={apiUrl}
          onChange={(e) => setApiUrl(e.target.value)}
          placeholder="http://localhost:5000/api/v1"
          helperText="The base URL of the AAR API server"
          sx={{ mb: 3 }}
        />

        <TextField
          fullWidth
          label="API Key"
          value={showApiKey ? apiKey : maskedApiKey}
          InputProps={{
            readOnly: true,
            endAdornment: (
              <InputAdornment position="end">
                <IconButton
                  onClick={() => setShowApiKey(!showApiKey)}
                  edge="end"
                >
                  {showApiKey ? <VisibilityOffIcon /> : <VisibilityIcon />}
                </IconButton>
              </InputAdornment>
            ),
          }}
          helperText="Your API key is stored securely in the browser"
          sx={{ mb: 2 }}
        />

        <Box sx={{ display: 'flex', gap: 2 }}>
          <Button
            variant="contained"
            startIcon={<SaveIcon />}
            onClick={handleSave}
          >
            Save Settings
          </Button>
        </Box>

        {saved && (
          <Alert severity="success" sx={{ mt: 2 }}>
            Settings saved successfully!
          </Alert>
        )}
      </Card>

      {/* About */}
      <Card sx={{ mb: 3 }}>
        <Typography variant="h6" gutterBottom>
          About
        </Typography>
        <Divider sx={{ mb: 2 }} />
        
        <Box sx={{ display: 'grid', gap: 1 }}>
          <Typography variant="body2">
            <strong>Application:</strong> AAR - AI Architecture Reviewer
          </Typography>
          <Typography variant="body2">
            <strong>Version:</strong> 1.0.0
          </Typography>
          <Typography variant="body2">
            <strong>Description:</strong> AI-powered code review and architecture analysis tool
          </Typography>
        </Box>
      </Card>

      {/* Danger Zone */}
      <Card sx={{ borderColor: 'error.main', borderWidth: 1, borderStyle: 'solid' }}>
        <Typography variant="h6" color="error" gutterBottom>
          Danger Zone
        </Typography>
        <Divider sx={{ mb: 2 }} />
        
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Logging out will remove your API key from this browser. You will need to
          enter it again to access the application.
        </Typography>

        <Button
          variant="outlined"
          color="error"
          startIcon={<LogoutIcon />}
          onClick={handleLogout}
        >
          Logout
        </Button>
      </Card>
    </Box>
  );
}
