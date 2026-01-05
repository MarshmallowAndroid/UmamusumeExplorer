using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Text;
using UmamusumeData;
using UmamusumeExplorer.Assets;
using UmamusumeExplorer.Music;
using UmamusumeExplorer.Music.Live;
using UmamusumeExplorer.Music.SampleProviders;
using UmamusumeExplorer.Utility;
using Color = System.Drawing.Color;

namespace UmamusumeExplorer.Controls
{
    partial class PlayerForm : Form
    {
        private readonly MusicManager liveManager;
        private readonly IExtendedSampleProvider? sampleProvider;
        private readonly int musicId;
        private readonly string songTitle;
        private readonly PinnedBitmap? songJacketPinnedBitmap;

        private readonly Thread? lyricsThread;
        private readonly Thread? voicesThread;

        private readonly FormAnimator? animator;

        private readonly IWavePlayer waveOut = new WaveOutEvent() { DesiredLatency = 250 };

        //private IExtendedSampleProvider sampleProvider;
        private SongMixer? songMixer;
        private List<LyricsTrigger>? lyricsTriggers;

        private int lyricsTriggerIndex = 0;
        private bool seeked = false;
        private bool playbackFinished = false;

        private string[]? currentSingers;
        private bool[]? singersEnabled;
        private bool[]? exEnabled;

        private volatile bool expanded = false;

        public PlayerForm(MusicManager manager)
        {
            InitializeComponent();

            liveManager = manager;
            sampleProvider = manager.SampleProvider;
            musicId = manager.MusicId;
            songTitle = AssetTables.GetText(TextCategory.MasterLiveTitle, musicId);

            songJacketPinnedBitmap = GameAssets.GetJacket(musicId, 'l');
            songJacketPictureBox.BackgroundImage = songJacketPinnedBitmap?.Bitmap;
            songTitleLabel.Text = songTitle;
            songInfoLabel.Text = AssetTables.GetText(TextCategory.MasterLiveAuthor, musicId).Replace("\\n", "\n");

            float heightFactor = AutoScaleDimensions.Height / 96F;

            int collapsedHeight = (int)(heightFactor * 470);
            int expandedHeight = (int)(heightFactor * 745);

            if (liveManager.CharacterPositions is not null)
            {
                lyricsThread = new(DoLyricsPlayback);
                voicesThread = new(DoVoiceUpdate);
                animator = new(this, collapsedHeight, expandedHeight);

                // collapsed: 470
                // expanded: 745
            }
            else
            {
                setupButton.Visible = false;
                expandButton.Visible = false;
            }

            Height = collapsedHeight;

            if (songJacketPinnedBitmap is not null)
                Icon = Icon.FromHandle(songJacketPinnedBitmap.Bitmap.GetHicon());
        }

        private void LiveMusicPlayerForm_Load(object sender, EventArgs e)
        {
            if (liveManager.SampleProvider is null)
            {
                Close();
                return;
            }

            songMixer = liveManager.SampleProvider as SongMixer;
            lyricsTriggers = liveManager.LyricsTriggers;

            if (songMixer is not null)
                waveOut.Init(songMixer);
            else
                waveOut.Init(new VolumeSampleProvider(sampleProvider) { Volume = 4.0F });
            waveOut.Play();
            UpdatePlayIcon();

            lyricsThread?.Start();
            voicesThread?.Start();
            updateTimer.Enabled = true;

            singersEnabled = new bool[liveManager.CharacterPositions?.Length ?? 0];
            exEnabled = new bool[liveManager.CharacterPositions?.Length ?? 0];

            // Update the total time and volume track bars
            totalTimeLabel.Text = $"{sampleProvider?.TotalTime:m\\:ss}";
            int volume = (int)Math.Ceiling(waveOut.Volume * 100.0F);
            volumeTrackbar.Value = volume;
            volumeLabel.Text = volume + "%";

            AddCharacters();
        }

