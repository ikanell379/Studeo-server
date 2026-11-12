# Studeo-server

Backend repo of Studeo. Shared rules and the product spec are in the parent folder (`../CLAUDE.md`, `../SPEC.md`).

## Tech Stack
- .NET 10, ASP.NET Core Web API with **Controllers** (no Minimal APIs)
- Entity Framework Core 10 with PostgreSQL (Npgsql)
- FluentValidation for request validation and for validating AI output
- Swagger for API documentation
- PdfPig (PDF text per page), QuestPDF (PDF export, Greek font embedded)
- Anthropic API called only from Services
- Keycloak (OIDC) for sign-in; JWT bearer auth, realm roles `teacher` / `student`

## Project Structure
```
Studeo-server/
  Studeo.slnx
  docker-compose.yml
  src/Studeo.Api/
    Controllers/    HTTP endpoints only, no business logic
    Services/       business logic (PDF extraction, sectioning, AI generation, export)
    Repositories/   data access from db (EF Core)
    Models/         database entities, StudeoDbContext, Migrations/
    Contracts/      request/response DTOs and the StudySet schema (source of truth for the UI types)
    Validators/     FluentValidation validators
    Prompts/        AI prompts, one file per prompt
    Auth/           Keycloak options, JWT setup, role claims mapping, role names
  Tests/Studeo.Tests/  all tests: Unit/ and Integration/
  samples/          sample PDFs for testing
  keycloak/         studeo realm, imported by Keycloak on first start
```

## Configuration and Secrets
- Never put connection strings, keys or any settings in `appsettings.json`. Settings go only in `appsettings.Development.json`.
- Anthropic key only in user-secrets: `dotnet user-secrets set "Anthropic:ApiKey" "<key>" --project src/Studeo.Api`
- AI provider: `"Ai:Provider"` in `appsettings.Development.json` is `Claude` (default) or `Gemini`. Gemini uses `Gemini:Model` and `Gemini:ApiKey` (recommended in user-secrets too: `dotnet user-secrets set "Gemini:ApiKey" "<key>" --project src/Studeo.Api`).

## Auth
- All endpoints need a signed-in user (fallback policy); `[AllowAnonymous]` only on health. Documents and study sets: `[Authorize(Roles = Roles.Any)]`; teacher-only features use `Roles.Teacher`.
- The browser never talks to Keycloak. `AuthController` (`/api/auth/register|login|refresh|logout`) does it through `KeycloakClient`, with the confidential client `studeo-api` (settings in `appsettings.Development.json` → `Keycloak`):
  - register: Admin API creates the user (email = username) with the chosen realm role, then signs in. If giving the role fails, the user is deleted again.
  - login: password grant. refresh: refresh-token grant. logout: ends the Keycloak session.
- Tokens go only into httpOnly, SameSite=Strict cookies (`studeo_access` path `/`, `studeo_refresh` path `/api/auth`); JWT bearer reads the access cookie (or an `Authorization: Bearer` header, e.g. Swagger/curl). The access token lives 5 min; the UI calls `/api/auth/refresh` on a 401.
- The role is fixed at registration; only an admin changes it in the Keycloak console (http://localhost:8180/admin). Accounts without a Studeo role get `no_role` and no session.
- `keycloak/studeo-realm.json` is imported only if the realm doesn't exist. After editing it: `docker compose rm -sf keycloak && docker volume rm studeo_studeo-keycloak && docker compose up -d keycloak` (deletes Keycloak users).
- Tests replace JWT with `TestAuthHandler` (`factory.CreateClientAs(TestUser.Teacher)`) and Keycloak with the in-memory `FakeKeycloakClient`.

## Running
- Database + Keycloak: `docker compose up -d` (PostgreSQL 17 on `localhost:5440`, Keycloak on http://localhost:8180, admin/admin)
- API: `dotnet run --project src/Studeo.Api --launch-profile http` → http://localhost:5110, Swagger at `/swagger`. Migrations apply on startup in Development.
- Tests: `dotnet test Studeo.slnx` (integration tests need the docker-compose database; each run uses a throwaway database)
- Migrations: `dotnet ef migrations add <Name> --project src/Studeo.Api --output-dir Models/Migrations`
- `nuget.config` uses only nuget.org (the global Telerik feed is broken).

## Workflow Rules
- Never commit alone
- Always i have to review before commit
- Run tests after every implementation
