# AAR Comprehensive Test Plan

## Overview

This document outlines the complete test strategy for the AAR (Automated Architecture Review) system, covering:
- Backend API unit and integration tests
- Worker service tests
- Frontend unit tests
- End-to-End (E2E) tests
- Load and resource safety tests

## Test Environment Setup

### Prerequisites
- .NET 10 SDK
- Node.js 18+
- Docker & Docker Compose
- Playwright (for E2E tests)

### Quick Start Commands
```bash
# Run backend unit tests
dotnet test tests/AAR.Tests/AAR.Tests.csproj

# Run frontend unit tests
cd aar-frontend && npm test

# Run E2E tests (requires backend running)
cd aar-frontend && npx playwright test

# Run all tests via Docker Compose
docker-compose -f docker-compose.e2e.yml up --abort-on-container-exit
```

---

## 1. API Unit & Integration Tests

### 1.1 Preflight Controller Tests

| Test ID | Test Name | Purpose | Preconditions | Steps | Expected Results | Success Criteria |
|---------|-----------|---------|---------------|-------|------------------|------------------|
| PRE-001 | ValidZip_ReturnsAccepted | Verify valid zip files pass preflight | Mock preflight service | POST /api/preflight/analyze with valid zip | 200 OK, IsAccepted=true | Response contains file counts |
| PRE-002 | OversizedRepo_ReturnsRejected | Verify large repos are rejected | Configure max size limit | POST with zip > max size | 200 OK, IsAccepted=false, RejectionCode set | Rejection reason explains size limit |
| PRE-003 | GitUrl_ReturnsEstimate | Verify git URL preflight works | Mock git metadata service | POST /api/preflight with git URL | 200 OK with estimated counts | Estimate includes direct/rag/skip counts |
| PRE-004 | InvalidZipFormat_ReturnsBadRequest | Verify non-zip files rejected | None | POST with .txt file | 400 Bad Request | Error message about zip format |
| PRE-005 | EmptyFile_ReturnsBadRequest | Verify empty files rejected | None | POST with empty file | 400 Bad Request | Error message about empty file |

### 1.2 Projects Controller Tests

| Test ID | Test Name | Purpose | Preconditions | Steps | Expected Results | Success Criteria |
|---------|-----------|---------|---------------|-------|------------------|------------------|
| PROJ-001 | CreateFromZip_ValidRequest_Returns201 | Verify project creation from zip | Mock blob storage | POST /api/v1/projects with form data | 201 Created | Returns ProjectCreatedResponse with ID |
| PROJ-002 | CreateFromGit_ValidUrl_Returns201 | Verify project creation from git | Mock git clone service | POST /api/v1/projects/git with URL | 201 Created | Returns ProjectCreatedResponse |
| PROJ-003 | GetProject_ExistingId_ReturnsProject | Verify project retrieval | Project exists in DB | GET /api/v1/projects/{id} | 200 OK | Returns ProjectDetailDto |
| PROJ-004 | GetProject_NonExistentId_Returns404 | Verify 404 for missing projects | Empty DB | GET /api/v1/projects/{randomId} | 404 Not Found | Error with NotFound code |
| PROJ-005 | ListProjects_Pagination_Works | Verify pagination | Multiple projects exist | GET /api/v1/projects?page=1&pageSize=5 | 200 OK | PagedResult with correct counts |
| PROJ-006 | StartAnalysis_ValidProject_Returns202 | Verify analysis start | Project in FilesReady state | POST /api/v1/projects/{id}/analyze | 202 Accepted | Returns job ID |
| PROJ-007 | StartAnalysis_AlreadyRunning_Returns400 | Verify duplicate prevention | Project already analyzing | POST analyze | 400 Bad Request | Error about existing analysis |
| PROJ-008 | DeleteProject_CascadesAllData | Verify complete deletion | Project with report/findings | DELETE /api/v1/projects/{id} | 204 No Content | All related data removed |
| PROJ-009 | ResetAnalysis_StuckProject_Succeeds | Verify stuck project reset | Project in stuck state | POST /api/v1/projects/{id}/reset | 200 OK | Status reset to FilesReady |

### 1.3 Resumable Upload Tests

