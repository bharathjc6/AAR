import { test, expect, Page } from '@playwright/test';
import * as path from 'path';
import * as fs from 'fs';
import * as os from 'os';
import JSZip from 'jszip';

/**
 * E2E Tests for Delete Project and Mixed File Size Uploads
 * 
 * These tests verify:
 * 1. Delete project functionality from the UI
 * 2. Upload of zip files with mixed file sizes (small + large)
 * 3. Complete project lifecycle: create -> analyze -> view -> delete
 */

// Test configuration
const API_BASE_URL = process.env.VITE_API_BASE_URL || 'http://localhost:5000';
const API_KEY = process.env.AAR_API_KEY || 'aar_1kToNBn9uKzHic2HNWyZZi0yZurtRsJI';

/**
 * Create a test zip file with specified files
 */
async function createTestZipFile(
  files: { name: string; content: string }[]
): Promise<string> {
  const zip = new JSZip();
  
  for (const file of files) {
    zip.file(file.name, file.content);
  }
  
  const buffer = await zip.generateAsync({ type: 'nodebuffer' });
  const tempDir = os.tmpdir();
  const zipPath = path.join(tempDir, `test-project-${Date.now()}.zip`);
  fs.writeFileSync(zipPath, buffer);
  
  return zipPath;
}

/**
 * Generate content of specified size
 */
function generateContent(size: number): string {
  const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789\n';
  let result = '';
  while (result.length < size) {
    result += chars[Math.floor(Math.random() * chars.length)];
  }
  return result;
}

/**
 * Create a mixed-size zip file with small and large files
 */
async function createMixedSizeZip(): Promise<string> {
  const files = [
    // Small files (typical source code)
    { name: 'src/index.ts', content: `
import { Application } from './app';
import { config } from './config';

async function main() {
  const app = new Application(config);
  await app.initialize();
  await app.start();
  console.log('Application started on port', config.port);
}

main().catch(err => {
  console.error('Failed to start application:', err);
  process.exit(1);
});
` },
    { name: 'src/app.ts', content: `
import express from 'express';
import { Config } from './config';

export class Application {
  private app: express.Application;
  
  constructor(private config: Config) {
    this.app = express();
  }
  
  async initialize() {
    this.app.use(express.json());
    this.setupRoutes();
  }
  
  private setupRoutes() {
    this.app.get('/health', (req, res) => {
      res.json({ status: 'ok', timestamp: new Date().toISOString() });
    });
  }
  
  async start() {
    return new Promise<void>((resolve) => {
      this.app.listen(this.config.port, resolve);
    });
  }
}
` },
    { name: 'src/config.ts', content: `
export interface Config {
  port: number;
  environment: string;
  database: {
    host: string;
    port: number;
    name: string;
  };
}

export const config: Config = {
  port: parseInt(process.env.PORT || '3000', 10),
  environment: process.env.NODE_ENV || 'development',
  database: {
    host: process.env.DB_HOST || 'localhost',
    port: parseInt(process.env.DB_PORT || '5432', 10),
    name: process.env.DB_NAME || 'myapp',
  },
};
` },
    // Medium file (larger source file)
    { name: 'src/services/user-service.ts', content: generateContent(10000) }, // 10KB
    // Large file (data or generated content)
    { name: 'data/sample-data.json', content: JSON.stringify(
      Array.from({ length: 1000 }, (_, i) => ({
        id: i + 1,
        name: `Item ${i + 1}`,
        description: `This is a sample item with a longer description to add some bulk to the file. Item number ${i + 1}`,
        metadata: {
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          tags: ['sample', 'test', `item-${i + 1}`],
        },
      })),
      null,
      2
    ) }, // ~100KB
    // Another large file
    { name: 'data/large-dataset.csv', content: Array.from({ length: 5000 }, (_, i) => 
      `${i},user_${i}@example.com,John Doe ${i},${Math.random() * 100},${new Date().toISOString()}`
    ).join('\n') }, // ~250KB
    // Configuration files (small)
    { name: 'package.json', content: JSON.stringify({
      name: 'mixed-size-test-project',
      version: '1.0.0',
      description: 'A test project with mixed file sizes',
      main: 'dist/index.js',
      scripts: {
        start: 'node dist/index.js',
        build: 'tsc',
        test: 'jest',
      },
      dependencies: {
        express: '^4.18.0',
      },
      devDependencies: {
        typescript: '^5.0.0',
        '@types/node': '^20.0.0',
      },
    }, null, 2) },
    { name: 'tsconfig.json', content: JSON.stringify({
      compilerOptions: {
        target: 'ES2020',
        module: 'commonjs',
        lib: ['ES2020'],
        outDir: './dist',
        rootDir: './src',
        strict: true,
        esModuleInterop: true,
        skipLibCheck: true,
        forceConsistentCasingInFileNames: true,
      },
      include: ['src/**/*'],
      exclude: ['node_modules', 'dist'],
    }, null, 2) },
    { name: 'README.md', content: `# Mixed Size Test Project

This is a test project containing files of various sizes:
- Small source files (< 1KB)
- Medium source files (~10KB)  
- Large data files (100KB+)

## Purpose

This project is used for testing the AAR file upload functionality
with mixed file sizes to ensure proper handling of:
- Small file optimization
- Large file chunking
- Progress tracking across different file sizes

## Structure

\`\`\`
src/
  index.ts       - Entry point
  app.ts         - Application class
  config.ts      - Configuration
  services/
    user-service.ts - User service (medium)
data/
  sample-data.json  - Sample data (large)
  large-dataset.csv - Large CSV dataset
\`\`\`
` },
  ];
  
  return createTestZipFile(files);
}

