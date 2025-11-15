using SQLite;
using System.ComponentModel;
using System.Data;
using System.Drawing.Imaging;
using System.Text;
using UmamusumeData;

namespace UmamusumeExplorer.Controls
{
    public partial class DownloadWorkaroundForm : Form
    {
        private static readonly string metaFile = UmaDataHelper.MetaFile;
        private static readonly string metaFileBackup = metaFile + ".bak";

        private readonly List<ManifestEntry> liveAudioEntries = [.. UmaDataHelper.GetManifestEntries(e => e.Name.StartsWith("sound/l/"))];

        public DownloadWorkaroundForm()
        {
            InitializeComponent();

            Task.Run(() =>
            {
                long totalSize = liveAudioEntries.Sum(s => s.Length);
                List<ManifestEntry> existingEntries = [.. liveAudioEntries.Where(e => File.Exists(UmaDataHelper.GetPath(e)))];
                Invoke(() => labelStep2.Text += $" ({GenerateSizeString(totalSize - existingEntries.Sum(e => e.Length))})");
            });

            buttonRevert.Enabled = false;

            if (File.Exists(metaFileBackup))
            {
                buttonModifyDatabase.Enabled = false;
                buttonRevert.Enabled = true;
            }
        }

        private void ButtonModifyDatabase_Click(object sender, EventArgs e)
        {
            if (!File.Exists(metaFileBackup))
                File.Copy(metaFile, metaFileBackup, true);

            foreach (var item in liveAudioEntries)
            {
                item.Group = AssetBundleGroup.Default;
            }
            UmaDataHelper.UpdateManifestEntries(liveAudioEntries);

            buttonModifyDatabase.Enabled = false;
            buttonRevert.Enabled = true;
        }

        private static string GenerateSizeString(long length)
        {
            StringBuilder sizeString = new();

            string[] units =
            [
                "B", "KB", "MB", "GB"
            ];

            int unitIndex = (int)Math.Floor(Math.Log(length, 10) / 3);
            unitIndex = unitIndex >= 0 ? unitIndex : 0;
            float divide = (float)Math.Pow(1000, unitIndex);
            sizeString.Append($"{length / divide:f2} {units[unitIndex]}");

            return sizeString.ToString();
        }

        private void ButtonCacheFiles_Click(object sender, EventArgs e)
        {
            string cacheDirectory = "LiveCache";

            if (Directory.Exists(cacheDirectory))
                Directory.Delete(cacheDirectory, true);
            Directory.CreateDirectory(cacheDirectory);

            BackgroundWorker backgroundWorker = new();
            backgroundWorker.WorkerReportsProgress = true;
            backgroundWorker.DoWork += (s, e) =>
            {

                //SQLiteConnection connection = new(metaFile, SQLiteOpenFlags.ReadWrite);
                //connection.RunInTransaction(() =>
                //{
                //    foreach (var item in liveAudioEntries)
                //    {
                //        item.Group = AssetBundleGroup.Default;
                //    }
                //    connection.UpdateAll(liveAudioEntries);
                //});
                string previousText = buttonCacheFiles.Text;

                Invoke(() =>
                {
                    buttonCacheFiles.Text = "Caching live audio resources...";
                    buttonCacheFiles.Enabled = false;
                });

                int currentFile = 1;
                foreach (var item in liveAudioEntries)
                {
                    string path = UmaDataHelper.GetPath(item);
                    string newPath = Path.Combine(cacheDirectory, item.BaseName);

                    if (!File.Exists(path) && !File.Exists(newPath))
                    {
                        MessageBox.Show("Download incomplete. Please download all resources in the game.", "Download incomplete", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                        Invoke(() =>
                        {
                            buttonCacheFiles.Text = previousText;
                            buttonCacheFiles.Enabled = true;
                            progressBarCache.Value = 0;
                        });
                        break;
                    }

                    File.Copy(path, newPath, true);
                    backgroundWorker.ReportProgress((int)((float)currentFile++ / liveAudioEntries.Count * 100.0F));
                }
            };
            backgroundWorker.ProgressChanged += (s, e) =>
            {
                progressBarCache.Value = e.ProgressPercentage;
                if (e.ProgressPercentage == 100)
                {
                    buttonCacheFiles.Text = "Live audio resource cache complete";
                    buttonCacheFiles.Enabled = true;
                }
            };
            backgroundWorker.RunWorkerAsync();

        }

        private void ButtonRevert_Click(object sender, EventArgs e)
        {
            File.Delete(metaFile);
            File.Move(metaFileBackup, metaFile);

            buttonRevert.Enabled = false;
            buttonModifyDatabase.Enabled = true;
        }
    }
}
