#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Cysharp.Text;
using Unity.Profiling;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Profiling;

// ReSharper disable once CheckNamespace
namespace MajdataPlay.Diagnostics
{
    public static class MajDebug
    {
        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Log<T>(string? tag, T obj, LogLevel level)
        {
            var sb = ZString.CreateStringBuilder();
            if (!string.IsNullOrEmpty(tag))
            {
                sb.Append('[');
                sb.Append(tag);
                sb.Append(']');
            }
            sb.Append(obj);
            if (obj is Exception)
            {
                sb.AppendLine();
                sb.Append(GetStackTrack());
            }

            switch (level)
            {
                case LogLevel.Error:
                    UnityEngine.Debug.LogError(sb.ToString());
                    break;
                case LogLevel.Warn:
                    UnityEngine.Debug.LogWarning(sb.ToString());
                    break;
                case LogLevel.Debug:
                case LogLevel.Info:
                default:
                    UnityEngine.Debug.Log(sb.ToString());
                    break;
            }
        }

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogDebug<T>(T obj) => Log(null, obj, LogLevel.Debug);

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogDebug<T>(string tag, T obj) => Log(tag, obj, LogLevel.Debug);

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogInfo<T>(T obj) => Log(null, obj, LogLevel.Info);

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogInfo<T>(string tag, T obj) => Log(tag, obj, LogLevel.Info);

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogWarning<T>(T obj) => Log(null, obj, LogLevel.Warn);

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogWarning<T>(string tag, T obj) => Log(tag, obj, LogLevel.Warn);

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogError<T>(T obj) => Log(null, obj, LogLevel.Error);

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogError<T>(string tag, T obj) => Log(tag, obj, LogLevel.Error);

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogException<T>(T obj) where T : Exception => Log(null, obj, LogLevel.Error);

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogException<T>(string tag, T obj) where T : Exception
            => Log(tag, obj, LogLevel.Error);


        private static string GetStackTrack()
        {
            return new StackTrace(3, true).ToString();
        }
    }

    
    [IgnoredByDeepProfiler]
    public readonly ref struct UnityProfiler
    {
        public string Name { get; }
        public UnityProfiler(string name) => Name = name;

        [HideInCallstack]
        [Conditional("ENABLE_PROFILER")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            Profiler.EndSample();
        }

        [HideInCallstack]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UnityProfiler Create(string name)
        {
            Profiler.BeginSample(name);
            return new(name);
        }
    }
}