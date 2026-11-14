using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstartesMod;

internal sealed class AstartesVocalSounds : IDisposable
{
    private const int SampleRate = 24000;

    private static readonly string[] cueNames =
    {
        "pant", "pain1", "pain2", "pain3", "yawn1", "yawn2",
        "exert1", "exert2", "exert3", "exert4", "fall", "frustration", "stretch", "gearshake",
        "swallow", "retch1", "retch2", "nausea", "nausea-blood",
        "speech1", "speech2", "speech3", "speech4"
    };

    private readonly SoundSet unhelmeted;
    private readonly SoundSet helmeted;
    private readonly AudioClip warCry;
    private int nextSyllable;

    internal AstartesVocalSounds()
    {
        var samples = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (string cue in cueNames)
            samples.Add(cue, ReadSamples(cue));

        warCry = CreateClip("Astartes War Cry", ReadSamples("warcry-en"));
        unhelmeted = new SoundSet(samples, radio: false);
        helmeted = new SoundSet(samples, radio: true);
    }

    public void Dispose()
    {
        Object.Destroy(warCry);
        unhelmeted.Dispose();
        helmeted.Dispose();
    }

    private SoundSet For(Body body) => AstartesGear.HasHelmet(body) ? helmeted : unhelmeted;

    internal AudioClip Syllable(Body body)
    {
        var speech = For(body).Speech;
        var clip = speech[nextSyllable];
        nextSyllable = (nextSyllable + 1) % speech.Length;
        return clip;
    }

    private static float[] ReadSamples(string cue)
    {
        using var stream = typeof(AstartesVocalSounds).Assembly.GetManifestResourceStream($"AstartesMod.Audio.{cue}.wav") ??
            throw new InvalidDataException($"找不到阿斯塔特音效 {cue}。");
        using var reader = new BinaryReader(stream);
        if (new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException($"阿斯塔特音效 {cue} 缺少 RIFF 文件头。");
        reader.ReadUInt32();
        if (new string(reader.ReadChars(8)) != "WAVEfmt " || reader.ReadUInt32() != 16 ||
            reader.ReadUInt16() != 1 || reader.ReadUInt16() != 1 || reader.ReadUInt32() != SampleRate)
            throw new InvalidDataException($"阿斯塔特音效 {cue} 应为 24 kHz 单声道 PCM。");
        reader.ReadUInt32();
        reader.ReadUInt16();
        if (reader.ReadUInt16() != 16 || new string(reader.ReadChars(4)) != "data")
            throw new InvalidDataException($"阿斯塔特音效 {cue} 应为 16 位 PCM。");

        int bytes = reader.ReadInt32();
        if (bytes <= 0 || bytes % sizeof(short) != 0 || bytes != stream.Length - stream.Position)
            throw new InvalidDataException($"阿斯塔特音效 {cue} 的样本长度无效。");

        var samples = new float[bytes / sizeof(short)];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = reader.ReadInt16() / 32768f;
        return samples;
    }

    private static float[] FilterHelmet(float[] samples)
    {
        var output = new float[samples.Length];
        float low = 0f;
        float radio = 0f;
        const int echoSamples = 228;
        for (int i = 0; i < samples.Length; i++)
        {
            low += 0.018f * (samples[i] - low);
            radio += 0.46f * (samples[i] - low - radio);
            float voice = 0.45f * samples[i] + 0.65f * radio;
            float echo = i >= echoSamples ? output[i - echoSamples] * 0.08f : 0f;
            output[i] = MathF.Tanh(voice * 1.30f) * 0.86f + echo;
        }
        return output;
    }

    private static AudioClip CreateClip(string name, float[] samples)
    {
        var clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private sealed class SoundSet : IDisposable
    {
        private readonly Dictionary<string, AudioClip> clips = new(StringComparer.Ordinal);
        internal AudioClip[] Pain { get; }
        internal AudioClip[] Yawn { get; }
        internal AudioClip[] Speech { get; }
        internal AudioClip this[string cue] => clips[cue];

        internal SoundSet(Dictionary<string, float[]> sources, bool radio)
        {
            foreach (var entry in sources)
            {
                var samples = radio ? FilterHelmet(entry.Value) : entry.Value;
                clips.Add(entry.Key, CreateClip($"Astartes {(radio ? "Vox" : "Voice")} {entry.Key}", samples));
            }
            Pain = new[] { clips["pain1"], clips["pain2"], clips["pain3"] };
            Yawn = new[] { clips["yawn1"], clips["yawn2"] };
            Speech = new[] { clips["speech1"], clips["speech2"], clips["speech3"], clips["speech4"] };
        }

        public void Dispose()
        {
            foreach (var clip in clips.Values)
                Object.Destroy(clip);
        }
    }

    private void ApplyBreathingSounds(PantSound component, Body body, AudioSource source)
    {
        var set = For(body);
        var pant = set["pant"];
        if (source.clip == pant)
            return;

        component.painSounds = set.Pain;
        component.yawnSounds = set.Yawn;
        source.clip = pant;
        source.Play();
    }

    [HarmonyPatch(typeof(PantSound), "Start")]
    private static class PantStartPatch
    {
        private static void Postfix(PantSound __instance, Body ___body, AudioSource ___source)
        {
            if (AstartesBody.IsAstartes(___body))
                AstartesPlugin.Voice.Sounds.ApplyBreathingSounds(__instance, ___body, ___source);
        }
    }

    [HarmonyPatch(typeof(PantSound), "Update")]
    private static class PantUpdatePatch
    {
        private static void Prefix(PantSound __instance, Body ___body, AudioSource ___source)
        {
            if (AstartesBody.IsAstartes(___body))
                AstartesPlugin.Voice.Sounds.ApplyBreathingSounds(__instance, ___body, ___source);
        }

        private static void Postfix(Body ___body, AudioSource ___source)
        {
            if (AstartesBody.IsAstartes(___body))
                ___source.pitch = Mathf.Lerp(1.04f, 0.96f, ___body.stamina / 70f);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var play = AccessTools.Method(typeof(Sound), nameof(Sound.Play), new[]
            {
                typeof(AudioClip), typeof(Vector2), typeof(bool), typeof(bool), typeof(Transform),
                typeof(float), typeof(float), typeof(bool), typeof(bool)
            });
            var playReaction = AccessTools.Method(typeof(PantUpdatePatch), nameof(PlayReaction));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(play))
                    instruction.operand = playReaction;
                yield return instruction;
            }
        }

        private static AudioSource PlayReaction(AudioClip clip, Vector2 position, bool twoDimensional, bool pitchShift,
            Transform follow, float volume, float pitch, bool noReverb, bool ignoreMixer)
        {
            // 原版用发声者的 Transform 作为跟随对象。
            if (follow && follow.TryGetComponent<AstartesBody>(out _))
                return Sound.Play(clip, position, twoDimensional, false, follow, volume, 1f, noReverb, ignoreMixer);
            return Sound.Play(clip, position, twoDimensional, pitchShift, follow, volume, pitch, noReverb, ignoreMixer);
        }
    }

