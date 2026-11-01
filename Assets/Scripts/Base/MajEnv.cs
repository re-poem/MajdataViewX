using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ManagedBass;
using UnityEngine;

namespace MajdataViewX.Base
{
    public static class MajEnv
    {
#if UNITY_EDITOR
        // 编辑器下，指向项目根目录（Assets 的上一级）
        public static readonly string MajBase = new DirectoryInfo(Application.dataPath).Parent!.FullName;
#elif UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
        // 打包后，Application.dataPath 的上一级在 Windows 下是 exe 目录
        // 但为了兼顾 Mac 等平台，用 AppContext 或者是 dataPath 的物理同级更安全
        public static string MajBase => System.AppDomain.CurrentDomain.BaseDirectory;
#else // Android / iOS / ...
        public static string MajBase => Application.persistentDataPath;
#endif

        public static string GetPath(string relativePath) =>
            Path.Combine(MajBase, relativePath);

        public static string MmfAudioTimePath => GetPath("majdata_time.dat");
        public static string RecordingDirectory => GetPath("Record");

        #region Bass

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetModuleFileName(IntPtr module, StringBuilder fileName, int size);
#elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        [DllImport("libdl.dylib", EntryPoint = "dlopen")]
        private static extern IntPtr DlOpen(string fileName, int flags);

        [DllImport("libdl.dylib", EntryPoint = "dlerror")]
        private static extern IntPtr DlError();
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX || UNITY_ANDROID
        [DllImport("libdl.so", EntryPoint = "dlopen")]
        private static extern IntPtr DlOpen(string fileName, int flags);

        [DllImport("libdl.so", EntryPoint = "dlerror")]
        private static extern IntPtr DlError();
#endif

        private const int RTLD_NOW = 2;

        private static readonly string[] _pluginSubDirectories = { "x64", "x86_64", "x86", "" };
        private static string _bassDirectory;
        private static bool _bassDirectoryResolved;
        private static bool _bassPluginsLoaded;

        /// <summary>
        /// 初始化 BASS 并挂上解码插件。
        /// </summary>
        public static void InitBass(int sampleRate)
        {
            // AudioManager.OnDestroy 会调 Bass.Free，重新进入场景要重开设备；
            // 设备已在初始化状态时 Bass.Init 只会返回 Already，直接复用即可
            if (Bass.CurrentDevice < 0 && !Bass.Init(-1, sampleRate))
            {
                // TODO: MajLog
                Debug.LogWarning($"[MajEnv] Bass Init Failed（{Bass.LastError}）");
            }

            // 插件随 bass 库驻留进程，Bass.Free 不会卸载它们，重复加载只会得到 Already
            if (_bassPluginsLoaded)
                return;

            LoadBassPlugin("bassmix", false);
            LoadBassPlugin("bassopus", true);
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX || UNITY_ANDROID
            LoadBassPlugin("bass_aac", true);
#endif // macOS / iOS 有系统解码器

            _bassPluginsLoaded = true;
        }

        private static string NativeLibName(string name)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return name + ".dll";
#elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX || UNITY_ANDROID
            return "lib" + name + ".so";
#elif UNITY_IOS
            // iOS 上插件是静态链接的，BASS_PluginLoad 收的是符号名而不是文件名
            return name;
#else
            return "lib" + name + ".dylib";
#endif
        }

        /// <summary>
        /// bass 原生的加载器不会搜索 bass 库所在目录，插件必须给全路径，
        /// 否则只会得到 BASS_ERROR_FILEOPEN。目录从已载入的 bass 模块反查，
        /// 编辑器（Assets/Plugins/Un4seen.Bass/Windows）与构建版（_Data/Plugins/x86_64）都成立。
        /// </summary>
        private static string ResolveBassDirectory()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            var module = GetModuleHandle("bass");
            if (module == IntPtr.Zero)
                module = GetModuleHandle("bass.dll");
            if (module == IntPtr.Zero)
                return null;

            var buffer = new StringBuilder(1024);
            var length = GetModuleFileName(module, buffer, buffer.Capacity);
            return length > 0 ? Path.GetDirectoryName(buffer.ToString(0, (int)length)) : null;
#else
            return null;
#endif
        }

        private static string BassDirectory
        {
            get
            {
                if (_bassDirectoryResolved)
                    return _bassDirectory;
                _bassDirectory = ResolveBassDirectory();
                _bassDirectoryResolved = true;
                return _bassDirectory;
            }
        }

        /// <summary>
        /// 返回插件的完整路径；定位不到时退化为裸文件名，交给系统加载器搜索。
        /// </summary>
        private static string ResolvePluginPath(string fileName)
        {
            var directory = BassDirectory;
            if (string.IsNullOrEmpty(directory))
                return fileName;

            foreach (var subDirectory in _pluginSubDirectories)
            {
                var candidate = subDirectory.Length == 0
                    ? Path.Combine(directory, fileName)
                    : Path.Combine(directory, subDirectory, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return fileName;
        }

        private static IntPtr LoadNativeLibrary(string path)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return LoadLibrary(path);
#elif UNITY_IOS
            return IntPtr.Zero;
#else
            return DlOpen(path, RTLD_NOW);
#endif
        }

        private static string NativeLoadError()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return $"win32 {Marshal.GetLastWin32Error()}";
#elif UNITY_IOS
            return "n/a";
#else
            var error = DlError();
            return error == IntPtr.Zero ? "unknown" : Marshal.PtrToStringAnsi(error);
#endif
        }

        private static void LoadBassPlugin(string name, bool registerAsFormatPlugin)
        {
            var path = ResolvePluginPath(NativeLibName(name));

            if (registerAsFormatPlugin)
            {
                // 解码插件导出 BASSplugin 符号，交给 BASS 注册进格式链即可
                if (Bass.PluginLoad(path) != 0)
                    return;
                // 之前已经加载过（插件随进程存活，Bass.Free 不会卸载），这不算失败
                if (Bass.LastError == Errors.Already)
                    return;
                // TODO: MajLog
                Debug.LogWarning($"[MajEnv] Plugin {name} Load Failed（{Bass.LastError}）- {path}");
                return;
            }

            // bassmix / bass_fx 不导出 BASSplugin（BASS_PluginLoad 只会返回 FILEFORM），
            // 只能原生加载，之后再通过 P/Invoke 直接调用
#if UNITY_IOS
            // iOS 上是静态库，符号已随可执行文件链接，无需动态加载
#else
            if (LoadNativeLibrary(path) != IntPtr.Zero)
                return;
            // TODO: MajLog
            Debug.LogWarning($"[MajEnv] Library {name} Load Failed（{NativeLoadError()}）- {path}");
#endif
        }

        #endregion
    }
}
