import JSZip from 'jszip';

/**
 * Generate test zip files with various configurations
 */
export interface TestFileConfig {
  name: string;
  size: number;  // Size in bytes
  content?: string;
}

/**
 * Generate a string content of specified size
 */
function generateContent(size: number, pattern: string = 'x'): string {
  const baseContent = pattern.repeat(100);
  let content = '';
  while (content.length < size) {
    content += baseContent;
  }
  return content.substring(0, size);
}

/**
 * Create a Buffer containing a zip file with specified files
 */
export async function createTestZip(files: TestFileConfig[]): Promise<Buffer> {
  const zip = new JSZip();
  
  for (const file of files) {
    const content = file.content || generateContent(file.size);
    zip.file(file.name, content);
  }
  
  return await zip.generateAsync({ type: 'nodebuffer' });
}

/**
 * Predefined test configurations
 */
export const testConfigs = {
  /**
   * Small zip with just a few tiny files
   */
  small: [
    { name: 'src/index.ts', size: 100, content: 'export const hello = "world";' },
    { name: 'README.md', size: 50, content: '# Test Project\n\nSimple test.' },
  ],
  
  /**
   * Mixed sizes: some small, some large files
   */
  mixed: [
    { name: 'src/index.ts', size: 100, content: 'export const main = () => console.log("hello");' },
    { name: 'src/utils.ts', size: 200, content: 'export const add = (a: number, b: number) => a + b;' },
    { name: 'src/large-data.json', size: 50000 },  // 50KB
    { name: 'src/medium.ts', size: 5000 },         // 5KB
    { name: 'README.md', size: 1000 },              // 1KB
    { name: 'docs/api.md', size: 10000 },          // 10KB
  ],
  
  /**
   * Large files that trigger chunked upload
   */
  large: [
    { name: 'src/huge-file.json', size: 2 * 1024 * 1024 },  // 2MB
    { name: 'src/large-file.ts', size: 500000 },            // 500KB
    { name: 'src/index.ts', size: 100 },
  ],
  
  /**
   * Edge case: empty-ish files
   */
  minimal: [
    { name: 'empty.txt', size: 0, content: '' },
    { name: 'tiny.ts', size: 10, content: 'const x=1;' },
  ],
  
  /**
   * Realistic project structure
   */
  realistic: [
    { name: 'src/index.ts', size: 500, content: `
import { App } from './App';
import { config } from './config';

async function main() {
  const app = new App(config);
  await app.start();
}

main().catch(console.error);
` },
    { name: 'src/App.ts', size: 2000 },
    { name: 'src/config.ts', size: 800 },
    { name: 'src/utils/helpers.ts', size: 3000 },
    { name: 'src/utils/validators.ts', size: 1500 },
    { name: 'src/services/api.ts', size: 4000 },
    { name: 'src/services/auth.ts', size: 2500 },
    { name: 'src/types/index.ts', size: 1000 },
    { name: 'package.json', size: 500, content: JSON.stringify({
      name: 'test-project',
      version: '1.0.0',
      scripts: { start: 'node dist/index.js' }
    }, null, 2) },
    { name: 'tsconfig.json', size: 300, content: JSON.stringify({
      compilerOptions: { target: 'es2020', module: 'commonjs' }
    }, null, 2) },
    { name: 'README.md', size: 2000 },
    { name: '.gitignore', size: 100, content: 'node_modules\ndist\n.env\n' },
  ],
};

/**
 * Create a Buffer for the small test config
 */
export async function createSmallTestZip(): Promise<Buffer> {
  return createTestZip(testConfigs.small);
}

/**
 * Create a Buffer for the mixed test config
 */
export async function createMixedTestZip(): Promise<Buffer> {
  return createTestZip(testConfigs.mixed);
}

/**
 * Create a Buffer for the large test config
 */
export async function createLargeTestZip(): Promise<Buffer> {
  return createTestZip(testConfigs.large);
}

/**
 * Create a Buffer for the realistic test config
 */
export async function createRealisticTestZip(): Promise<Buffer> {
  return createTestZip(testConfigs.realistic);
}
