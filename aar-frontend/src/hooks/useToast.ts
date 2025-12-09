import { useSnackbar, VariantType } from 'notistack';
import { useCallback } from 'react';

/**
 * Hook for showing toast notifications with common presets
 */
export function useToast() {
  const { enqueueSnackbar, closeSnackbar } = useSnackbar();

  const toast = useCallback(
    (message: string, variant: VariantType = 'default') => {
      return enqueueSnackbar(message, { variant });
    },
    [enqueueSnackbar]
  );

  const success = useCallback(
    (message: string) => {
      return enqueueSnackbar(message, { variant: 'success' });
    },
    [enqueueSnackbar]
  );

  const error = useCallback(
    (message: string) => {
      return enqueueSnackbar(message, { variant: 'error' });
    },
    [enqueueSnackbar]
  );

  const warning = useCallback(
    (message: string) => {
      return enqueueSnackbar(message, { variant: 'warning' });
    },
    [enqueueSnackbar]
  );

  const info = useCallback(
    (message: string) => {
      return enqueueSnackbar(message, { variant: 'info' });
    },
    [enqueueSnackbar]
  );

  return {
    toast,
    success,
    error,
    warning,
    info,
    close: closeSnackbar,
  };
}
