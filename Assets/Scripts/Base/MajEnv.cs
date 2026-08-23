using System.IO;
using UnityEngine;

namespace MajdataViewX.Base
{
    public static class MajEnv
    {
#if UNITY_EDITOR
        // 编辑器下，指向项目根目录（Assets 的上一级）
        public static string MajBase => new DirectoryInfo(Application.dataPath).Parent!.FullName;
#elif UNITY_STANDALONE_OSX
        public static string MajBase => Path.Combine(Application.dataPath, "MacOS");
#else
        public static string MajBase => System.AppDomain.CurrentDomain.BaseDirectory;
#endif

        public static string GetPath(string relativePath) =>
            Path.Combine(MajBase, relativePath);

        public static string MmfAudioTimePath =>
            Path.Combine(Application.persistentDataPath, "majdata_time.dat");
        public const long MmfChartDataCapacity = 64 * 1024 * 1024; //64mb
        public static string MmfChartDataPath =>
            Path.Combine(Application.persistentDataPath, "majdata_chart.dat");
    }
}
