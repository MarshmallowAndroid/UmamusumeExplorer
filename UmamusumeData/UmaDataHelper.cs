﻿using SQLite;
using UmamusumeData.DataDirectories;

namespace UmamusumeData
{
    public static class UmaDataHelper
    {
        private const string key = "9c2bab97bcf8c0c4f1a9ea7881a213f6c9ebf9d8d4c6a8e43ce5a259bde7e9fd";

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
                BinaryReader reader = new(File.OpenRead(MetaFile));
                isMetaFileEncrypted = reader.ReadUInt32() != 0x694C5153;
                reader.Dispose();
            }

            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3());
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
                return manifestEntries.Where(condition).ToList();
            else
                return manifestEntries;
        }

        public static List<T> GetMasterDatabaseRows<T>(Func<T, bool>? condition = null) where T : new()
            => GetRows(OpenDatabase(MasterFile), condition);

        private static SQLiteConnection OpenDatabase(string databaseFile, bool encrypted = false)
        {
            SQLiteConnection connection = new(databaseFile, SQLiteOpenFlags.ReadWrite);

            if (encrypted)
            {
                connection.ExecuteScalar<string>($"pragma hexkey = '{key}';");
            }

            return connection;
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
