using CriWareLibrary;

namespace UmamusumeExplorer.Controls
{
    internal class AtomAudioSource : AudioSource
    {
        private readonly AcbParser acbParser;
        private readonly AcbReader acbReader;
        private readonly AwbReader awbReader;
        private readonly bool isMemoryAwb;

        private AtomAudioSource(AcbParser acbParser, AcbReader acbReader, AwbReader awbReader, bool isMemoryAwb, string name)
        {
            this.acbParser = acbParser;
            this.acbReader = acbReader;
            this.awbReader = awbReader;
            this.isMemoryAwb = isMemoryAwb;
            Name = name;
            TrackCount = awbReader.Waves.Count;
        }

        public override string Name { get; }

        public override int TrackCount { get; }

        public override void Dispose()
        {
            acbReader.Dispose();
            awbReader.Dispose();
        }

        public static bool TryCreate(string name, string acbPath, string awbPath, out AtomAudioSource? audioSource)
        {
            audioSource = null;

            FileStream acbFile = File.OpenRead(acbPath);
            AcbParser acbParser = new(acbFile);
            AcbReader acbReader = new(acbFile);
            AwbReader awbReader;

            if (string.IsNullOrEmpty(awbPath) && acbReader.HasMemoryAwb)
                awbReader = acbReader.GetAwb();
            else if (string.IsNullOrEmpty(awbPath))
                return false;
            else
                awbReader = new AwbReader(File.OpenRead(awbPath));

            audioSource = new(acbParser, acbReader, awbReader, awbReader.IsEmbedded, name);

            return true;
        }

        protected override IAudioTrack[] InitializeTracks()
        {
            AtomAudioTrack[] tracks = new AtomAudioTrack[TrackCount];

            for (int i = 0; i < TrackCount; i++)
            {
                tracks[i] = new AtomAudioTrack(acbParser, awbReader, i, isMemoryAwb);
            }

            return tracks;
        }
    }
}