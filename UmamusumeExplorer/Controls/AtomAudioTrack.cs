using CriWareLibrary;
using NAudio.Wave;
using UmamusumeAudio;

namespace UmamusumeExplorer.Controls
{
    internal class AtomAudioTrack : IAudioTrack
    {
        private readonly AwbReader awbReader;
        private readonly int waveIndex;

        public AtomAudioTrack(AcbParser acbParser, AwbReader awbReader, int waveIndex, bool memoryAwb)
        {
            this.awbReader = awbReader;
            this.waveIndex = waveIndex;

            Name = acbParser.LoadWaveName(waveIndex, 0, memoryAwb);
        }

        public string Name { get; }

        public WaveStream WaveStream
        {
            get
            {
                return new UmaWaveStream(awbReader, awbReader.Waves[waveIndex].WaveId);
            }
        }
    }
}
