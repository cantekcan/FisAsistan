using FisAsistan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FisAsistan.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
        builder.HasIndex(u => u.Email).IsUnique();
        builder.Property(u => u.PasswordHash).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(200);
    }
}

public class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.OriginalFileName).IsRequired().HasMaxLength(500);
        builder.Property(r => r.StoredFileName).IsRequired().HasMaxLength(500);
        builder.Property(r => r.StoragePath).IsRequired().HasMaxLength(1000);
        builder.Property(r => r.ContentType).HasMaxLength(200);

        builder.HasOne(r => r.User)
            .WithMany(u => u.Receipts)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Fields)
            .WithOne(f => f.Receipt)
            .HasForeignKey(f => f.ReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.VatLines)
            .WithOne(v => v.Receipt)
            .HasForeignKey(v => v.ReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.ValidationIssues)
            .WithOne(i => i.Receipt)
            .HasForeignKey(i => i.ReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.UserId);
        builder.HasIndex(r => r.Status);
    }
}

public class ReceiptFieldConfiguration : IEntityTypeConfiguration<ReceiptField>
{
    public void Configure(EntityTypeBuilder<ReceiptField> builder)
    {
        builder.HasKey(f => f.Id);
        builder.HasIndex(f => new { f.ReceiptId, f.FieldName }).IsUnique();
    }
}

public class ReceiptVatLineConfiguration : IEntityTypeConfiguration<ReceiptVatLine>
{
    public void Configure(EntityTypeBuilder<ReceiptVatLine> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.RatePercent).HasPrecision(5, 2);
        builder.Property(v => v.BaseAmount).HasPrecision(18, 2);
        builder.Property(v => v.VatAmount).HasPrecision(18, 2);
    }
}

public class ReceiptValidationIssueConfiguration : IEntityTypeConfiguration<ReceiptValidationIssue>
{
    public void Configure(EntityTypeBuilder<ReceiptValidationIssue> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Message).IsRequired().HasMaxLength(1000);
        builder.Property(i => i.RuleCode).HasMaxLength(100);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Action).IsRequired().HasMaxLength(200);
        builder.HasIndex(a => a.ReceiptId);
    }
}
