using System;
using Game.Framework.Story;
using Game.Framework.Story.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class StoryAuthoringTests
{
    [Test]
    public void InspectorStoryAssetsSurviveSaveUnloadAndReimport()
    {
        string folder = "Assets/NativeStoryAuthoring_" + Guid.NewGuid().ToString("N");
        AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
        try
        {
            var sequence = ScriptableObject.CreateInstance<StorySequenceAsset>();
            sequence.sequenceId = "persisted-story";
            sequence.version = "content-1";
            sequence.nodes = new[]
            {
                new StoryNodeData { id = "say", kind = StoryNodeKind.Say, textKey = "story.hello" },
                new StoryNodeData { id = "end", kind = StoryNodeKind.End }
            };
            var library = ScriptableObject.CreateInstance<StoryAssetLibrary>();
            library.entries = new[] { new StoryAssetLibrary.Entry { id = "sequence", asset = sequence } };
            var settings = ScriptableObject.CreateInstance<StorySettings>();
            settings.sequence = sequence;
            settings.assets = library;
            settings.charactersPerSecond = 27;
            string sequencePath = folder + "/Sequence.asset";
            string libraryPath = folder + "/Library.asset";
            string settingsPath = folder + "/Settings.asset";
            AssetDatabase.CreateAsset(sequence, sequencePath);
            AssetDatabase.CreateAsset(library, libraryPath);
            AssetDatabase.CreateAsset(settings, settingsPath);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(settings);
            Resources.UnloadAsset(library);
            Resources.UnloadAsset(sequence);
            foreach (string path in new[] { sequencePath, libraryPath, settingsPath })
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }
            var loaded = AssetDatabase.LoadAssetAtPath<StorySettings>(settingsPath);
            Assert.That(loaded, Is.Not.Null, "StorySettings must resolve to its matching MonoScript after disk reimport.");
            Assert.That(MonoScript.FromScriptableObject(loaded).name, Is.EqualTo(nameof(StorySettings)));
            Assert.That(loaded.sequence, Is.Not.Null);
            Assert.That(MonoScript.FromScriptableObject(loaded.sequence).name, Is.EqualTo(nameof(StorySequenceAsset)));
            Assert.That(loaded.assets, Is.Not.Null);
            Assert.That(MonoScript.FromScriptableObject(loaded.assets).name, Is.EqualTo(nameof(StoryAssetLibrary)));
            Assert.That(loaded.charactersPerSecond, Is.EqualTo(27));
            Assert.That(loaded.sequence.Build().Id, Is.EqualTo("persisted-story"));
            Assert.That(loaded.sequence.Build().Version, Is.EqualTo("content-1"));
            Assert.That(loaded.sequence.nodes[0].textKey, Is.EqualTo("story.hello"));
            using (var lease = loaded.assets.LoadAsync<StorySequenceAsset>("sequence", default).GetAwaiter().GetResult())
            {
                Assert.That(lease.Asset, Is.SameAs(loaded.sequence), "The serialized asset reference must survive reimport.");
            }
        }
        finally
        {
            AssetDatabase.DeleteAsset(folder);
        }
    }
}
