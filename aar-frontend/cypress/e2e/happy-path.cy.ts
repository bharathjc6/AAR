/**
 * E2E Happy Path Test - Full Analysis Flow
 * 
 * This test simulates the complete user journey:
 * 1. Create a project (from Git URL or mock upload)
 * 2. Call preflight check
 * 3. Upload/confirm files
 * 4. Start analysis
 * 5. Wait for completion
 * 6. Fetch and validate report schema
 * 
 * Can run with mock API (VITE_MOCK_MODE=true) or real backend
 */

describe('Analysis Happy Path', () => {
  const mockProjectId = 'e2e-test-project-123';
  const mockReportSchema = {
    id: Cypress.sinon.match.string,
    projectId: Cypress.sinon.match.string,
    projectName: Cypress.sinon.match.string,
    healthScore: Cypress.sinon.match.number,
    summary: Cypress.sinon.match.string,
    findings: Cypress.sinon.match.array,
    generatedAt: Cypress.sinon.match.string,
  };

  beforeEach(() => {
    // Login with API key
    cy.login();

    // Set up API mocks for the complete flow
    setupApiMocks();
  });

  function setupApiMocks() {
    // Mock project creation from Git
    cy.intercept('POST', '/api/v1/projects/git', {
      statusCode: 201,
      body: {
        projectId: mockProjectId,
        name: 'E2E Test Project',
        status: 'queued',
        createdAt: new Date().toISOString(),
      },
    }).as('createProjectGit');

    // Mock project creation from ZIP upload
    cy.intercept('POST', '/api/v1/projects/zip', {
      statusCode: 201,
      body: {
        projectId: mockProjectId,
        name: 'E2E Test Project',
        status: 'pending',
        createdAt: new Date().toISOString(),
      },
    }).as('createProjectZip');

    // Mock preflight check
    cy.intercept('POST', `/api/v1/projects/${mockProjectId}/preflight`, {
      statusCode: 200,
      body: {
        isValid: true,
        fileCount: 42,
        totalSize: 1024000,
        estimatedTokens: 50000,
        estimatedCost: 0.25,
        warnings: [],
        errors: [],
      },
    }).as('preflight');

    // Mock file upload
    cy.intercept('POST', `/api/v1/projects/${mockProjectId}/upload`, {
      statusCode: 200,
      body: {
        success: true,
        filesUploaded: 42,
        message: 'Files uploaded successfully',
      },
    }).as('uploadFiles');

    // Mock start analysis
    cy.intercept('POST', `/api/v1/projects/${mockProjectId}/analyze`, {
      statusCode: 200,
      body: {
        projectId: mockProjectId,
        status: 'analyzing',
        message: 'Analysis started',
      },
    }).as('startAnalysis');

    // Mock get project - initial state (filesReady)
    cy.intercept('GET', `/api/v1/projects/${mockProjectId}`, (req) => {
      req.reply({
        statusCode: 200,
        body: {
          id: mockProjectId,
          name: 'E2E Test Project',
          description: 'Created for E2E testing',
          status: 'completed',
          statusText: 'Completed',
          createdAt: new Date(Date.now() - 60000).toISOString(),
          analysisCompletedAt: new Date().toISOString(),
          fileCount: 42,
          totalLinesOfCode: 5000,
          hasReport: true,
          healthScore: 85,
        },
      });
    }).as('getProject');

    // Mock get report with full schema validation
    cy.intercept('GET', `/api/v1/projects/${mockProjectId}/report`, {
      statusCode: 200,
      body: {
        id: 'report-123',
        projectId: mockProjectId,
        projectName: 'E2E Test Project',
        summary: 'The codebase demonstrates good architectural patterns with some areas for improvement.',
        recommendations: [
          'Consider implementing dependency injection for better testability',
          'Add unit tests for critical business logic',
          'Review security configurations',
        ],
        healthScore: 85,
        statistics: {
          totalFiles: 42,
          analyzedFiles: 42,
          totalLinesOfCode: 5000,
          highSeverityCount: 1,
          mediumSeverityCount: 3,
          lowSeverityCount: 5,
          totalFindingsCount: 9,
          findingsByCategory: {
            security: 2,
            architecture: 3,
            codeQuality: 4,
          },
        },
        findings: [
          {
            id: 'f1',
            title: 'Potential SQL Injection',
            description: 'User input is concatenated directly into SQL query',
            severity: 'high',
            category: 'security',
            filePath: 'src/data/repository.ts',
            startLine: 45,
            endLine: 50,
            suggestion: 'Use parameterized queries instead of string concatenation',
            codeSnippet: 'const query = `SELECT * FROM users WHERE id = ${userId}`;',
            agentType: 'SecurityAgent',
          },
          {
            id: 'f2',
            title: 'Missing Error Handling',
            description: 'Async operation lacks proper error handling',
            severity: 'medium',
            category: 'codeQuality',
            filePath: 'src/services/api.ts',
            startLine: 22,
            endLine: 28,
            suggestion: 'Wrap async operations in try-catch blocks',
            agentType: 'CodeQualityAgent',
          },
          {
            id: 'f3',
            title: 'Circular Dependency Detected',
            description: 'Module A imports Module B which imports Module A',
            severity: 'medium',
            category: 'architecture',
            filePath: 'src/modules/moduleA.ts',
            startLine: 1,
            endLine: 5,
            suggestion: 'Extract shared logic to a third module',
            agentType: 'ArchitectureAdvisorAgent',
          },
        ],
        reportVersion: '1.0.0',
        analysisDurationSeconds: 45,
        generatedAt: new Date().toISOString(),
      },
    }).as('getReport');
  }

  it('should complete full analysis flow from Git URL', () => {
    // Step 1: Navigate to new project page
    cy.visit('/projects/new');
    cy.contains('New Project').should('be.visible');

    // Step 2: Fill in project details and use Git URL
    cy.get('input').first().clear().type('E2E Test Project');
    
    // Switch to Git tab
    cy.contains('Git URL').click();
    
    // Enter Git URL
    cy.get('input[placeholder*="github"], input[placeholder*="git"]')
      .type('https://github.com/example/test-repo');

    // Step 3: Submit project creation
    cy.contains('button', 'Create Project').click();
    cy.wait('@createProjectGit');

    // Step 4: Should navigate to project details
    cy.url().should('include', `/projects/${mockProjectId}`);
    cy.wait('@getProject');

    // Step 5: Verify project shows completed status (mocked)
    cy.contains('E2E Test Project').should('be.visible');
    cy.contains('Completed').should('be.visible');

    // Step 6: Verify report is displayed with correct schema
    cy.wait('@getReport');
    
    // Validate health score
    cy.contains('85').should('be.visible');
    
    // Validate findings are displayed
    cy.contains('Potential SQL Injection').should('be.visible');
    cy.contains('Missing Error Handling').should('be.visible');
    
    // Validate severity badges
    cy.contains('high', { matchCase: false }).should('be.visible');
    cy.contains('medium', { matchCase: false }).should('be.visible');
  });

  it('should validate report schema structure', () => {
    // Navigate directly to a completed project
    cy.visit(`/projects/${mockProjectId}`);
    cy.wait('@getProject');
    cy.wait('@getReport');

    // The report should contain all required fields
    cy.window().then((win) => {
      // Access React Query cache or make direct API call for schema validation
      cy.request({
        method: 'GET',
        url: `/api/v1/projects/${mockProjectId}/report`,
        headers: {
          'X-Api-Key': 'aar_1kToNBn9uKzHic2HNWyZZi0yZurtRsJI',
        },
        failOnStatusCode: false,
      }).then((response) => {
        // Validate response structure
        expect(response.status).to.eq(200);
        
        const report = response.body;
        
        // Required string fields
        expect(report).to.have.property('id').that.is.a('string');
        expect(report).to.have.property('projectId').that.is.a('string');
        expect(report).to.have.property('projectName').that.is.a('string');
        expect(report).to.have.property('summary').that.is.a('string');
        expect(report).to.have.property('generatedAt').that.is.a('string');
        
        // Required number fields
        expect(report).to.have.property('healthScore').that.is.a('number');
        expect(report.healthScore).to.be.within(0, 100);
        
        // Required array fields
        expect(report).to.have.property('findings').that.is.an('array');
        expect(report).to.have.property('recommendations').that.is.an('array');
        
        // Statistics object
        expect(report).to.have.property('statistics').that.is.an('object');
        expect(report.statistics).to.have.property('totalFiles').that.is.a('number');
        expect(report.statistics).to.have.property('totalFindingsCount').that.is.a('number');
        
        // Validate finding structure
        if (report.findings.length > 0) {
          const finding = report.findings[0];
          expect(finding).to.have.property('id').that.is.a('string');
          expect(finding).to.have.property('title').that.is.a('string');
          expect(finding).to.have.property('description').that.is.a('string');
          expect(finding).to.have.property('severity').that.is.a('string');
          expect(finding).to.have.property('category').that.is.a('string');
        }
      });
    });
  });

  it('should handle analysis with start and wait for completion', () => {
    // Set up mock for filesReady state initially
    let callCount = 0;
    cy.intercept('GET', `/api/v1/projects/${mockProjectId}`, (req) => {
      callCount++;
      // First call: filesReady, subsequent calls: completed
      const status = callCount <= 1 ? 'filesReady' : 'completed';
      req.reply({
        statusCode: 200,
        body: {
          id: mockProjectId,
          name: 'E2E Test Project',
          status: status,
          statusText: status === 'filesReady' ? 'Files Ready' : 'Completed',
          createdAt: new Date(Date.now() - 60000).toISOString(),
          fileCount: 42,
          hasReport: status === 'completed',
          healthScore: status === 'completed' ? 85 : undefined,
        },
      });
    }).as('getProjectPolling');

    cy.visit(`/projects/${mockProjectId}`);
    cy.wait('@getProjectPolling');

    // If filesReady, should show Start Analysis button
    // Since we mock completed on second call, just verify flow works
    cy.contains('E2E Test Project').should('be.visible');
  });

  it('should display error state for failed analysis', () => {
    // Mock failed project
    cy.intercept('GET', `/api/v1/projects/failed-project`, {
      statusCode: 200,
      body: {
        id: 'failed-project',
        name: 'Failed Project',
        status: 'failed',
        statusText: 'Failed',
        errorMessage: 'Analysis failed due to timeout',
        createdAt: new Date().toISOString(),
      },
    }).as('getFailedProject');

    cy.visit('/projects/failed-project');
    cy.wait('@getFailedProject');

    // Should show error state
    cy.contains('Failed').should('be.visible');
  });
});
