using System;
using ManagedBass;
using ManagedBass.Aac;
using ManagedBass.Opus;
using UnityEngine;

namespace MajdataViewX.Utils
{
    public static class BassHelper
    {
        public static int CreateStream(string file, long offset, long length, BassFlags flags)
            => Create(format => format switch
            {
                0 => Bass.CreateStream(file, offset, length, flags),
                1 => BassOpus.CreateStream(file, offset, length, flags),
#if UNITY_ANDROID || UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
                2 => BassAac.CreateStream(file, offset, length, flags),
#endif
                _ => throw new ArgumentOutOfRangeException()
            });

        public static int CreateStream(byte[] buffer, long offset, long length, BassFlags flags)
            => Create(format => format switch
            {
                0 => Bass.CreateStream(buffer, offset, length, flags),
                1 => BassOpus.CreateStream(buffer, offset, length, flags),
#if UNITY_ANDROID || UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
                2 => BassAac.CreateStream(buffer, offset, length, flags),
#endif
                _ => throw new ArgumentOutOfRangeException()
            });

        public static int CreateStream(IntPtr buffer, long offset, long length, BassFlags flags)
            => Create(format => format switch
            {
                0 => Bass.CreateStream(buffer, offset, length, flags),
                1 => BassOpus.CreateStream(buffer, offset, length, flags),
#if UNITY_ANDROID || UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
                2 => BassAac.CreateStream(buffer, offset, length, flags),
#endif
                _ => throw new ArgumentOutOfRangeException()
            });

        // 依次用第 0、1、2... 个解码器尝试，只有"格式不认识"才换下一个 
        private static int Create(Func<int, int> tryDecoder)
        {
            for (var format = 0; ; format++)
            {
                var handle = tryDecoder(format);
                
                if (handle != 0)
                {
                    Debug.Log($"Decoded format: {Bass.ChannelGetInfo(handle).ChannelType}");
                    return handle;
                }

                var error = Bass.LastError;
                if (error == Errors.FileFormat)
                    continue;

                Debug.LogError(error);
                throw new Exception($"BASS Error {error}");
            }
        }
    }
}