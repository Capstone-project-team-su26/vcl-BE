---
name: ef-migration-handler
description: Guides the agent on how to correctly build the projects and run EF Core migrations and database updates for VCL_DLL and VCL_API.
---
# Entity Framework Core Migration Handler

This skill standardizes running EF Core migrations in the layered architecture of the VCL project.

## Instructions
1. Always build the project first using `dotnet build VCL_BLL/VCL_BLL.csproj` or `dotnet build` to ensure there are no compilation errors before adding migrations.
2. To add a new migration, use:
   ```bash
   dotnet ef migrations add <MigrationName> --project VCL_DLL --startup-project VCL_API
   ```
3. To update the database, use:
   ```bash
   dotnet ef database update --project VCL_DLL --startup-project VCL_API
   ```
4. Always verify that connection strings used during database updates are loaded from environment variables or local user secrets rather than hardcoded in the DbContext.
