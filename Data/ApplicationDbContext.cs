
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using CEMS.Models;

namespace CEMS.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Expense> Expenses { get; set; }
        public DbSet<ExpenseReport> ExpenseReports { get; set; }
        public DbSet<ExpenseItem> ExpenseItems { get; set; }
        public DbSet<Approval> Approvals { get; set; }
        public DbSet<Budget> Budgets { get; set; }

        // Account Management Tables
        public DbSet<CEOProfile> CEOProfiles { get; set; }
        public DbSet<ManagerProfile> ManagerProfiles { get; set; }
        public DbSet<FinanceProfile> FinanceProfiles { get; set; }
        public DbSet<DriverProfile> DriverProfiles { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Configure Expense -> IdentityUser relationship
            builder.Entity<Expense>()
                .HasOne<IdentityUser>(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Ensure decimal precision/scale is explicit to avoid truncation warnings.
            // Adjust precision/scale to match your domain requirements.
            builder.Entity<Expense>()
                .Property(e => e.Amount)
                .HasPrecision(18, 2);

            builder.Entity<ExpenseReport>()
                .HasOne<IdentityUser>(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ExpenseReport>()
                .Property(r => r.TotalAmount)
                .HasPrecision(18, 2);

            builder.Entity<ExpenseReport>()
                .Property(r => r.Reimbursed)
                .HasDefaultValue(false);

            builder.Entity<ExpenseItem>()
                .HasOne(i => i.Report)
                .WithMany(r => r.Items)
                .HasForeignKey(i => i.ReportId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ExpenseItem>()
                .Property(i => i.Amount)
                .HasPrecision(18, 2);

            builder.Entity<Approval>()
                .HasOne(a => a.Report)
                .WithMany()
                .HasForeignKey(a => a.ReportId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Budget>()
                .Property(b => b.Allocated)
                .HasPrecision(18, 2);

            builder.Entity<Budget>()
                .Property(b => b.Spent)
                .HasPrecision(18, 2);

            // Account Profile tables
            builder.Entity<CEOProfile>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<CEOProfile>()
                .HasIndex(p => p.UserId)
                .IsUnique();

            builder.Entity<ManagerProfile>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ManagerProfile>()
                .HasIndex(p => p.UserId)
                .IsUnique();

            builder.Entity<FinanceProfile>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<FinanceProfile>()
                .HasIndex(p => p.UserId)
                .IsUnique();

            builder.Entity<DriverProfile>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<DriverProfile>()
                .HasIndex(p => p.UserId)
                .IsUnique();
        }
    }
}