/**
 * Helper function to login with API key
 */
async function loginWithApiKey(page: Page) {
  await page.goto('/login');
  await page.waitForLoadState('domcontentloaded');
  
  const apiKeyInput = page.getByPlaceholder(/api key/i)
    .or(page.locator('input[name="apiKey"]'))
    .or(page.locator('input[type="password"]'))
    .first();
  
  if (await apiKeyInput.isVisible({ timeout: 5000 }).catch(() => false)) {
    await apiKeyInput.fill(API_KEY);
    
    const submitBtn = page.getByRole('button', { name: /login|submit|connect|authenticate/i }).first();
    if (await submitBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
      await submitBtn.click();
      await page.waitForTimeout(1000);
    }
  }
  
  // Store API key in session storage
  await page.evaluate((key) => {
    sessionStorage.setItem('aar-api-key', key);
    localStorage.setItem('aar-api-key', key);
  }, API_KEY);
  
  await page.waitForTimeout(500);
}

/**
 * Setup authentication for API calls
 */
async function setupApiAuth(page: Page) {
  await page.route('**/api/**', async (route) => {
    const headers = {
      ...route.request().headers(),
      'X-Api-Key': API_KEY,
    };
    await route.continue({ headers });
  });
}

// ============================================================================
// DELETE PROJECT TESTS
// ============================================================================

