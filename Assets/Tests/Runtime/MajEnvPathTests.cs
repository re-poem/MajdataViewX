using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class MajEnvPathTests
{
    [Test]
    [UnityPlatform(RuntimePlatform.OSXPlayer)]
    public void MacPlayerUsesBundleExecutableDirectoryForReleaseAssets()
    {
        var majEnv = Type.GetType("MajdataViewX.Base.MajEnv, Assembly-CSharp", throwOnError: true)!;
        var majBase = (string)majEnv.GetProperty("MajBase")!.GetValue(null)!;

        Assert.That(majBase, Is.EqualTo(Path.Combine(Application.dataPath, "MacOS")));
        Assert.That(Directory.Exists(majBase), Is.True);
    }

    [UnityTest]
    [UnityPlatform(RuntimePlatform.OSXEditor)]
    public IEnumerator UnsupportedRecordingLeavesPlayStateUntouched()
    {
        var playManagerType = Type.GetType("MajdataViewX.Managers.PlayManager, Assembly-CSharp", throwOnError: true)!;
        var wsServerType = Type.GetType("MajdataViewX.WsServer, Assembly-CSharp", throwOnError: true)!;
        var majCtxType = Type.GetType("MajdataViewX.Base.MajCtx, Assembly-CSharp", throwOnError: true)!;
        var state = playManagerType.GetField("_state", BindingFlags.NonPublic | BindingFlags.Static)!;
        var wsServer = majCtxType.GetProperty("_wsServer", BindingFlags.Public | BindingFlags.Static)!;
        var loaded = Enum.Parse(state.FieldType, "Loaded");
        var previousState = state.GetValue(null);
        var previousWsServer = wsServer.GetValue(null);
        var wsServerObject = new GameObject("Test WsServer");
        var playManagerObject = new GameObject(nameof(UnsupportedRecordingLeavesPlayStateUntouched));
        playManagerObject.SetActive(false);

        try
        {
            wsServerObject.AddComponent(wsServerType);
            var playManager = playManagerObject.AddComponent(playManagerType);
            state.SetValue(null, loaded);

            var playAsync = playManagerType.GetMethod("PlayAsync")!;
            var record = Enum.Parse(playAsync.GetParameters()[0].ParameterType, "Record");
            var task = playAsync.Invoke(playManager, new[] { record, 0d, 1f, string.Empty })!;
            var awaiter = task.GetType().GetMethod("GetAwaiter")!.Invoke(task, null)!;
            var isCompleted = awaiter.GetType().GetProperty("IsCompleted")!;
            while (!(bool)isCompleted.GetValue(awaiter)!)
                yield return null;
            awaiter.GetType().GetMethod("GetResult")!.Invoke(awaiter, null);

            Assert.That(state.GetValue(null), Is.EqualTo(loaded));
        }
        finally
        {
            state.SetValue(null, previousState);
            wsServer.SetValue(null, previousWsServer);
            UnityEngine.Object.DestroyImmediate(playManagerObject);
            UnityEngine.Object.DestroyImmediate(wsServerObject);
        }
    }
}
