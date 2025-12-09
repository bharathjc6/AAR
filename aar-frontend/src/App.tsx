import { Suspense, lazy, useEffect } from 'react';
import { Routes, Route, Navigate } from 'react-router-dom';
import { AnimatePresence } from 'framer-motion';
import Layout from './components/Layout';
import LoadingScreen from './components/LoadingScreen';
import { DiagnosticsPanel } from './components/DiagnosticsPanel';
import { useAuth } from './hooks/useAuth';
import { useApiLogger, setGlobalApiLogCallback } from './hooks/useApiLogger';

// Lazy load pages for code splitting
const LoginPage = lazy(() => import('./features/auth/LoginPage'));
const DashboardPage = lazy(() => import('./features/dashboard/DashboardPage'));
const ProjectsListPage = lazy(() => import('./features/projects/ProjectsListPage'));
const ProjectDetailsPage = lazy(() => import('./features/projects/ProjectDetailsPage'));
const NewProjectPage = lazy(() => import('./features/projects/NewProjectPage'));
const LogsPage = lazy(() => import('./features/logs/LogsPage'));
const SettingsPage = lazy(() => import('./features/settings/SettingsPage'));

/**
 * Protected Route wrapper - redirects to login if not authenticated
 */
function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth();

  if (isLoading) {
    return <LoadingScreen />;
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  return <>{children}</>;
}

/**
 * Main App component with routing
 */
function App() {
  // API logging hook for diagnostics panel
  const { logs, isEnabled, addLog, clearLogs, toggleEnabled, exportLogs } = useApiLogger();

  // Register the global log callback for axios interceptors
  useEffect(() => {
    setGlobalApiLogCallback(addLog);
    return () => setGlobalApiLogCallback(null);
  }, [addLog]);

  return (
    <>
      <AnimatePresence mode="wait">
        <Suspense fallback={<LoadingScreen />}>
          <Routes>
            {/* Public routes */}
            <Route path="/login" element={<LoginPage />} />

            {/* Protected routes with layout */}
            <Route
              path="/"
              element={
                <ProtectedRoute>
                  <Layout />
                </ProtectedRoute>
              }
            >
              <Route index element={<Navigate to="/dashboard" replace />} />
              <Route path="dashboard" element={<DashboardPage />} />
              <Route path="projects" element={<ProjectsListPage />} />
              <Route path="projects/new" element={<NewProjectPage />} />
              <Route path="projects/:id" element={<ProjectDetailsPage />} />
              <Route path="logs" element={<LogsPage />} />
              <Route path="settings" element={<SettingsPage />} />
            </Route>

            {/* 404 fallback */}
            <Route path="*" element={<Navigate to="/dashboard" replace />} />
          </Routes>
        </Suspense>
      </AnimatePresence>

      {/* Developer Diagnostics Panel */}
      {import.meta.env.DEV && (
        <DiagnosticsPanel
          logs={logs}
          isEnabled={isEnabled}
          onToggle={toggleEnabled}
          onClear={clearLogs}
          onExport={exportLogs}
        />
      )}
    </>
  );
}

export default App;
