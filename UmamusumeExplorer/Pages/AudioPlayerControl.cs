using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.ComponentModel;
using System.Data;
using UmamusumeData;
using UmamusumeAudio;
using UmamusumeExplorer.Assets;
using UmamusumeExplorer.Controls;
using Color = System.Drawing.Color;

namespace UmamusumeExplorer.Pages
{
    partial class AudioPlayerControl : UserControl
    {
        private readonly WaveOutEvent waveOut;
        private readonly object waveOutLock = new();

        private readonly IEnumerable<ManifestEntry> audioAssetEntries = AssetTables.AudioAssetEntries;
        private char audioType;
        private IEnumerable<ManifestEntry>? acbOnly;

        private IEnumerable<ListViewItem>? defaultItems;
        private IEnumerable<ListViewItem>? targetItems;
        private Func<ListViewItem, bool>? filterPredicate = null;

        private int totalFiles;

        private UmaWaveStream? umaWaveStream;
        private VolumeSampleProvider? volumeSampleProvider;

        public AudioPlayerControl()
        {
            InitializeComponent();

            waveOut = new WaveOutEvent();

            if (Application.UseVisualStyles)
                seekTrackBar.BackColor = Color.FromArgb(255, 249, 249, 249);
        }

        private void AudioPlayerControl_Load(object sender, EventArgs e)
        {
            if (!audioAssetEntries.Any()) return;

            audioTypeComboBox.SelectedIndex = 0;
        }

        private void AudioTypeComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            audioType = audioTypeComboBox.SelectedIndex switch
            {
                0 => 'b',
                1 => 'c',
                2 => 'j',
                3 => 'l',
                4 => 's',
                _ => 'v',
            };

            acbOnly = audioAssetEntries.Where((gf) => gf.Name.StartsWith($"sound/{audioType}/") && gf.BaseName.EndsWith(".acb"));
            if (acbOnly is null) return;

            totalFiles = acbOnly.Count();

            fileListView.Items.Clear();

            loadingProgressBar.Visible = true;
            loadingFileNameLabel.Visible = true;
            audioTypeComboBox.Enabled = false;
            refreshButton.Enabled = false;

            loadingBackgroundWorker.RunWorkerAsync();
        }

        private void LoadingBackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            if (sender is not BackgroundWorker backgroundWorker) return;
            if (acbOnly is null) return;

            List<ListViewItem> listViewItems = [];

            backgroundWorker.ReportProgress(0);

            int currentFile = 0;
            foreach (var gameFile in acbOnly)
            {
                string baseName = gameFile.BaseName[0..^4];
                string awbName = baseName + ".awb";
                string acbPath = UmaDataHelper.GetPath(gameFile);
                string awbPath = UmaDataHelper.GetPath(AssetTables.AudioAssetEntries.FirstOrDefault((gf) => gf.BaseName == awbName));

                ListViewItem item = new([baseName, ""]);
                listViewItems.Add(item);
                item.ForeColor = Color.Red;

                Task.Run(() =>
                {
                    if ((File.Exists(acbPath) && File.Exists(awbPath)) || File.Exists(acbPath))
                    {
                        if (!AtomAudioSource.TryCreate(baseName, acbPath, awbPath, out AtomAudioSource? audioSource)) return;
                        if (audioSource is null) return;

                        int trackCount = audioSource.TrackCount;
                        if (trackCount > 0)
                        {
                            fileListView.Invoke(() =>
                            {
                                item.SubItems[1].Text = trackCount.ToString();
                            });
                            item.Tag = audioSource;
                            item.ForeColor = Color.Black;
                        }
                    }
                });

                currentFile++;

                backgroundWorker.ReportProgress(currentFile, gameFile.BaseName);
            }

