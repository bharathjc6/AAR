// ***********************************************************
// Custom Cypress Commands
// ***********************************************************

declare global {
  namespace Cypress {
    interface Chainable {
      /**
       * Log in with an API key
       */
      login(apiKey?: string): Chainable<void>;
      
      /**
       * Get element by data-testid
       */
      getByTestId(testId: string): Chainable<JQuery<HTMLElement>>;
      
      /**
       * Wait for loading to complete
       */
      waitForLoading(): Chainable<void>;
    }
  }
}

// Login command
Cypress.Commands.add('login', (apiKey = 'aar_1kToNBn9uKzHic2HNWyZZi0yZurtRsJI') => {
  // Set the API key in localStorage
  window.localStorage.setItem('aar-auth-storage', JSON.stringify({
    state: {
      apiKey,
      isAuthenticated: true,
    },
    version: 0,
  }));
});

// Get by test ID
Cypress.Commands.add('getByTestId', (testId: string) => {
  return cy.get(`[data-testid="${testId}"]`);
});

// Wait for loading
Cypress.Commands.add('waitForLoading', () => {
  cy.get('[role="progressbar"]', { timeout: 1000 }).should('not.exist');
});

export {};
