#nullable disable
using Microsoft.EntityFrameworkCore;

namespace VCL_DLL.Models;

public partial class VCLDBContext
{
    /// <summary>Tham số hệ thống Admin chỉnh được (giới hạn tạo đơn...).</summary>
    public virtual DbSet<SystemSetting> SystemSettings { get; set; }

    /// <summary>Nhật ký thay đổi tham số hệ thống.</summary>
    public virtual DbSet<SystemSettingHistory> SystemSettingHistories { get; set; }

    /// <summary>Bảng tham số hệ thống — gọi từ <c>OnModelCreatingPartial</c>.</summary>
    private static void ConfigureSystemSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_SYSTEM_SETTINGS");
            entity.ToTable("SYSTEM_SETTINGS");
            entity.Property(e => e.Id).HasDefaultValueSql("(newid())").HasColumnName("id");
            entity.Property(e => e.SettingKey).IsRequired().HasMaxLength(100).HasColumnName("setting_key");
            entity.Property(e => e.SettingGroup).IsRequired().HasMaxLength(50).HasColumnName("setting_group");
            entity.Property(e => e.DecimalValue).HasColumnType("decimal(18, 2)").HasColumnName("decimal_value");
            entity.Property(e => e.Description).HasMaxLength(500).HasColumnName("description");
            entity.Property(e => e.UpdatedById).HasColumnName("updated_by");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())").HasColumnName("created_at");

            entity.HasIndex(e => e.SettingKey).IsUnique().HasDatabaseName("UX_SYSTEM_SETTINGS_KEY");

            entity.HasOne<User>().WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_SYSTEM_SETTINGS_UPDATED_BY");
        });

        modelBuilder.Entity<SystemSettingHistory>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK_SYSTEM_SETTING_HISTORIES");
            entity.ToTable("SYSTEM_SETTING_HISTORIES");
            entity.Property(e => e.Id).HasDefaultValueSql("(newid())").HasColumnName("id");
            entity.Property(e => e.SettingKey).IsRequired().HasMaxLength(100).HasColumnName("setting_key");
            entity.Property(e => e.OldValue).HasColumnType("decimal(18, 2)").HasColumnName("old_value");
            entity.Property(e => e.NewValue).HasColumnType("decimal(18, 2)").HasColumnName("new_value");
            entity.Property(e => e.ChangedById).HasColumnName("changed_by");
            entity.Property(e => e.ChangedAt).HasDefaultValueSql("(getutcdate())").HasColumnName("changed_at");
            entity.Property(e => e.Note).HasMaxLength(500).HasColumnName("note");

            entity.HasIndex(e => new { e.SettingKey, e.ChangedAt }).HasDatabaseName("IX_SYSTEM_SETTING_HISTORIES_KEY_TIME");

            entity.HasOne<User>().WithMany()
                .HasForeignKey(e => e.ChangedById)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_SYSTEM_SETTING_HISTORIES_CHANGED_BY");
        });
    }
}
