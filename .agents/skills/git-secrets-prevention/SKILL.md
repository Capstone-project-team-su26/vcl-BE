---
name: git-secrets-prevention
description: Enforce security by checking and preventing database connection strings, API keys, and secret credentials from being hardcoded or committed to git.
---
# Git Secrets Prevention

This skill prevents credentials from being hardcoded or committed to the repository.

## Instructions
1. Before committing any code or modifying configurations (e.g., `appsettings.json`, `appsettings.Development.json` in VCL_API), check for sensitive credentials:
   - Database connection strings containing passwords (e.g., `Server=...;User Id=...;Password=...`).
   - Private keys, JWT symmetric keys, or third-party API tokens.
2. If any credentials are found, instruct the team to use environment variables (`.env`) or .NET User Secrets (`dotnet user-secrets`).
3. Ensure that `.env` files and local user secret files are listed in [`.gitignore`](file:///c:/DO_AN/vcl-BE/.gitignore) and never committed.
