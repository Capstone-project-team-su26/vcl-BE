using Microsoft.EntityFrameworkCore;
using VCL_BLL.Helpers;
using VCL_DLL.Models;

namespace VCL_BLL.Seeding;

public static class VCLDatabaseSeeder
{
    private sealed record SeedUser(
        string FullName,
        string Email,
        string Phone,
        string Password,
        string Role,
        string UserType,
        string Status,
        bool IsEmailVerified = true);

    private sealed record SeedCustomerProfile(
        string Country,
        string Address);

    public static async Task SeedAsync(VCLDBContext context, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var employees = new[]
        {
            new SeedUser("System Admin", "admin@vcl.com", "0900000001", "Admin123!", "Admin", "Employee", "Active"),
            new SeedUser("SALE 01", "sale01@employee.com", "0999999999", "Employee123", "Sale", "Employee", "Active"),
            new SeedUser("Warehouse Staff", "warehouse@vcl.com", "0900000002", "Employee123", "WarehouseStaff", "Employee", "Active"),
            new SeedUser("Operations Manager", "ops@vcl.com", "0900000003", "Employee123", "OperationsManager", "Employee", "Active"),
            new SeedUser("Delivery Staff", "delivery@vcl.com", "0900000004", "Employee123", "Delivery", "Employee", "Active"),
        };

        foreach (var employee in employees)
        {
            await EnsureEmployeeAsync(context, employee, now, cancellationToken);
        }

        await EnsureCustomerAsync(
            context,
            new SeedUser("Nguyen Van A", "customer@example.com", "0901234567", "Password123", "Customer", "Customer", "Active"),
            new SeedCustomerProfile("Vietnam", "Ho Chi Minh City"),
            now,
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureEmployeeAsync(
        VCLDBContext context,
        SeedUser seed,
        DateTime createdAt,
        CancellationToken cancellationToken)
    {
        var exists = await context.Users.AnyAsync(
            u => u.Email == seed.Email,
            cancellationToken);

        if (exists)
            return;

        context.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            FullName = seed.FullName,
            Email = seed.Email,
            Phone = seed.Phone,
            Password = PasswordHelper.HashPassword(seed.Password),
            Role = seed.Role,
            UserType = seed.UserType,
            Status = seed.Status,
            IsEmailVerified = seed.IsEmailVerified,
            CreatedAt = createdAt
        });
    }

    private static async Task EnsureCustomerAsync(
        VCLDBContext context,
        SeedUser seed,
        SeedCustomerProfile profile,
        DateTime createdAt,
        CancellationToken cancellationToken)
    {
        var exists = await context.Users.AnyAsync(
            u => u.Email == seed.Email,
            cancellationToken);

        if (exists)
            return;

        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        context.Users.Add(new User
        {
            Id = userId,
            FullName = seed.FullName,
            Email = seed.Email,
            Phone = seed.Phone,
            Password = PasswordHelper.HashPassword(seed.Password),
            Role = seed.Role,
            UserType = seed.UserType,
            Status = seed.Status,
            IsEmailVerified = seed.IsEmailVerified,
            CreatedAt = createdAt
        });

        context.Customers.Add(new Customer
        {
            Id = customerId,
            UserId = userId,
            CustomerCode = $"CUS-{createdAt:yyyyMMddHHmmss}-{customerId.ToString("N")[..6].ToUpperInvariant()}",
            FullName = seed.FullName,
            Email = seed.Email,
            Phone = seed.Phone,
            Country = profile.Country,
            Address = profile.Address,
            CustomerType = "Individual",
            Status = seed.Status,
            CreatedAt = createdAt
        });
    }
}
