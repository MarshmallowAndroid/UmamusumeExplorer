using SQLite;
using SQLitePCL;
using UmamusumeData.DataDirectories;

namespace UmamusumeData
{
    public static class UmaDataHelper
    {
        private static readonly string[] keys =
        {
            "9c2bab97bcf8c0c4f1a9ea7881a213f6c9ebf9d8d4c6a8e43ce5a259bde7e9fd",
            "c753a5e8f5f78294f7fef57df4a14ffbf9a896cea1d4e09947e0d904e7fde8eaf0"
        };

        private static string key = "";

        private readonly static List<DataDirectory> dataDirectories =
        [
            new DefaultDataDirectory(),
            new DmmDataDirectory(),
            new SteamJapanDataDirectory()
        ];

        private static bool isMetaFileEncrypted = false;
        private static List<ManifestEntry>? manifestEntries;

        public static string UmamusumeDirectory { get; set; } = "";

        public static string ResourceDirectory => Path.Combine(UmamusumeDirectory, "dat");

        public static string MetaFile => Path.Combine(UmamusumeDirectory, "meta");

        public static string MasterFile => Path.Combine(UmamusumeDirectory, "master", "master.mdb");

        public static List<DataDirectory> ScanDirectories()
        {
            List<DataDirectory> existingDirectories = [];

            foreach (var dataDirectory in dataDirectories)
            {
                if (dataDirectory.Exists())
                {
                    existingDirectories.Add(dataDirectory);
                }
            }

            return existingDirectories;
        }

        public static bool CheckDirectory()
        {
            return DataDirectory.CheckDirectory(UmamusumeDirectory);
        }

        public static void Initialize()
        {
            if (File.Exists(MetaFile))
            {
                FileStream fileStream = new(MetaFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                BinaryReader reader = new(fileStream);
                isMetaFileEncrypted = reader.ReadUInt32() != 0x694C5153;

                reader.Dispose();
            }

            SQLitePCL.Batteries_V2.Init();
        }

        public static string GetPath(ManifestEntry? entry)
        {
            if (entry is not null)
            {
                string path = Path.Combine(ResourceDirectory, entry.HashName[..2], entry.HashName);

                if (File.Exists(path)) return path;
                else return "";
            }
            else return "";
        }

        public static string GetPath(IEnumerable<ManifestEntry> entryList, string gameFileBaseName)
        {
            ManifestEntry? entry = entryList.FirstOrDefault(gf => gf.BaseName == gameFileBaseName);

            if (entry is not null)
                return GetPath(entry);
            else return "";
        }

        public static List<ManifestEntry> GetManifestEntries(Func<ManifestEntry, bool>? condition = null)
        {
            manifestEntries ??= GetRows<ManifestEntry>(OpenDatabase(MetaFile, isMetaFileEncrypted));

            if (condition is not null)
                return [.. manifestEntries.Where(condition)];
            else
                return manifestEntries;
        }

        public static void UpdateManifestEntries(IEnumerable<ManifestEntry> entries)
        {
            SQLiteConnection connection = OpenDatabase(MetaFile, isMetaFileEncrypted);
            connection.UpdateAll(entries);
            connection.Close();
            connection.Dispose();
        }

        public static List<T> GetMasterDatabaseRows<T>(Func<T, bool>? condition = null) where T : new()
            => GetRows(OpenDatabase(MasterFile), condition);

        private static SQLiteConnection OpenDatabase(string databaseFile, bool encrypted = false)
        {
            if (encrypted)
            {
                if (string.IsNullOrEmpty(key))
                {
                    Exception? lastException = null;

                    foreach (string checkKey in keys)
                    {
                        try
                        {
                            using SQLiteConnection testConnection = OpenEncryptedDatabase(databaseFile, checkKey);
                            _ = testConnection.ExecuteScalar<int>("SELECT count(*) FROM sqlite_master;");
                            key = checkKey;
                            break;
                        }
                        catch (Exception exception) when (exception is SQLiteException or InvalidOperationException)
                        {
                            lastException = exception;
                        }
                    }

                    if (string.IsNullOrEmpty(key))
                    {
                        throw new InvalidOperationException("Unable to open encrypted meta database with any known key.", lastException);
                    }
                }

                return OpenEncryptedDatabase(databaseFile, key);
            }

            return new SQLiteConnection(databaseFile, SQLiteOpenFlags.ReadWrite);
        }

        private static SQLiteConnection OpenEncryptedDatabase(string databaseFile, string hexKey)
        {
            SQLiteConnection connection = new(databaseFile, SQLiteOpenFlags.ReadWrite);

            try
            {
                connection.ExecuteScalar<string>("pragma cipher = 'chacha20';");

                int result = raw.sqlite3_key(connection.Handle, Convert.FromHexString(hexKey));
                if (result != raw.SQLITE_OK)
                {
                    throw new InvalidOperationException($"sqlite3_key failed with result {result}.");
                }

                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        private static List<T> GetRows<T>(SQLiteConnection connection, Func<T, bool>? condition = null) where T : new()
        {
            List<T> rows;

            try
            {
                TableQuery<T> table = connection.Table<T>();

                if (condition is not null)
                    rows = [.. table.Where(condition)];
                else
                    rows = [.. table];

                connection.Close();
                connection.Dispose();
            }
            catch (SQLiteException)
            {
                // for global version with missing tables
                // we'll just have to wait and see what breaks with this

                rows = [];
            }

            return rows;
        }
    }
}
