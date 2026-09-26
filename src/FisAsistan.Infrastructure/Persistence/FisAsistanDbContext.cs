using FisAsistan.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FisAsistan.Infrastructure.Persistence;

public class FisAsistanDbContext : DbContext
{
    public FisAsistanDbContext(DbContextOptions<FisAsistanDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptField> ReceiptFields => Set<ReceiptField>();
    public DbSet<ReceiptVatLine> ReceiptVatLines => Set<ReceiptVatLine>();
    public DbSet<ReceiptValidationIssue> ReceiptValidationIssues => Set<ReceiptValidationIssue>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FisAsistanDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
