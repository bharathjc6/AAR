describe('Authentication', () => {
  beforeEach(() => {
    // Clear auth state before each test
    cy.clearLocalStorage();
  });

  it('should redirect to login page when not authenticated', () => {
    cy.visit('/');
    cy.url().should('include', '/login');
  });

  it('should show login form', () => {
    cy.visit('/login');
    cy.contains('Sign in to AAR').should('be.visible');
    cy.get('input[type="password"]').should('be.visible');
    cy.contains('button', 'Sign In').should('be.visible');
  });

  it('should show error for invalid API key', () => {
    cy.visit('/login');
    cy.get('input[type="password"]').type('invalid_key');
    cy.contains('button', 'Sign In').click();
    
    // Should show error (API returns 401)
    cy.contains('Invalid API key').should('be.visible');
  });

  it('should login with valid API key', () => {
    // Intercept the health check
    cy.intercept('GET', '/api/v1/projects*', {
      statusCode: 200,
      body: { items: [], totalCount: 0, page: 1, pageSize: 10 },
    }).as('getProjects');

    cy.visit('/login');
    cy.get('input[type="password"]').type('aar_1kToNBn9uKzHic2HNWyZZi0yZurtRsJI');
    cy.contains('button', 'Sign In').click();

    // Should redirect to dashboard
    cy.url().should('include', '/dashboard');
  });

  it('should persist login state', () => {
    cy.login();
    
    // Intercept API calls
    cy.intercept('GET', '/api/v1/projects*', {
      statusCode: 200,
      body: { items: [], totalCount: 0, page: 1, pageSize: 10 },
    });

    cy.visit('/');
    
    // Should not redirect to login
    cy.url().should('include', '/dashboard');
  });
});
