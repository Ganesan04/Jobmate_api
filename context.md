# Jobmate API — Project Context & Architecture

> **ASP.NET Core 9 + PostgreSQL Backend for the Jobmate Flutter Application**  
> *A high-concurrency, domain-driven on-demand workforce and job marketplace backend.*

---

## 1. Executive Summary & Purpose

**Jobmate API** is a modular monolith backend built with **ASP.NET Core 9** and **PostgreSQL (Npgsql EF Core)**. It serves as the single source of truth and backend engine for the **Jobmate Flutter** mobile application.

The platform functions as an on-demand labor and job marketplace operating in India (using Indian phone format `+91`, currency INR in whole rupees, UPI/Cash payments, and geolocation proximity matching). It connects two primary user roles:
1. **Workers (`Worker`)**: Find short-term or recurring shift jobs, apply, check in to shifts using cryptographic OTP codes, receive payouts, track earnings in a dedicated digital wallet, chat with employers, and maintain verified skill/identity profiles.
2. **Work Givers (`WorkGiver`)**: Post jobs using a 6-step wizard, review applicant profiles, shortlist and atomically book workers, issue on-site check-in codes, release wages (with automatic 5% platform fee deduction), rate workers, and resolve disputes.
3. **Administrators (`Admin`)**: Monitor platform health, manage user suspensions, audit jobs, review KYC identity/skill verification documents, and resolve user disputes and support tickets.

---

## 2. Architecture & Design Principles

The codebase follows **Clean Architecture (Onion Architecture)**, **Domain-Driven Design (DDD)**, and **CQRS (Command Query Responsibility Segregation)** with **MediatR**.

```
┌────────────────────────────────────────────────────────┐
│                   Jobmate.Api (Host)                   │
│  Controllers, Routing, Middleware, Swagger, Media Store │
└───────────┬────────────────────────────────┬───────────┘
            │                                │
            ▼                                ▼
┌───────────────────────────────┐  ┌─────────────────────────────┐
│      Jobmate.Application      │  │   Jobmate.Infrastructure    │
│  MediatR Handlers, CQRS,      │◄─┤  EF Core, Postgres, Npgsql, │
│  FluentValidation, Behaviors, │  │  JWT, BCrypt, Mock Services,│
│  DTOs, Mappings, Interfaces   │  │  WorkerSlotStore, Seeders   │
└───────────┬───────────────────┘  └──────────────┬──────────────┘
            │                                     │
            ▼                                     ▼
┌────────────────────────────────────────────────────────────────┐
│                        Jobmate.Domain                          │
│   Entities, Enums, Value Objects, Domain Rules, Exceptions    │
│                     (Zero Dependencies)                        │
└────────────────────────────────────────────────────────────────┘
```

### Key Architectural Guidelines
- **Strict Inward Dependency Rule**:
  - `Jobmate.Domain` has zero external dependencies (no EF Core, no ASP.NET, no third-party libraries).
  - `Jobmate.Application` depends only on `Domain`. All external infrastructure (persistence, security, third-party APIs) is abstracted via interfaces (`IAppDbContext`, `ITokenService`, `IPasswordHasher`, `IOtpService`, `IPaymentService`, `IPushNotificationService`, `IWorkerSlotStore`).
  - `Jobmate.Infrastructure` implements the application interfaces using concrete providers.
  - `Jobmate.Api` serves as the composition root, configuring DI, middleware, rate limiting, and HTTP presentation.
- **Client Model Synchronization**:
  - All JSON payloads are serialized to `camelCase` with string enum values to mirror Dart models directly without client-side mapping layers.
  - Uniform envelope structure used for all responses and errors.
- **Data Integrity in Database & Domain**:
  - Business rules are verified both in domain logic and guaranteed by PostgreSQL constraints (`CHECK` constraints for all enums, money bounds, star ratings, and `UNIQUE` indexes for concurrency prevention).
  - Race conditions (e.g., two employers filling the last slot, or duplicate button taps) are handled via atomic SQL updates and row locks.

---

## 3. Technology Stack & Packages

