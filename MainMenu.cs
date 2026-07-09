using CM0102_Starter_Kit.Properties;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using static CM0102_Starter_Kit.Helper;

namespace CM0102_Starter_Kit {
    partial class MainMenu
    #if DEBUG
        : MiddleForm
    #else
        : HidableForm
    #endif
    {
        internal readonly VersionMenu versionMenu;

        public MainMenu() {
            this.versionMenu = new VersionMenu(this);
            InitialiseSharedControls("Setup Game", 373, false);
            InitializeComponent();
        }
 
        protected override List<Button> GetButtons() {
            return new List<Button> {
                this.switch_update,
                this.install_var,
                this.editor,
                this.play_game,
                this.cm_scout,
                this.player_finder,
                this.backup_saves,
                this.restore_saves,
                this.cm_explorer
            };
        }

        protected override void RefreshForm() {
            if (File.Exists(ExistingCommentaryBackup)) {
                this.install_var.Text = "Uninstall VAR Commentary";
            } else {
                this.install_var.Text = "Install VAR Commentary";
            }
            // Always show which database the Play button will launch - saves are only
            // compatible with the database/exe they were created with
            this.play_game.Text = DataFolderExists() ? "Play " + CurrentDatabase().Label : "Play Game";
        }

        private void RefreshExeFile(ProgressWindow progressWindow) {
            progressWindow.SetProgressPercentage(40);
            File.WriteAllBytes(Path.Combine(GameFolder, Cm0102ExeFilename), Resources.cm0102_exe);
            progressWindow.SetProgressPercentage(80);
        }

        private void RunExternalProcess(string workingDirectory, string executableFile) {
            ProcessStartInfo playPsi = new ProcessStartInfo {
                WorkingDirectory = workingDirectory,
                FileName = executableFile,
                UseShellExecute = false
            };
            Process playProcess = Process.Start(playPsi);
            playProcess.Close();
        }

        private void SwitchUpdate_Click(object sender, EventArgs e) {
            ShowNewScreen(versionMenu);
        }

        private void InstallVar_Click(object sender, EventArgs e) {
            string result = SwitchUpdateMessage;
            if (DataFolderExists()) {
                if (File.Exists(ExistingCommentaryBackup)) {
                    File.Delete(ExistingCommentary);
                    File.Move(ExistingCommentaryBackup, ExistingCommentary);
                    result = "VAR Commentary successfully uninstalled!";
                } else {
                    File.Move(ExistingCommentary, ExistingCommentaryBackup);
                    File.WriteAllBytes(ExistingCommentary, Resources.events_eng);
                    result = "VAR Commentary successfully installed! Please note this only applies when playing the game in English!";
                }
                RefreshForm();
            }
            DisplayMessage(result);
        }

        private void Editor_Click(object sender, EventArgs e) {
            if (DataFolderExists()) {
                RunExternalProcess(DataFolder, Path.Combine(GameFolder, "Editor", "cm0102ed.exe"));
            } else {
                DisplayMessage(SwitchUpdateMessage);
            }
        }

        // Launches the game directly via the loader with the default config - the loader
        // options are managed per-database by SetupDatabase, so there is no play submenu.
        private void PlayGame_Click(object sender, EventArgs e) {
            if (!DataFolderExists()) {
                DisplayMessage(SwitchUpdateMessage);
                return;
            }
            ShowLoader();

            // Remove any temporary files that weren't removed since the last session
            foreach (FileInfo tmpFile in new DirectoryInfo(GameFolder).GetFiles("*.tmp")) {
                File.Delete(tmpFile.FullName);
            }
            foreach (FileInfo lngFile in new DirectoryInfo(GameFolder).GetFiles("*.lng")) {
                File.Delete(lngFile.FullName);
            }

            File.WriteAllBytes(Path.Combine(GameFolder, Cm0102ExeFilename), CurrentDatabase().ExeFile);
            ProcessStartInfo playPsi = new ProcessStartInfo {
                WorkingDirectory = GameFolder,
                FileName = Path.Combine(GameFolder, CmLoaderExeFilename),
                UseShellExecute = false,
                Arguments = CmLoaderConfigFilename
            };
            Process playProcess = Process.Start(playPsi);
            playProcess.WaitForExit();
            playProcess.Close();

            // The loader is a stub process for the game, so let's wait for the game to be closed
            foreach (Process process in Process.GetProcessesByName("cm0102")) {
                process.WaitForExit();
                process.Close();
            }
            File.WriteAllBytes(Path.Combine(GameFolder, Cm0102ExeFilename), Resources.cm0102_exe);
            HideLoader();
            RefreshForm();
        }

        private void BackupSaves_Click(object sender, EventArgs e) {
            string result = "No save games found!";
            FileInfo[] saveGames = new DirectoryInfo(GameFolder).GetFiles("*.sav");

            if (saveGames.Length > 0) {
                ProgressWindow progressWindow = CreateNewProgressWindow("Backing up save games", 85);
                int progressPerc = 0;
                progressWindow.SetProgressPercentage(progressPerc);

                if (!Directory.Exists(BackupSavesFolder)) {
                    Directory.CreateDirectory(BackupSavesFolder);
                }
                foreach (FileInfo save in saveGames) {
                    File.Copy(save.FullName, Path.Combine(BackupSavesFolder, save.Name), true);
                    progressPerc += 5;
                    progressWindow.SetProgressPercentage(Math.Min(progressPerc, 100));
                }
                result = saveGames.Length + @" save game(s) successfully backed up to your desktop!";
                progressWindow.SetProgressPercentage(100);
                progressWindow.Close();
            }
            DisplayMessage(result);
        }

        private void RestoreSaves_Click(object sender, EventArgs e) {
            if (Directory.Exists(BackupSavesFolder)) {
                this.restoreSaveDialog.InitialDirectory = BackupSavesFolder;
                this.restoreSaveDialog.ShowDialog();
            } else {
                DisplayMessage("No backed up save games found!");
            }
        }

        private void RestoreSavesDialog_FileOk(object sender, System.ComponentModel.CancelEventArgs e) {
            string destFile = Path.Combine(GameFolder, Path.GetFileName(this.restoreSaveDialog.FileName));
            File.Copy(this.restoreSaveDialog.FileName, destFile, true);
            DisplayMessage("Save game successfully restored!");
        }

        private void CmScout_Click(object sender, EventArgs e) {
            RunExternalProcess(GameFolder, Path.Combine(GameFolder, "cmscout.exe"));
        }

        private void PlayerFinder_Click(object sender, EventArgs e) {
            RunExternalProcess(GameFolder, Path.Combine(GameFolder, "gpf2.exe"));
        }

        private void CmExplorer_Click(object sender, EventArgs e) {
            string cmExplorerFolder = Path.Combine(GameFolder, "CMExplorer");
            string cmExplorerExe = Path.Combine(cmExplorerFolder, "CMExplorer.exe");
            // CM Explorer is embedded as a zip and extracted on first use
            if (!File.Exists(cmExplorerExe)) {
                string cmExplorerZipFile = cmExplorerFolder + ".zip";
                File.WriteAllBytes(cmExplorerZipFile, Resources.cmexplorer);
                new FastZip().ExtractZip(cmExplorerZipFile, cmExplorerFolder, null);
                File.Delete(cmExplorerZipFile);
                DisplayMessage("CM Explorer can only open UNCOMPRESSED save games. In the game, untick \"Compress Save Game Files\" in the Options screen before saving.");
            }
            // Working directory is the Game folder, where the .sav files live
            RunExternalProcess(GameFolder, cmExplorerExe);
        }

        private void MainMenu_Load(object sender, EventArgs e) {
            Process[] mainProcesses = Process.GetProcessesByName("CM0102StarterKit");
            if (mainProcesses.Length > 1) { 
                DisplayMessage("The Starter Kit is already running! Exiting...");
                Application.Exit();
            }
            ProgressWindow progressWindow = CreateNewProgressWindow("Loading Starter Kit", 100);

            if (!Directory.Exists(GameFolder)) {
                string gameZipFile = GameFolder + ".zip";
                File.WriteAllBytes(gameZipFile, Resources.Game);
                progressWindow.SetProgressPercentage(30);
                new FastZip().ExtractZip(gameZipFile, GameFolder, null);
                progressWindow.SetProgressPercentage(60);
                File.Delete(gameZipFile);

                Thread.Sleep(2000);
                progressWindow.SetProgressPercentage(90);
            }
            RefreshExeFile(progressWindow);
            progressWindow.SetProgressPercentage(100);
            RefreshForm();
            progressWindow.Close();
        }

        private void MainMenu_FormClosed(object sender, FormClosedEventArgs e) {
            Application.Exit();
        }
    }
}