            listViewItems.Sort((lvi1, lvi2) => string.Compare(lvi1.Name, lvi2.Name));
            e.Result = listViewItems;
        }

        private void LoadingBackgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            loadingProgressBar.Value = (int)((float)e.ProgressPercentage / totalFiles * 100);
            loadingFileNameLabel.Text = e.UserState as string;
        }

        private void LoadingBackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            loadingProgressBar.Visible = false;
            loadingFileNameLabel.Visible = false;
            audioTypeComboBox.Enabled = true;
            refreshButton.Enabled = true;

            if (e.Result is not null)
            {
                defaultItems = e.Result as List<ListViewItem>;
                Filter();
                if (targetItems is not null)
                    fileListView.Items.AddRange([.. targetItems]);
            }

            fileListView.Columns[0].Width = (int)(fileListView.ClientSize.Width * (float)0.80F);
            fileListView.Columns[1].Width = -2;
        }

        private void FileListView_ItemActivate(object sender, EventArgs e)
        {
            if (sender is not ListView fileList) return;
            AudioSource? audioSource = fileList.SelectedItems[0].Tag as AudioSource;
            ChangeAudioSource(audioSource);
        }

        private void FileListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.A)
            {
                e.SuppressKeyPress = true;

                foreach (ListViewItem item in fileListView.Items)
                {
                    item.Selected = true;
                }
            }
        }

        private void TracksComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            updateTimer.Enabled = false;

            if (sender is not ComboBox comboBox) return;
            if (comboBox.SelectedItem is not TrackComboBoxItem trackComboBoxItem) return;
            IAudioTrack audioTrack = trackComboBoxItem.Track;

            lock (waveOutLock)
            {
                waveOut?.Stop();

                umaWaveStream?.Dispose();
                umaWaveStream = (UmaWaveStream)audioTrack.WaveStream;

                timeLengthLabel.Text = umaWaveStream.TotalTime.ToString("mm\\:ss");

                InitializeWaveOut(umaWaveStream);
                waveOut?.Play();
                UpdatePlayIcon();
            }

            updateTimer.Enabled = true;
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (umaWaveStream is null) return;
            seekTrackBar.Value = (int)((float)umaWaveStream.Position / umaWaveStream.Length * 100);
            timeElapsedLabel.Text = umaWaveStream.CurrentTime.ToString("mm\\:ss");
        }

        private void SeekTrackBar_Scroll(object sender, EventArgs e)
        {
            if (sender is not TrackBar trackBar) return;
            if (umaWaveStream is null) return;
            umaWaveStream.Position = (long)(umaWaveStream.Length * ((float)trackBar.Value / trackBar.Maximum)) - 1;
        }

        private void PlayButton_Click(object sender, EventArgs e)
        {
            if (sender is not Button button) return;
            if (umaWaveStream is null) return;

            try
            {
                if (waveOut.PlaybackState == PlaybackState.Playing)
                {
                    waveOut.Pause();
                }
                else
                {
                    waveOut.Play();
                }
            }
            catch (Exception)
            {
                InitializeWaveOut(umaWaveStream);
                waveOut.Play();
            }

            UpdatePlayIcon();
        }

        private void PrevTrackButton_Click(object sender, EventArgs e)
        {
            if (tracksComboBox.SelectedIndex > 0)
                tracksComboBox.SelectedIndex--;
            else tracksComboBox.SelectedIndex = tracksComboBox.Items.Count - 1;
        }

        private void NextTrackButton_Click(object sender, EventArgs e)
        {
            if (tracksComboBox.SelectedIndex + 1 < tracksComboBox.Items.Count)
                tracksComboBox.SelectedIndex++;
            else tracksComboBox.SelectedIndex = 0;
        }

        private void PrevBankButton_Click(object sender, EventArgs e)
        {
            if (fileListView.SelectedIndices.Count > 0)
            {
                int selectedItemIndex = fileListView.SelectedIndices[0];
                fileListView.Items[selectedItemIndex].Selected = false;

                if (selectedItemIndex > 0)
                    selectedItemIndex--;
                else selectedItemIndex = fileListView.Items.Count - 1;

                ListViewItem newItem = fileListView.Items[selectedItemIndex];
                newItem.Selected = true;
                newItem.EnsureVisible();

                ChangeAudioSource((AudioSource?)newItem.Tag);
            }
        }

        private void NextBankButton_Click(object sender, EventArgs e)
        {
            if (fileListView.SelectedIndices.Count > 0)
            {
                int selectedItemIndex = fileListView.SelectedIndices[0];
                fileListView.Items[selectedItemIndex].Selected = false;

                if (selectedItemIndex + 1 < fileListView.Items.Count)
                    selectedItemIndex++;
                else selectedItemIndex = 0;

                ListViewItem newItem = fileListView.Items[selectedItemIndex];
                newItem.Selected = true;
                newItem.EnsureVisible();

                ChangeAudioSource((AudioSource?)newItem.Tag);
            }
        }

        private void ExportButton_Click(object sender, EventArgs e)
        {
            if (tracksComboBox.SelectedItem is not TrackComboBoxItem tracksComboBoxItem) return;
            IAudioTrack audioTrack = tracksComboBoxItem.Track;
            ExportTrack(audioTrack);
        }

        private void ConfigureLoopButton_Click(object sender, EventArgs e)
        {
            if (umaWaveStream is not null)
                ControlHelpers.ShowFormDialogCenter(new ConfigureLoopForm(umaWaveStream), this);
        }

        private void VolumeUpDown_ValueChanged(object sender, EventArgs e)
        {
            if (volumeSampleProvider is not null)
                volumeSampleProvider.Volume = (float)volumeUpDown.Value;
        }

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            fileListView.Items.Clear();

            loadingProgressBar.Visible = true;
            loadingFileNameLabel.Visible = true;
            audioTypeComboBox.Enabled = false;
            refreshButton.Enabled = false;

            loadingBackgroundWorker.RunWorkerAsync();
        }

        private void FilterButton_Click(object sender, EventArgs e)
        {
            if (filterPredicate is null)
            {
                filterPredicate = ga => ga.Text.Contains(searchBox.Text);
                filterButton.Text = "Reset";
            }
            else
            {
                filterPredicate = null;
                filterButton.Text = "Filter";
            }

            Filter();

            fileListView.Items.Clear();

            if (targetItems is not null)
                fileListView.Items.AddRange([.. targetItems]);
        }

        private void ExportToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<AudioSource> audioSources = [];
            foreach (ListViewItem item in fileListView.SelectedItems)
            {
                if (item.Tag is AudioSource audioSource)
                    audioSources.Add(audioSource);
            }
            ExportBank([.. audioSources]);
        }

        private void FileListView_Resize(object sender, EventArgs e)
        {
            fileListView.Columns[0].Width = (int)(fileListView.Width * 0.80F);
            fileListView.Columns[1].Width = -2;
        }

        private void Filter()
        {
            if (filterPredicate is not null)
            {
                targetItems = defaultItems?.Where(filterPredicate);
            }
            else
            {
                targetItems = defaultItems;
            }
        }

        private void ChangeAudioSource(AudioSource? audioSource)
        {
            if (audioSource is null) return;

            tracksComboBox.Items.Clear();
            for (int i = 0; i < audioSource.Tracks.Length; i++)
            {
                IAudioTrack track = audioSource.Tracks[i];
                tracksComboBox.Items.Add(
                    new TrackComboBoxItem(track.Name, i, track));
            }
            tracksComboBox.SelectedIndex = 0;
        }

        private void InitializeWaveOut(WaveStream waveStream)
        {
            waveOut.Stop();
            volumeSampleProvider = new VolumeSampleProvider(waveStream.ToSampleProvider()) { Volume = (float)volumeUpDown.Value };
            waveOut.Init(volumeSampleProvider);
        }

        private void UpdatePlayIcon()
        {
            if (waveOut.PlaybackState == PlaybackState.Playing)
            {
                playButton.Image = Properties.Resources.PauseIcon;
            }
            else
            {
                playButton.Image = Properties.Resources.PlayIcon;
            }
        }

        private void ExportBank(AudioSource[] audioSources)
        {
            FolderBrowserDialog folderBrowser = new();

            if (audioSources.Length == 1)
            {
                AudioSource single = audioSources[0];
                if (single.Tracks.Length == 1)
                    ExportTrack(single.Tracks[0]);
                else
                {
                    DialogResult result = folderBrowser.ShowDialog();
                    if (result != DialogResult.OK) return;

                    string selected = folderBrowser.SelectedPath;
                    string directory = Path.Combine(selected, single.Name);

                    Directory.CreateDirectory(directory);

                    Task.Run(() =>
                    {
                        int currentTrack = 1;
                        foreach (var track in single.Tracks)
                        {
                            UmaWaveStream copy = (UmaWaveStream)track.WaveStream;
                            copy.Loop = false;

                            string fileName = Path.Combine(directory, track.Name + $"_{currentTrack++:d2}.wav");

                            if (fileName.Length > 128)
                                fileName = fileName[..128];

                            WaveFileWriter.CreateWaveFile16(fileName,
                                new VolumeSampleProvider(copy.ToSampleProvider())
                                {
                                    Volume = (float)volumeUpDown.Value
                                });

                            WaveFileWriter.CreateWaveFile16(fileName, copy.ToSampleProvider());

                            copy.Dispose();
                            Invoke(() => exportButton.Enabled = true);
                        }

                        MessageBox.Show("Export complete.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    });
                }
            }
            else
            {
                DialogResult result = folderBrowser.ShowDialog();
                if (result != DialogResult.OK) return;

                string directory = folderBrowser.SelectedPath;

                ControlHelpers.ShowFormCenter(new MultipleExportDialog(audioSources, directory), this);
            }
        }

        private void ExportTrack(IAudioTrack audioTrack)
        {
            SaveFileDialog save = new()
            {
                FileName = audioTrack.Name.Length > 128 ? audioTrack.Name[..128] : audioTrack.Name,
                Filter = "16-bit PCM WAV|*.wav"
            };

            DialogResult result = save.ShowDialog();
            if (result != DialogResult.OK) return;

            exportButton.Enabled = false;
            Task.Run(() =>
            {
                UmaWaveStream copy = (UmaWaveStream)audioTrack.WaveStream;
                copy.Loop = false;

                WaveFileWriter.CreateWaveFile16(save.FileName,
                    new VolumeSampleProvider(copy.ToSampleProvider())
                    {
                        Volume = (float)volumeUpDown.Value
                    });

                MessageBox.Show("Export complete.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);

                copy.Dispose();

                Invoke(() => exportButton.Enabled = true);
            });
        }
    }
}