        private void PlayButton_Click(object sender, EventArgs e)
        {
            if (lyricsThread is not null && lyricsThread.ThreadState.HasFlag(ThreadState.Unstarted))
                lyricsThread.Start();

            if (voicesThread is not null && voicesThread.ThreadState.HasFlag(ThreadState.Unstarted))
                voicesThread.Start();

            if (waveOut.PlaybackState == PlaybackState.Playing)
            {
                waveOut.Pause();
            }
            else
            {
                waveOut.Play();
            }

            updateTimer.Enabled = waveOut.PlaybackState == PlaybackState.Playing;

            UpdatePlayIcon();
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (sampleProvider is null) return;

            currentTimeLabel.Text = $"{sampleProvider.CurrentTime:m\\:ss}";
            seekTrackBar.Value = (int)(sampleProvider.CurrentTime / sampleProvider.TotalTime * 100.0F);
        }

        private void PlayerForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            playbackFinished = true;
            lyricsThread?.Join();
            voicesThread?.Join();

            songJacketPinnedBitmap?.Dispose();

            waveOut.Stop();
            waveOut.Dispose();
            songMixer?.Dispose();
        }

        private void SeekTrackBar_Scroll(object sender, EventArgs e)
        {
            if (sampleProvider is null) return;

            sampleProvider.Position = (long)(sampleProvider.Length * (float)(seekTrackBar.Value / 100.0F));
            seeked = true;
        }

        private void VolumeTrackbar_Scroll(object sender, EventArgs e)
        {
            waveOut.Volume = volumeTrackbar.Value / 100.0F;
            volumeLabel.Text = (int)Math.Ceiling(waveOut.Volume * 100.0F) + "%";
        }

        private void SetupButton_Click(object sender, EventArgs e)
        {
            if (!liveManager.SetupLive(this)) return;
            AddCharacters();
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void ExportButton_Click(object sender, EventArgs e)
        {
            if (sampleProvider is null) return;
            if (songMixer is null) return;
            if (currentSingers is null) return;

            StringBuilder fileNameString = new();
            fileNameString.Append(songTitle + " (");
            for (int i = 0; i < currentSingers.Length; i++)
            {
                if (!songMixer.CharaTracks[i].Enabled) continue;
                if (i > 0) fileNameString.Append('・');
                fileNameString.Append(currentSingers[i]);
            }
            fileNameString.Append(").wav");

            SaveFileDialog saveFileDialog = new()
            {
                FileName = fileNameString.ToString(),
                Filter = "16-bit PCM WAV|*.wav"
            };

            DialogResult result = saveFileDialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                PlaybackState playbackState = waveOut.PlaybackState;
                waveOut.Pause();

                Task.Run(() =>
                {
                    long restorePosition = sampleProvider.Position;
                    string restoreInfo = songInfoLabel.Text;

                    sampleProvider.Position = 0;
                    lyricsTriggerIndex = 0;
                    Invoke(() =>
                    {
                        songInfoLabel.Text = "Exporting...";
                        setupButton.Enabled = false;
                        playButton.Enabled = false;
                        stopButton.Enabled = false;
                        updateTimer.Enabled = true;
                    });

                    WaveFileWriter.CreateWaveFile16(saveFileDialog.FileName, sampleProvider);

                    sampleProvider.Position = restorePosition;
                    lyricsTriggerIndex = 0;

                    if (playbackState == PlaybackState.Playing)
                        waveOut.Play();

                    Invoke(() =>
                    {
                        songInfoLabel.Text = restoreInfo;
                        setupButton.Enabled = true;
                        playButton.Enabled = true;
                        stopButton.Enabled = true;
                        updateTimer.Enabled = waveOut.PlaybackState == PlaybackState.Playing;
                    });
                });
            }
        }

        private void MuteBgmCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (songMixer is null) return;

