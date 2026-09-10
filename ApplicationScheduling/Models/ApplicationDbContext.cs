using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ApplicationScheduling.Models
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Appointment> Appointments { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // The Npgsql provider changed its default DateTime mapping from
            // "timestamp without time zone" to "timestamp with time zone" in v6.
            // Pin the original type so behaviour (and the existing schema) is unchanged
            // across the .NET 5 -> .NET 10 upgrade. Proper timezone-aware handling is
            // tracked as a separate follow-up (see docs/dotnet-10-migration-plan.md).
            builder.Entity<Appointment>(entity =>
            {
                entity.Property(e => e.StartDate).HasColumnType("timestamp without time zone");
                entity.Property(e => e.EndDate).HasColumnType("timestamp without time zone");
            });
        }
    }
}
