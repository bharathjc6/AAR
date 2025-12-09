import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';
import { User } from '../types';
import { setApiKey, getApiKey } from '../api';

interface AuthStore {
  user: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  _hasHydrated: boolean;
  login: (apiKey: string, persist?: boolean) => Promise<void>;
  logout: () => void;
  setUser: (user: User | null) => void;
  rehydrateApiKey: () => void;
}

/**
 * Authentication store using Zustand
 * Stores user info in memory, optionally persists API key to session storage
 */
export const useAuthStore = create<AuthStore>()(
  persist(
    (set, get) => ({
      user: null,
      isAuthenticated: false,
      isLoading: false,
      _hasHydrated: false,

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

      // Rehydrate API key from session storage if auth state was persisted
      rehydrateApiKey: () => {
        const state = get();
        if (state.isAuthenticated && !getApiKey()) {
          // Auth state was restored but API key is gone - check session storage
          const storedKey = sessionStorage.getItem('aar-api-key');
          if (storedKey) {
            setApiKey(storedKey, true);
          } else {
            // No API key available, logout
            set({ user: null, isAuthenticated: false });
          }
        }
        set({ _hasHydrated: true });
      },
    }),
    {
      name: 'aar-auth',
      storage: createJSONStorage(() => sessionStorage),
      partialize: (state) => ({
        user: state.user,
        isAuthenticated: state.isAuthenticated,
      }),
      onRehydrateStorage: () => (state) => {
        // Called after zustand has rehydrated the state from storage
        if (state) {
          state.rehydrateApiKey();
        }
      },
    }
  )
);

/**
 * Hook for accessing auth state and actions
 */
export function useAuth() {
  return useAuthStore();
}
