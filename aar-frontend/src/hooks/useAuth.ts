import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';
import { User } from '../types';
import { setApiKey, getApiKey } from '../api';

interface AuthStore {
  user: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (apiKey: string, persist?: boolean) => Promise<void>;
  logout: () => void;
  setUser: (user: User | null) => void;
}

/**
 * Authentication store using Zustand
 * Stores user info in memory, optionally persists API key to session storage
 */
export const useAuthStore = create<AuthStore>()(
  persist(
    (set) => ({
      user: null,
      isAuthenticated: false,
      isLoading: false,

      login: async (apiKey: string, persistKey = false) => {
        set({ isLoading: true });

        try {
          // Store the API key
          setApiKey(apiKey, persistKey);

          // Mock user validation - in production this would validate with the server
          // For now, if the key starts with 'aar_' we consider it valid
          if (apiKey.startsWith('aar_')) {
            const mockUser: User = {
              id: 'user-1',
              name: 'Demo User',
              email: 'demo@example.com',
              isAdmin: true,
              apiKey: apiKey.substring(0, 12) + '...',
            };

            set({
              user: mockUser,
              isAuthenticated: true,
              isLoading: false,
            });
          } else {
            throw new Error('Invalid API key format');
          }
        } catch (error) {
          setApiKey(null);
          set({ isLoading: false });
          throw error;
        }
      },

      logout: () => {
        setApiKey(null);
        set({
          user: null,
          isAuthenticated: false,
        });
      },

      setUser: (user) => {
        set({
          user,
          isAuthenticated: !!user,
        });
      },
    }),
    {
      name: 'aar-auth',
      storage: createJSONStorage(() => sessionStorage),
      partialize: (state) => ({
        user: state.user,
        isAuthenticated: state.isAuthenticated,
      }),
    }
  )
);

/**
 * Hook for accessing auth state and actions
 */
export function useAuth() {
  const store = useAuthStore();

  // Check for existing API key on mount
  const existingKey = getApiKey();
  if (existingKey && !store.isAuthenticated && !store.isLoading) {
    // Try to restore session
    store.login(existingKey, false).catch(() => {
      // Silently fail - user will need to re-authenticate
    });
  }

  return store;
}
