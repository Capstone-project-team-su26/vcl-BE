# Project Rules for Vietnam Cross-Border Logistics System (VCL)

This document contains rules and guidelines for agents working on the VCL backend codebase.

## 1. Architectural Integrity
- Keep clean boundaries between layers:
  - **VCL_API** (Controllers, DTOs, HTTP/Configuration logic) calls **VCL_BLL**.
  - **VCL_BLL** (Services, Business Logic) handles calculations, validators, and queries **VCL_DLL**.
  - **VCL_DLL** (DbContext, Entities, Repositories) interacts directly with the database.
- Do not reference `Microsoft.AspNetCore.Http` or Controller types in `VCL_DLL`.

## 2. API Contract Documentation
- Always check and keep [API_INTEGRATION.md](file:///c:/DO_AN/vcl-BE/docs/API_INTEGRATION.md) up-to-date whenever any API endpoint changes.
- Ensure TypeScript type declarations in the docs match the C# API DTO classes exactly.

## 3. Database & Secret Security
- Never hardcode the SQL Server connection string (especially with user credentials like `sa`) into `appsettings.json` or source code files.
- Always load sensitive credentials using environment variables or local user secrets.