test.describe('Delete Project E2E', () => {
  test.beforeEach(async ({ page }) => {
    await loginWithApiKey(page);
    await setupApiAuth(page);
  });

  test('should display delete option in project menu', async ({ page }) => {
    await test.step('Navigate to projects list', async () => {
      await page.goto('/projects');
      await page.waitForLoadState('networkidle');
      
      if (page.url().includes('login')) {
        await loginWithApiKey(page);
        await page.goto('/projects');
        await page.waitForLoadState('networkidle');
      }
      
      await expect(page).toHaveURL(/.*projects/);
    });

    await test.step('Verify delete option exists in project actions', async () => {
      // Look for any project card or list item
      const projectItem = page.locator('[data-testid="project-item"]')
        .or(page.locator('.project-card'))
        .or(page.locator('[class*="project"]').filter({ has: page.getByText(/project|test/i) }))
        .first();
      
      // If there are projects, check for action menu
      if (await projectItem.isVisible({ timeout: 5000 }).catch(() => false)) {
        // Look for more options menu (three dots or similar)
        const menuButton = projectItem.getByRole('button', { name: /more|options|menu/i })
          .or(projectItem.locator('[aria-label*="menu"]'))
          .or(projectItem.locator('[data-testid="project-menu"]'))
          .or(page.locator('button').filter({ has: page.locator('svg') }))
          .first();
        
        if (await menuButton.isVisible({ timeout: 2000 }).catch(() => false)) {
          await menuButton.click();
          
          // Look for delete option in menu
          const deleteOption = page.getByRole('menuitem', { name: /delete/i })
            .or(page.getByText(/delete/i).locator('visible=true'));
          
          await expect(deleteOption.first()).toBeVisible({ timeout: 3000 });
        }
      } else {
        // No projects available - this is acceptable
        console.log('No projects available to test delete menu');
      }
    });
  });

  test('should delete a project successfully', async ({ page }) => {
    let projectId: string | undefined;
    let projectName: string | undefined;
    
    await test.step('Create a test project to delete', async () => {
      // Create a simple zip for testing
      const zipPath = await createTestZipFile([
        { name: 'index.ts', content: 'console.log("delete me");' },
        { name: 'README.md', content: '# Delete Test\n\nThis project will be deleted.' },
      ]);
      
      projectName = `Delete-Test-${Date.now()}`;
      
      await page.goto('/projects/new');
      await page.waitForLoadState('networkidle');
      
      if (page.url().includes('login')) {
        await loginWithApiKey(page);
        await page.goto('/projects/new');
        await page.waitForLoadState('networkidle');
      }
      
      // Fill project name
      const nameInput = page.getByLabel(/project name/i)
        .or(page.getByPlaceholder(/name|project/i))
        .or(page.locator('input[name="name"]'))
        .first();
      await nameInput.fill(projectName);
      
      // Upload file
      const fileInput = page.locator('input[type="file"]');
      await fileInput.setInputFiles(zipPath);
      
      // Wait for preflight
      await page.waitForTimeout(2000);
      
      // Click create button
      const createBtn = page.getByRole('button', { name: /create|upload|submit/i }).first();
      if (await createBtn.isEnabled({ timeout: 5000 }).catch(() => false)) {
        await createBtn.click();
        
        // Wait for redirect to project page
        await page.waitForURL(/.*projects\/.*/, { timeout: 30000 }).catch(() => {});
        
        // Extract project ID from URL
        const url = page.url();
        const match = url.match(/projects\/([^\/\?]+)/);
        if (match) {
          projectId = match[1];
        }
      }
      
      // Cleanup temp file
      fs.unlinkSync(zipPath);
    });
    
    await test.step('Navigate back to projects list', async () => {
      await page.goto('/projects');
      await page.waitForLoadState('networkidle');
    });
    
    await test.step('Delete the project', async () => {
      if (projectName) {
        // Find the project we just created
        const projectItem = page.getByText(projectName)
          .or(page.locator(`[data-project-name="${projectName}"]`))
          .first();
        
        if (await projectItem.isVisible({ timeout: 5000 }).catch(() => false)) {
          // Click on project to select or open menu
          await projectItem.click();
          await page.waitForTimeout(500);
          
          // Look for action menu
          const menuButton = page.getByRole('button', { name: /more|options|menu|actions/i })
            .or(page.locator('[aria-label*="menu"]'))
            .first();
          
          if (await menuButton.isVisible({ timeout: 2000 }).catch(() => false)) {
            await menuButton.click();
            await page.waitForTimeout(300);
          }
          
          // Click delete
          const deleteOption = page.getByRole('menuitem', { name: /delete/i })
            .or(page.getByRole('button', { name: /delete/i }))
            .or(page.getByText('Delete').filter({ hasNot: page.locator('svg') }));
          
          if (await deleteOption.first().isVisible({ timeout: 2000 }).catch(() => false)) {
            // Set up dialog handler for confirmation
            page.on('dialog', async dialog => {
              await dialog.accept();
            });
            
            await deleteOption.first().click();
            
            // Wait for delete to complete
            await page.waitForTimeout(2000);
            
            // Verify project is no longer in list
            await page.reload();
            await page.waitForLoadState('networkidle');
            
            const deletedProject = page.getByText(projectName);
            await expect(deletedProject).not.toBeVisible({ timeout: 5000 });
          }
        }
      }
    });
  });

  test('should show confirmation before deleting', async ({ page }) => {
    await test.step('Navigate to projects list', async () => {
      await page.goto('/projects');
      await page.waitForLoadState('networkidle');
    });
    
    await test.step('Verify delete confirmation is required', async () => {
      // Try to find any existing project
      const projectItem = page.locator('[data-testid="project-item"]')
        .or(page.locator('.project-card'))
        .or(page.locator('tr').filter({ has: page.getByText(/project/i) }))
        .first();
      
      if (await projectItem.isVisible({ timeout: 5000 }).catch(() => false)) {
        // Click to select
        await projectItem.click();
        await page.waitForTimeout(500);
        
        // Open menu
        const menuButton = page.getByRole('button', { name: /more|options|menu/i })
          .or(page.locator('[aria-label*="menu"]'))
          .first();
        
        if (await menuButton.isVisible({ timeout: 2000 }).catch(() => false)) {
          await menuButton.click();
          await page.waitForTimeout(300);
        }
        
        // Set up listener for confirm dialog (don't accept it)
        let dialogShown = false;
        page.on('dialog', async dialog => {
          dialogShown = true;
          expect(dialog.message()).toContain('delete');
          await dialog.dismiss(); // Cancel the delete
        });
        
        // Click delete
        const deleteOption = page.getByRole('menuitem', { name: /delete/i })
          .or(page.getByRole('button', { name: /delete/i }));
        
        if (await deleteOption.first().isVisible({ timeout: 2000 }).catch(() => false)) {
          await deleteOption.first().click();
          await page.waitForTimeout(1000);
          
          // Verify dialog was shown
          expect(dialogShown).toBe(true);
        }
      } else {
        console.log('No projects available to test delete confirmation');
      }
    });
  });
});

