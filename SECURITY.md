# Security Policy

## Reporting a Vulnerability

The AAR team takes security vulnerabilities seriously. We appreciate your efforts to responsibly disclose your findings.

### How to Report

**DO NOT** create a public GitHub issue for security vulnerabilities.

Instead, please report security vulnerabilities by emailing:
- **Email**: bharath.jc24@gmail.com
- **Subject**: [SECURITY] AAR Vulnerability Report

### What to Include

Please include the following information in your report:
- Type of vulnerability (e.g., SQL injection, XSS, path traversal)
- Full paths of affected source file(s)
- Location of the affected source code (tag/branch/commit or direct URL)
- Step-by-step instructions to reproduce the issue
- Proof-of-concept or exploit code (if possible)
- Impact of the vulnerability

### Response Timeline

- **Initial Response**: Within 48 hours
- **Status Update**: Within 7 days
- **Resolution Target**: Within 30 days for critical issues

## Security Features

### Authentication & Authorization

AAR implements multiple layers of authentication:

1. **API Key Authentication** - Machine-to-machine authentication
   - Keys are prefixed with `aar_` for easy identification
   - Keys are hashed using SHA-256 before storage
   - Rate limiting per API key

2. **JWT Bearer Authentication** (Production)
   - RS256 signing algorithm
   - Azure AD integration support
   - Role-based access control (Admin, User, System)

### Rate Limiting

Protection against abuse through multiple rate limiting strategies:
- **Fixed Window**: 100 requests per minute per IP
- **Sliding Window**: 10 requests per 10 seconds per IP
- **Token Bucket**: Burst protection for authenticated users

### Secure File Handling

The `SecureFileService` implements comprehensive file security:

1. **Upload Validation**
   - File extension whitelist (only `.zip` for uploads)
   - MIME type validation
   - File size limits (100MB default)
   - User quota management

2. **ZIP Extraction Security**
   - Path traversal prevention
   - Executable content rejection
   - ZIP bomb protection (compression ratio limits)
   - Maximum entry count limits

3. **Disallowed Content**
   - Executables: `.exe`, `.dll`, `.so`, `.dylib`
   - Scripts: `.bat`, `.cmd`, `.ps1`, `.sh`, `.vbs`
   - Installers: `.msi`, `.msp`
   - Macro-enabled Office files: `.docm`, `.xlsm`, `.pptm`

### Security Headers

All responses include OWASP-recommended security headers:
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `X-XSS-Protection: 1; mode=block`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `Content-Security-Policy` (restricted default)
- `Strict-Transport-Security` (HSTS in production)
- `Permissions-Policy` (restrictive defaults)

### Logging & Monitoring

Sensitive data protection in logs:
- API keys are redacted
- Bearer tokens are redacted
- Connection strings are masked
- Password fields are never logged

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 1.x.x   | :white_check_mark: |
| < 1.0   | :x:                |

## Security Best Practices for Deployment

### Environment Variables

Required secrets should be configured via environment variables or Azure Key Vault:

```bash
# Required
AZURE_OPENAI_ENDPOINT=your-endpoint
AZURE_OPENAI_KEY=your-key          # Store in Key Vault
API_KEY_SALT=random-salt           # Store in Key Vault

# Optional but recommended
AZURE_STORAGE_CONNECTION_STRING=   # Store in Key Vault
JWT_SECRET=                        # Store in Key Vault (or use Azure AD)
```

### Docker Security

Our Docker images follow security best practices:
- Run as non-root user
- Read-only root filesystem
- No new privileges flag
- Minimal base images
- Regular security scanning with Trivy

### Network Security

- Use HTTPS in production (enforced via middleware)
- Configure proper CORS origins
- Place behind a reverse proxy (nginx, Azure Front Door)
- Enable Azure Web Application Firewall (WAF) for additional protection

## Automated Security Scanning

### CI/CD Pipeline

Every PR and push triggers:
- **CodeQL Analysis** - SAST for C# code
- **Dependency Audit** - NuGet vulnerability scanning
- **Secret Scanning** - TruffleHog for exposed secrets
- **Container Scanning** - Trivy for Docker images

### Dependabot

Automated dependency updates:
- Weekly NuGet package updates
- GitHub Actions updates
- Docker base image updates
- Immediate security patch PRs

## Acknowledgments

We thank the following for their contributions to AAR security:
- [Your name here after responsible disclosure]

---

*Last updated: December 2025*
