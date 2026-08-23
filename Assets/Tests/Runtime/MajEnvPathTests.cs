using System;
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

    [Test]
    [UnityPlatform(RuntimePlatform.OSXEditor)]
    public void UnsupportedRecordingLeavesPlayStateUntouched()
    {
        var playManagerType = Type.GetType("MajdataViewX.Managers.PlayManager, Assembly-CSharp", throwOnError: true)!;
        var wsServerType = Type.GetType("MajdataViewX.WsServer, Assembly-CSharp", throwOnError: true)!;
        var state = playManagerType.GetField("_state", BindingFlags.NonPublic | BindingFlags.Static)!;
        var loaded = Enum.Parse(state.FieldType, "Loaded");
        var previousState = state.GetValue(null);
        var gameObject = new GameObject(nameof(UnsupportedRecordingLeavesPlayStateUntouched));

        try
        {
            gameObject.AddComponent(wsServerType);
            var playManager = gameObject.AddComponent(playManagerType);
            state.SetValue(null, loaded);

            var playAsync = playManagerType.GetMethod("PlayAsync")!;
            var record = Enum.Parse(playAsync.GetParameters()[0].ParameterType, "Record");
            playAsync.Invoke(playManager, new[] { record, 0d, 1f, string.Empty });

            Assert.That(state.GetValue(null), Is.EqualTo(loaded));
        }
        finally
        {
            state.SetValue(null, previousState);
            LogAssert.ignoreFailingMessages = true;
            UnityEngine.Object.DestroyImmediate(gameObject);
            LogAssert.ignoreFailingMessages = false;
        }
    }
}
