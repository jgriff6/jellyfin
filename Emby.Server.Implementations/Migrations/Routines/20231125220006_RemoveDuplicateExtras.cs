using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Emby.Server.Implementations.Data;
using MediaBrowser.Controller;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.Migrations.Routines
{
    /// <summary>
    /// Remove duplicate entries which were caused by a bug where a file was considered to be an "Extra" to itself.
    /// </summary>
    public partial class RemoveDuplicateExtras : IPostStartupMigrationRoutine
    {
        private const string DbFilename = "library.db";
        private readonly ILogger<RemoveDuplicateExtras> _logger;
        private readonly IServerApplicationPaths _paths;

        /// <summary>
        /// Initializes a new instance of the <see cref="RemoveDuplicateExtras"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="paths">The application paths.</param>
        public RemoveDuplicateExtras(ILogger<RemoveDuplicateExtras> logger, IServerApplicationPaths paths)
        {
            _logger = logger;
            _paths = paths;
        }

        /// <inheritdoc/>
        public Guid Id => Guid.Parse("{ACBE17B7-8435-4A83-8B64-6FCF162CB9BD}");

        /// <inheritdoc/>
        public string Name => "RemoveDuplicateExtras";

        /// <inheritdoc />
        public long Timestamp => 20231125220006L;

        /// <inheritdoc/>
        public bool PerformOnNewInstall => false;

        /// <inheritdoc/>
        public void Perform()
        {
            var dataPath = _paths.DataPath;
            var dbPath = Path.Combine(dataPath, DbFilename);
            using var connection = new SqliteConnection($"Filename={dbPath}");
            connection.Open();
            using (var transaction = connection.BeginTransaction())
            {
                // Query the database for the ids of duplicate extras
                var queryResult = connection.Query("SELECT t1.Path FROM TypedBaseItems AS t1, TypedBaseItems AS t2 WHERE t1.Path=t2.Path AND t1.Type!=t2.Type AND t1.Type='MediaBrowser.Controller.Entities.Video'");
                var bads = string.Join(", ", queryResult.Select(x => x.GetString(0)));

                // Do nothing if no duplicate extras were detected
                if (bads.Length == 0)
                {
                    _logger.LogInformation("No duplicate extras detected, skipping migration.");
                    return;
                }

                // Back up the database before deleting any entries
                for (int i = 1; ; i++)
                {
                    var bakPath = string.Format(CultureInfo.InvariantCulture, "{0}.bak{1}", dbPath, i);
                    if (!File.Exists(bakPath))
                    {
                        try
                        {
                            File.Copy(dbPath, bakPath);
                            _logger.LogInformation("Library database backed up to {BackupPath}", bakPath);
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Cannot make a backup of {Library} at path {BackupPath}", DbFilename, bakPath);
                            throw;
                        }
                    }
                }

                // Delete all duplicate extras
                _logger.LogInformation("Removing found duplicated extras for the following items: {DuplicateExtras}", bads);
                connection.Execute("DELETE FROM TypedBaseItems WHERE rowid IN (SELECT t1.rowid FROM TypedBaseItems AS t1, TypedBaseItems AS t2 WHERE t1.Path=t2.Path AND t1.Type!=t2.Type AND t1.Type='MediaBrowser.Controller.Entities.Video')");
                transaction.Commit();
            }
        }
    }
}