    [HarmonyPatch(typeof(PantSound), nameof(PantSound.Bark))]
    private static class WarCryPatch
    {
        private static bool Prefix(Body ___body)
        {
            if (!___body || !___body.TryGetComponent<AstartesBody>(out var astartes))
                return true;
            if (!___body.conscious || Time.unscaledTime < astartes.NextWarCryTime)
                return false;

            var clip = AstartesPlugin.Voice.Sounds.warCry;
            astartes.NextWarCryTime = Time.unscaledTime + clip.length + 0.25f;
            ___body.eatTime = Mathf.Max(___body.eatTime, 0.5f);
            Sound.Play(clip, ___body.transform.position, pitchShift: false, follow: ___body.transform, volume: 0.92f);
            return false;
        }
    }

    [HarmonyPatch(typeof(Locale), nameof(Locale.GetOther), typeof(string))]
    private static class WarCryBindingLabelPatch
    {
        private static void Postfix(string str, ref string __result)
        {
            if (str == "gamesetbark" && PlayerCamera.main && AstartesBody.IsAstartes(PlayerCamera.main.body))
                __result = AstartesVoice.UsesChinese ? "战吼" : "War Cry";
        }
    }

    [HarmonyPatch(typeof(Body), nameof(Body.Scream))]
    private static class FallScreamPatch
    {
        private static void Postfix(Body __instance)
        {
            if (!__instance.conscious || Body.censorPain || !__instance.TryGetComponent<AstartesBody>(out var astartes) ||
                Time.unscaledTime < astartes.NextScreamTime)
                return;

            astartes.NextScreamTime = Time.unscaledTime + 1f;
            Sound.Play(AstartesPlugin.Voice.Sounds.For(__instance)["fall"], __instance.transform.position,
                pitchShift: false, follow: __instance.transform, volume: 0.68f);
        }
    }

    [HarmonyPatch]
    private static class PlayerCuePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Body), nameof(Body.TryExertSound));
            yield return AccessTools.Method(typeof(Body), "HandleVisuals");
            yield return AccessTools.Method(typeof(PantSound), nameof(PantSound.TryGrowl));
            yield return AccessTools.Method(typeof(Vomiter), nameof(Vomiter.Vomit));
            yield return AccessTools.Method(typeof(Vomiter), nameof(Vomiter.VomitBlood));
            yield return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(Body), "NapCoroutine"));
            yield return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(Body), "WaterShake"));
            yield return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(Vomiter), "DoVomit"));
            yield return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(Vomiter), "DoBloodVomit"));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var play = AccessTools.Method(typeof(Sound), nameof(Sound.Play), new[]
            {
                typeof(string), typeof(Vector2), typeof(bool), typeof(bool), typeof(Transform),
                typeof(float), typeof(float), typeof(bool), typeof(bool)
            });
            var playCue = AccessTools.Method(typeof(PlayerCuePatch), nameof(PlayCue));
            var owner = original.Name == "MoveNext" ? AccessTools.Field(original.DeclaringType, "<>4__this") : null;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(play))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0)
                        .MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                    if (owner != null)
                        yield return new CodeInstruction(OpCodes.Ldfld, owner);
                    instruction.operand = playCue;
                }
                yield return instruction;
            }
        }

        private static AudioSource PlayCue(string sound, Vector2 position, bool twoDimensional, bool pitchShift,
            Transform follow, float volume, float pitch, bool noReverb, bool ignoreMixer, MonoBehaviour emitter)
        {
            string? cue = sound switch
            {
                "dogshake" => "gearshake",
                "burp" => "swallow",
                "stretch" or "exert1" or "exert2" or "exert3" or "exert4" => sound,
                "vomit1" => "retch1",
                "vomit2" => "retch2",
                "vomitwarning" => "nausea",
                "bloodvomitwarning" => "nausea-blood",
                _ when sound.StartsWith("growl", StringComparison.Ordinal) => "frustration",
                _ => null
            };
            var body = cue != null ? emitter.GetComponent<Body>() : null;
            if (body is not null && AstartesBody.IsAstartes(body))
            {
                var clip = AstartesPlugin.Voice.Sounds.For(body)[cue!];
                return Sound.Play(clip, position, twoDimensional, false, follow, volume, 1f, noReverb, ignoreMixer);
            }
            return Sound.Play(sound, position, twoDimensional, pitchShift, follow, volume, pitch, noReverb, ignoreMixer);
        }
    }
}
