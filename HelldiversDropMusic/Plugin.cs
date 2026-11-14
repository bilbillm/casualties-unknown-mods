using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

[assembly: AssemblyTitle(HelldiversDropMusic.HelldiversDropMusicPlugin.PluginName)]
[assembly: AssemblyProduct(HelldiversDropMusic.HelldiversDropMusicPlugin.PluginName)]
[assembly: AssemblyCopyright("Copyright (c) 2026 lumoren")]
[assembly: AssemblyVersion(HelldiversDropMusic.HelldiversDropMusicPlugin.PluginVersion)]
[assembly: AssemblyFileVersion(HelldiversDropMusic.HelldiversDropMusicPlugin.PluginVersion)]

namespace HelldiversDropMusic;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class HelldiversDropMusicPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "casualtiesunknown.helldivers-drop-music";
    public const string PluginName = "Helldivers Drop Music";
    public const string PluginVersion = "1.0.0";

    private static readonly string[] DefaultAudioFileNames =
    {
        "drop_music.ogg",
        "drop_music.mp3",
        "drop_music.wav"
    };

    internal static HelldiversDropMusicPlugin Instance = null!;
    internal static ConfigEntry<bool> Enabled = null!;
    internal static ConfigEntry<string> AudioFileName = null!;
    internal static ConfigEntry<float> VolumeMultiplier = null!;

    private readonly Dictionary<int, float> _originalVolumes = new Dictionary<int, float>();
    private Harmony _harmony = null!;
    private AudioClip? _dropClip;
    private string? _dropClipPath;
    private bool _loadFinished;

    private void Awake()
    {
        Instance = this;
        Enabled = Config.Bind("General", "Enabled", true, "Enable drop pod music replacement.");
        AudioFileName = Config.Bind("Audio", "AudioFileName", string.Empty, "Audio file name in this plugin directory. Leave empty to try drop_music.ogg/mp3/wav.");
        VolumeMultiplier = Config.Bind("Audio", "VolumeMultiplier", 1.0f, "Volume multiplier applied to the original drop pod AudioSource volume.");

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(typeof(HelldiversDropMusicPlugin).Assembly);

        Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        StartCoroutine(LoadConfiguredClip());
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        Instance = null!;
    }

    internal void ApplyToWorld(WorldGeneration world)
    {
        if (!Enabled.Value || world == null)
        {
            return;
        }

        if (!_loadFinished || _dropClip == null)
        {
            return;
        }

        var source = world.genPodSource;
        if (source == null)
        {
            return;
        }

        var instanceId = source.GetInstanceID();
        if (!_originalVolumes.ContainsKey(instanceId))
        {
            _originalVolumes[instanceId] = source.volume;
        }

        var wasPlaying = source.isPlaying;
        if (source.clip != _dropClip)
        {
            source.clip = _dropClip;
            Logger.LogInfo("Applied custom drop pod music to WorldGeneration.genPodSource: " + Path.GetFileName(_dropClipPath ?? string.Empty));

            if (wasPlaying)
            {
                source.Stop();
                source.Play();
            }
        }

        source.volume = _originalVolumes[instanceId] * Mathf.Max(0f, VolumeMultiplier.Value);
    }

    private IEnumerator LoadConfiguredClip()
    {
        _loadFinished = false;
        _dropClip = null;
        _dropClipPath = ResolveAudioPath();

        if (_dropClipPath == null)
        {
            _loadFinished = true;
            Logger.LogWarning("No drop music file found. Put drop_music.ogg, drop_music.mp3, or drop_music.wav next to HelldiversDropMusic.dll.");
            yield break;
        }

        var audioType = GetAudioType(_dropClipPath);
        if (audioType == AudioType.UNKNOWN)
        {
            _loadFinished = true;
            Logger.LogWarning("Unsupported drop music extension: " + _dropClipPath);
            yield break;
        }

        var uri = new Uri(_dropClipPath).AbsoluteUri;
        using var request = UnityWebRequestMultimedia.GetAudioClip(uri, audioType);
        yield return request.SendWebRequest();

        if (HasRequestError(request))
        {
            _loadFinished = true;
            Logger.LogWarning("Failed to load drop music file: " + _dropClipPath + " | " + request.error);
            yield break;
        }

        var clip = DownloadHandlerAudioClip.GetContent(request);
        if (clip == null)
        {
            _loadFinished = true;
            Logger.LogWarning("Failed to decode drop music file: " + _dropClipPath);
            yield break;
        }

        clip.name = Path.GetFileNameWithoutExtension(_dropClipPath);
        _dropClip = clip;
        _loadFinished = true;
        Logger.LogInfo("Loaded drop music file: " + _dropClipPath);
    }

    private string? ResolveAudioPath()
    {
        var pluginDir = Path.GetDirectoryName(typeof(HelldiversDropMusicPlugin).Assembly.Location);
        if (string.IsNullOrWhiteSpace(pluginDir))
        {
            return null;
        }

        var configuredName = AudioFileName.Value.Trim();
        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            var configuredPath = Path.Combine(pluginDir, configuredName);
            return File.Exists(configuredPath) ? configuredPath : null;
        }

        return DefaultAudioFileNames
            .Select(name => Path.Combine(pluginDir, name))
            .FirstOrDefault(File.Exists);
    }

    private static AudioType GetAudioType(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".ogg" => AudioType.OGGVORBIS,
            ".mp3" => AudioType.MPEG,
            ".wav" => AudioType.WAV,
            _ => AudioType.UNKNOWN
        };
    }

    private static bool HasRequestError(UnityWebRequest request)
    {
#pragma warning disable CS0618
        return request.isNetworkError || request.isHttpError;
#pragma warning restore CS0618
    }
}

[HarmonyPatch(typeof(WorldGeneration), "Start")]
internal static class WorldGenerationStartPatch
{
    private static void Postfix(WorldGeneration __instance)
    {
        HelldiversDropMusicPlugin.Instance?.ApplyToWorld(__instance);
    }
}

[HarmonyPatch(typeof(WorldGeneration), "Update")]
internal static class WorldGenerationUpdatePatch
{
    private static void Postfix(WorldGeneration __instance)
    {
        HelldiversDropMusicPlugin.Instance?.ApplyToWorld(__instance);
    }
}
