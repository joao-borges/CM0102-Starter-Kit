using CM0102_Starter_Kit.Properties;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CM0102_Starter_Kit {
    class Helper {
        internal static readonly string GameFolder = Path.Combine(Directory.GetCurrentDirectory(), "Game");
        internal static readonly string DataFolderName = "Data";
        internal static readonly string DataFolder = Path.Combine(GameFolder, DataFolderName);
        internal static readonly string FontsFolder = Path.Combine(DataFolder, "Fonts");
        internal static readonly string CmLoaderConfigFilename = "CM0102LoaderDefault.ini";
        internal static readonly string CmLoaderCustomConfigFilename = "CM0102LoaderCustom.ini";
        internal static readonly string Cm0102ExeFilename = "cm0102.exe";
        internal static readonly string CmLoaderExeFilename = "CM0102Loader.exe";
        internal static readonly string PlayerSetupFilename = "player_setup.cfg";
        internal static readonly string BackupSavesFolder = Path.Combine(Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory), "CM0102 Backups");
        internal static readonly string CustomDatabasesFolder = Path.Combine(GameFolder, "Custom Databases");
        internal static readonly string PatchesFolderName = "Patches";
        internal static readonly string PatchesFolder = Path.Combine(GameFolder, PatchesFolderName);
        internal static readonly string OptionalPatchesFolder = Path.Combine(PatchesFolder, "Optional");
        internal static readonly string PointsDeductionPatch = "PointsDeductions.patch";
        internal static readonly string SwitchUpdateMessage = "Please use the Data Updates menu to load up a database first!";
        internal static readonly CultureInfo CultureInfo = new CultureInfo("en-GB");

        internal static bool DataFolderExists() {
            return Directory.Exists(DataFolder);
        }

        internal class ConfigLine {
            internal ConfigLine(int lineNumber, string name, string value) {
                this.LineNumber = lineNumber;
                this.Name = name;
                this.Value = value;
            }

            internal int LineNumber { get; }
            internal string Name { get; }
            internal string Value { get; }
        }











        // Every option here was verified byte-by-byte against the loader source
        // (nckstwrt/CM0102Loader) on the GSLP exes. TRUE = the loader's patch sites are
        // stock (or already carry Nick's bytes, making the write idempotent) so it applies
        // cleanly: uncap, work permits, 1280x800 (spacemaker prelude pre-applied), hidden
        // bids. FALSE + locked = either pre-baked and working as-is (coloured attributes -
        // GS ships Nick's patch with his own palette, user-confirmed rendering) or GS baked
        // his OWN variant at those sites (regen fixes, load-all-players) - the loader
        // blindly overwriting his code corrupts the exe (the original player-screen crash).
        // 9 subs is pre-baked; foreign limits ships as a patch file. HiddenAttributes
        // (extra attribute columns) is BAKED into the GSLP exes at file offsets
        // (CM0102Patcher tools/gslp_hiddencols.py): the loader .patch variant must NEVER
        // be applied on top - the loader writes the cave at base+RVA (0xADC000 = live GS
        // runtime BSS -> save-load/squad crash, user-confirmed A/B 2026-07-08) instead of
        // the VA 0xDE7000 the hook expects.
        private static Dictionary<int, ConfigLine> GslpConfigLines(string year) {
            return new Dictionary<int, ConfigLine> {
                { 1, new ConfigLine(1, "Year", year) },
                { 2, new ConfigLine(2, "SpeedMultiplier", "4") },
                { 3, new ConfigLine(3, "CurrencyMultiplier", "1.00") },
                { 4, new ConfigLine(4, "ColouredAttributes", "false") },
                { 5, new ConfigLine(5, "DisableUnprotectedContracts", "false") },
                { 6, new ConfigLine(6, "HideNonPublicBids", "true") },
                { 7, new ConfigLine(7, "IncreaseToSevenSubs", "false") },
                { 8, new ConfigLine(8, "RegenFixes", "false") },
                { 9, new ConfigLine(9, "ForceLoadAllPlayers", "false") },
                { 10, new ConfigLine(10, "AddTapaniRegenCode", "false") },
                { 11, new ConfigLine(11, "UnCap20s", "true") },
                { 12, new ConfigLine(12, "RemoveForeignPlayerLimit", "false") },
                { 13, new ConfigLine(13, "NoWorkPermits", "true") },
                { 14, new ConfigLine(14, "ChangeTo1280x800", "true") },
                { 16, new ConfigLine(16, "PatchFileDirectory", PatchesFolderName) }
            };
        }

        internal static readonly List<string> GslpPatchFiles = new List<string> {
            "NoForeignRestrictionsForAll.patch",
            // Null-club/all-leagues protections. GS pre-baked 2 of the 5 fixes in the
            // GSLP exes; the other 3 sites are byte-verified stock, so the loader patch
            // applies cleanly (re-writing the baked sites with identical bytes).
            "ProtectionPatches.patch",
            // The Database.cpp 17353 null-club check returns safely but spams a dialog
            // on all-leagues saves; this silences just the popup (site == stock bytes).
            "SilenceDatabase17353.patch",
            // usa_mls.cpp 1819: the engine hardcodes 12 stock MLS clubs; modern-MLS
            // clubs asserted nonstop. Fallback assigns them club-id % 3 conferences.
            "MlsConferenceFallback.patch"
        };



        internal class Database {
            internal Database(string name, string label, byte[] dataFile, bool deleteDataFolder, byte[] exeFile) {
                this.Name = name;
                this.Label = label;
                this.DataFile = dataFile;
                this.DeleteDataFolder = deleteDataFolder;
                this.ConfigLines = new Dictionary<int, ConfigLine>();
                this.ExeFile = exeFile;
            }

            internal Database(string name, string label, byte[] dataFile, bool deleteDataFolder, byte[] exeFile, Database prerequisiteDatabase) : this(name, label, dataFile, deleteDataFolder, exeFile) {
                this.PrerequisiteDatabase = prerequisiteDatabase;
            }

            internal Database(string name, string label, byte[] dataFile, bool deleteDataFolder, byte[] exeFile, Dictionary<int, ConfigLine> configLines) : this(name, label, dataFile, deleteDataFolder, exeFile) {
                this.ConfigLines = configLines;
            }

            internal Database(string name, string label, byte[] dataFile, bool deleteDataFolder, byte[] exeFile, Database prerequisiteDatabase, Dictionary<int, ConfigLine> configLines) : this(name, label, dataFile, deleteDataFolder, exeFile, prerequisiteDatabase) {
                this.ConfigLines = configLines;
            }

            internal string Name { get; }
            internal string Label { get; }
            internal byte[] DataFile { get; set; }
            internal bool DeleteDataFolder { get; }
            internal Database PrerequisiteDatabase { get; }
            internal Dictionary<int, ConfigLine> ConfigLines { get; }
            internal byte[] ExeFile { get; }
        }

        internal static readonly Database PatchedDatabase = new Database("patched_database", "Patched (3.9.68)", Resources.patched_data, true, Resources.cm0102_exe);
        internal static readonly Database CustomDatabase = new Database("custom_database", "Custom Database", null, false, Resources.cm0102_exe, PatchedDatabase);
        // GSLP (GS Leagues Patch) x May-2026 transplant datasets. Both share one data zip
        // (Resources.gslp_data, no marker inside - SetupDatabase writes the detector file);
        // the exes are pre-patched (re-year + WC-2026 field on the 2025 one + fixes), so the
        // Year is locked. 25/26 = Euro season 2025/26, Brazilian season 2026, WC-2026 at the
        // end of the first season. 26/27 = Euro season 2026/27, Brazilian season 2027.
        internal static readonly Database Gslp2526Database = new Database("gslp_2526_database", "25/26 (2026)", Resources.gslp_data, true, Resources.cm0102_gslp2025_exe, GslpConfigLines("2025"));
        internal static readonly Database Gslp2627Database = new Database("gslp_2627_database", "26/27 (2027)", Resources.gslp_data, true, Resources.cm0102_gslp2026_exe, GslpConfigLines("2026"));

        internal static readonly List<Database> Databases = new List<Database> {
            PatchedDatabase, Gslp2526Database, Gslp2627Database
        };

        internal static bool IsGslpDatabase(Database database) {
            return Gslp2526Database.Equals(database) || Gslp2627Database.Equals(database);
        }

        internal static Database CurrentDatabase() {
            foreach (Database database in Databases) {
                if (File.Exists(Path.Combine(DataFolder, database.Name + ".txt"))) {
                    return database;
                }
            }
            // Default case if any other database is loaded
            return CustomDatabase;
        }

        internal static void WriteToFile(List<string> lines, string file) {
            using (StreamWriter writer = new StreamWriter(file)) {
                for (int currentLine = 1; currentLine <= lines.Count; ++currentLine) {
                    writer.WriteLine(lines[currentLine - 1]);
                }
            }
        }

        internal static List<string> GetDefaultConfigFileLines(string configFile, Database database) {
            string[] existingLines = File.ReadAllLines(configFile);
            List<string> newLines = new List<string>();

            for (int currentLine = 1; currentLine <= existingLines.Length; ++currentLine) {
                if (database.ConfigLines.TryGetValue(currentLine, out ConfigLine configLine)) {
                    // Year is a special case - set it to 0 in the file if there is a custom value set for it
                    if (currentLine == 1) {
                        newLines.Add("Year = 0");
                    } else {
                        newLines.Add(configLine.Name + " = " + configLine.Value);
                    }
                } else {
                    newLines.Add(existingLines[currentLine - 1]);
                }
            }
            return newLines;
        }
    }
}
