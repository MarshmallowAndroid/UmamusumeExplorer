namespace UmamusumeExplorer.Controls
{
    public abstract class AudioSource : IDisposable
    {
        private IAudioTrack[]? tracks;

        public abstract string Name { get; }

        public abstract int TrackCount { get; }

        public IAudioTrack[] Tracks
        {
            get
            {
                tracks ??= InitializeTracks();
                return tracks;
            }
        }

        protected abstract IAudioTrack[] InitializeTracks();

        public abstract void Dispose();
    }
}
