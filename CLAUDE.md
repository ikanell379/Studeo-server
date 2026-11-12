# Studeo-server

Backend repo of Studeo. Shared rules and the product spec are in the parent folder (`../CLAUDE.md`, `../SPEC.md`).

## Tech Stack
- .NET 10, ASP.NET Core Web API with **Controllers** (no Minimal APIs)
- Entity Framework Core 10 with PostgreSQL (Npgsql)
- FluentValidation for request validation and for validating AI output
- Swagger for API documentation
- PdfPig (PDF text per page), QuestPDF (PDF export, Greek font embedded)
- Anthropic API called only from Services

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
  Tests/Studeo.Tests/  all tests: Unit/ and Integration/
  samples/          sample PDFs for testing
```

## Configuration and Secrets
- Never put connection strings, keys or any settings in `appsettings.json`. Settings go only in `appsettings.Development.json`.
- Anthropic key only in user-secrets: `dotnet user-secrets set "Anthropic:ApiKey" "<key>" --project src/Studeo.Api`

## Running
- Database: `docker compose up -d` (PostgreSQL 17 on `localhost:5440`)
- API: `dotnet run --project src/Studeo.Api --launch-profile http` → http://localhost:5110, Swagger at `/swagger`. Migrations apply on startup in Development.
- Tests: `dotnet test Studeo.slnx` (integration tests need the docker-compose database; each run uses a throwaway database)
- Migrations: `dotnet ef migrations add <Name> --project src/Studeo.Api --output-dir Models/Migrations`
- `nuget.config` uses only nuget.org (the global Telerik feed is broken).

## Workflow Rules
- Never commit alone
- Always i have to review before commit
- Run tests after every implementation
