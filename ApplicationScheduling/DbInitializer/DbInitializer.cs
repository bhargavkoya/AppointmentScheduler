using ApplicationScheduling.Models;
using ApplicationScheduling.Utility;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ApplicationScheduling.DbInitializer
{
    public class DbInitializer : IDbInitializer
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DbInitializer> _logger;


        public DbInitializer(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager, IConfiguration configuration, ILogger<DbInitializer> logger)
        {
            _db = db;
            _roleManager = roleManager;
            _userManager = userManager;
            _configuration = configuration;
            _logger = logger;
        }

        public void Initalize()
        {
            try
            {
                if (_db.Database.GetPendingMigrations().Count() > 0)
                {
                    _db.Database.Migrate();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Applying pending EF Core migrations on startup failed.");
            }

            foreach (var roleName in new[] { Helper.Admin, Helper.Doctor, Helper.Patient })
            {
                if (!_roleManager.RoleExistsAsync(roleName).GetAwaiter().GetResult())
                {
                    _roleManager.CreateAsync(new IdentityRole(roleName)).GetAwaiter().GetResult();
                }
            }

            var adminEmail = _configuration["SeedAdmin:Email"];
            var adminName = _configuration["SeedAdmin:Name"] ?? "Administrator";
            var adminPassword = _configuration["SeedAdmin:Password"];

            if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
            {
                _logger.LogWarning("SeedAdmin:Email / SeedAdmin:Password not configured; skipping admin user seeding.");
                return;
            }

            if (_db.Users.Any(u => u.Email == adminEmail))
            {
                return;
            }

            var result = _userManager.CreateAsync(new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                Name = adminName
            }, adminPassword).GetAwaiter().GetResult();

            if (!result.Succeeded)
            {
                _logger.LogError("Failed to create seed admin user: {Errors}",
                    string.Join("; ", result.Errors.Select(e => e.Description)));
                return;
            }

            var user = _db.Users.FirstOrDefault(u => u.Email == adminEmail);
            _userManager.AddToRoleAsync(user, Helper.Admin).GetAwaiter().GetResult();
        }
    }
}
