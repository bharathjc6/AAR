# AAR Threat Model

## Overview

This document outlines the threat model for the Architecture Analysis & Review (AAR) application, mapping potential threats to mitigations and aligning with OWASP Top 10 2021.

## System Architecture

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   Client/CLI    │────▶│    AAR.Api      │────▶│   AAR.Worker    │
│                 │     │   (REST API)    │     │  (Background)   │
└─────────────────┘     └─────────────────┘     └─────────────────┘
                               │                        │
                               ▼                        ▼
                        ┌─────────────────┐     ┌─────────────────┐
                        │   PostgreSQL    │     │  Azure OpenAI   │
                        │   (Database)    │     │   (AI Service)  │
                        └─────────────────┘     └─────────────────┘
                               │
                               ▼
                        ┌─────────────────┐
                        │  Blob Storage   │
                        │  (Files/ZIPs)   │
                        └─────────────────┘
```

## Trust Boundaries

1. **External → API**: Untrusted client requests
2. **API → Database**: Internal, authenticated
3. **API → Worker**: Internal, via queue
4. **Worker → OpenAI**: External, authenticated
5. **API/Worker → Storage**: Internal, authenticated

---

## OWASP Top 10 Mapping

### A01:2021 - Broken Access Control

| Threat | Impact | Mitigation | Status |
|--------|--------|------------|--------|
| Unauthorized API access | HIGH | API key authentication required for all endpoints | ✅ |
| Privilege escalation | HIGH | Role-based authorization (Admin, User, System) | ✅ |
| Accessing other users' projects | HIGH | User ID scoping on all queries | ✅ |
| Direct object reference attacks | MEDIUM | GUID-based IDs, ownership validation | ✅ |

### A02:2021 - Cryptographic Failures

| Threat | Impact | Mitigation | Status |
|--------|--------|------------|--------|
| API keys stored in plaintext | HIGH | SHA-256 hashing with salt | ✅ |
| Sensitive data in transit | HIGH | HTTPS enforced, HSTS enabled | ✅ |
| Secrets in source code | HIGH | Environment variables, Azure Key Vault | ✅ |
| Weak key generation | MEDIUM | Cryptographically secure random generation | ✅ |

### A03:2021 - Injection

| Threat | Impact | Mitigation | Status |
|--------|--------|------------|--------|
| SQL injection | CRITICAL | Entity Framework Core parameterized queries | ✅ |
| Command injection | CRITICAL | No shell command execution from user input | ✅ |
| Path traversal | HIGH | SecureFileService validates all paths | ✅ |
| Log injection | MEDIUM | Structured logging, input sanitization | ✅ |

### A04:2021 - Insecure Design

| Threat | Impact | Mitigation | Status |
|--------|--------|------------|--------|
| Lack of rate limiting | HIGH | Per-IP and per-API-key rate limits | ✅ |
| No input validation | HIGH | FluentValidation on all DTOs | ✅ |
| Missing business logic validation | MEDIUM | Service layer validation | ✅ |
| No timeout on long operations | MEDIUM | Cancellation token support | ✅ |

### A05:2021 - Security Misconfiguration

| Threat | Impact | Mitigation | Status |
|--------|--------|------------|--------|
| Debug mode in production | HIGH | Environment-based configuration | ✅ |
| Default credentials | HIGH | No default API keys, forced generation | ✅ |
| Verbose error messages | MEDIUM | Exception handling middleware | ✅ |
| Missing security headers | MEDIUM | SecurityHeadersMiddleware | ✅ |
| CORS misconfiguration | MEDIUM | Strict CORS policy in production | ✅ |

### A06:2021 - Vulnerable and Outdated Components

| Threat | Impact | Mitigation | Status |
|--------|--------|------------|--------|
| Known vulnerabilities in dependencies | HIGH | Dependabot automated updates | ✅ |
| Outdated framework | MEDIUM | .NET 10 (latest LTS) | ✅ |
| Unpatched containers | HIGH | Trivy scanning in CI/CD | ✅ |

### A07:2021 - Identification and Authentication Failures

| Threat | Impact | Mitigation | Status |
|--------|--------|------------|--------|
| Brute force attacks | HIGH | Rate limiting on auth endpoints | ✅ |
| Credential stuffing | HIGH | Unique API key format with entropy | ✅ |
| Session fixation | MEDIUM | Stateless JWT tokens | ✅ |
| Weak passwords | N/A | API key only, no user passwords | ✅ |

### A08:2021 - Software and Data Integrity Failures

| Threat | Impact | Mitigation | Status |
|--------|--------|------------|--------|
| Malicious file upload | CRITICAL | SecureFileService validation, virus scan | ✅ |
| Untrusted deserialization | HIGH | System.Text.Json with strict options | ✅ |
| CI/CD tampering | MEDIUM | Protected branches, signed commits | ⏳ |
| Dependency confusion | MEDIUM | NuGet.config with trusted sources | ✅ |

### A09:2021 - Security Logging and Monitoring Failures

| Threat | Impact | Mitigation | Status |
|--------|--------|------------|--------|
| Insufficient logging | HIGH | Serilog with structured logging | ✅ |
| Sensitive data in logs | HIGH | SensitiveDataRedactor enricher | ✅ |
| No alerting | MEDIUM | Azure Monitor integration ready | ⏳ |
| Log tampering | MEDIUM | Centralized logging (Azure/ELK) | ⏳ |

### A10:2021 - Server-Side Request Forgery (SSRF)

| Threat | Impact | Mitigation | Status |
|--------|--------|------------|--------|
| SSRF via repository URL | HIGH | Git URL validation, allowlist | ✅ |
| Internal network scanning | MEDIUM | Network isolation in containers | ✅ |
| Cloud metadata access | HIGH | No arbitrary URL fetch from user input | ✅ |

---

## Threat Scenarios

### TS-1: Malicious ZIP Upload

**Scenario**: Attacker uploads a ZIP file containing path traversal entries or executables.

**Attack Vector**:
```
POST /api/projects
Content-Type: multipart/form-data