            songMixer.MuteBgm = muteBgmCheckBox.Checked;
        }

        private void CustomVoiceControlCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (songMixer is null) return;
            if (singersEnabled is null) return;

            songMixer.CustomMode = customVoiceControlCheckBox.Checked;

            foreach (var control in charaContainerPanel.Controls)
            {
                if (control is CharacterPositionControl chara)
                {
                    CharaTrack track = songMixer.CharaTracks[chara.Position];

                    if (customVoiceControlCheckBox.Checked)
                    {
                        track.Enabled = singersEnabled[chara.Position];
                    }
                    else
                    {
                        track.Enabled = true;
                    }

                    chara.BackColor = track.Enabled ? Color.FromKnownColor(KnownColor.Control) : Color.FromKnownColor(KnownColor.ControlDark);
                }
            }
        }

        private void ExpandButton_Click(object sender, EventArgs e)
        {
            if (animator is null) return;

            if (!expanded)
                expanded = animator.Expand();
            else
                expanded = !animator.Collapse();
        }

        private void AddCharacters()
        {
            if (liveManager.CharacterPositions is null) return;
            if (songMixer is null) return;

            bool hasEx = songMixer.CharaTracks[0].HasEx;

            currentSingers = new string[liveManager.CharacterPositions.Length];

            // Add characters position controls to voice control panel
            charaContainerPanel.Controls.Clear();
            foreach (var characterPosition in liveManager.CharacterPositions)
            {
                charaContainerPanel.Controls.Add(
                    new CharacterPositionControl(characterPosition.Position, CharacterClick, ModeChanged, 70)
                    {
                        CharacterId = characterPosition.CharacterId,
                        FontSize = 12F,
                        Position = characterPosition.Position,
                        ShowEx = hasEx
                    });

                currentSingers[characterPosition.Position] = AssetTables.GetText(TextCategory.MasterCharaName, characterPosition.CharacterId);
            }
        }

        private void CharacterClick(object? sender, EventArgs e)
        {
            if (customVoiceControlCheckBox.Checked)
            {
                if (sender is not Control control) return;
                if (control.Parent is not CharacterPositionControl character) return;

                if (songMixer is null) return;
                if (singersEnabled is null) return;

                CharaTrack track = songMixer.CharaTracks[character.Position];
                track.Enabled = !track.Enabled;
                singersEnabled[character.Position] = track.Enabled;
            }
        }

        private void ModeChanged(object? sender, MultiStateButtonEventArgs e)
        {
            if (customVoiceControlCheckBox.Checked)
            {
                if (sender is not Control control) return;
                if (control.Parent is not CharacterPositionControl character) return;

                if (songMixer is null) return;
                if (singersEnabled is null) return;

                CharaTrack track = songMixer.CharaTracks[character.Position];
                track.ForceMode = (TrackMode)e.Value;
            }
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

        private void DoLyricsPlayback()
        {
            double msElapsed;

            while (!playbackFinished)
            {
                if (sampleProvider is null) continue;
                if (songMixer is null) continue;
                if (lyricsTriggers is null) continue;

                msElapsed = sampleProvider.CurrentTime.TotalMilliseconds;

                if (seeked)
                {
                    lyricsTriggerIndex = 0;
                    while (msElapsed >= lyricsTriggers[lyricsTriggerIndex].TimeMs)
                    {
                        if (lyricsTriggerIndex < lyricsTriggers.Count - 1)
                            lyricsTriggerIndex++;
                        else break;
                    }

                    if (lyricsTriggerIndex >= lyricsTriggers.Count - 1) break;

                    string lyrics = lyricsTriggers[lyricsTriggerIndex - 1].Lyrics;
                    BeginInvoke(() =>
                    {
                        lyricsLabel.Text = lyrics;
                    });

                    seeked = false;
                }
                else
                {
                    while (msElapsed >= lyricsTriggers[lyricsTriggerIndex].TimeMs)
                    {
                        string lyrics = lyricsTriggers[lyricsTriggerIndex].Lyrics;
                        BeginInvoke(() =>
                        {
                            lyricsLabel.Text = lyrics;
                        });

                        if (lyricsTriggerIndex < lyricsTriggers.Count - 1)
                            lyricsTriggerIndex++;
                        else break;
                    }

                }

                Thread.Sleep(1);
            }
        }

        private void DoVoiceUpdate()
        {
            while (!playbackFinished)
            {
                if (songMixer?.CharaTracks.Count > 0)
                {
                    foreach (var control in charaContainerPanel.Controls)
                    {
                        if (control is not CharacterPositionControl chara) continue;
                        BeginInvoke(() =>
                        {
                            chara.Disabled = !songMixer.CharaTracks[chara.Position].Active && !customVoiceControlCheckBox.Checked;

                            if (!customVoiceControlCheckBox.Checked)
                            {
                                chara.Mode = songMixer.CharaTracks[chara.Position].Mode;
                            }

                            //if (customVoiceControlCheckBox.Checked)
                            //{
                            chara.BackColor = songMixer.CharaTracks[chara.Position].Enabled ?
                                Color.FromKnownColor(KnownColor.Control) : Color.FromKnownColor(KnownColor.ControlDark);
                            //}
                        });
                    }
                }

                Thread.Sleep(1);
            }
        }
    }
}
