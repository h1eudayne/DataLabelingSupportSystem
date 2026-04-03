# Data Labeling Support System

## Overview
This is the Backend API for the Data Labeling Support System. The system is designed to manage the workflow of data annotation projects, connecting Admins, Annotators, and Reviewers.

It handles project management, task assignment, image labeling processes, and quality assurance.

## Tech Stack
* **Framework:** .NET 8 Web API (Core)
* **Database:** MySQL
* **ORM:** Entity Framework Core (Code First)
* **Authentication:** JWT (JSON Web Tokens)
* **Architecture:** 3-layer flow with a shared `Core` project
    * `API`: Controllers & entry point
    * `BLL`: Business Logic Layer
    * `DAL`: Data Access Layer (Repositories)
    * `Core`: Shared entities, DTOs, enums, interfaces

## Key Features
* **User Management:**
    * Register/Login with JWT.
    * Role-based authorization (Admin, Manager, Annotator, Reviewer).
    * **Soft Delete:** Users are deactivated (`IsActive = false`) instead of being removed from DB.
    * **Ban/Unban:** Admin can toggle user status via API.
* **Project Workflow:**
    * Create projects, define labels/tags.
    * Upload datasets.
* **Task Lifecycle:**
    * `New` -> `Assigned` -> `InProgress` -> `Submitted` -> `Approved` / `Rejected`.
* **Statistics:** Track user performance and project progress.

## Project Structure
```text
DataLabelingSupportSystem/
├── API/              # Controllers, Configurations, Swagger
├── BLL/              # Services, Business Logic
├── DAL/              # DbContext, Repositories, DB configuration
└── Core/             # Entities, Request/Response Models, Enums
```

### Layering note
The runtime request flow still follows the 3-layer model:

* `API -> BLL -> DAL`
* `Core` is a shared contracts/domain assembly, not a shortcut that bypasses the service or repository layers.

## Railway Deployment
This backend is prepared to deploy to Railway as a Dockerized API service.

### Current deployment assumptions
* The application now targets MySQL for Railway deployment.
* Railway can provide the MySQL service directly through its MySQL template.
* Files uploaded to `wwwroot/uploads` and `wwwroot/avatars` are not persistent on Railway after redeploy or restart.
* A fresh `InitialMySql` EF migration is included for MySQL deployments.

### Required environment variables
Copy values from `.env.example` and set them in Railway:

* `MYSQLHOST`
* `MYSQLPORT`
* `MYSQLUSER`
* `MYSQLPASSWORD`
* `MYSQLDATABASE`
* `MYSQL_URL` (optional)
* `ConnectionStrings__DefaultConnection` (optional override)
* `Jwt__Key`
* `Jwt__Issuer`
* `Jwt__Audience`
* `Database__ApplyMigrationsOnStartup`
* `Database__EnsureCreatedOnStartup`
* `Database__ServerVersion` (optional override)
* `SeedAdmin__Enabled`
* `SeedAdmin__Email` / `SeedAdmin__Password` (only when intentionally bootstrapping an admin account)
* `Cors__AllowedOrigins`
* `EmailSettings__DeliveryMode` (`Network` for real SMTP, `PickupDirectory` for local mail-drop)
* `EmailSettings__MailServer` (required for `Network`)
* `EmailSettings__MailPort` (required for `Network`)
* `EmailSettings__SenderName`
* `EmailSettings__SenderEmail` (required)
* `EmailSettings__Username` (optional, defaults to sender email)
* `EmailSettings__Password` (required for `Network` when `UseDefaultCredentials=false`)
* `EmailSettings__PickupDirectory` (optional, used by `PickupDirectory`)
* `EmailSettings__QueueCapacity` (`10000` by default, requests fail fast when the in-memory queue is full)
* `EmailSettings__TimeoutSeconds` (`10` by default, each SMTP attempt times out faster when the mail server is unreachable)
* `UserImport__MaxRows` (`1000` by default, set `0` or a negative number to remove the row-limit check)

### Railway setup steps
1. Create a new Railway service from this repository.
2. Add a Railway MySQL database to the same project.
3. Leave the API service Root Directory empty when deploying this backend repository directly.
4. Let Railway build using the included `Dockerfile`.
5. Expose the MySQL variables from the database service to the API service.
   You can either reference `MYSQLHOST` / `MYSQLPORT` / `MYSQLUSER` / `MYSQLPASSWORD` / `MYSQLDATABASE`
   directly, or provide a full `ConnectionStrings__DefaultConnection` override.
6. Set `Cors__AllowedOrigins` to your public frontend domain, for this project:
   `https://data-labeling-support-system.vercel.app`
7. Set the health check path to `/health`.
8. Deploy the service.

If you later move this backend into a larger monorepo, then configure Railway to use the backend folder as the Root Directory.

### Runtime behavior
* The API listens on Railway's `PORT` automatically, with `8080` as the container fallback.
* Production now defaults to `Database__ApplyMigrationsOnStartup=false`, so a normal redeploy does not automatically change schema.
* Development still auto-applies the bundled MySQL EF migration by default.
* If automatic migrations are explicitly enabled, the API now blocks startup before running DDL when the target database already contains app tables but has no `__EFMigrationsHistory` table.
* `Database__EnsureCreatedOnStartup` is now only a fallback mode for local or disposable databases.
* Production admin seeding is opt-in. Set `SeedAdmin__Enabled=true` together with `SeedAdmin__Email` and `SeedAdmin__Password` only when you intentionally want startup to create or reconcile the default admin account.
* In production, the API now fails fast if `Cors__AllowedOrigins` is missing.
* Email delivery is now queued in memory and processed by a background worker, so add user, reset password, and similar requests do not wait on slow SMTP round-trips.
* In local development, email delivery defaults to a file-based pickup directory at `API/mail-drop` so workflow emails can still be verified without a live SMTP password.
* `/health` returns `200` only when the API can reach the database.
* `/` returns a simple service status payload that helps confirm the container started.

### Applying migrations manually
Run schema changes as a separate release step instead of during every deploy:

```powershell
cd API
dotnet tool restore
dotnet tool run dotnet-ef database update --project ../DAL/DAL.csproj --startup-project API.csproj
```