[ZIP with "../../../etc/passwd" or "malware.exe"]
```

**Mitigations**:
1. `SecureFileService` validates all ZIP entries
2. Path traversal patterns are rejected
3. Executable extensions are blocked
4. ZIP bomb protection via size/ratio limits

**Risk Level**: HIGH → **Mitigated**

### TS-2: API Key Brute Force

**Scenario**: Attacker attempts to guess valid API keys.

**Attack Vector**:
```
GET /api/projects
X-API-Key: aar_AAAA...
GET /api/projects
X-API-Key: aar_AAAB...
```

**Mitigations**:
1. 44-character keys with 256-bit entropy
2. Rate limiting: 10 requests/10 seconds
3. API key hashing prevents timing attacks

**Risk Level**: HIGH → **Mitigated**

### TS-3: Denial of Service via Large Files

**Scenario**: Attacker uploads extremely large files to exhaust storage/memory.

**Attack Vector**:
```
POST /api/projects
[100GB ZIP file]
```

**Mitigations**:
1. 100MB file size limit
2. 1GB user quota limit
3. Streaming upload processing
4. Request size limits in Kestrel

**Risk Level**: MEDIUM → **Mitigated**

### TS-4: Data Exfiltration via Error Messages

**Scenario**: Attacker triggers errors to extract sensitive information.

**Attack Vector**:
```
GET /api/projects/invalid-id
```

**Mitigations**:
1. Generic error messages to clients
2. Detailed errors only in Development
3. Exception handling middleware
4. No stack traces in production

**Risk Level**: LOW → **Mitigated**

---

## Security Controls Summary

| Control Category | Controls Implemented |
|-----------------|---------------------|
| Authentication | API Keys, JWT Bearer, Azure AD ready |
| Authorization | Role-based (Admin, User, System) |
| Input Validation | FluentValidation, SecureFileService |
| Output Encoding | System.Text.Json encoding |
| Cryptography | SHA-256, secure random, HTTPS |
| Error Handling | Global exception middleware |
| Logging | Serilog with redaction |
| Rate Limiting | Fixed window, sliding window, token bucket |
| Security Headers | OWASP recommended headers |
| Dependency Management | Dependabot, NuGet audit |

---

## Residual Risks

| Risk | Likelihood | Impact | Mitigation Plan |
|------|------------|--------|-----------------|
| Zero-day in .NET | Low | High | Monitor security advisories |
| OpenAI API abuse | Medium | Medium | Quota limits, monitoring |
| Insider threat | Low | High | Audit logging, least privilege |
| DDoS attack | Medium | High | Azure DDoS Protection, WAF |

---

## Review Schedule

- **Quarterly**: Review threat model
- **Per Release**: Security testing
- **Annually**: Penetration testing
- **Continuous**: Automated security scanning

---

*Last updated: December 2025*
*Next review: March 2026*
