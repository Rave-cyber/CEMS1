using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CEMS.Data;
using CEMS.Models;
using Microsoft.AspNetCore.Identity;

namespace CEMS.Services
{
    public class BackupFileInfo
    {
        public string FileName    { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public long   SizeBytes   { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public interface IDatabaseBackupService
    {
        Task<(byte[] Data, string FileName)> CreateFullBackupAsync();
        Task<(bool Success, string Message)> RestoreBackupAsync(Stream backupStream);
        Task<(bool Success, string Message)> RestoreBackupByNameAsync(string fileName);
        Task<List<BackupFileInfo>> GetBackupHistoryAsync();
    }

    public class DatabaseBackupService : IDatabaseBackupService
    {
        private readonly ApplicationDbContext      _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ILogger<DatabaseBackupService> _logger;

        private static readonly string BackupFolder =
            Path.Combine(Directory.GetCurrentDirectory(), "Backups");

        public DatabaseBackupService(
            ApplicationDbContext context,
            UserManager<IdentityUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ILogger<DatabaseBackupService> logger)
        {
            _context     = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _logger      = logger;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // CREATE BACKUP — saves to disk AND returns bytes for download
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<(byte[] Data, string FileName)> CreateFullBackupAsync()
        {
            _logger.LogInformation("Starting full database backup at {DateTime}", DateTime.UtcNow);

            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                var users          = await _context.Users.ToListAsync();
                var roles          = await _context.Roles.ToListAsync();
                var userRoles      = await _context.UserRoles.ToListAsync();
                var expenses       = await _context.Expenses.ToListAsync();
                var expenseReports = await _context.ExpenseReports.ToListAsync();
                var expenseItems   = await _context.ExpenseItems.ToListAsync();
                var approvals      = await _context.Approvals.ToListAsync();
                var budgets        = await _context.Budgets.ToListAsync();
                var ceoProfiles    = await _context.CEOProfiles.ToListAsync();
                var mgrProfiles    = await _context.ManagerProfiles.ToListAsync();
                var finProfiles    = await _context.FinanceProfiles.ToListAsync();
                var drvProfiles    = await _context.DriverProfiles.ToListAsync();
                var payments       = await _context.ReimbursementPayments.ToListAsync();
                var auditLogs      = await _context.AuditLogs.ToListAsync();
                var notifications  = await _context.Notifications.ToListAsync();
                var fuelPrices     = await _context.FuelPrices.ToListAsync();

                AddToArchive(archive, "AspNetUsers.json",           users);
                AddToArchive(archive, "AspNetRoles.json",           roles);
                AddToArchive(archive, "AspNetUserRoles.json",       userRoles);
                AddToArchive(archive, "Expenses.json",              expenses);
                AddToArchive(archive, "ExpenseReports.json",        expenseReports);
                AddToArchive(archive, "ExpenseItems.json",          expenseItems);
                AddToArchive(archive, "Approvals.json",             approvals);
                AddToArchive(archive, "Budgets.json",               budgets);
                AddToArchive(archive, "CEOProfiles.json",           ceoProfiles);
                AddToArchive(archive, "ManagerProfiles.json",       mgrProfiles);
                AddToArchive(archive, "FinanceProfiles.json",       finProfiles);
                AddToArchive(archive, "DriverProfiles.json",        drvProfiles);
                AddToArchive(archive, "ReimbursementPayments.json", payments);
                AddToArchive(archive, "AuditLogs.json",             auditLogs);
                AddToArchive(archive, "Notifications.json",         notifications);
                AddToArchive(archive, "FuelPrices.json",            fuelPrices);
                AddToArchive(archive, "BACKUP_METADATA.json", new
                {
                    BackupDate    = DateTime.UtcNow,
                    BackupVersion = "2.0",
                    Counts = new
                    {
                        Users = users.Count, Roles = roles.Count,
                        Expenses = expenses.Count, ExpenseReports = expenseReports.Count,
                        Budgets = budgets.Count, FuelPrices = fuelPrices.Count
                    }
                });
            }

            var bytes    = ms.ToArray();
            var fileName = $"CEMS_Backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";

            // Save to disk so it can be restored later without re-uploading
            try
            {
                Directory.CreateDirectory(BackupFolder);
                var filePath = Path.Combine(BackupFolder, fileName);
                await File.WriteAllBytesAsync(filePath, bytes);
                _logger.LogInformation("Backup saved to disk: {Path}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not save backup to disk: {Err}", ex.Message);
            }

            _logger.LogInformation("Backup completed: {File}", fileName);
            return (bytes, fileName);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // RESTORE FROM STREAM (upload)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<(bool Success, string Message)> RestoreBackupAsync(Stream backupStream)
        {
            try
            {
                using var archive = new ZipArchive(backupStream, ZipArchiveMode.Read);
                return await RestoreFromArchive(archive);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restore failed");
                return (false, $"Restore failed: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // RESTORE BY FILENAME (from stored backups on disk)
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<(bool Success, string Message)> RestoreBackupByNameAsync(string fileName)
        {
            try
            {
                // Sanitize — only allow simple filenames, no path traversal
                var safeName = Path.GetFileName(fileName);
                var filePath = Path.Combine(BackupFolder, safeName);

                if (!File.Exists(filePath))
                    return (false, $"Backup file '{safeName}' not found on server.");

                using var fs      = File.OpenRead(filePath);
                using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
                return await RestoreFromArchive(archive);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restore by name failed");
                return (false, $"Restore failed: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // LIST STORED BACKUPS
        // ─────────────────────────────────────────────────────────────────────────
        public Task<List<BackupFileInfo>> GetBackupHistoryAsync()
        {
            var list = new List<BackupFileInfo>();
            try
            {
                if (!Directory.Exists(BackupFolder))
                    return Task.FromResult(list);

                list = Directory.GetFiles(BackupFolder, "*.zip")
                    .OrderByDescending(File.GetCreationTime)
                    .Select(f =>
                    {
                        var fi = new FileInfo(f);
                        return new BackupFileInfo
                        {
                            FileName    = fi.Name,
                            DisplayName = fi.Name,
                            SizeBytes   = fi.Length,
                            CreatedAt   = fi.CreationTime
                        };
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing backups");
            }
            return Task.FromResult(list);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // CORE RESTORE LOGIC
        // ─────────────────────────────────────────────────────────────────────────
        private async Task<(bool Success, string Message)> RestoreFromArchive(ZipArchive archive)
        {
            if (archive.GetEntry("BACKUP_METADATA.json") == null)
                return (false, "Invalid backup file: Missing BACKUP_METADATA.json");

            _logger.LogInformation("Starting restore at {DateTime}", DateTime.UtcNow);

            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
            };

            // ── Roles ─────────────────────────────────────────────────────────────
            var backupRoles = ReadFromArchive<List<IdentityRole>>(archive, "AspNetRoles.json", opts);
            if (backupRoles != null)
            {
                var existing = (await _context.Roles.AsNoTracking().Select(r => r.Id).ToListAsync()).ToHashSet();
                foreach (var role in backupRoles)
                    await UpsertEntity(role, existing.Contains(role.Id));
                _logger.LogInformation("Roles: done");
            }

            // ── Users ─────────────────────────────────────────────────────────────
            var backupUsers = ReadFromArchive<List<IdentityUser>>(archive, "AspNetUsers.json", opts);
            if (backupUsers != null)
            {
                var existing = (await _context.Users.AsNoTracking().Select(u => u.Id).ToListAsync()).ToHashSet();
                foreach (var user in backupUsers)
                    await UpsertEntity(user, existing.Contains(user.Id));
                _logger.LogInformation("Users: done");
            }

            // ── UserRoles ─────────────────────────────────────────────────────────
            var backupUserRoles = ReadFromArchive<List<IdentityUserRole<string>>>(archive, "AspNetUserRoles.json", opts);
            if (backupUserRoles != null)
            {
                var existing = (await _context.UserRoles.AsNoTracking()
                    .Select(ur => ur.UserId + "|" + ur.RoleId).ToListAsync()).ToHashSet();
                foreach (var ur in backupUserRoles)
                    if (!existing.Contains(ur.UserId + "|" + ur.RoleId))
                        await UpsertEntity(ur, false);
                _logger.LogInformation("UserRoles: done");
            }

            // ── FuelPrices (raw SQL so deleted rows with original IDs come back) ──
            var fuelPrices = ReadFromArchive<List<FuelPrice>>(archive, "FuelPrices.json", opts);
            if (fuelPrices != null)
            {
                await RawUpsertFuelPrices(fuelPrices);
                _logger.LogInformation("FuelPrices: done");
            }

            // ── Budgets ───────────────────────────────────────────────────────────
            await UpsertById(archive, "Budgets.json", _context.Budgets, x => x.Id, opts);

            // ── Profiles ──────────────────────────────────────────────────────────
            await UpsertById(archive, "CEOProfiles.json",     _context.CEOProfiles,     x => x.Id, opts);
            await UpsertById(archive, "ManagerProfiles.json", _context.ManagerProfiles, x => x.Id, opts);
            await UpsertById(archive, "FinanceProfiles.json", _context.FinanceProfiles, x => x.Id, opts);
            await UpsertById(archive, "DriverProfiles.json",  _context.DriverProfiles,  x => x.Id, opts);

            // ── Expenses ──────────────────────────────────────────────────────────
            await UpsertById(archive, "Expenses.json",              _context.Expenses,              x => x.Id, opts);
            await UpsertById(archive, "ExpenseReports.json",        _context.ExpenseReports,        x => x.Id, opts);
            await UpsertById(archive, "ExpenseItems.json",          _context.ExpenseItems,          x => x.Id, opts);
            await UpsertById(archive, "Approvals.json",             _context.Approvals,             x => x.Id, opts);
            await UpsertById(archive, "ReimbursementPayments.json", _context.ReimbursementPayments, x => x.Id, opts);
            await UpsertById(archive, "AuditLogs.json",             _context.AuditLogs,             x => x.Id, opts);
            await UpsertById(archive, "Notifications.json",         _context.Notifications,         x => x.Id, opts);

            _logger.LogInformation("Restore completed successfully");
            return (true, "✅ Restore completed. Existing data preserved. Deleted records were brought back.");
        }

        /// <summary>
        /// Restores FuelPrices using raw SQL MERGE so deleted rows with their
        /// original identity IDs are re-inserted correctly.
        /// </summary>
        private async Task RawUpsertFuelPrices(List<FuelPrice> items)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                var conn = _context.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    await conn.OpenAsync();

                using var tx = await conn.BeginTransactionAsync();
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = (System.Data.Common.DbTransaction)tx;

                    cmd.CommandText = "SET IDENTITY_INSERT [FuelPrices] ON";
                    await cmd.ExecuteNonQueryAsync();

                    foreach (var fp in items)
                    {
                        cmd.CommandText = @"
                            MERGE [FuelPrices] AS target
                            USING (SELECT @Id AS Id) AS source ON target.Id = source.Id
                            WHEN MATCHED THEN
                                UPDATE SET
                                    [Name]            = @Name,
                                    [Description]     = @Description,
                                    [Price]           = @Price,
                                    [Unit]            = @Unit,
                                    [Icon]            = @Icon,
                                    [CssClass]        = @CssClass,
                                    [UpdatedAt]       = @UpdatedAt,
                                    [UpdatedByUserId] = @UpdatedByUserId
                            WHEN NOT MATCHED THEN
                                INSERT ([Id],[Name],[Description],[Price],[Unit],[Icon],[CssClass],[UpdatedAt],[UpdatedByUserId])
                                VALUES (@Id,@Name,@Description,@Price,@Unit,@Icon,@CssClass,@UpdatedAt,@UpdatedByUserId);";

                        cmd.Parameters.Clear();
                        AddParam(cmd, "@Id",              fp.Id);
                        AddParam(cmd, "@Name",            fp.Name);
                        AddParam(cmd, "@Description",     fp.Description);
                        AddParam(cmd, "@Price",           fp.Price);
                        AddParam(cmd, "@Unit",            fp.Unit);
                        AddParam(cmd, "@Icon",            fp.Icon);
                        AddParam(cmd, "@CssClass",        fp.CssClass);
                        AddParam(cmd, "@UpdatedAt",       fp.UpdatedAt);
                        AddParam(cmd, "@UpdatedByUserId", (object?)fp.UpdatedByUserId ?? DBNull.Value);

                        await cmd.ExecuteNonQueryAsync();
                    }

                    cmd.CommandText = "SET IDENTITY_INSERT [FuelPrices] OFF";
                    cmd.Parameters.Clear();
                    await cmd.ExecuteNonQueryAsync();

                    await tx.CommitAsync();
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    _logger.LogError(ex, "RawUpsertFuelPrices failed");
                    throw;
                }
            });
        }

        private static void AddParam(System.Data.Common.DbCommand cmd, string name, object? value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Upsert a single entity — clears tracker first to avoid conflicts.</summary>
        private async Task UpsertEntity<T>(T entity, bool exists) where T : class
        {
            try
            {
                _context.ChangeTracker.Clear();
                _context.Entry(entity).State = exists ? EntityState.Modified : EntityState.Added;
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Skipping {Type}: {Err}", typeof(T).Name, ex.Message);
                _context.ChangeTracker.Clear();
            }
        }

        /// <summary>
        /// Upsert a table with int PK. Uses SET IDENTITY_INSERT so deleted rows
        /// can be re-inserted with their original IDs.
        /// </summary>
        private async Task UpsertById<T>(
            ZipArchive archive,
            string filename,
            DbSet<T> dbSet,
            Func<T, int> getId,
            JsonSerializerOptions opts) where T : class
        {
            var items = ReadFromArchive<List<T>>(archive, filename, opts);
            if (items == null || items.Count == 0) return;

            var entityType = _context.Model.FindEntityType(typeof(T));
            var tableName  = entityType?.GetTableName() ?? typeof(T).Name;

            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                using var tx = await _context.Database.BeginTransactionAsync();
                try
                {
                    _context.ChangeTracker.Clear();
                    var existingIds = (await dbSet.AsNoTracking().ToListAsync())
                                      .Select(getId).ToHashSet();

                    int ins = 0, upd = 0;

                    await _context.Database.ExecuteSqlRawAsync(
                        $"SET IDENTITY_INSERT [{tableName}] ON");

                    foreach (var item in items)
                    {
                        try
                        {
                            _context.ChangeTracker.Clear();
                            var exists = existingIds.Contains(getId(item));
                            _context.Entry(item).State = exists
                                ? EntityState.Modified
                                : EntityState.Added;
                            await _context.SaveChangesAsync();
                            if (exists) upd++; else ins++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("Skipping {File} id={Id}: {Err}",
                                filename, getId(item), ex.Message);
                            _context.ChangeTracker.Clear();
                        }
                    }

                    await _context.Database.ExecuteSqlRawAsync(
                        $"SET IDENTITY_INSERT [{tableName}] OFF");

                    await tx.CommitAsync();
                    _context.ChangeTracker.Clear();
                    _logger.LogInformation("{File}: {I} inserted, {U} updated", filename, ins, upd);
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    _logger.LogError(ex, "UpsertById failed for {File}", filename);
                    throw;
                }
            });
        }

        private void AddToArchive<T>(ZipArchive archive, string filename, T data)
        {
            var entry = archive.CreateEntry(filename);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true,
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
            }));
        }

        private T? ReadFromArchive<T>(ZipArchive archive, string filename, JsonSerializerOptions opts)
        {
            var entry = archive.GetEntry(filename);
            if (entry == null)
            {
                _logger.LogWarning("{File} not found in backup — skipping", filename);
                return default;
            }
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<T>(reader.ReadToEnd(), opts);
        }
    }
}