| Test ID | Test Name | Purpose | Preconditions | Steps | Expected Results | Success Criteria |
|---------|-----------|---------|---------------|-------|------------------|------------------|
| UPL-001 | InitiateUpload_ValidRequest_Returns201 | Verify upload session creation | None | POST /api/uploads | 201 Created | Returns session ID and upload URL |
| UPL-002 | UploadPart_ValidPart_Returns200 | Verify part upload | Active session exists | PUT /api/uploads/{session}/parts/1 | 200 OK | Part marked received |
| UPL-003 | UploadPart_InvalidSession_Returns404 | Verify session validation | No session | PUT with random session ID | 404 Not Found | Error message |
| UPL-004 | UploadPart_WrongPartNumber_Returns400 | Verify part number validation | Session expects part 1 | PUT with part 5 | 400 Bad Request | Error about part order |
| UPL-005 | Finalize_AllPartsUploaded_CreatesProject | Verify finalization | All parts uploaded | POST /api/uploads/{session}/finalize | 200 OK | Project created, files assembled |
| UPL-006 | Finalize_MissingParts_Returns400 | Verify incomplete upload handling | Parts 1,2 uploaded, missing 3 | POST finalize | 400 Bad Request | Error listing missing parts |
| UPL-007 | GetStatus_ActiveSession_ReturnsProgress | Verify status endpoint | Session with some parts | GET /api/uploads/{session} | 200 OK | Shows uploaded parts and total |
| UPL-008 | Cancel_ActiveSession_DeletesData | Verify cancellation cleanup | Session with parts | DELETE /api/uploads/{session} | 204 No Content | Session and parts removed |

### 1.4 Reports Controller Tests

| Test ID | Test Name | Purpose | Preconditions | Steps | Expected Results | Success Criteria |
|---------|-----------|---------|---------------|-------|------------------|------------------|
| RPT-001 | GetReport_CompletedProject_ReturnsReport | Verify report retrieval | Completed analysis | GET /api/v1/projects/{id}/report | 200 OK | Full ReportDto with findings |
| RPT-002 | GetReport_AnalyzingProject_Returns202 | Verify in-progress handling | Project analyzing | GET report | 202 Accepted | Message about not ready |
| RPT-003 | GetReport_NoReport_Returns404 | Verify missing report handling | Project without report | GET report | 404 Not Found | Appropriate error |
| RPT-004 | GetReportPdf_ValidProject_ReturnsPdf | Verify PDF generation | Completed analysis | GET /api/v1/projects/{id}/report/pdf | 200 OK, application/pdf | Valid PDF bytes |
| RPT-005 | GetChunk_ValidChunkId_ReturnsContent | Verify chunk retrieval | Chunks exist | GET /api/v1/projects/{id}/chunks/{chunkId} | 200 OK | Chunk content string |
| RPT-006 | GetChunk_InvalidChunkId_Returns404 | Verify chunk validation | No such chunk | GET with random chunk ID | 404 Not Found | Error message |

---

## 2. Worker Integration Tests

### 2.1 Indexing Flow Tests

| Test ID | Test Name | Purpose | Preconditions | Steps | Expected Results | Success Criteria |
|---------|-----------|---------|---------------|-------|------------------|------------------|
| WRK-001 | SmallRepo_DirectSendPath | Verify small files go direct to LLM | Files <10KB | Index sample files | Files marked as direct-send | No chunking occurs |
| WRK-002 | MediumRepo_RagChunkingPath | Verify medium files get chunked | Files 10KB-200KB | Index medium files | Chunks created, embeddings stored | Vector store populated |
| WRK-003 | LargeFile_SkippedPath | Verify large files are skipped | File >200KB | Index large file | File marked skipped | Report notes skipped file |
| WRK-004 | MixedRepo_CorrectRouting | Verify mixed files route correctly | Mix of file sizes | Index mixed repo | Each file routed appropriately | Routing stats match expected |

### 2.2 Analysis Flow Tests

| Test ID | Test Name | Purpose | Preconditions | Steps | Expected Results | Success Criteria |
|---------|-----------|---------|---------------|-------|------------------|------------------|
| WRK-005 | Analysis_GeneratesFindings | Verify findings generation | Indexed files | Run analysis | Findings created | Findings have severity/category |
| WRK-006 | Analysis_SynthesizesReport | Verify report synthesis | Agent findings complete | Complete analysis | Report with summary | Health score calculated |
| WRK-007 | Analysis_EmitsPartialResults | Verify SignalR integration | Analysis running | Monitor SignalR | Partial findings emitted | Frontend receives updates |

### 2.3 Checkpoint & Resume Tests

| Test ID | Test Name | Purpose | Preconditions | Steps | Expected Results | Success Criteria |
|---------|-----------|---------|---------------|-------|------------------|------------------|
| WRK-008 | Checkpoint_CreatedDuringIndexing | Verify checkpoints saved | Indexing in progress | Index files | Checkpoints in DB | Checkpoint has file progress |
| WRK-009 | Resume_FromCheckpoint | Verify resume works | Checkpoint exists | Start worker | Processing resumes | No duplicate work |
| WRK-010 | Crash_Recovery_Completes | Verify crash recovery | Simulate crash | Restart worker | Job completes | Final report generated |

### 2.4 Concurrency Tests

