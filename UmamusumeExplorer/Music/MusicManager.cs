using AssetsTools.NET;
using AssetsTools.NET.Extra;
using CriWareLibrary;
using UmamusumeData;
using UmamusumeData.Tables;
using UmamusumeAudio;
using UmamusumeExplorer.Controls;
using UmamusumeExplorer.Utility;
using UmamusumeExplorer.Assets;
using UmamusumeExplorer.Music.Live;
using UmamusumeExplorer.Music.SampleProviders;
using System.Text;

namespace UmamusumeExplorer.Music
{
    internal class MusicManager
    {
        private readonly Random random = new();

        private readonly int musicId;
        private readonly string cueSheetNameShort = "";
        private readonly string cueSheetNameGameSize = "";

        private bool musicScoreLoaded = false;

        public MusicManager(LiveData liveData)
        {
            musicId = liveData.MusicId;
        }

        public MusicManager(JukeboxMusicData jukeboxMusicData)
        {
            musicId = jukeboxMusicData.MusicId;
            cueSheetNameShort = jukeboxMusicData.BgmCuesheetNameShort;
            cueSheetNameGameSize = jukeboxMusicData.BgmCuesheetNameGamesize;
        }

        public int MusicId => musicId;

        public List<PartTrigger> PartTriggers { get; } = [];

        public IExtendedSampleProvider? SampleProvider { get; private set; }

        public CharacterPosition[]? CharacterPositions { get; private set; }

        public List<LyricsTrigger> LyricsTriggers { get; } = [];

        public int VoiceTrigger { get; private set; }

        public bool SetupLive(Control parent)
        {
            // Load music score before everything else
            if (!LoadMusicScore()) return ShowMissingResources(0);

            // Get possible audio assets for music ID
            IEnumerable<ManifestEntry> audioAssetEntries = UmaDataHelper.GetManifestEntries(ga => ga.Name.StartsWith($"sound/l/{musicId}"));

            // Retrieve count of members that actually sing
            int singingMembers = GetSingingMembers();

            // Show unit setup form
            UnitSetupForm unitSetupForm = new(musicId, singingMembers);
            if (ControlHelpers.ShowFormDialogCenter(unitSetupForm, parent) != DialogResult.OK) return false;
            CharacterPositions = unitSetupForm.CharacterPositions;

            // Get BGM with or without sound effects
            int sfx = unitSetupForm.Sfx ? 1 : 2;
            AwbReader? okeAwb = GetAwbFile(audioAssetEntries.FirstOrDefault(aa => aa.BaseName == $"snd_bgm_live_{musicId}_oke_{sfx:d2}.awb"));

            // Variation
            int variation = random.Next(1, 6);
            okeAwb ??= GetAwbFile(audioAssetEntries.FirstOrDefault(
                aa => aa.BaseName == $"snd_bgm_live_{musicId:d4}_oke_{sfx:d2}_{variation:d2}.awb"));

            if (okeAwb is null) return ShowMissingResources(1);

            // Abort unit setup when character selection is not confirmed
            if (CharacterPositions is null) return false;

            // For Legend-Changer
            int mainCharacterId = CharacterPositions.First(p => p.Position == 0).CharacterId;
            int announcerGender = random.Next(1, 3);

            // Voice over for certain Umamusume with either male or female announcer
            AwbReader? voiceOverAwb = GetAwbFile(audioAssetEntries.FirstOrDefault(aa => aa.BaseName == $"snd_bgm_live_{musicId}_vo_{mainCharacterId:d4}_{announcerGender:d2}.awb"));
            voiceOverAwb ??= GetAwbFile(audioAssetEntries.FirstOrDefault(aa => aa.BaseName == $"snd_bgm_live_{musicId}_vo_{0:d4}_{announcerGender:d2}.awb"));

            IEnumerable<LivePermissionData> allowedCharas = LivePermissionDataHelper.GetLivePermissionData(musicId);
            for (int i = 0; i < CharacterPositions.Length; i++)
            {
                if (CharacterPositions[i].CharacterId == 0)
                {
                    int charaId;
                    do
                    {
                        charaId = allowedCharas.ElementAt(random.Next(0, allowedCharas.Count())).CharaId;
                        if (CharacterPositions.Where(c => c.CharacterId == charaId).Count() > 0) continue;
                        CharacterPositions[i].CharacterId = charaId;
                        break;
                    } while (true);
                }
            }

            // Add AWB files for the selected characters
            AwbReader[] charaAwbs = new AwbReader[singingMembers];

            List<int> missingCharas = [];

            // Get character audio resources
            foreach (var characterPosition in CharacterPositions)
            {
                AwbReader? charaAwb = GetAwbFile(audioAssetEntries.First(aa => aa.BaseName == $"snd_bgm_live_{musicId}_chara_{characterPosition.CharacterId}_01.awb"));
                if (charaAwb is null)
                {
                    missingCharas.Add(characterPosition.CharacterId);
                    continue;
                }
                charaAwbs[characterPosition.Position] = charaAwb;
            }

            if (missingCharas.Count > 0)
            {
                return ShowMissingResources(2, missingCharas);
            }

            // Initialize song mixer on first playback
            SampleProvider ??= new SongMixer(okeAwb, PartTriggers);
            if (SampleProvider is not SongMixer songMixer) return false;

            // Change background music
            if (songMixer.Position > 0)
                songMixer.ChangeOke(okeAwb);

            // Initialize tracks, can be done during playback
            songMixer.InitializeCharaTracks(charaAwbs);

            if (voiceOverAwb is not null)
                songMixer.InitializeVoiceOver(voiceOverAwb, VoiceTrigger);

            // Unit setup success
            return true;
        }

