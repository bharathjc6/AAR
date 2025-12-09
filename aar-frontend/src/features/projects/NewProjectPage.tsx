import { useState, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Box,
  Typography,
  Button,
  TextField,
  Tab,
  Tabs,
  Alert,
  LinearProgress,
  List,
  ListItem,
  ListItemIcon,
  ListItemText,
} from '@mui/material';
import {
  CloudUpload as UploadIcon,
  GitHub as GitHubIcon,
  FolderZip as ZipIcon,
  InsertDriveFile as FileIcon,
  CheckCircle as CheckIcon,
  ArrowBack as BackIcon,
} from '@mui/icons-material';
import { useDropzone } from 'react-dropzone';
import { motion, AnimatePresence } from 'framer-motion';
import { Card } from '../../components';
import { useCreateProjectFromZip, useCreateProjectFromGit, usePreflight } from '../../hooks';
import { PreflightResponse, UploadProgress } from '../../types';

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
 * New project creation page with zip upload or git URL
 */
export default function NewProjectPage() {
  const navigate = useNavigate();

  // Tab state
  const [tabValue, setTabValue] = useState(0);

  // Form state
  const [projectName, setProjectName] = useState('');
  const [description, setDescription] = useState('');
  const [gitUrl, setGitUrl] = useState('');

  // Upload state
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [uploadProgress, setUploadProgress] = useState<UploadProgress | null>(null);
  const [preflightResult, setPreflightResult] = useState<PreflightResponse | null>(null);

  // Hooks
  const { mutate: createFromZip, isPending: isUploading } = useCreateProjectFromZip();
  const { mutate: createFromGit, isPending: isCreatingGit } = useCreateProjectFromGit();
  const { mutate: runPreflight, isPending: isPreflighting } = usePreflight();

  // Dropzone config
  const onDrop = useCallback((acceptedFiles: File[]) => {
    const file = acceptedFiles[0];
    if (file) {
      setSelectedFile(file);
      setPreflightResult(null);

      // Auto-set project name from filename if empty
      if (!projectName) {
        const name = file.name.replace(/\.zip$/i, '');
        setProjectName(name);
      }

      // Run preflight check
      runPreflight(file, {
        onSuccess: (result) => {
          setPreflightResult(result);
        },
        onError: () => {
          // Preflight failed, but we can still try to upload
          setPreflightResult({
            isValid: true,
            fileCount: 0,
            totalSize: file.size,
            estimatedTokens: 0,
            estimatedCost: 0,
            warnings: ['Could not analyze file contents'],
            errors: [],
          });
        },
      });
    }
  }, [projectName, runPreflight]);

  const { getRootProps, getInputProps, isDragActive } = useDropzone({
    onDrop,
    accept: {
      'application/zip': ['.zip'],
      'application/x-zip-compressed': ['.zip'],
    },
    maxFiles: 1,
    maxSize: 100 * 1024 * 1024, // 100MB
  });

  // Handle zip upload
  const handleZipUpload = () => {
    if (!selectedFile || !projectName.trim()) return;

    createFromZip(
      {
        name: projectName.trim(),
        file: selectedFile,
        description: description.trim() || undefined,
        onProgress: setUploadProgress,
      },
      {
        onSuccess: (data) => {
          navigate(`/projects/${data.projectId}`);
        },
      }
    );
  };

  // Handle git creation
  const handleGitCreate = () => {
    if (!gitUrl.trim() || !projectName.trim()) return;

    createFromGit(
      {
        name: projectName.trim(),
        gitRepoUrl: gitUrl.trim(),
        description: description.trim() || undefined,
      },
      {
        onSuccess: (data) => {
          navigate(`/projects/${data.projectId}`);
        },
      }
    );
  };

  // Format file size
  const formatSize = (bytes: number): string => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  const isZipFormValid = selectedFile && projectName.trim();
  const isGitFormValid = gitUrl.trim() && projectName.trim();

  return (
    <Box sx={{ maxWidth: 800, mx: 'auto' }}>
      {/* Header */}
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 3 }}>
        <Button
          startIcon={<BackIcon />}
          onClick={() => navigate('/projects')}
          sx={{ mr: 1 }}
        >
          Back
        </Button>
        <Box>
          <Typography variant="h4" fontWeight={600}>
            New Project
          </Typography>
          <Typography variant="body2" color="text.secondary">
            Upload a zip file or provide a Git repository URL
          </Typography>
        </Box>
      </Box>

      {/* Project name and description */}
      <Card sx={{ mb: 3 }}>
        <TextField
          fullWidth
          label="Project Name"
          placeholder="My Awesome Project"
          value={projectName}
          onChange={(e) => setProjectName(e.target.value)}
          required
          sx={{ mb: 2 }}
        />
        <TextField
          fullWidth
          label="Description (optional)"
          placeholder="Brief description of the project"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          multiline
          rows={2}
        />
      </Card>

      {/* Source tabs */}
      <Card>
        <Tabs
          value={tabValue}
          onChange={(_, newValue) => setTabValue(newValue)}
          sx={{ borderBottom: 1, borderColor: 'divider' }}
        >
          <Tab
            icon={<ZipIcon />}
            iconPosition="start"
            label="Upload Zip"
          />
          <Tab
            icon={<GitHubIcon />}
            iconPosition="start"
            label="Git URL"
          />
        </Tabs>

        {/* Zip Upload Tab */}
        <TabPanel value={tabValue} index={0}>
          {/* Dropzone */}
          <Box
            {...getRootProps()}
            sx={{
              border: 2,
              borderStyle: 'dashed',
              borderColor: isDragActive ? 'primary.main' : 'divider',
              borderRadius: 2,
              p: 4,
              textAlign: 'center',
              cursor: 'pointer',
              bgcolor: isDragActive ? 'primary.light' : 'background.default',
              transition: 'all 0.2s',
              '&:hover': {
                borderColor: 'primary.main',
                bgcolor: 'action.hover',
              },
            }}
          >
            <input {...getInputProps()} />
            <UploadIcon sx={{ fontSize: 48, color: 'text.secondary', mb: 2 }} />
            <Typography variant="body1" gutterBottom>
              {isDragActive
                ? 'Drop the zip file here...'
                : 'Drag & drop a zip file here, or click to select'}
            </Typography>
            <Typography variant="caption" color="text.secondary">
              Max file size: 100MB
            </Typography>
          </Box>

          {/* Selected file info */}
          <AnimatePresence>
            {selectedFile && (
              <motion.div
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0 }}
              >
                <Box sx={{ mt: 3 }}>
                  <Box
                    sx={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: 2,
                      p: 2,
                      bgcolor: 'action.hover',
                      borderRadius: 1,
                    }}
                  >
                    <ZipIcon color="primary" />
                    <Box sx={{ flex: 1 }}>
                      <Typography variant="body2" fontWeight={500}>
                        {selectedFile.name}
                      </Typography>
                      <Typography variant="caption" color="text.secondary">
                        {formatSize(selectedFile.size)}
                      </Typography>
                    </Box>
                    <Button
                      size="small"
                      color="error"
                      onClick={(e) => {
                        e.stopPropagation();
                        setSelectedFile(null);
                        setPreflightResult(null);
                      }}
                    >
                      Remove
                    </Button>
                  </Box>

                  {/* Preflight loading */}
                  {isPreflighting && (
                    <Box sx={{ mt: 2 }}>
                      <Typography variant="body2" color="text.secondary" gutterBottom>
                        Analyzing file...
                      </Typography>
                      <LinearProgress />
                    </Box>
                  )}

                  {/* Preflight results */}
                  {preflightResult && !isPreflighting && (
                    <Box sx={{ mt: 2 }}>
                      <Typography variant="subtitle2" gutterBottom>
                        Preflight Analysis
                      </Typography>
                      <List dense>
                        <ListItem>
                          <ListItemIcon>
                            <FileIcon />
                          </ListItemIcon>
                          <ListItemText
                            primary={`${preflightResult.fileCount} files detected`}
                            secondary={`Total size: ${formatSize(preflightResult.totalSize)}`}
                          />
                        </ListItem>
                        {preflightResult.estimatedTokens > 0 && (
                          <ListItem>
                            <ListItemIcon>
                              <CheckIcon color="success" />
                            </ListItemIcon>
                            <ListItemText
                              primary={`~${preflightResult.estimatedTokens.toLocaleString()} tokens estimated`}
                              secondary={`Estimated cost: $${preflightResult.estimatedCost.toFixed(4)}`}
                            />
                          </ListItem>
                        )}
                      </List>

                      {/* Warnings */}
                      {preflightResult.warnings.length > 0 && (
                        <Alert severity="warning" sx={{ mt: 1 }}>
                          {preflightResult.warnings.map((w, i) => (
                            <Typography key={i} variant="body2">
                              {w}
                            </Typography>
                          ))}
                        </Alert>
                      )}

                      {/* Errors */}
                      {preflightResult.errors.length > 0 && (
                        <Alert severity="error" sx={{ mt: 1 }}>
                          {preflightResult.errors.map((e, i) => (
                            <Typography key={i} variant="body2">
                              {e}
                            </Typography>
                          ))}
                        </Alert>
                      )}
                    </Box>
                  )}

                  {/* Upload progress */}
                  {uploadProgress && isUploading && (
                    <Box sx={{ mt: 2 }}>
                      <Typography variant="body2" color="text.secondary" gutterBottom>
                        Uploading... {uploadProgress.percentage}%
                      </Typography>
                      <LinearProgress
                        variant="determinate"
                        value={uploadProgress.percentage}
                      />
                    </Box>
                  )}
                </Box>
              </motion.div>
            )}
          </AnimatePresence>

          {/* Submit button */}
          <Box sx={{ mt: 3, display: 'flex', justifyContent: 'flex-end' }}>
            <Button
              variant="contained"
              size="large"
              disabled={!isZipFormValid || isUploading || (preflightResult && !preflightResult.isValid)}
              onClick={handleZipUpload}
              startIcon={<UploadIcon />}
            >
              {isUploading ? 'Uploading...' : 'Create Project'}
            </Button>
          </Box>
        </TabPanel>

        {/* Git URL Tab */}
        <TabPanel value={tabValue} index={1}>
          <TextField
            fullWidth
            label="Git Repository URL"
            placeholder="https://github.com/owner/repo"
            value={gitUrl}
            onChange={(e) => setGitUrl(e.target.value)}
            helperText="HTTPS URLs from GitHub, GitLab, Bitbucket, or Azure DevOps"
            sx={{ mb: 3 }}
          />

          {gitUrl && (
            <Alert severity="info" sx={{ mb: 3 }}>
              The repository will be cloned and analyzed. Make sure the repository is public or
              you have configured access credentials.
            </Alert>
          )}

          <Box sx={{ display: 'flex', justifyContent: 'flex-end' }}>
            <Button
              variant="contained"
              size="large"
              disabled={!isGitFormValid || isCreatingGit}
              onClick={handleGitCreate}
              startIcon={<GitHubIcon />}
            >
              {isCreatingGit ? 'Creating...' : 'Create Project'}
            </Button>
          </Box>
        </TabPanel>
      </Card>
    </Box>
  );
}
