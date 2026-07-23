using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CM0102_Starter_Kit {
    /// <summary>
    /// Parser/editor for UNCOMPRESSED CM 01/02 save games. Format knowledge from the
    /// GSLP reconstruction sessions (see CM0102Patcher docs/gslp-reconstruction):
    /// - header int32==3 (uncompressed; 4 = compressed, unsupported), block count at +8,
    ///   then 268-byte block-table entries: pos at +0, size at +4, name (ASCIIZ) at +8.
    /// - club.dat: 581-byte records; id at +0, long name (50 bytes) at +4, gender at
    ///   +55, short name (26 bytes) at +56, "Bank" int32 at +101 (feeds the board's
    ///   transfer-budget maths).
    /// - finance.dat: 359-byte records indexed by club id; the LIVE balance is an
    ///   int64 at +0 (the engine does 64-bit arithmetic and per-status clamps on it).
    ///   Values at/above ~2.1 billion overflow the 32-bit budget calculations
    ///   ("rich club, zero transfer funds"), hence SafeMoneyMax.
    /// The save is accessed through a FileStream, NOT loaded whole: saves are 250 MB+
    /// and a contiguous buffer that size exhausts the 32-bit address space under Wine.
    /// Blocks are read transiently for parsing; edits accumulate in a pending-write
    /// overlay and Save() patches only the changed bytes after taking a backup.
    /// </summary>
    class SaveGame : IDisposable {
        public const int SafeMoneyMax = 500000000;

        readonly string path;
        FileStream stream;
        readonly Dictionary<string, Block> blocks = new Dictionary<string, Block>();
        readonly List<Club> clubs = new List<Club>();
        // pending edits, byte by byte, keyed by absolute file offset
        readonly SortedDictionary<int, byte> pending = new SortedDictionary<int, byte>();

        class Block {
            public int Pos;
            public int Size;
        }

        public class Club {
            public int Id;
            public int RecordBase;      // file offset of the club.dat record
            public string LongName;
            public string ShortName;
            public long Balance;
            public int Bank;
        }

        public SaveGame(string path) {
            this.path = path;
        }

        public string FileName { get { return Path.GetFileName(this.path); } }
        public IList<Club> Clubs { get { return this.clubs; } }

        public void Dispose() {
            if (this.stream != null) {
                this.stream.Dispose();
                this.stream = null;
            }
        }

        // ---------------- raw access (pending-write overlay included) ----------------

        byte[] ReadRaw(int offset, int count) {
            byte[] buffer = new byte[count];
            this.stream.Seek(offset, SeekOrigin.Begin);
            int got = 0;
            while (got < count) {
                int n = this.stream.Read(buffer, got, count - got);
                if (n <= 0) {
                    throw new EndOfStreamException("Save truncated at offset " + (offset + got));
                }
                got += n;
            }
            if (this.pending.Count > 0) {
                for (int i = 0; i < count; i++) {
                    byte value;
                    if (this.pending.TryGetValue(offset + i, out value)) {
                        buffer[i] = value;
                    }
                }
            }
            return buffer;
        }

        void WriteRaw(int offset, byte[] bytes) {
            for (int i = 0; i < bytes.Length; i++) {
                this.pending[offset + i] = bytes[i];
            }
        }

        public byte ReadByte(int offset) { return ReadRaw(offset, 1)[0]; }
        public sbyte ReadSByte(int offset) { return (sbyte) ReadRaw(offset, 1)[0]; }
        public short ReadInt16(int offset) { return BitConverter.ToInt16(ReadRaw(offset, 2), 0); }
        public int ReadInt32(int offset) { return BitConverter.ToInt32(ReadRaw(offset, 4), 0); }
        public void WriteByte(int offset, byte value) { WriteRaw(offset, new byte[] { value }); }
        public void WriteSByte(int offset, sbyte value) { WriteRaw(offset, new byte[] { (byte) value }); }
        public void WriteInt16(int offset, short value) { WriteRaw(offset, BitConverter.GetBytes(value)); }
        public void WriteInt32(int offset, int value) { WriteRaw(offset, BitConverter.GetBytes(value)); }

        byte[] ReadBlock(Block block) {
            return ReadRaw(block.Pos, block.Size);
        }

        // ---------------- loading ----------------

        public void Load() {
            this.stream = new FileStream(this.path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] header = ReadRaw(0, 12);
            if (BitConverter.ToInt32(header, 0) != 3) {
                throw new InvalidDataException("Not an uncompressed CM 01/02 save. " +
                    "Untick \"Compress Save Game Files\" in the in-game options and save again.");
            }
            int count = BitConverter.ToInt32(header, 8);
            byte[] table = ReadRaw(12, count * 268);
            for (int i = 0; i < count; i++) {
                int entry = i * 268;
                int end = Array.IndexOf(table, (byte) 0, entry + 8, 260);
                string name = Encoding.ASCII.GetString(table, entry + 8, end - (entry + 8));
                this.blocks[name] = new Block {
                    Pos = BitConverter.ToInt32(table, entry),
                    Size = BitConverter.ToInt32(table, entry + 4)
                };
            }
            if (!this.blocks.ContainsKey("club.dat") || !this.blocks.ContainsKey("finance.dat")) {
                throw new InvalidDataException("Save is missing the club/finance tables.");
            }
            ReadClubs();
        }

        static string BufferString(byte[] buffer, int offset, int max) {
            int end = Array.IndexOf(buffer, (byte) 0, offset, max);
            if (end < 0) {
                end = offset + max;
            }
            // The game data is Windows-1252-ish; Latin-1 keeps all accents readable
            return Encoding.GetEncoding(1252).GetString(buffer, offset, end - offset);
        }

        void ReadClubs() {
            this.clubs.Clear();
            Block clubBlock = this.blocks["club.dat"], financeBlock = this.blocks["finance.dat"];
            byte[] clubBuffer = ReadBlock(clubBlock);
            byte[] financeBuffer = ReadBlock(financeBlock);
            int clubCount = clubBlock.Size / 581, financeCount = financeBlock.Size / 359;
            for (int record = 0; record < clubCount; record++) {
                int recordBase = record * 581;
                Club club = new Club {
                    Id = BitConverter.ToInt32(clubBuffer, recordBase),
                    RecordBase = clubBlock.Pos + recordBase,
                    LongName = BufferString(clubBuffer, recordBase + 4, 50),
                    ShortName = BufferString(clubBuffer, recordBase + 56, 26),
                    Bank = BitConverter.ToInt32(clubBuffer, recordBase + 101)
                };
                if (club.Id >= 0 && club.Id < financeCount) {
                    club.Balance = BitConverter.ToInt64(financeBuffer, club.Id * 359);
                }
                this.clubs.Add(club);
            }
        }

        public void SetClubMoney(Club club, long balance, int bank) {
            if (balance < 0 || balance > SafeMoneyMax || bank < 0 || bank > SafeMoneyMax) {
                throw new ArgumentOutOfRangeException("balance",
                    "Money values must be between 0 and " + SafeMoneyMax.ToString("N0") +
                    " to stay inside the game's 32-bit budget arithmetic.");
            }
            Block financeBlock = this.blocks["finance.dat"];
            WriteRaw(financeBlock.Pos + club.Id * 359, BitConverter.GetBytes(balance));
            WriteRaw(club.RecordBase + 101, BitConverter.GetBytes(bank));
            club.Balance = balance;
            club.Bank = bank;
        }

        // ---------------- Players (layouts from CM0102Patcher Scouter/SaveReader) ----------------
        // staff.dat: 110-byte records. +0 id, +4/+8/+12 first/second/common name ids,
        // +16 DOB (0-BASED day-of-year int16, year int16, leap int32), +26 nation,
        // +30 second nation, +34/+35 international caps/goals (bytes), +57 club id,
        // +82 value, +86..+93 mentals (bytes 0-20, alphabetical), +97 player.dat id.
        // player.dat: 70-byte records. +0 id, +4 squad number, +5/+7 CA/PA,
        // +9/+11/+13 home/current/world reputation (int16), +15..+26 twelve position
        // ratings, +27..+68 forty-two playing attributes (sbyte), +69 morale (0-20).
        // injury.dat: fitness table of staff-count 31-byte records by staff index
        // (+8 fitness, +10 condition 0-10000, +18 injury type 0xff = healthy,
        // +19 severity), then an event pool (not touched here).
        // Preferences.dat: 52-byte records indexed by staff id (table can be SHORTER
        // than staff.dat): +0 id, then 12 int32s: fav clubs x3, disliked clubs x3,
        // fav staff x3, disliked staff x3 (-1 = empty).

        public class PlayerRef {
            public int StaffId;
            public int StaffBase;       // file offset of staff record
            public int PlayerBase;      // file offset of player record
            public int ContractBase;    // file offset of contract record (-1 if none)
            public int FitnessBase;     // file offset of injury.dat fitness record (-1 if none)
            public int PrefsBase;       // file offset of Preferences.dat record (-1 if none)
            public string Name;
            public string ClubName;
            public string Nation;
            public string Position;
            public int Age;
        }

        public class Nation {
            public int Id;
            public string Name;
        }

        public class StaffEntry {
            public int Id;
            public string Name;
            public string ClubName;
        }

        readonly List<PlayerRef> players = new List<PlayerRef>();
        readonly List<Nation> nations = new List<Nation>();
        readonly List<StaffEntry> staffDirectory = new List<StaffEntry>();
        readonly Dictionary<int, string> staffNamesById = new Dictionary<int, string>();
        bool playersLoaded;
        int gameYear;
        int gameDay;

        public IList<PlayerRef> Players { get { return this.players; } }
        public IList<Nation> Nations { get { return this.nations; } }
        public IList<StaffEntry> StaffDirectory { get { return this.staffDirectory; } }
        public int GameYear { get { return this.gameYear; } }
        public int GameDay { get { return this.gameDay; } }

        public string StaffNameById(int staffId) {
            string name;
            return this.staffNamesById.TryGetValue(staffId, out name) ? name : "";
        }

        List<string> ReadNames(string blockName) {
            List<string> names = new List<string>();
            Block block = this.blocks[blockName];
            byte[] buffer = ReadBlock(block);
            for (int record = 0; record < block.Size / 60; record++) {
                names.Add(BufferString(buffer, record * 60, 50));
            }
            return names;
        }

        static string NameAt(List<string> names, int id) {
            return id >= 0 && id < names.Count ? names[id] : "";
        }

        /// <summary>
        /// In-game style position string ("GK", "D C", "D/WB/DM R", ...) from the
        /// twelve position ratings at player record +15. Logic ported from Nick's
        /// CM0102Patcher Scouter (ShortPosition), with wingbacks added.
        /// Rating order: GK, SW, D, DM, M, AM, ATT, WB, Right, Left, Centre, FreeRole.
        /// </summary>
        static string PositionString(byte[] playerBuffer, int recordBase) {
            sbyte gk = (sbyte) playerBuffer[recordBase + 15], sw = (sbyte) playerBuffer[recordBase + 16],
                  d = (sbyte) playerBuffer[recordBase + 17], dm = (sbyte) playerBuffer[recordBase + 18],
                  m = (sbyte) playerBuffer[recordBase + 19], am = (sbyte) playerBuffer[recordBase + 20],
                  att = (sbyte) playerBuffer[recordBase + 21], wb = (sbyte) playerBuffer[recordBase + 22],
                  right = (sbyte) playerBuffer[recordBase + 23], left = (sbyte) playerBuffer[recordBase + 24],
                  centre = (sbyte) playerBuffer[recordBase + 25], free = (sbyte) playerBuffer[recordBase + 26];
            List<string> parts = new List<string>();
            if (gk > 14) parts.Add("GK");
            if (sw > 14) parts.Add("SW");
            if (d > 14) parts.Add("D");
            if (wb > 14) parts.Add("WB");
            if (dm > 14) parts.Add("DM");
            if (m > 14 && dm <= 14 && am <= 14) parts.Add("M");
            if (am > 14 && dm <= 14 && (att <= 14 || m > 14)) parts.Add("AM");
            if (att > 14) {
                parts.Add(am > 14 || left > 14 || right > 14 || free > 14 ? "F" : "S");
            }
            string result = string.Join("/", parts.ToArray());
            if (gk <= 14) {
                string sides = "";
                if (right > 14) sides += "R";
                if (left > 14) sides += "L";
                if (centre > 14) sides += "C";
                if (sides.Length > 0) {
                    result = (result + " " + sides).Trim();
                }
            }
            return result;
        }

        public void LoadPlayers() {
            if (this.playersLoaded) {
                return;
            }
            List<string> firstNames = ReadNames("first_names.dat");
            List<string> secondNames = ReadNames("second_names.dat");
            List<string> commonNames = ReadNames("common_names.dat");
            Block generalBlock = this.blocks["general.dat"];
            byte[] date = ReadRaw(generalBlock.Pos + 3944, 4);
            this.gameDay = BitConverter.ToInt16(date, 0);
            this.gameYear = BitConverter.ToInt16(date, 2);

            Dictionary<int, string> clubNames = new Dictionary<int, string>();
            foreach (Club club in this.clubs) {
                clubNames[club.Id] = club.LongName;
            }

            // nation.dat: 290-byte records, id at +0, name (50 chars) at +4
            Dictionary<int, string> nationNames = new Dictionary<int, string>();
            Block nationBlock = this.blocks["nation.dat"];
            byte[] nationBuffer = ReadBlock(nationBlock);
            for (int record = 0; record < nationBlock.Size / 290; record++) {
                int recordBase = record * 290;
                int nationId = BitConverter.ToInt32(nationBuffer, recordBase);
                string nationName = BufferString(nationBuffer, recordBase + 4, 50);
                nationNames[nationId] = nationName;
                this.nations.Add(new Nation { Id = nationId, Name = nationName });
            }
            this.nations.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

            // player.dat id -> record index; the buffer is kept for attribute parsing
            Block playerBlock = this.blocks["player.dat"];
            byte[] playerBuffer = ReadBlock(playerBlock);
            Dictionary<int, int> playerRecordBases = new Dictionary<int, int>();
            for (int record = 0; record < playerBlock.Size / 70; record++) {
                int recordBase = record * 70;
                playerRecordBases[BitConverter.ToInt32(playerBuffer, recordBase)] = recordBase;
            }

            // contract.dat: preamble of two counts + 21-byte entries, then 80-byte records
            Dictionary<int, int> contractOffsets = new Dictionary<int, int>();
            Block contractBlock = this.blocks["contract.dat"];
            byte[] contractBuffer = ReadBlock(contractBlock);
            int preCount = BitConverter.ToInt32(contractBuffer, 0);
            int contractCount = BitConverter.ToInt32(contractBuffer, 4);
            int cursor = 8 + preCount * 21;
            if (preCount > 0) {
                contractCount = BitConverter.ToInt32(contractBuffer, cursor - 21 + 17);
            }
            for (int record = 0; record < contractCount; record++, cursor += 80) {
                int staffId = BitConverter.ToInt32(contractBuffer, cursor);
                if (!contractOffsets.ContainsKey(staffId)) {
                    contractOffsets[staffId] = contractBlock.Pos + cursor;
                }
            }
            contractBuffer = null;

            // injury.dat fitness table: staff-count 31-byte records in staff table order
            Block injuryBlock;
            this.blocks.TryGetValue("injury.dat", out injuryBlock);
            // Preferences.dat: 52-byte records indexed by staff id, may cover fewer
            // staff than staff.dat does
            Block prefsBlock;
            this.blocks.TryGetValue("Preferences.dat", out prefsBlock);
            byte[] prefsBuffer = prefsBlock != null ? ReadBlock(prefsBlock) : null;
            int prefsCount = prefsBlock != null ? prefsBlock.Size / 52 : 0;

            Block staffBlock = this.blocks["staff.dat"];
            byte[] staffBuffer = ReadBlock(staffBlock);
            int staffCount = staffBlock.Size / 110;
            for (int record = 0; record < staffCount; record++) {
                int staffBase = record * 110;
                int staffId = BitConverter.ToInt32(staffBuffer, staffBase);
                string commonName = NameAt(commonNames, BitConverter.ToInt32(staffBuffer, staffBase + 12));
                string name = commonName.Length > 0 ? commonName
                    : (NameAt(firstNames, BitConverter.ToInt32(staffBuffer, staffBase + 4)) + " " +
                       NameAt(secondNames, BitConverter.ToInt32(staffBuffer, staffBase + 8))).Trim();
                int clubId = BitConverter.ToInt32(staffBuffer, staffBase + 57);
                string clubName;
                if (!clubNames.TryGetValue(clubId, out clubName)) {
                    clubName = "-";
                }
                this.staffNamesById[staffId] = name;
                this.staffDirectory.Add(new StaffEntry { Id = staffId, Name = name, ClubName = clubName });

                int playerId = BitConverter.ToInt32(staffBuffer, staffBase + 97);
                int playerRecordBase;
                if (playerId < 0 || !playerRecordBases.TryGetValue(playerId, out playerRecordBase)) {
                    continue;
                }
                int nationId = BitConverter.ToInt32(staffBuffer, staffBase + 26);
                string nationName;
                int birthDay = BitConverter.ToInt16(staffBuffer, staffBase + 16);
                int birthYear = BitConverter.ToInt16(staffBuffer, staffBase + 18);
                int contractBase;
                int fitnessBase = injuryBlock != null && (record + 1) * 31 <= injuryBlock.Size
                    ? injuryBlock.Pos + record * 31 : -1;
                int prefsBase = prefsBuffer != null && staffId >= 0 && staffId < prefsCount &&
                    BitConverter.ToInt32(prefsBuffer, staffId * 52) == staffId
                    ? prefsBlock.Pos + staffId * 52 : -1;
                this.players.Add(new PlayerRef {
                    StaffId = staffId,
                    StaffBase = staffBlock.Pos + staffBase,
                    PlayerBase = playerBlock.Pos + playerRecordBase,
                    ContractBase = contractOffsets.TryGetValue(staffId, out contractBase) ? contractBase : -1,
                    FitnessBase = fitnessBase,
                    PrefsBase = prefsBase,
                    Name = name,
                    ClubName = clubName,
                    Nation = nationNames.TryGetValue(nationId, out nationName) ? nationName : "-",
                    Position = PositionString(playerBuffer, playerRecordBase),
                    // birthday-aware, like the game shows it
                    Age = birthYear > 1800
                        ? this.gameYear - birthYear - (this.gameDay < birthDay ? 1 : 0) : 0
                });
            }
            this.playersLoaded = true;
        }

        // ---------------- saving ----------------

        /// <summary>Backs up the save beside itself, then patches ONLY the edited
        /// bytes into the file (grouped into consecutive runs).</summary>
        public string Save() {
            string backup = this.path + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
            File.Copy(this.path, backup);
            using (FileStream writer = new FileStream(this.path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) {
                int runStart = -1;
                List<byte> run = new List<byte>();
                foreach (KeyValuePair<int, byte> edit in this.pending) {
                    if (runStart >= 0 && edit.Key == runStart + run.Count) {
                        run.Add(edit.Value);
                        continue;
                    }
                    if (runStart >= 0) {
                        writer.Seek(runStart, SeekOrigin.Begin);
                        writer.Write(run.ToArray(), 0, run.Count);
                    }
                    runStart = edit.Key;
                    run.Clear();
                    run.Add(edit.Value);
                }
                if (runStart >= 0) {
                    writer.Seek(runStart, SeekOrigin.Begin);
                    writer.Write(run.ToArray(), 0, run.Count);
                }
            }
            this.pending.Clear();
            return backup;
        }
    }
}
