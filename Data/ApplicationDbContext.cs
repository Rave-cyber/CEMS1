
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
        public DbSet<Budget> Budgets { get; set; }

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

            builder.Entity<Budget>()
                .Property(b => b.Allocated)
                .HasPrecision(18, 2);

            builder.Entity<Budget>()
                .Property(b => b.Spent)
                .HasPrecision(18, 2);
        }
    }
}