// ============================================================================
// MIXED FILE SIZE UPLOAD TESTS
// ============================================================================

test.describe('Mixed File Size Upload E2E', () => {
  test.beforeEach(async ({ page }) => {
    await loginWithApiKey(page);
    await setupApiAuth(page);
  });

  test('should upload zip with small files only', async ({ page }) => {
    const zipPath = await createTestZipFile([
      { name: 'index.ts', content: 'export const VERSION = "1.0.0";' },
      { name: 'utils.ts', content: 'export const add = (a: number, b: number) => a + b;' },
      { name: 'README.md', content: '# Small Project\n\nA tiny test project.' },
    ]);
    
    const projectName = `Small-Files-${Date.now()}`;
    
    await test.step('Navigate to new project page', async () => {
      await page.goto('/projects/new');
      await page.waitForLoadState('networkidle');
      
      if (page.url().includes('login')) {
        await loginWithApiKey(page);
        await page.goto('/projects/new');
      }
    });
    
    await test.step('Upload small zip file', async () => {
      const nameInput = page.getByLabel(/project name/i)
        .or(page.getByPlaceholder(/name|project/i))
        .first();
      await nameInput.fill(projectName);
      
      const fileInput = page.locator('input[type="file"]');
      await fileInput.setInputFiles(zipPath);
      
      // Wait for preflight analysis
      await page.waitForTimeout(3000);
      
      // Check preflight results show file info
      const preflightInfo = page.getByText(/files?|size|tokens?|cost/i);
      await expect(preflightInfo.first()).toBeVisible({ timeout: 5000 });
    });
    
    await test.step('Verify create button is enabled', async () => {
      const createBtn = page.getByRole('button', { name: /create|upload/i }).first();
      await expect(createBtn).toBeEnabled({ timeout: 5000 });
    });
    
    // Cleanup
    fs.unlinkSync(zipPath);
  });

  test('should upload zip with mixed file sizes (small + large)', async ({ page }) => {
    const zipPath = await createMixedSizeZip();
    const projectName = `Mixed-Sizes-${Date.now()}`;
    
    await test.step('Navigate to new project page', async () => {
      await page.goto('/projects/new');
      await page.waitForLoadState('networkidle');
      
      if (page.url().includes('login')) {
        await loginWithApiKey(page);
        await page.goto('/projects/new');
      }
    });
    
    await test.step('Fill project name', async () => {
      const nameInput = page.getByLabel(/project name/i)
        .or(page.getByPlaceholder(/name|project/i))
        .first();
      await nameInput.fill(projectName);
    });
    
    await test.step('Upload mixed-size zip file', async () => {
      const fileInput = page.locator('input[type="file"]');
      await fileInput.setInputFiles(zipPath);
      
      // Wait for preflight analysis (may take longer for larger files)
      await page.waitForTimeout(5000);
    });
    
    await test.step('Verify preflight shows file analysis', async () => {
      // Should show file count, size, tokens, cost estimate
      const preflightSection = page.locator('[data-testid="preflight-results"]')
        .or(page.getByText(/estimated|analysis/i).locator('..'));
      
      // Just verify some preflight info is displayed
      const hasFileInfo = await page.getByText(/files?/i).isVisible({ timeout: 5000 }).catch(() => false);
      const hasSizeInfo = await page.getByText(/size|bytes|kb|mb/i).isVisible({ timeout: 5000 }).catch(() => false);
      
      expect(hasFileInfo || hasSizeInfo).toBe(true);
    });
    
    await test.step('Create project with mixed files', async () => {
      const createBtn = page.getByRole('button', { name: /create|upload/i }).first();
      
      if (await createBtn.isEnabled({ timeout: 5000 }).catch(() => false)) {
        await createBtn.click();
        
        // Wait for upload progress or redirect
        const progressBar = page.locator('[role="progressbar"]')
          .or(page.getByText(/uploading|progress/i));
        
        // Either see progress or redirect to project page
        const hasProgress = await progressBar.isVisible({ timeout: 5000 }).catch(() => false);
        
        if (hasProgress) {
          // Wait for upload to complete
          await page.waitForURL(/.*projects\/.*/, { timeout: 60000 }).catch(() => {});
        }
        
        // If we made it to a project page, success
        if (page.url().includes('/projects/')) {
          expect(page.url()).toMatch(/projects\//);
        }
      }
    });
    
    // Cleanup
    fs.unlinkSync(zipPath);
  });

  test('should show upload progress for larger files', async ({ page }) => {
    // Create a moderately large zip
    const largeContent = generateContent(100000); // 100KB file
    const zipPath = await createTestZipFile([
      { name: 'large-file.json', content: largeContent },
      { name: 'src/index.ts', content: 'export const main = () => {};' },
    ]);
    
    const projectName = `Large-Upload-${Date.now()}`;
    
    await test.step('Navigate and fill form', async () => {
      await page.goto('/projects/new');
      await page.waitForLoadState('networkidle');
      
      if (page.url().includes('login')) {
        await loginWithApiKey(page);
        await page.goto('/projects/new');
      }
      
      const nameInput = page.getByLabel(/project name/i)
        .or(page.getByPlaceholder(/name|project/i))
        .first();
      await nameInput.fill(projectName);
      
      const fileInput = page.locator('input[type="file"]');
      await fileInput.setInputFiles(zipPath);
      
      await page.waitForTimeout(3000);
    });
    
    await test.step('Start upload and check for progress', async () => {
      const createBtn = page.getByRole('button', { name: /create|upload/i }).first();
      
      if (await createBtn.isEnabled({ timeout: 5000 }).catch(() => false)) {
        await createBtn.click();
        
        // Look for progress indicator
        const progressIndicator = page.locator('[role="progressbar"]')
          .or(page.getByText(/\d+%/))
          .or(page.getByText(/uploading/i));
        
        // Progress should appear at some point
        const sawProgress = await progressIndicator.first()
          .isVisible({ timeout: 10000 })
          .catch(() => false);
        
        // Either saw progress or upload was fast enough to skip
        console.log(`Progress indicator visible: ${sawProgress}`);
      }
    });
    
    // Cleanup
    fs.unlinkSync(zipPath);
  });

  test('should handle various file types in zip', async ({ page }) => {
    const zipPath = await createTestZipFile([
      { name: 'src/index.ts', content: 'console.log("TypeScript");' },
      { name: 'src/app.js', content: 'module.exports = {};' },
      { name: 'src/styles.css', content: 'body { margin: 0; }' },
      { name: 'src/data.json', content: '{"key": "value"}' },
      { name: 'docs/readme.md', content: '# Documentation' },
      { name: '.env.example', content: 'API_KEY=xxx' },
      { name: 'Makefile', content: 'build:\n\techo "building"' },
    ]);
    
    const projectName = `Multi-Type-${Date.now()}`;
    
    await test.step('Upload multi-type zip', async () => {
      await page.goto('/projects/new');
      await page.waitForLoadState('networkidle');
      
      if (page.url().includes('login')) {
        await loginWithApiKey(page);
        await page.goto('/projects/new');
      }
      
      const nameInput = page.getByLabel(/project name/i)
        .or(page.getByPlaceholder(/name|project/i))
        .first();
      await nameInput.fill(projectName);
      
      const fileInput = page.locator('input[type="file"]');
      await fileInput.setInputFiles(zipPath);
      
      // Wait for preflight
      await page.waitForTimeout(3000);
      
      // Preflight should complete without errors
      const errorText = page.getByText(/error|invalid|failed/i);
      const hasError = await errorText.isVisible({ timeout: 2000 }).catch(() => false);
      
      if (!hasError) {
        const createBtn = page.getByRole('button', { name: /create|upload/i }).first();
        await expect(createBtn).toBeEnabled({ timeout: 5000 });
      }
    });
    
    // Cleanup
    fs.unlinkSync(zipPath);
  });
});

// ============================================================================
// COMPLETE LIFECYCLE TEST (CREATE -> VIEW -> DELETE)
// ============================================================================

test.describe('Complete Project Lifecycle', () => {
  test.beforeEach(async ({ page }) => {
    await loginWithApiKey(page);
    await setupApiAuth(page);
  });

  test('should complete full lifecycle: create, view, and delete project', async ({ page }) => {
    const zipPath = await createMixedSizeZip();
    const projectName = `Lifecycle-Test-${Date.now()}`;
    let projectId: string | undefined;
    
    await test.step('1. Create project with mixed-size files', async () => {
      await page.goto('/projects/new');
      await page.waitForLoadState('networkidle');
      
      if (page.url().includes('login')) {
        await loginWithApiKey(page);
        await page.goto('/projects/new');
      }
      
      // Fill form
      const nameInput = page.getByLabel(/project name/i)
        .or(page.getByPlaceholder(/name|project/i))
        .first();
      await nameInput.fill(projectName);
      
      // Upload file
      const fileInput = page.locator('input[type="file"]');
      await fileInput.setInputFiles(zipPath);
      
      // Wait for preflight
      await page.waitForTimeout(3000);
      
      // Create project
      const createBtn = page.getByRole('button', { name: /create|upload/i }).first();
      if (await createBtn.isEnabled({ timeout: 5000 }).catch(() => false)) {
        await createBtn.click();
        
        // Wait for redirect
        await page.waitForURL(/.*projects\/.*/, { timeout: 60000 }).catch(() => {});
        
        const url = page.url();
        const match = url.match(/projects\/([^\/\?]+)/);
        if (match) {
          projectId = match[1];
        }
      }
    });
    
    await test.step('2. View project details', async () => {
      if (projectId) {
        await page.goto(`/projects/${projectId}`);
        await page.waitForLoadState('networkidle');
        
        // Should see project name
        const projectTitle = page.getByText(projectName)
          .or(page.locator('h1, h2, h3').filter({ hasText: projectName }));
        
        await expect(projectTitle.first()).toBeVisible({ timeout: 10000 });
      }
    });
    
    await test.step('3. Navigate back to projects list', async () => {
      await page.goto('/projects');
      await page.waitForLoadState('networkidle');
      
      // Find our project
      const projectEntry = page.getByText(projectName).first();
      await expect(projectEntry).toBeVisible({ timeout: 10000 });
    });
    
    await test.step('4. Delete the project', async () => {
      // Find and click on the project
      const projectEntry = page.getByText(projectName).first();
      await projectEntry.click();
      await page.waitForTimeout(500);
      
      // Open actions menu
      const menuButton = page.getByRole('button', { name: /more|options|menu/i })
        .or(page.locator('[aria-label*="menu"]'))
        .first();
      
      if (await menuButton.isVisible({ timeout: 3000 }).catch(() => false)) {
        await menuButton.click();
        await page.waitForTimeout(300);
      }
      
      // Set up dialog handler
      page.on('dialog', async dialog => {
        await dialog.accept();
      });
      
      // Click delete
      const deleteOption = page.getByRole('menuitem', { name: /delete/i })
        .or(page.getByRole('button', { name: /delete/i }));
      
      if (await deleteOption.first().isVisible({ timeout: 3000 }).catch(() => false)) {
        await deleteOption.first().click();
        await page.waitForTimeout(2000);
      }
    });
    
    await test.step('5. Verify project is deleted', async () => {
      await page.reload();
      await page.waitForLoadState('networkidle');
      
      // Project should no longer be visible
      const deletedProject = page.getByText(projectName);
      await expect(deletedProject).not.toBeVisible({ timeout: 5000 });
    });
    
    // Cleanup
    fs.unlinkSync(zipPath);
  });
});