| Test ID | Test Name | Purpose | Preconditions | Steps | Expected Results | Success Criteria |
|---------|-----------|---------|---------------|-------|------------------|------------------|
| WRK-011 | ConcurrencyLimit_Respected | Verify bounded concurrency | Concurrency limit = 5 | Queue 20 files | Max 5 concurrent | Semaphore enforced |
| WRK-012 | EmbeddingConcurrency_Bounded | Verify embedding concurrency | Limit = 3 | Request many embeddings | Max 3 concurrent | No overload |

---

## 3. Frontend Unit Tests

### 3.1 API Client Tests

| Test ID | Test Name | Purpose | Preconditions | Steps | Expected Results | Success Criteria |
|---------|-----------|---------|---------------|-------|------------------|------------------|
| FE-001 | AxiosInstance_AddsApiKey | Verify API key header | API key set | Make request | X-Api-Key header present | Correct key value |
| FE-002 | ProjectsApi_List_MapsResponse | Verify response mapping | Mock response | Call projectsApi.list() | Typed ProjectListItem[] | All fields mapped |
| FE-003 | ProjectsApi_CreateFromZip_SendsFormData | Verify form data format | File selected | Call createFromZip() | FormData sent correctly | Name and file included |
| FE-004 | ReportsApi_GetChunk_ReturnsContent | Verify chunk fetch | Mock chunk response | Call getChunk() | String content returned | Content matches mock |

### 3.2 Hook Tests

| Test ID | Test Name | Purpose | Preconditions | Steps | Expected Results | Success Criteria |
|---------|-----------|---------|---------------|-------|------------------|------------------|
| FE-005 | UseSignalR_ConnectsToHub | Verify SignalR connection | Mock hub | Mount hook | Connection established | Status = connected |
| FE-006 | UseSignalR_ReceivesProgress | Verify progress updates | Connected hub | Emit progress event | State updated | Progress object correct |
| FE-007 | UseSignalR_ReceivesFinding | Verify finding updates | Connected hub | Emit finding event | Finding added to state | Finding properties correct |
| FE-008 | UseProjects_FetchesList | Verify project list hook | Mock API | Mount hook | Projects loaded | Query state correct |

### 3.3 Component Tests

| Test ID | Test Name | Purpose | Preconditions | Steps | Expected Results | Success Criteria |
|---------|-----------|---------|---------------|-------|------------------|------------------|
| FE-009 | PreflightResults_ShowsCounts | Verify preflight display | Preflight response | Render component | Counts displayed | Direct/RAG/Skip shown |
| FE-010 | UploadProgress_ShowsPercentage | Verify upload progress UI | Upload in progress | Render with progress | Percentage shown | Bar animates |
| FE-011 | ReportViewer_DisplaysFindings | Verify findings display | Report with findings | Render component | Findings listed | Severity colors correct |
| FE-012 | CodeViewModal_ShowsChunk | Verify chunk modal | Chunk ID provided | Open modal | Chunk content shown | Syntax highlighted |

---

## 4. End-to-End Tests (Playwright)

### 4.1 Happy Path Scenarios

| Test ID | Test Name | Purpose | Preconditions | Steps | Expected Results | Success Criteria |
|---------|-----------|---------|---------------|-------|------------------|------------------|
| E2E-001 | ZipUpload_HappyPath | Complete zip upload flow | Sample zip file | Login → Upload → Preflight → Analyze → Report | Report displayed | All steps complete |
| E2E-002 | GitUrl_HappyPath | Complete git URL flow | Mock git clone | Login → Enter URL → Analyze → Report | Report displayed | Git cloning simulated |
| E2E-003 | ViewFinding_CodeChunk | Verify code view | Completed report | Click finding → View code | Code modal shows chunk | Syntax highlighting works |
| E2E-004 | DownloadReport_Pdf | Verify PDF download | Completed report | Click Download PDF | PDF file downloaded | File is valid PDF |

### 4.2 RAG Path Scenarios

| Test ID | Test Name | Purpose | Preconditions | Steps | Expected Results | Success Criteria |
|---------|-----------|---------|---------------|-------|------------------|------------------|
| E2E-005 | MediumRepo_RagProcessing | Verify RAG path | Medium-size repo zip | Upload → Analyze | Chunks created | Evidence linked |
| E2E-006 | LargeFile_SkippedMarker | Verify skip handling | Repo with >200KB file | Upload → Analyze → Report | Skipped file noted | UI shows skip reason |

### 4.3 Resumable Upload Scenarios

| Test ID | Test Name | Purpose | Preconditions | Steps | Expected Results | Success Criteria |
|---------|-----------|---------|---------------|-------|------------------|------------------|
| E2E-007 | ResumableUpload_Complete | Verify chunked upload | Large file | Upload in parts | All parts assembled | Project created |
| E2E-008 | ResumableUpload_NetworkError | Verify retry on error | Simulate network fail | Upload with intermittent errors | Upload resumes | Eventually succeeds |

