using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Emit;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace AstartesMod;

internal sealed class AstartesVoice : IDisposable
{
    private static readonly Dictionary<string, List<string>> chineseLines = LoadLines("zh-CN");
    private static readonly Dictionary<string, List<string>> englishLines = LoadLines("EN");

    internal AstartesVocalSounds Sounds { get; } = new();
    internal static bool UsesChinese => Locale.currentLangName?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true;

    private static Dictionary<string, List<string>> LoadLines(string language)
    {
        var assembly = typeof(AstartesVoice).Assembly;
        using var stream = assembly.GetManifestResourceStream($"AstartesMod.Dialogue.{language}.json") ??
            throw new InvalidDataException($"缺少阿斯塔特台词资源 {language}。");
        using var reader = new StreamReader(stream);
        return JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(reader.ReadToEnd()) ??
            throw new InvalidDataException($"阿斯塔特台词表 {language} 为空。");
    }

    public void Dispose() => Sounds.Dispose();

    [HarmonyPatch(typeof(Locale), nameof(Locale.GetCharacter), typeof(string))]
    private static class PlayerLinesPatch
    {
        private static bool Prefix(string str, ref List<string> __result)
        {
            if (!PlayerCamera.main || !AstartesBody.IsAstartes(PlayerCamera.main.body))
                return true;

            var lines = UsesChinese ? chineseLines : englishLines;
            if (!lines.TryGetValue(str, out var variants))
                return true;

            __result = variants;
            return false;
        }
    }

    [HarmonyPatch(typeof(Talker), "Update")]
    private static class SpeechSoundPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var play = AccessTools.Method(typeof(Sound), nameof(Sound.Play), new[]
            {
                typeof(string), typeof(Vector2), typeof(bool), typeof(bool), typeof(Transform),
                typeof(float), typeof(float), typeof(bool), typeof(bool)
            });
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(play))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0)
                        .MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                    instruction.operand = AccessTools.Method(typeof(SpeechSoundPatch), nameof(PlaySpeech));
                }
                yield return instruction;
            }
        }

        private static AudioSource PlaySpeech(string sound, Vector2 position, bool twoDimensional, bool pitchShift,
            Transform follow, float volume, float pitch, bool noReverb, bool ignoreMixer, Talker talker)
        {
            if (sound is "speech" or "speechbad" && AstartesBody.IsAstartes(talker.body))
            {
                var clip = AstartesPlugin.Voice.Sounds.Syllable(talker.body);
                bool muffled = sound == "speechbad";
                return Sound.Play(clip, position, twoDimensional, false, follow,
                    volume * (muffled ? 0.42f : 0.58f), muffled ? 0.92f : 1f, noReverb, ignoreMixer);
            }
            return Sound.Play(sound, position, twoDimensional, pitchShift, follow, volume, pitch, noReverb, ignoreMixer);
        }
    }

    [HarmonyPatch(typeof(WorldGeneration), "FinishWorldGeneration")]
    private static class LandingLinePatch
    {
        private static void Postfix(WorldGeneration __instance, ref IEnumerator __result)
        {
            if (__instance.biomeDepth == 0 && __instance.biomeOverride == WorldGeneration.OverrideSceneType.None &&
                !SaveSystem.loadedRun && AstartesBody.IsAstartes(PlayerCamera.main.body))
                __result = SpeakAfterLanding(__result, PlayerCamera.main.body);
        }

        private static IEnumerator SpeakAfterLanding(IEnumerator finish, Body body)
        {
            yield return finish;
            if (body && body.talker)
                body.talker.TalkDelayed(0.65f, Locale.GetCharacter("astartesLanding"), layerRestrict: 0);
        }
    }
}
