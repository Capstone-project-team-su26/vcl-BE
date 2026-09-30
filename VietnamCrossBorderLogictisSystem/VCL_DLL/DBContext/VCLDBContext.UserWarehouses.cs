#nullable disable
using Microsoft.EntityFrameworkCore;

namespace VCL_DLL.Models;

public partial class VCLDBContext
{
    /// <summary>Nhân viên kho ↔ kho được gán.</summary>
    public virtual DbSet<UserWarehouse> UserWarehouses { get; set; }

    /// <summary>Bảng gán nhân viên kho vào kho — gọi từ <c>OnModelCreatingPartial</c>.</summary>
    private static void ConfigureUserWarehouses(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserWarehouse>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_USER_WAREHOUSES");
            entity.ToTable("USER_WAREHOUSES");
            entity.Property(e => e.Id).HasDefaultValueSql("(newid())").HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");
            entity.Property(e => e.AssignedById).HasColumnName("assigned_by");
            entity.Property(e => e.AssignedAt).HasDefaultValueSql("(getutcdate())").HasColumnName("assigned_at");
            entity.Property(e => e.Note).HasMaxLength(500).HasColumnName("note");

            // Một nhân viên chỉ được gán vào một kho một lần.
            entity.HasIndex(e => new { e.UserId, e.WarehouseId })
                .IsUnique()
                .HasDatabaseName("UX_USER_WAREHOUSES_USER_WAREHOUSE");
            entity.HasIndex(e => e.WarehouseId).HasDatabaseName("IX_USER_WAREHOUSES_WAREHOUSE");

            entity.HasOne<User>().WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_USER_WAREHOUSES_USER");
            entity.HasOne<Warehouse>().WithMany()
                .HasForeignKey(e => e.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_USER_WAREHOUSES_WAREHOUSE");
        });
    }
}
