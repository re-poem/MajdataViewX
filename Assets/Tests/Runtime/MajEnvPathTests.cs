using System;
using System.IO;
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
    }
}
