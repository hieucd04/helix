using System.Collections.Generic;
using Helix.Persistence.Abstractions;
using JetBrains.Annotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Helix.Persistence
{
    class SQLitePersistence<TDataTransferObject> : ISQLitePersistence<TDataTransferObject> where TDataTransferObject : class
    {
        readonly string _pathToDatabaseFile;
        readonly Dictionary<string, object> _publicApiLockMap;

        public SQLitePersistence(string pathToDatabaseFile)
        {
            _pathToDatabaseFile = pathToDatabaseFile;
            _publicApiLockMap = new Dictionary<string, object>
            {
                { $"{nameof(Save)}", new object() },
                { $"{nameof(Update)}", new object() }
            };
            EnsureDatabaseIsRecreated();

            void EnsureDatabaseIsRecreated()
            {
                using (var reportDatabaseContext = new SQLiteDbContext(pathToDatabaseFile))
                {
                    reportDatabaseContext.Database.EnsureDeleted();
                    reportDatabaseContext.Database.EnsureCreated();
                }
            }
        }

        public TDataTransferObject GetByPrimaryKey(params object[] primaryKeyValues)
        {
            using (var sqLiteDbContext = new SQLiteDbContext(_pathToDatabaseFile))
                return sqLiteDbContext.DataTransferObjects.Find(primaryKeyValues);
        }

        public void Save(params TDataTransferObject[] dataTransferObjects)
        {
            lock (_publicApiLockMap[nameof(Save)])
            {
                using (var sqLiteDbContext = new SQLiteDbContext(_pathToDatabaseFile))
                {
                    sqLiteDbContext.DataTransferObjects.AddRange(dataTransferObjects);
                    sqLiteDbContext.SaveChanges();
                }
            }
        }

        public void Update(params TDataTransferObject[] dataTransferObjects)
        {
            lock (_publicApiLockMap[nameof(Update)])
            {
                using (var sqLiteDbContext = new SQLiteDbContext(_pathToDatabaseFile))
                {
                    sqLiteDbContext.DataTransferObjects.UpdateRange(dataTransferObjects);
                    sqLiteDbContext.SaveChanges();
                }
            }
        }

        class SQLiteDbContext : DbContext
        {
            readonly string _pathToDatabaseFile;

            public DbSet<TDataTransferObject> DataTransferObjects { get; [UsedImplicitly] set; }

            public SQLiteDbContext(string pathToDatabaseFile) { _pathToDatabaseFile = pathToDatabaseFile; }

            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            {
                optionsBuilder.UseSqlite(new SqliteConnectionStringBuilder { DataSource = _pathToDatabaseFile }.ToString());
            }
        }
    }
}