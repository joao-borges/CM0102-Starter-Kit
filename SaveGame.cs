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
    /// - club.dat: 581-byte records; id at +0, long name (51 bytes) at +4,
    ///   short name (26 bytes) at +55, "Bank" int32 at +101 (feeds the board's
    ///   transfer-budget maths).
    /// - finance.dat: 359-byte records indexed by club id; the LIVE balance is an
    ///   int64 at +0 (the engine does 64-bit arithmetic and per-status clamps on it).
    ///   Values at/above ~2.1 billion overflow the 32-bit budget calculations
    ///   ("rich club, zero transfer funds"), hence SafeMoneyMax.
    /// </summary>
    class SaveGame {
        public const int SafeMoneyMax = 500000000;

        readonly string path;
        byte[] data;
        readonly Dictionary<string, Block> blocks = new Dictionary<string, Block>();
        readonly List<Club> clubs = new List<Club>();

        class Block {
            public int Pos;
            public int Size;
        }

        public class Club {
            public int Id;
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

        public void Load() {
            this.data = File.ReadAllBytes(this.path);
            if (this.data.Length < 12 || BitConverter.ToInt32(this.data, 0) != 3) {
                throw new InvalidDataException("Not an uncompressed CM 01/02 save. " +
                    "Untick \"Compress Save Game Files\" in the in-game options and save again.");
            }
            int count = BitConverter.ToInt32(this.data, 8);
            int offset = 12;
            for (int i = 0; i < count; i++, offset += 268) {
                int end = Array.IndexOf(this.data, (byte) 0, offset + 8, 260 - 8);
                string name = Encoding.ASCII.GetString(this.data, offset + 8, end - (offset + 8));
                this.blocks[name] = new Block {
                    Pos = BitConverter.ToInt32(this.data, offset),
                    Size = BitConverter.ToInt32(this.data, offset + 4)
                };
            }
            if (!this.blocks.ContainsKey("club.dat") || !this.blocks.ContainsKey("finance.dat")) {
                throw new InvalidDataException("Save is missing the club/finance tables.");
            }
            ReadClubs();
        }

        static string ReadString(byte[] data, int offset, int max) {
            int end = Array.IndexOf(data, (byte) 0, offset, max);
            if (end < 0) {
                end = offset + max;
            }
            // The game data is Windows-1252-ish; Latin-1 keeps all accents readable
            return Encoding.GetEncoding(1252).GetString(data, offset, end - offset);
        }

        void ReadClubs() {
            this.clubs.Clear();
            Block clubBlock = this.blocks["club.dat"], financeBlock = this.blocks["finance.dat"];
            int clubCount = clubBlock.Size / 581, financeCount = financeBlock.Size / 359;
            for (int record = 0; record < clubCount; record++) {
                int recordBase = clubBlock.Pos + record * 581;
                Club club = new Club {
                    Id = BitConverter.ToInt32(this.data, recordBase),
                    LongName = ReadString(this.data, recordBase + 4, 51),
                    ShortName = ReadString(this.data, recordBase + 55, 26),
                    Bank = BitConverter.ToInt32(this.data, recordBase + 101)
                };
                if (club.Id >= 0 && club.Id < financeCount) {
                    club.Balance = BitConverter.ToInt64(this.data, financeBlock.Pos + club.Id * 359);
                }
                this.clubs.Add(club);
            }
        }

        int ClubRecordBase(Club club) {
            // Club records are stored in id order in every save we have seen, but look
            // the id up defensively rather than assuming index == id
            Block clubBlock = this.blocks["club.dat"];
            int count = clubBlock.Size / 581;
            if (club.Id >= 0 && club.Id < count &&
                BitConverter.ToInt32(this.data, clubBlock.Pos + club.Id * 581) == club.Id) {
                return clubBlock.Pos + club.Id * 581;
            }
            for (int record = 0; record < count; record++) {
                if (BitConverter.ToInt32(this.data, clubBlock.Pos + record * 581) == club.Id) {
                    return clubBlock.Pos + record * 581;
                }
            }
            throw new InvalidDataException("Club record not found for id " + club.Id);
        }

        public void SetClubMoney(Club club, long balance, int bank) {
            if (balance < 0 || balance > SafeMoneyMax || bank < 0 || bank > SafeMoneyMax) {
                throw new ArgumentOutOfRangeException("balance",
                    "Money values must be between 0 and " + SafeMoneyMax.ToString("N0") +
                    " to stay inside the game's 32-bit budget arithmetic.");
            }
            Block financeBlock = this.blocks["finance.dat"];
            byte[] balanceBytes = BitConverter.GetBytes(balance);
            Array.Copy(balanceBytes, 0, this.data, financeBlock.Pos + club.Id * 359, 8);
            byte[] bankBytes = BitConverter.GetBytes(bank);
            Array.Copy(bankBytes, 0, this.data, ClubRecordBase(club) + 101, 4);
            club.Balance = balance;
            club.Bank = bank;
        }

        // ---------------- Players (layouts from CM0102Patcher Scouter/SaveReader) ----------------
        // staff.dat: 110-byte records. +0 id, +4/+8/+12 first/second/common name ids,
        // +16 DOB (day int16, year int16, leap int32), +26 nation, +30 second nation,
        // +34/+35 international caps/goals (bytes), +57 club id, +82 value,
        // +86..+93 mentals (bytes 0-20, alphabetical: Adaptability, Ambition,
        // Determination, Loyalty, Pressure, Professionalism, Sportsmanship,
        // Temperament), +97 player.dat id.
        // player.dat: 70-byte records. +0 id, +4 squad number, +5/+7 CA/PA,
        // +9/+11/+13 home/current/world reputation (int16), +15..+26 twelve position
        // ratings, +27..+68 forty-two playing attributes (sbyte), +69 morale (0-20).
        // Attribute bytes are CA-weighted intrinsics for a subset of attributes; the
        // in-game 1-20 value = f(intrinsic, CA) with goalkeeper-dependent branches.
        // injury.dat: a fitness table of staff-count 31-byte records indexed by staff
        // table index, then an event pool (not touched here). Fitness record: +8 int16
        // fitness (0-10000), +10 int16 condition (0-10000), +18 injury type byte
        // (0xff = healthy), +19 injury severity byte.
        // Preferences.dat: 52-byte records indexed by staff id (table is SHORTER than
        // staff.dat - always bounds-check): +0 id, then 12 int32s: fav clubs x3,
        // disliked clubs x3, fav staff x3, disliked staff x3 (-1 = empty).

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

        /// <summary>
        /// In-game style position string ("GK", "D/DM C", "AM/F RLC", ...) from the
        /// twelve position ratings at player record +15. Logic ported from Nick's
        /// CM0102Patcher Scouter (ShortPosition), with wingbacks added.
        /// Rating order: GK, SW, D, DM, M, AM, ATT, WB, Right, Left, Centre, FreeRole.
        /// </summary>
        string PositionString(int playerBase) {
            sbyte gk = ReadSByte(playerBase + 15), sw = ReadSByte(playerBase + 16),
                  d = ReadSByte(playerBase + 17), dm = ReadSByte(playerBase + 18),
                  m = ReadSByte(playerBase + 19), am = ReadSByte(playerBase + 20),
                  att = ReadSByte(playerBase + 21), wb = ReadSByte(playerBase + 22),
                  right = ReadSByte(playerBase + 23), left = ReadSByte(playerBase + 24),
                  centre = ReadSByte(playerBase + 25), free = ReadSByte(playerBase + 26);
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
            for (int record = 0; record < block.Size / 60; record++) {
                names.Add(ReadString(this.data, block.Pos + record * 60, 50));
            }
            return names;
        }

        static string NameAt(List<string> names, int id) {
            return id >= 0 && id < names.Count ? names[id] : "";
        }

        public void LoadPlayers() {
            if (this.playersLoaded) {
                return;
            }
            List<string> firstNames = ReadNames("first_names.dat");
            List<string> secondNames = ReadNames("second_names.dat");
            List<string> commonNames = ReadNames("common_names.dat");
            Block generalBlock = this.blocks["general.dat"];
            this.gameDay = BitConverter.ToInt16(this.data, generalBlock.Pos + 3944);
            this.gameYear = BitConverter.ToInt16(this.data, generalBlock.Pos + 3944 + 2);

            Dictionary<int, string> clubNames = new Dictionary<int, string>();
            foreach (Club club in this.clubs) {
                clubNames[club.Id] = club.LongName;
            }

            // nation.dat: 290-byte records, id at +0, name (50 chars) at +4
            Dictionary<int, string> nationNames = new Dictionary<int, string>();
            Block nationBlock = this.blocks["nation.dat"];
            for (int record = 0; record < nationBlock.Size / 290; record++) {
                int recordBase = nationBlock.Pos + record * 290;
                int nationId = BitConverter.ToInt32(this.data, recordBase);
                string nationName = ReadString(this.data, recordBase + 4, 50);
                nationNames[nationId] = nationName;
                this.nations.Add(new Nation { Id = nationId, Name = nationName });
            }
            this.nations.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

            // player.dat id -> record offset
            Block playerBlock = this.blocks["player.dat"];
            Dictionary<int, int> playerOffsets = new Dictionary<int, int>();
            for (int record = 0; record < playerBlock.Size / 70; record++) {
                int recordBase = playerBlock.Pos + record * 70;
                playerOffsets[BitConverter.ToInt32(this.data, recordBase)] = recordBase;
            }

            // contract.dat: preamble of two counts + 21-byte entries, then 80-byte records
            Dictionary<int, int> contractOffsets = new Dictionary<int, int>();
            Block contractBlock = this.blocks["contract.dat"];
            int preCount = BitConverter.ToInt32(this.data, contractBlock.Pos);
            int contractCount = BitConverter.ToInt32(this.data, contractBlock.Pos + 4);
            int cursor = contractBlock.Pos + 8 + preCount * 21;
            if (preCount > 0) {
                contractCount = BitConverter.ToInt32(this.data, cursor - 21 + 17);
            }
            for (int record = 0; record < contractCount; record++, cursor += 80) {
                int staffId = BitConverter.ToInt32(this.data, cursor);
                if (!contractOffsets.ContainsKey(staffId)) {
                    contractOffsets[staffId] = cursor;
                }
            }

            // injury.dat fitness table: staff-count 31-byte records in staff table order
            Block injuryBlock;
            this.blocks.TryGetValue("injury.dat", out injuryBlock);
            // Preferences.dat: 52-byte records indexed by staff id, may cover fewer
            // staff than staff.dat does
            Block prefsBlock;
            this.blocks.TryGetValue("Preferences.dat", out prefsBlock);
            int prefsCount = prefsBlock != null ? prefsBlock.Size / 52 : 0;

            Block staffBlock = this.blocks["staff.dat"];
            int staffCount = staffBlock.Size / 110;
            for (int record = 0; record < staffCount; record++) {
                int staffBase = staffBlock.Pos + record * 110;
                int staffId = BitConverter.ToInt32(this.data, staffBase);
                string commonName = NameAt(commonNames, BitConverter.ToInt32(this.data, staffBase + 12));
                string name = commonName.Length > 0 ? commonName
                    : (NameAt(firstNames, BitConverter.ToInt32(this.data, staffBase + 4)) + " " +
                       NameAt(secondNames, BitConverter.ToInt32(this.data, staffBase + 8))).Trim();
                int clubId = BitConverter.ToInt32(this.data, staffBase + 57);
                string clubName;
                if (!clubNames.TryGetValue(clubId, out clubName)) {
                    clubName = "-";
                }
                this.staffNamesById[staffId] = name;
                this.staffDirectory.Add(new StaffEntry { Id = staffId, Name = name, ClubName = clubName });

                int playerId = BitConverter.ToInt32(this.data, staffBase + 97);
                int playerBase;
                if (playerId < 0 || !playerOffsets.TryGetValue(playerId, out playerBase)) {
                    continue;
                }
                int nationId = BitConverter.ToInt32(this.data, staffBase + 26);
                string nationName;
                int birthDay = BitConverter.ToInt16(this.data, staffBase + 16);
                int birthYear = BitConverter.ToInt16(this.data, staffBase + 18);
                int contractBase;
                int fitnessBase = injuryBlock != null && (record + 1) * 31 <= injuryBlock.Size
                    ? injuryBlock.Pos + record * 31 : -1;
                int prefsBase = staffId >= 0 && staffId < prefsCount &&
                    BitConverter.ToInt32(this.data, prefsBlock.Pos + staffId * 52) == staffId
                    ? prefsBlock.Pos + staffId * 52 : -1;
                this.players.Add(new PlayerRef {
                    StaffId = staffId,
                    StaffBase = staffBase,
                    PlayerBase = playerBase,
                    ContractBase = contractOffsets.TryGetValue(staffId, out contractBase) ? contractBase : -1,
                    FitnessBase = fitnessBase,
                    PrefsBase = prefsBase,
                    Name = name,
                    ClubName = clubName,
                    Nation = nationNames.TryGetValue(nationId, out nationName) ? nationName : "-",
                    Position = PositionString(playerBase),
                    // birthday-aware, like the game shows it
                    Age = birthYear > 1800
                        ? this.gameYear - birthYear - (this.gameDay < birthDay ? 1 : 0) : 0
                });
            }
            this.playersLoaded = true;
        }

        public byte ReadByte(int offset) { return this.data[offset]; }
        public sbyte ReadSByte(int offset) { return (sbyte) this.data[offset]; }
        public short ReadInt16(int offset) { return BitConverter.ToInt16(this.data, offset); }
        public int ReadInt32(int offset) { return BitConverter.ToInt32(this.data, offset); }
        public void WriteByte(int offset, byte value) { this.data[offset] = value; }
        public void WriteSByte(int offset, sbyte value) { this.data[offset] = (byte) value; }
        public void WriteInt16(int offset, short value) {
            Array.Copy(BitConverter.GetBytes(value), 0, this.data, offset, 2);
        }
        public void WriteInt32(int offset, int value) {
            Array.Copy(BitConverter.GetBytes(value), 0, this.data, offset, 4);
        }

        /// <summary>Backs up the save beside itself, then writes the edited bytes.</summary>
        public string Save() {
            string backup = this.path + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
            File.Copy(this.path, backup);
            File.WriteAllBytes(this.path, this.data);
            return backup;
        }
    }
}
