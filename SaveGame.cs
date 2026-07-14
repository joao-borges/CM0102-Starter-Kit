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

        /// <summary>Backs up the save beside itself, then writes the edited bytes.</summary>
        public string Save() {
            string backup = this.path + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
            File.Copy(this.path, backup);
            File.WriteAllBytes(this.path, this.data);
            return backup;
        }
    }
}