        public bool SetupJukeBoxMusic(SongLength songLength)
        {
            string cueSheetName = "";

            // Select available versions
            if (songLength == SongLength.ShortVersion)
            {
                cueSheetName = cueSheetNameShort;
                if (cueSheetName == string.Empty)
                {
                    MessageBox.Show("Music has no short version. Playing game size version instead.", "No short version", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    cueSheetName = cueSheetNameGameSize;
                }
            }
            else
            {
                cueSheetName = cueSheetNameGameSize;
            }

            // Get possible audio assets for music ID
            IEnumerable<ManifestEntry> audioAssetEntries = UmaDataHelper.GetManifestEntries(
                ga => ga.Name.ToLower().Contains(cueSheetName.ToLower() + ".awb"));

            // Get BGM without sound effects
            AwbReader? okeAwb = GetAwbFile(audioAssetEntries.FirstOrDefault());

            if (okeAwb is null) return ShowMissingResources(1);

            // Initialize song mixer on first playback
            SampleProvider ??= new ExtendedSampleProvider(new UmaWaveStream(okeAwb, 0));

            return true;
        }

        private int GetSingingMembers()
        {
            bool[] membersSing = new bool[PartTriggers[0].MemberTracks.Length];

            foreach (var partTrigger in PartTriggers)
            {
                for (int i = 0; i < partTrigger.MemberTracks.Length; i++)
                {
                    if (partTrigger.MemberTracks[i] > 0) membersSing[i] = true;
                }
            }

            int activeMembers = 0;
            for (int i = 0; i < membersSing.Length; i++)
            {
                if (membersSing[i]) activeMembers++;
            }

            return activeMembers;
        }

        private bool LoadMusicScore()
        {
            if (musicScoreLoaded) return true;

            string musicScorePath = $"live/musicscores/m{musicId}/m{musicId}";
            string lyricsFile = $"{musicScorePath}_lyrics";
            string partFile = $"{musicScorePath}_part";
            string timelinePath = $"cutt/cutt_son{musicId}/son{musicId}_camera";
            string timelinePath2 = $"cutt/cutt_son{musicId}/cutt_son{musicId}_camera";

            AssetsManager assetsManager = new();
            GameAssets.LoadAsset(assetsManager, lyricsFile);
            GameAssets.LoadAsset(assetsManager, partFile);

            CsvReader? lyricsCsv = GetLiveCsv(assetsManager, "lyrics");
            if (lyricsCsv is null) return false;
            lyricsCsv.ReadCsvLine();
            while (!lyricsCsv.EndOfStream)
            {
                string line = lyricsCsv.ReadCsvLine();

                if (string.IsNullOrEmpty(line)) continue;

                LyricsTrigger trigger = new(line);
                LyricsTriggers.Add(trigger);
            }

            CsvReader? partCsv = GetLiveCsv(assetsManager, "part");
            if (partCsv is null) return false;
            bool hasVolumeRate = partCsv.ReadCsvLine().Contains("volume_rate");
            while (!partCsv.EndOfStream)
            {
                PartTrigger trigger = new(partCsv.ReadCsvLine(), hasVolumeRate);
                PartTriggers.Add(trigger);
            }

            GameAssets.LoadAsset(assetsManager, timelinePath);
            GameAssets.LoadAsset(assetsManager, timelinePath2);
            VoiceTrigger = (int)(GetLiveTimelineData(assetsManager) / 60F * 1000F);

            return musicScoreLoaded = true;
        }

        private CsvReader? GetLiveCsv(AssetsManager assetsManager, string category)
        {
            string idString = $"{musicId:d4}";
            string fileName = $"m{idString}_{category}";

            AssetTypeValueField? field = GameAssets.GetFileBaseField(assetsManager, fileName, out AssetsFileInstance? _);
            if (field is not null)
                return new(new MemoryStream(field["m_Script"].AsByteArray));

            return null;
        }

        private int GetLiveTimelineData(AssetsManager assetsManager)
        {
            string idString = $"{musicId:d4}";
            string fileName = $"son{idString}_Camera";
            string fileName2 = $"cutt_son{idString}_Camera";

            AssetTypeValueField? field;
            field = GameAssets.GetFileBaseField(assetsManager, fileName, out AssetsFileInstance? _);
            field ??= GameAssets.GetFileBaseField(assetsManager, fileName2, out AssetsFileInstance? _);
            if (field is null) return -1;
            field = field["VoiceKeys"];
            if (!field.Any()) return -1;
            field = field["thisList"][0];
            if (!field.Any()) return -1;
            return field[0]["frame"].AsInt;
        }

        private static AwbReader? GetAwbFile(ManifestEntry? gameFile)
        {
            if (gameFile is null) return null;

            string awbPath = UmaDataHelper.GetPath(gameFile);
            string cachePath = Path.Combine("LiveCache", gameFile.BaseName);

            string path = awbPath;

            if (!File.Exists(path))
                path = cachePath;

            if (!File.Exists(path))
                return null;
            else
                return new AwbReader(File.OpenRead(path));
        }

        private static bool ShowMissingResources(int what, List<int>? missingCharas = null)
        {
            string missing;
            switch (what)
            {
                case 0:
                    missing = "music score";
                    break;
                case 1:
                    missing = "background music";
                    break;
                default:
                    missing = "resources";
                    break;
            }
            if (what == 2)
            {
                StringBuilder namesBuilder = new();
                if (missingCharas is not null)
                {
                    foreach (var item in missingCharas)
                    {
                        namesBuilder.AppendLine(TextHelpers.GetCharaName(item, true, true));
                    }
                }

                MessageBox.Show($"Selected music is missing voice resources for the following characters:\n\n{namesBuilder.ToString()}\nPlease download all resources in the game.",
                                "Missing resources",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Exclamation);
            }
            else
                MessageBox.Show($"Missing {missing} resource for selected music. Please download all resources in the game.", "Missing resources", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            return false;
        }
    }
}