### 4.4 Error & Edge Cases

| Test ID | Test Name | Purpose | Preconditions | Steps | Expected Results | Success Criteria |
|---------|-----------|---------|---------------|-------|------------------|------------------|
| E2E-009 | InvalidApiKey_ShowsError | Verify auth error handling | Invalid API key | Try to login | Error displayed | Friendly message |
| E2E-010 | EmptyProject_Handled | Verify empty zip handling | Empty zip file | Upload empty zip | Appropriate error | Not crash |
| E2E-011 | SignalR_Reconnect | Verify reconnection | Disconnect SignalR | Simulate disconnect | Auto-reconnect | Updates resume |

---

## 5. Load & Resource Safety Tests

### 5.1 Large Repository Tests

| Test ID | Test Name | Purpose | Preconditions | Steps | Expected Results | Success Criteria |
|---------|-----------|---------|---------------|-------|------------------|------------------|
| LOAD-001 | LargeRepo_NoOOM | Verify memory safety | 1000+ files, mixed sizes | Index full repo | No OOM exception | Memory < MaxWorkerMemoryMB |
| LOAD-002 | DiskBackedChunks_Used | Verify disk chunking | UseDiskBackedChunks=true | Index large files | Chunks on disk | Memory stable |
| LOAD-003 | Checkpoint_Resume_LargeRepo | Verify checkpoint at scale | Large repo indexing | Simulate restart | Resume from checkpoint | No lost progress |

### 5.2 Concurrency Stress Tests

| Test ID | Test Name | Purpose | Preconditions | Steps | Expected Results | Success Criteria |
|---------|-----------|---------|---------------|-------|------------------|------------------|
| LOAD-004 | Concurrent_Jobs | Verify job isolation | 5 concurrent jobs | Queue multiple jobs | All complete | No cross-contamination |
| LOAD-005 | Concurrent_Embeddings | Verify embedding queue | 100 embedding requests | Queue all | All processed | Bounded concurrency |

---

## 6. Mock Services

### MockOpenAiService
- Returns deterministic responses based on input patterns
- Simulates token counting
- Configurable delay for testing timeouts

### MockEmbeddingService
- Returns consistent vectors for same inputs
- Simulates batch processing
- Tracks call counts for concurrency testing

### MockBlobStorageService
- In-memory or temp folder storage
- Simulates upload/download
- Tracks operations for verification

### InMemoryVectorStore
- Hash-based vector similarity
- Fast for testing
- Full CRUD support

---

## 7. CI Integration

### GitHub Actions Workflows

#### e2e.yml (PR)
1. Checkout code
2. Set up .NET 10 & Node 18
3. Restore dependencies
4. Run unit tests
5. Build Docker images
6. Start services (docker-compose.e2e.yml)
7. Wait for health checks
8. Run Playwright E2E (smoke tests only)
9. Upload artifacts (screenshots, traces)
10. Tear down

#### e2e-full.yml (Nightly)
1. All PR steps
2. Full E2E test suite
3. Load tests (scaled down)
4. Performance metrics collection

---

## 8. Test Data

### Sample Files
- `test-samples/test-mixed-files.zip` - Mixed small/large files
- `test-samples/small-repo.zip` - 5 small files <10KB
- `test-samples/medium-repo.zip` - 20 files 10KB-100KB
- `test-samples/large-file-repo.zip` - Contains 1 file >200KB

### Generated Test Data
- `LargeRepoSimulationTests` generates files programmatically
- Configurable file counts and sizes
- Deterministic for reproducibility

---

## 9. Acceptance Criteria

- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes all unit/integration tests (mock services)
- [ ] Frontend `npm test` passes
- [ ] Playwright E2E happy path passes locally
- [ ] Resumable upload works end-to-end
- [ ] Preflight → Analyze → Report flow complete
- [ ] SignalR partial results received
- [ ] Large repo test proves no OOM
- [ ] Disk-backed chunking works
- [ ] Job resume from checkpoint works
- [ ] CI pipeline includes smoke tests on PR
- [ ] Nightly CI runs full test suite

---

## 10. Test Commands Reference

```bash
# Unit tests only
dotnet test --filter "Category!=Integration"

# Integration tests only
dotnet test --filter "Category=Integration"

# All backend tests
dotnet test

# Frontend unit tests
cd aar-frontend && npm test

# E2E tests (headless)
cd aar-frontend && npx playwright test

# E2E tests (headed, debug)
cd aar-frontend && npx playwright test --headed --debug

# Run specific E2E test
cd aar-frontend && npx playwright test project-flow.spec.ts

# Generate E2E test report
cd aar-frontend && npx playwright show-report
```
