describe('Projects', () => {
  beforeEach(() => {
    cy.login();
  });

  describe('Projects List', () => {
    it('should display empty state when no projects', () => {
      cy.intercept('GET', '/api/v1/projects*', {
        statusCode: 200,
        body: { items: [], totalCount: 0, page: 1, pageSize: 10 },
      });

      cy.visit('/projects');
      cy.contains('No projects yet').should('be.visible');
      cy.contains('Create your first project').should('be.visible');
    });

    it('should display projects list', () => {
      cy.intercept('GET', '/api/v1/projects*', {
        statusCode: 200,
        body: {
          items: [
            {
              id: '1',
              name: 'Test Project',
              status: 'completed',
              statusText: 'Completed',
              createdAt: new Date().toISOString(),
              fileCount: 42,
              healthScore: 85,
            },
            {
              id: '2',
              name: 'Another Project',
              status: 'analyzing',
              statusText: 'Analyzing',
              createdAt: new Date().toISOString(),
              fileCount: 10,
            },
          ],
          totalCount: 2,
          page: 1,
          pageSize: 10,
        },
      });

      cy.visit('/projects');
      cy.contains('Test Project').should('be.visible');
      cy.contains('Another Project').should('be.visible');
      cy.contains('Completed').should('be.visible');
      cy.contains('Analyzing').should('be.visible');
    });

    it('should filter projects by status', () => {
      cy.intercept('GET', '/api/v1/projects*', {
        statusCode: 200,
        body: {
          items: [
            {
              id: '1',
              name: 'Completed Project',
              status: 'completed',
              statusText: 'Completed',
              createdAt: new Date().toISOString(),
            },
          ],
          totalCount: 1,
          page: 1,
          pageSize: 10,
        },
      }).as('getProjects');

      cy.visit('/projects');
      
      // Click on a status filter
      cy.contains('button', 'Completed').click();
      
      // Verify filter is applied
      cy.wait('@getProjects');
    });

    it('should navigate to new project page', () => {
      cy.intercept('GET', '/api/v1/projects*', {
        statusCode: 200,
        body: { items: [], totalCount: 0, page: 1, pageSize: 10 },
      });

      cy.visit('/projects');
      cy.contains('button', 'New Project').click();
      cy.url().should('include', '/projects/new');
    });
  });

  describe('Project Details', () => {
    it('should display project details', () => {
      cy.intercept('GET', '/api/v1/projects/123', {
        statusCode: 200,
        body: {
          id: '123',
          name: 'Test Project',
          description: 'A test project',
          status: 'completed',
          statusText: 'Completed',
          createdAt: new Date().toISOString(),
          fileCount: 42,
          totalLinesOfCode: 5000,
        },
      });

      cy.intercept('GET', '/api/v1/projects/123/report', {
        statusCode: 200,
        body: {
          projectId: '123',
          healthScore: 85,
          findings: [
            {
              id: 'f1',
              title: 'Security Issue',
              description: 'Found a potential security vulnerability',
              severity: 'high',
              category: 'security',
              filePath: 'src/auth.ts',
              startLine: 10,
              endLine: 15,
            },
          ],
          filesAnalyzed: 42,
          summary: 'Overall good quality',
        },
      });

      cy.visit('/projects/123');
      cy.contains('Test Project').should('be.visible');
      cy.contains('Completed').should('be.visible');
      cy.contains('85').should('be.visible'); // Health score
      cy.contains('Security Issue').should('be.visible');
    });

    it('should allow starting analysis', () => {
      cy.intercept('GET', '/api/v1/projects/123', {
        statusCode: 200,
        body: {
          id: '123',
          name: 'Test Project',
          status: 'filesReady',
          statusText: 'Files Ready',
          createdAt: new Date().toISOString(),
        },
      });

      cy.intercept('POST', '/api/v1/projects/123/analyze', {
        statusCode: 200,
        body: { message: 'Analysis started' },
      }).as('startAnalysis');

      cy.visit('/projects/123');
      cy.contains('button', 'Start Analysis').click();
      cy.wait('@startAnalysis');
    });
  });

  describe('New Project', () => {
    it('should display new project form', () => {
      cy.visit('/projects/new');
      cy.contains('New Project').should('be.visible');
      cy.get('input[label="Project Name"]').should('exist');
      cy.contains('Upload Zip').should('be.visible');
      cy.contains('Git URL').should('be.visible');
    });

    it('should create project from Git URL', () => {
      cy.intercept('POST', '/api/v1/projects/git', {
        statusCode: 201,
        body: {
          projectId: '456',
          name: 'Git Project',
          status: 'queued',
        },
      }).as('createProject');

      cy.visit('/projects/new');
      
      // Fill in project name
      cy.get('input').first().type('My Git Project');
      
      // Switch to Git tab
      cy.contains('Git URL').click();
      
      // Fill in Git URL
      cy.get('input[placeholder*="github"]').type('https://github.com/owner/repo');
      
      // Submit
      cy.contains('button', 'Create Project').click();
      
      cy.wait('@createProject');
      cy.url().should('include', '/projects/456');
    });
  });
});
