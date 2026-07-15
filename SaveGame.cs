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
        // +57 club id, +82 value, +97 player.dat id.
        // player.dat: 70-byte records. +0 id, +4 squad number, +5/+7 CA/PA,
        // +9/+11/+13 home/current/world reputation (int16), +15..+26 twelve position
        // ratings, +27..+68 forty-two playing attributes (sbyte), +69 morale.
        // Attribute bytes are CA-weighted intrinsics for a subset of attributes; the
        // in-game 1-20 value = f(intrinsic, CA) with goalkeeper-dependent branches.

        public class PlayerRef {
            public int StaffBase;       // file offset of staff record
            public int PlayerBase;      // file offset of player record
            public int ContractBase;    // file offset of contract record (-1 if none)
            public string Name;
            public string ClubName;
            public int Age;
        }

        readonly List<PlayerRef> players = new List<PlayerRef>();
        bool playersLoaded;
        int gameYear;

        public IList<PlayerRef> Players { get { return this.players; } }

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
            this.gameYear = BitConverter.ToInt16(this.data, generalBlock.Pos + 3944 + 2);

            Dictionary<int, string> clubNames = new Dictionary<int, string>();
            foreach (Club club in this.clubs) {
                clubNames[club.Id] = club.LongName;
            }

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

            Block staffBlock = this.blocks["staff.dat"];
            for (int record = 0; record < staffBlock.Size / 110; record++) {
                int staffBase = staffBlock.Pos + record * 110;
                int playerId = BitConverter.ToInt32(this.data, staffBase + 97);
                int playerBase;
                if (playerId < 0 || !playerOffsets.TryGetValue(playerId, out playerBase)) {
                    continue;
                }
                int staffId = BitConverter.ToInt32(this.data, staffBase);
                string commonName = NameAt(commonNames, BitConverter.ToInt32(this.data, staffBase + 12));
                string name = commonName.Length > 0 ? commonName
                    : (NameAt(firstNames, BitConverter.ToInt32(this.data, staffBase + 4)) + " " +
                       NameAt(secondNames, BitConverter.ToInt32(this.data, staffBase + 8))).Trim();
                int clubId = BitConverter.ToInt32(this.data, staffBase + 57);
                string clubName;
                int birthYear = BitConverter.ToInt16(this.data, staffBase + 18);
                int contractBase;
                this.players.Add(new PlayerRef {
                    StaffBase = staffBase,
                    PlayerBase = playerBase,
                    ContractBase = contractOffsets.TryGetValue(staffId, out contractBase) ? contractBase : -1,
                    Name = name,
                    ClubName = clubNames.TryGetValue(clubId, out clubName) ? clubName : "-",
                    Age = birthYear > 1800 ? this.gameYear - birthYear : 0
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
