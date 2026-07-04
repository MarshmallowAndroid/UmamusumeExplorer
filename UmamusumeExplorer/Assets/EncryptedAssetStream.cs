using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeExplorer.Assets
{
    internal class EncryptedAssetStream : FileStream
    {
        private const int headerSize = 256;
        private static readonly byte[] baseKeys =
        {
           0x53, 0x2B, 0x46, 0x31, 0xE4, 0xA7, 0xB9, 0x47, 0x3E, 0x7C, 0xFB
        };

        private readonly byte[] keys;

        public EncryptedAssetStream(string fileName, long key) : base(fileName, FileMode.Open, FileAccess.Read)
        {
            keys = new byte[baseKeys.Length * 8];

            byte[] keyBytes = BitConverter.GetBytes(key);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(keyBytes);
            }

            for (int i = 0; i < baseKeys.Length; i++)
            {
                byte baseKey = baseKeys[i];

                for (int j = 0; j < 8; j++)
                {
                    int index = j + (i * 8);
                    keys[index] = (byte)(baseKey ^ keyBytes[j]);
                }
            }
        }

        public override void CopyTo(Stream destination, int bufferSize)
        {
            byte[] buffer = new byte[bufferSize];
            int read;
            while ((read = Read(buffer, 0, buffer.Length)) != 0)
            {
                destination.Write(buffer, 0, read);
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = base.Read(buffer, offset, count);

            if (read > 0)
            {
                long streamPosition = Position - read;
                int decryptOffset = streamPosition < headerSize
                    ? (int)Math.Min(read, headerSize - streamPosition)
                    : 0;

                for (int i = decryptOffset; i < read; i++)
                {
                    buffer[offset + i] = (byte)(buffer[offset + i] ^ keys[(int)((streamPosition + i) % keys.Length)]);
                }
            }

            return read;
        }
    }
}