| Category | Technology / Library | Version | Details & Purpose |
|---|---|---|---|
| **Runtime & SDK** | .NET 9.0 (C# 13) | `net9.0` | Modern, high-performance runtime |
| **Framework** | ASP.NET Core Web API | 9.0 | Minimal hosting, controllers, endpoint routing |
| **Database** | PostgreSQL | 15+ | Relational persistence, custom schema `jobmate` |
| **ORM & Data Access** | Entity Framework Core | 9.0.4 | Relational mapping, migrations, interceptors |
| **Database Driver** | Npgsql.EntityFrameworkCore.PostgreSQL | 9.0.4 | Native PostgreSQL provider with retry logic |
| **Mediator / CQRS** | MediatR | 12.4.1 | In-process command/query dispatching |
| **Validation** | FluentValidation | 11.11.0 | Strongly-typed request validation via pipeline behavior |
| **Authentication** | Microsoft.AspNetCore.Authentication.JwtBearer | 9.0.4 | JWT validation, custom challenge/forbidden error envelopes |
| **Tokens & Security** | System.IdentityModel.Tokens.Jwt | 8.2.1 | HS256 JWT access and refresh token management |
| **Password Hashing** | BCrypt.Net-Next | 4.0.3 | Secure salted password hashing for stored credentials |
| **API Versioning** | Asp.Versioning.Mvc.ApiExplorer | 8.1.0 | URL segment, header, and query parameter API versioning |
| **Documentation** | Swashbuckle.AspNetCore | 7.2.0 | Swagger UI with Bearer JWT auth & XML comments |
| **Logging** | Serilog.AspNetCore | 8.0.3 | Structured logging with Console and Daily Rolling File sinks |
| **Testing** | xUnit, WebApplicationFactory, Npgsql | - | Real PostgreSQL acceptance tests against `jobmate_test` |

---

## 4. Project Layout & Source Organization

```
Jobmate_api/
├── Database/
│   ├── README.md                          # Schema conventions, naming rules, and migration checklist
│   └── schema.sql                         # Read-only generated snapshot of the database schema
├── scripts/
│   └── smoke-test.ps1                     # Comprehensive PowerShell end-to-end integration test
├── src/
│   ├── Jobmate.Domain/                    # Core business domain (zero dependencies)
│   │   ├── Common/                        # AuditableEntity (Id, CreatedAt, UpdatedAt), Paging primitives
│   │   ├── Entities/                      # User, WorkerProfile, GiverProfile, Job, JobApplication,
│   │   │                                  # WorkerAssignment, Payment, Rating, Conversation, Message,
│   │   │                                  # Notification, UserDevice, VerificationRequest, Dispute,
│   │   │                                  # SavedJob, SupportReport, AuthEntities
│   │   ├── Enums/                         # JobStatus, ApplicationStatus, PaymentStatus, UserRole, etc.
│   │   ├── Exceptions/                    # DomainException, NotFoundException, ConflictException, etc.
│   │   ├── Rules/                         # Pure business logic (JobLifecycle, ApplicationRules,
│   │   │                                  # SelectionRules, PaymentRules, RatingRules, TrustRules, etc.)
│   │   └── ValueObjects/                  # EmailAddress, PhoneNumber (+91 E.164), GeoDistance (Haversine)
│   │
│   ├── Jobmate.Application/               # CQRS application logic & orchestration
│   │   ├── Common/                        # ApiResponse, IAppDbContext, ICurrentUser, RateLimitPolicies,
│   │   │                                  # Behaviors (ValidationBehavior, LoggingBehavior), Services
│   │   ├── DTOs/<Feature>/                # Feature-grouped request and response transfer models:
│   │   │                                  # <Feature>Requests.cs and <Feature>Dtos.cs
│   │   └── Features/<Feature>/            # Commands, Queries, Handlers, and FluentValidators:
│   │       ├── Admin/                     # Dashboard stats, verification queue, user/job moderation
│   │       ├── Applications/              # Job applications, review, and worker selection
│   │       ├── Auth/                      # Registration, OTP verification, login, refresh, password reset
│   │       ├── Jobs/                      # Job postings CRUD, discovery, distance search, giver/worker stats
│   │       ├── Messaging/                 # Conversations, chat messages, polling, unread counts, archiving
│   │       ├── Notifications/             # Inbox notifications, unread badges, push device tokens
│   │       ├── Payments/                  # Wage calculations, payments, settlement, refunds, wallet stats
│   │       ├── Profiles/                  # Worker & Giver profiles, skills, weekly availability, media
│   │       ├── Ratings/                   # Participant ratings, reviews, check-ins, work history
│   │       ├── Trust/                     # Disputes, resolution, support issue reports
│   │       └── Verifications/             # KYC document submissions, trust badge rollups
│   │
│   ├── Jobmate.Infrastructure/            # Data access, security & external implementations
│   │   ├── Configuration/                 # JwtOptions, ExternalServicesOptions, StorageOptions
│   │   ├── Persistence/                   # AppDbContext, PersistenceConfiguration, SchemaConventions,
│   │   │   │                              # WorkerSlotStore, AdminSeeder, DemoDataSeeder
│   │   │   ├── Configurations/            # EF Core IEntityTypeConfiguration classes per aggregate
│   │   │   └── Migrations/                # EF Core database migrations
│   │   ├── Security/                      # JwtService, BcryptPasswordHasher
│   │   └── Services/                      # MockOtpService, MockPaymentService, MockPushNotificationService
│   │
│   └── Jobmate.Api/                       # HTTP API Host & Presentation
│       ├── Controllers/                   # 13 REST controllers exposing v1 endpoints
│       ├── Media/                         # LocalMediaStore (file system uploads & downloads)
│       ├── Middleware/                    # ExceptionHandlingMiddleware (standardized error envelopes)
│       ├── Properties/                    # launchSettings.json (HTTP: 5234)
│       ├── appsettings.json               # Default configuration & rate limits
│       ├── appsettings.Development.json   # Development demo data toggle & credentials
│       └── Program.cs                     # Composition root, Serilog, rate limiter, Swagger, seeders
│
└── tests/
    ├── Jobmate.Api.Tests/                 # WebApplicationFactory integration tests with real Postgres
    ├── Jobmate.Application.Tests/         # Unit tests for domain rules, value objects, abuse guards
    └── Jobmate.Infrastructure.Tests/      # Schema conventions test suite & security tests
```

---

## 5. Domain Models & Database Schema

The database runs in PostgreSQL under the dedicated schema `jobmate`.

### 5.1 Entities Catalog

| Entity | Table Name | Description | Key Relationships |
|---|---|---|---|
| `User` | `users` | Core identity and login entity. Holds credentials, role, overall verification status. | 1:1 `WorkerProfile`, 1:1 `GiverProfile`, 1:N `RefreshTokens`, 1:N `Devices` |
| `WorkerProfile` | `worker_profiles` | Worker-specific details: bio, experience, expected pay, availability, rating aggregate. | N:1 `User`, 1:N `WorkerSkills`, 1:N `WorkerDocuments`, 1:N `WorkerAvailabilitySlots` |
| `WorkerSkill` | `worker_skills` | Normalized trade skill associated with a worker. Unique per worker. | N:1 `WorkerProfile` |
| `WorkerDocument` | `worker_documents` | Uploaded KYC document (ID, certificate, address proof) for verification. | N:1 `WorkerProfile` |
| `WorkerAvailabilitySlot`| `worker_availability_slots`| Recurring weekly availability window (`DayOfWeek 1-7`, `StartTime`, `EndTime`). | N:1 `WorkerProfile` |
| `GiverProfile` | `giver_profiles` | Work giver details: individual vs business, organization name, GST number, rating. | N:1 `User` |
| `Job` | `jobs` | Job posting details: category, shift date/times, wage, location coordinates, status. | N:1 `User` (Giver), 1:N `JobSkills`, 1:N `JobWeekdays`, 1:N `Applications`, 1:N `Assignments` |
| `JobSkill` | `job_skills` | Required skills for a job posting. | N:1 `Job` |
| `JobWeekday` | `job_weekdays` | Active weekdays for recurring jobs (ISO 1=Mon ... 7=Sun). | N:1 `Job` |
| `JobApplication` | `job_applications` | Worker application to a job with message, expected pay, and status tracking. | N:1 `Job`, N:1 `User` (Worker). Unique on `(job_id, worker_user_id)` |
| `WorkerAssignment` | `worker_assignments` | Confirmed hiring booking. Tracks check-in code, actual times, agreed pay, and status. | N:1 `Job`, N:1 `User` (Worker), N:1 `JobApplication`, 1:1 `Payment` |
| `Payment` | `payments` | Monetary transaction for a booking. Tracks base pay, overtime, 5% fee, and status. | N:1 `Job`, N:1 `Worker`, N:1 `Giver`, 1:1 `WorkerAssignment`. Unique `assignment_id` |
| `Rating` | `ratings` | Star rating (1-5) and text feedback between participants of a completed job. | N:1 `Job`, N:1 `FromUser`, N:1 `ToUser`. Unique `(job_id, from_user_id, to_user_id)` |
| `Conversation` | `conversations` | 1-on-1 chat thread, optionally scoped to a job. | N:1 `Job` (optional), 1:N `Participants`, 1:N `Messages` |
| `ConversationParticipant`| `conversation_participants`| Participant join entity with unread badge counter and archive flag. | N:1 `Conversation`, N:1 `User` |
| `Message` | `messages` | Chat bubble text, type (Text, System), read status, and delivery timestamp. | N:1 `Conversation`, N:1 `User` (Sender) |
| `Notification` | `notifications` | In-app notification inbox item with deep link target route and ID. | N:1 `User` |
| `UserDevice` | `user_devices` | Registered device push token (FCM), platform, and app version. | N:1 `User` |
| `VerificationRequest` | `verification_requests` | User request to verify Identity, Phone, Email, Skills, or Address. | N:1 `User`, N:1 `ReviewedBy` (Admin) |
| `Dispute` | `disputes` | Formal complaint on a job between participants. Can hold funds. | N:1 `Job`, N:1 `RaisedBy`, N:1 `AgainstUser`, N:1 `ResolvedBy` |
| `SavedJob` | `saved_jobs` | Worker bookmarked job. Soft-deletable (`deleted_at`). | N:1 `User`, N:1 `Job`. Unique `(user_id, job_id, deleted_at IS NULL)` |
| `SupportReport` | `support_reports` | General user support ticket / safety issue (does not require a booking). | N:1 `User` |
| `RefreshToken` | `refresh_tokens` | Opaque SHA-256 hashed refresh token with family tracking for reuse detection. | N:1 `User` |
| `OtpChallenge` | `otp_challenges` | Short-lived numeric OTP with hashed code and max attempt counter. | Hashed mobile phone lookup |
| `PasswordResetToken` | `password_reset_tokens` | Single-use password reset token. | N:1 `User` |

### 5.2 Schema Conventions (`SchemaConventions.cs`)
- **Primary Keys**: UUID generated client-side/application-side; column name `{singular_table}_id` (e.g., `job_id`, `user_id`).
- **Constraints**:
  - Primary Key: `pk_{table}`
  - Foreign Key: `fk_{table}_{column}` with mandatory index on the FK column.
  - Indexes: `ix_{table}_{columns}` and unique `ux_{table}_{columns}`.
  - Check Constraints: `chk_{table}_{column}` automatically generated for every C# `enum` column, validating strings against allowed enum values.
- **Audit Columns**: Every table inherits from `AuditableEntity` with `created_at timestamptz NOT NULL` and `updated_at timestamptz NOT NULL` stamped automatically in UTC by `AppDbContext`.
- **Soft Deletes**: Used only where users explicitly delete records: `jobs` (draft delete) and `saved_jobs` (unsave) via `deleted_at timestamptz NULL`. Global query filters in EF Core automatically filter them out.
- **Money Representation**: Stored in whole integer rupees (e.g. `250` INR).
- **Time Representation**: Calendar dates stored as `date` (`DateOnly`), times of day as `time` (`TimeOnly`), and timestamps as `timestamptz` (UTC `DateTime`).

---

## 6. Functional Capabilities & Subsystems

### 6.1 Authentication & Session Management
- **Phone-First Registration & Login**: Users register with mobile number (`+91...`), name, password, and role. Registration generates an OTP challenge.
- **OTP Verification**: Tested via development OTP (`123456`) or `IOtpService` abstraction. Phone numbers are validated and stored in E.164 format.
- **Multi-Identifier Login**: Users can sign in via either email or 10-digit mobile number with password.
- **Dual-Token System (JWT + Refresh Tokens)**:
  - Access Token: Short-lived (30 minutes default) HS256 JWT containing `sub`, `name`, `email`, `role`, `area`.
  - Refresh Token: Long-lived (30 days default) cryptographically random string. Only the SHA-256 hash is stored in PostgreSQL.
  - **Token Rotation & Reuse Detection**: Each refresh rotates the token pair and points `replaced_by_token_id`. Reusing an already rotated token immediately revokes the entire token family to defend against token theft.
- **Admin Isolation**: Admin sign-in is strictly email-only via `/api/v1/admin/auth/login`. Non-admin accounts are rejected with 401 even with valid passwords.

### 6.2 Job Creation, Discovery & Lifecycle
- **Creation Wizard**: Maps the Flutter 6-step creation flow (`JobDraft`). Supports one-time or recurring shifts (`JobWeekday`), overtime rates, break durations, travel/food allowances, and exact vs. hidden addresses.
- **Job Lifecycle States**:
  $$\text{Draft} \longrightarrow \text{Active} \longrightarrow \text{InProgress} \longrightarrow \text{Completed}$$
  *(With cancel paths to $\text{Cancelled}$ and automated expiration to $\text{Expired}$).*
- **Discovery Search Engine**:
  - Filter by category, wage bounds, urgent/recurring status, workplace type (OnSite/Remote), date range, verified employer only.
  - **Geospatial Distance Search**: Computes distance in kilometers between job coordinates and user latitude/longitude using the spherical Haversine formula in PostgreSQL SQL queries.
  - Privacy Guard: If `location_visible = false`, exact address and coordinates are withheld from public search results and revealed only to booked workers.

### 6.3 Applications & Atomic Worker Selection
- **Application Flow**: Workers submit an application with optional pitch message and expected rate. Duplicate applications are blocked by database unique indexes.
- **Employer Review**: Work givers view applicants (which automatically marks them `Viewed`), shortlist with custom proposed pay, or reject with a reason.
- **Race-Free Slot Selection**:
  - Selecting workers creates a `WorkerAssignment` and takes a worker slot.
  - `WorkerSlotStore` executes an atomic conditional update directly in PostgreSQL:
    ```sql
    UPDATE jobmate.jobs
    SET workers_filled = workers_filled + 1, updated_at = @now
    WHERE job_id = @jobId AND status IN ('Active', 'Pending') AND workers_filled < workers_needed
    ```
  - Eliminates read-modify-write race conditions. If two employers confirm the last slot concurrently, PostgreSQL's row-level lock ensures one succeeds and the other receives a `409 Conflict`.

### 6.4 Attendance Verification & Completion
- **Check-In OTP Protocol**:
  - Employer triggers `/api/v1/jobs/{jobId}/check-in-code/{assignmentId}` to read a cryptographically secure 4-digit code.
  - The worker on site enters the code via `/api/v1/assignments/{id}/check-in`.
  - The single-use code is verified and cleared, transitioning the assignment to `CheckedIn`.
- **Job Completion**: When the last booking completes, the job automatically completes, opening both parties to submit ratings.

### 6.5 Payments, Platform Fee & Digital Wallet
- **Wage Calculations (`PayoutBreakdown`)**:
  - Platform Fee: Fixed at 5% of `(Base Pay + Overtime)`.
  - Giver Total: $\text{Base Pay} + \text{Overtime} + \text{Platform Fee}$.
  - Worker Net: $\text{Base Pay} + \text{Overtime} - \text{Platform Fee}$.
- **Idempotency**: Givers submit `idempotencyKey` when releasing payment. Repeated calls return the existing receipt without double charging.
- **Worker Wallet**: Dedicated endpoints provide booking-level earnings, month-by-month summaries, and aggregated wallet balances (earned, pending, disputed).
- **Admin Refunds**: Only administrators can execute refunds against settled payments.

### 6.6 Trust, Badges, Ratings & Disputes
- **Mutual Ratings**: 1-to-5 star ratings with optional comments. Only participants of a completed job can rate, exactly once per job. Updates average ratings on profiles.
- **Trust Badge Rollup**:
  - Aggregates status across 4 core checks: Identity document, Mobile phone, Email address, and Skills.
  - Overall status is `Verified` only when all 4 are approved. Any active rejection renders the overall badge `Rejected`.
- **Dispute Resolution**: Participants can raise a dispute against a booking. Disputing an amount locks the payment in `Disputed` status until an admin resolves or rejects the case.

### 6.7 In-App Messaging & Notifications
- **Chat Engine**: 1-on-1 conversations scoped to specific jobs or general inquiries between involved users.
- **Unread Message Counters**: Maintained directly on `conversation_participants` join records so the conversation list does not require expensive count queries over message tables.
- **Notification Inbox**: Persists system, payment, job, and chat alerts. Supports deep links (`route` and `referenceId`) matching Flutter app routes (`/job-details`, `/application-details`).
- **Device Management**: Stores FCM push tokens per device; automatically handles token updates and cleans up on logout.

### 6.8 Media Storage
- Uploads handled via `LocalMediaStore` saved to `wwwroot/media/profile-images/` and `wwwroot/media/documents/`.
- Strict file validation: 6 MB size limit, MIME type whitelist (JPEG, PNG, WebP, PDF), path traversal protection, and `X-Content-Type-Options: nosniff`.
- Access Control: Profile images are public to authenticated users; identity documents are strictly private to the owner and administrators.

---

## 7. API Design & Endpoint Catalog

All routes are versioned under `/api/v1/`.

### 7.1 Response Envelope Structure

#### Success Response
```json
{
  "success": true,
  "data": { ... },
  "message": "Success"
}
```

#### Paginated Response
```json
{
  "success": true,
  "data": [ ... ],
  "pagination": {
    "page": 1,
    "pageSize": 20,
    "totalItems": 45,
    "totalPages": 3,
    "hasPrevious": false,
    "hasNext": true
  },
  "message": "Success"
}
```

#### Error Response
```json
{
  "success": false,
  "message": "One or more validation errors occurred.",
  "errorCode": "ValidationFailure",
  "errors": [
    {
      "field": "pay",
      "message": "Pay must be greater than zero.",
      "code": "ValidationFailure"
    }
  ]
}
```

### 7.2 Complete Endpoint Reference

| Group / Controller | Method | Route | Description & Access |
|---|---|---|---|
| **Auth** | `POST` | `/api/v1/auth/register` | Register new account and send OTP (Public, Rate-limited) |
| | `POST` | `/api/v1/auth/verify-otp` | Verify phone number and issue JWT token pair |
| | `POST` | `/api/v1/auth/resend-otp` | Resend verification code |
| | `POST` | `/api/v1/auth/login` | Sign in with email/phone and password |
| | `POST` | `/api/v1/auth/refresh` | Rotate refresh token for a new token pair |
| | `POST` | `/api/v1/auth/logout` | Revoke current session or all user sessions |
| | `POST` | `/api/v1/auth/forgot-password` | Request password reset code via phone |
| | `POST` | `/api/v1/auth/reset-password` | Set new password with verified OTP |
| **Admin Auth** | `POST` | `/api/v1/admin/auth/login` | Dedicated admin console sign-in (Admin only) |
| **Jobs** | `POST` | `/api/v1/jobs` | Create job posting from wizard (WorkGiver) |
| | `PUT` | `/api/v1/jobs/{id}` | Update job details (Poster only) |
| | `GET` | `/api/v1/jobs/{id}` | Get single job with applicant count & distance (Public) |
| | `GET` | `/api/v1/jobs` | Public search feed with filters & geolocation (Public) |
| | `POST` | `/api/v1/jobs/{id}/apply` | Submit job application (Worker) |
| | `GET` | `/api/v1/jobs/{id}/applications` | List applicants for a job (WorkGiver) |
| | `POST` | `/api/v1/jobs/{id}/select-workers` | Bulk select and book workers (WorkGiver) |
| | `POST` | `/api/v1/jobs/{id}/publish` | Publish draft job to active status |
| | `POST` | `/api/v1/jobs/{id}/cancel` | Cancel job and notify participants (WorkGiver) |
| | `DELETE`| `/api/v1/jobs/{id}` | Delete unpublished draft job (WorkGiver) |
| | `POST` | `/api/v1/jobs/{id}/start` | Mark job InProgress (WorkGiver) |
| | `POST` | `/api/v1/jobs/{id}/complete` | Mark job Completed and unlock ratings |
| | `POST` | `/api/v1/jobs/{id}/remind` | Send reminder notification to unverified workers |
| **Giver Surface**| `GET` | `/api/v1/giver/jobs` | List caller's posted jobs (WorkGiver) |
| | `GET` | `/api/v1/giver/stats` | Giver home screen counters (WorkGiver) |
| | `GET` | `/api/v1/giver/work-history` | Work history of bookings from employer view |
| **Worker Surface**| `GET` | `/api/v1/worker/jobs` | Filterable discovery feed for available jobs (Worker) |
| | `GET` | `/api/v1/worker/stats` | Worker home screen counters & earnings summary |
| | `GET` | `/api/v1/worker/applications` | List caller's submitted applications (Worker) |
| | `GET` | `/api/v1/worker/saved-jobs` | List bookmarked jobs (Worker) |
| | `POST` | `/api/v1/worker/saved-jobs/{jobId}`| Bookmark a job (Worker) |
| | `DELETE`| `/api/v1/worker/saved-jobs/{jobId}`| Remove bookmark (Worker) |
| | `GET` | `/api/v1/worker/earnings` | Line-by-line wallet transactions (Worker) |
| | `GET` | `/api/v1/worker/earnings/summary`| Month-grouped earnings totals (Worker) |
| | `GET` | `/api/v1/worker/wallet` | Earned, pending, and disputed wallet totals |
| | `GET` | `/api/v1/worker/work-history` | Worker's completed and active bookings |
| **Applications** | `POST` | `/api/v1/applications/{id}/withdraw` | Withdraw pending application (Worker) |
| | `POST` | `/api/v1/applications/{id}/shortlist`| Shortlist applicant with proposed pay (WorkGiver) |
| | `POST` | `/api/v1/applications/{id}/reject` | Reject applicant with reason (WorkGiver) |
| | `POST` | `/api/v1/applications/{id}/select` | Atomically book single worker (WorkGiver) |
| **Attendance** | `GET` | `/api/v1/jobs/{jobId}/check-in-code/{assignmentId}` | Read 4-digit check-in code (WorkGiver) |
| | `POST` | `/api/v1/assignments/{id}/check-in` | Submit 4-digit check-in code on site (Worker) |
| | `POST` | `/api/v1/assignments/{id}/complete` | Mark booking completed with overtime |
| **Payments** | `GET` | `/api/v1/payments` | List caller's receipts (Worker, Giver, Admin) |
| | `POST` | `/api/v1/jobs/{jobId}/payments/{workerId}` | Release payment with 5% fee (WorkGiver) |
| | `POST` | `/api/v1/payments/{id}/confirm` | Settle in-flight payment (WorkGiver / Webhook) |
| | `POST` | `/api/v1/payments/{id}/refund` | Refund payment (Admin only) |
| **Ratings** | `POST` | `/api/v1/ratings` | Submit star rating & feedback for completed job |
| | `GET` | `/api/v1/ratings` | List feedback received |
| **Profiles** | `GET` | `/api/v1/profile/me` | Full profile of signed-in user (Owner) |
| | `GET` | `/api/v1/profile/{userId}` | Sanitized public profile of another user |
| | `PUT` | `/api/v1/profile/worker` | Update worker profile & skills (Worker) |
| | `PUT` | `/api/v1/profile/giver` | Update employer profile & GST (WorkGiver) |
| | `PUT` | `/api/v1/profile/availability`| Replace weekly availability slots (Worker) |
| | `PUT` | `/api/v1/profile/presence` | Update online/offline heartbeat (Worker) |
| | `POST` | `/api/v1/profile/image` | Upload profile avatar (Multipart, Rate-limited) |
| | `POST` | `/api/v1/profile/documents` | Upload KYC verification document (Worker) |
| **Media** | `GET` | `/api/v1/media/profile-images/{file}` | Stream profile photo (Authenticated) |
| | `GET` | `/api/v1/media/documents/{file}` | Stream identity document (Owner or Admin only) |
| **Messaging** | `GET` | `/api/v1/conversations` | List chat threads with unread counts |
| | `POST` | `/api/v1/conversations` | Start or re-open 1-on-1 thread |
| | `GET` | `/api/v1/conversations/{id}` | Get thread details & other user profile |
| | `GET` | `/api/v1/conversations/{id}/messages` | Paginated messages (newest first) |
| | `POST` | `/api/v1/conversations/{id}/messages` | Send chat message (Rate-limited) |
| | `POST` | `/api/v1/conversations/{id}/read` | Mark thread as read |
| | `POST` | `/api/v1/conversations/{id}/archive` | Archive or unarchive thread |
| **Notifications**| `GET` | `/api/v1/notifications` | Paginated in-app notification inbox |
| | `GET` | `/api/v1/notifications/unread-count` | Get unread badge counts |
| | `POST` | `/api/v1/notifications/{id}/read` | Mark single notification read |
| | `POST` | `/api/v1/notifications/read-all` | Mark all notifications read |
| | `GET` | `/api/v1/devices` | List registered push devices |
| | `POST` | `/api/v1/devices/register` | Register or refresh FCM device token |
| | `DELETE`| `/api/v1/devices/{id}` | Unregister device push token |
| | `DELETE`| `/api/v1/devices` | Unregister all devices (Sign-out) |
| **Verifications**| `GET` | `/api/v1/verifications` | Get caller's verification history & checklist |
| | `GET` | `/api/v1/verifications/summary` | Get aggregated trust badge status |
| | `POST` | `/api/v1/verifications` | Submit verification request for review |
| | `POST` | `/api/v1/verifications/{id}/withdraw`| Withdraw pending verification request |
| **Disputes** | `POST` | `/api/v1/disputes` | Raise booking dispute and hold pay |
| | `GET` | `/api/v1/disputes` | List caller's active/past disputes |
| | `GET` | `/api/v1/disputes/{id}` | View dispute details and resolution |
| | `POST` | `/api/v1/support-reports` | File general platform support ticket |
| **Admin Console**| `GET` | `/api/v1/admin/dashboard` | High-level metrics & queue counts |
| | `GET` | `/api/v1/admin/verifications` | Review queue for KYC verification requests |
| | `POST` | `/api/v1/admin/verifications/{id}/approve` | Approve document check & update badge |
| | `POST` | `/api/v1/admin/verifications/{id}/reject` | Reject document check with explanation |
| | `GET` | `/api/v1/admin/users` | Search and filter all users |
| | `POST` | `/api/v1/admin/users/{id}/status` | Suspend or reinstate user account |
| | `GET` | `/api/v1/admin/jobs` | Search and audit all posted jobs |
| | `GET` | `/api/v1/admin/jobs/{id}` | View job detail with financial conversions |
| | `GET` | `/api/v1/admin/disputes` | Open dispute review queue |
| | `POST` | `/api/v1/admin/disputes/{id}/review` | Mark dispute under investigation |
| | `POST` | `/api/v1/admin/disputes/{id}/resolve` | Close dispute and release payment hold |
| | `GET` | `/api/v1/admin/support-reports` | Queue of submitted support reports |
| | `POST` | `/api/v1/admin/support-reports/{id}/resolve` | Mark support report resolved |

---

## 8. Security, Rate Limiting & Resilience

### 8.1 Abuse Prevention & Rate Limiting (`RateLimitPolicies.cs`)
Configured using ASP.NET Core `Microsoft.AspNetCore.RateLimiting` with fixed-window limiters.
- **Partitioning Strategy (`AbuseGuardPartitioner`)**:
  - Authenticated calls: Partitioned per User ID (`user:{id}`). Prevents one user on a shared Wi-Fi/office network from exhausting the allowance of others.
  - Anonymous calls: Partitioned per Remote IP (`ip:{address}`).
- **Configured Policy Windows**:
  - `auth`: 10 requests per minute (login, registration, OTP send/verify, password reset).
  - `messaging`: 30 requests per minute (posting messages and starting threads).
  - `media`: 10 requests per minute (file uploads).
  - `reports`: 5 requests per hour (disputes and support reports).
  - `verification`: 10 requests per hour (submitting KYC documents).
- **Client Back-Off Contract**: On rejection (HTTP 429), returns `Retry-After` header and structured error response with estimated wait time.

### 8.2 Logging & Observability
- **Serilog Integration**: Structured JSON logging. Overrides noisy EF Core command and framework messages to `Warning` while keeping business actions at `Information`.
- **Log Sanitation**: User passwords, JWT secrets, and IP partition keys are excluded from log messages.

### 8.3 Concurrency & Database Retries
- **PostgreSQL Transient Retries**: EF Core connection configured with `EnableRetryOnFailure(3, 5s)` for network hiccups or failover recovery.
- **Atomic Operations**: Critical sections use `IAppDbContext.ExecuteInTransactionAsync<T>` to wrap multiple writes in a serializable execution strategy.

---

## 9. Testing & Quality Assurance

The repository includes a comprehensive 3-tier test suite under `tests/`:

1. **`Jobmate.Api.Tests` (Integration & Acceptance)**:
   - Uses `WebApplicationFactory<Program>` to spin up the real ASP.NET Core pipeline.
   - Tests execute against a real PostgreSQL instance (`jobmate_test`), executing real queries (including Haversine distance, text search, and groupings).
   - Database tables are truncated between test classes (`TestDatabase.ResetAsync`) while preserving the migration history table.
   - Separate test suites verify every feature: Admin, Applications, Auth sessions, Jobs, Messaging, Notifications, Payments, Profiles, Ratings, Selection concurrency, Disputes, Verifications, and Rate Limiter behavior.
2. **`Jobmate.Application.Tests` (Domain & Unit)**:
   - Fast unit tests executing without database dependencies.
   - Tests value objects (`EmailAddress`, `PhoneNumber`, `GeoDistance`), payment 5% fee calculations, rating bounds, dispute state machines, and rate limit partitioners.
3. **`Jobmate.Infrastructure.Tests`**:
   - `SchemaConventionTests`: Verifies that the EF Core model matches all convention rules (snake_case column names, PK `{singular}_id`, index prefixes `ix_`/`ux_`, enum check constraints). Fails the build if a model change is missing a migration.
   - Password hasher verification and demo data gating tests.
4. **Smoke Testing Script (`scripts/smoke-test.ps1`)**:
   - An end-to-end PowerShell script that exercises reachability, worker registration, OTP verification, login with email/phone, token rotation, token replay detection, admin role isolation, and response envelope error shapes against a live running API.

---

## 10. Development, Build & Operation Guide

### 10.1 Build Guard & Process Lock Management
- Running the API executes `Jobmate.Api.exe` from `src/Jobmate.Api/bin/Debug/net9.0`.
- The running process locks its output binaries, which would normally cause `dotnet build` to fail with `MSB3021` / `MSB3027`.
- **Automated Process Cleanup**: `Jobmate.Api.csproj` contains a custom MSBuild target `StopRunningApi` that automatically terminates any active `Jobmate.Api` process before `CoreBuild`. Therefore, running `dotnet build` is always safe.
- To run with auto-reload during development:
  ```powershell
  dotnet watch run --project src/Jobmate.Api
  ```

### 10.2 Configuration & Secrets Setup
Secrets are stored in .NET user secrets (never in `appsettings.json`):
```powershell
cd src/Jobmate.Api
dotnet user-secrets set "ConnectionStrings:WriteConnection" "Host=localhost;Port=5432;Database=jobmate;Username=postgres;Password=yourpassword"
dotnet user-secrets set "ConnectionStrings:ReadConnection" "Host=localhost;Port=5432;Database=jobmate;Username=postgres;Password=yourpassword"
dotnet user-secrets set "Jwt:Issuer" "jobmate-api"
dotnet user-secrets set "Jwt:Audience" "jobmate-app"
dotnet user-secrets set "Jwt:SigningKey" "a-secure-random-key-at-least-32-characters-long"
dotnet user-secrets set "Seed:AdminPassword" "InitialAdminPassword123!"
```

### 10.3 Database Migrations Workflow
```powershell
# Add a new migration after entity changes:
dotnet ef migrations add <MigrationName> --project src/Jobmate.Infrastructure --startup-project src/Jobmate.Api --output-dir Persistence/Migrations

# Apply migrations to database:
dotnet ef database update --project src/Jobmate.Infrastructure --startup-project src/Jobmate.Api

# Refresh the read-only schema snapshot:
dotnet ef migrations script --project src/Jobmate.Infrastructure --startup-project src/Jobmate.Api -o Database/schema.sql
```

### 10.4 Running Tests
```powershell
# Run all unit, infrastructure, and acceptance tests:
dotnet test
```

### 10.5 Running the API & Swagger Documentation
```powershell
dotnet run --project src/Jobmate.Api
```
- API Base URL: `http://localhost:5234`
- Swagger UI Documentation: `http://localhost:5234/swagger`

