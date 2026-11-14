using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AstartesMod;

internal static class AstartesRun
{
    internal const string CharacterId = "astartes";
    internal const int MenuSelection = -1;
    private const string SaveKey = "casualtiesunknown.astartes-mod";
    private const string OrgansKey = "casualtiesunknown.astartes-organs";
    private static bool pendingSpawn;

    [HarmonyPatch(typeof(PreRunScript), nameof(PreRunScript.WaitLoad))]
    private static class PrepareRunPatch
    {
        private static void Prefix()
        {
            pendingSpawn = !SaveSystem.loadedRun && PreRunScript.characterSelected == MenuSelection;

            // 过场、对白和原版存档继续使用实验体兼容索引。
            if (pendingSpawn)
                PreRunScript.characterSelected = 0;
        }
    }

    [HarmonyPatch(typeof(PlayerCamera), "Awake")]
    private static class SpawnPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceCall(instructions,
                AccessTools.Method(typeof(PlayerCamera), nameof(PlayerCamera.SpawnCharacter)),
                AccessTools.Method(typeof(AstartesRun), nameof(SpawnSelectedCharacter)));
        }
    }

    private static GameObject SpawnSelectedCharacter(PlayerCamera camera, int vanillaCharacter)
    {
        bool spawnAstartes = pendingSpawn && !SaveSystem.loadedRun;
        pendingSpawn = false;
        if (!spawnAstartes)
            return camera.SpawnCharacter(vanillaCharacter);

        var actor = Utils.Create("Special/Characters/Experiment", Vector2.zero, 0f);
        actor.name = "Astartes";
        var body = actor.GetComponentInChildren<Body>();
        body.gameObject.AddComponent<AstartesBody>().Initialize(body, newRun: true);
        AstartesPlugin.Log.LogInfo("阿斯塔特新局已生成。");
        return actor;
    }

    [HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.TryLoadGame))]
    private static class LoadIdentityPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceCall(instructions,
                AccessTools.Method(typeof(JObject), nameof(JObject.Parse), new[] { typeof(string) }),
                AccessTools.Method(typeof(AstartesRun), nameof(ParseLoadedCharacter)));
        }
    }

    private static JObject ParseLoadedCharacter(string json)
    {
        // QoL Unknown 的换层绕过菜单，存档槽也可能在场景加载后才替换。使用原版实际恢复的存档。
        var save = JObject.Parse(json);
        var body = PlayerCamera.main.body;
        if (body.charType != 0 || !SaveBelongsToAstartes(save))
            return save;

        if (!body.TryGetComponent<AstartesBody>(out var astartes))
        {
            body.transform.root.name = "Astartes";
            astartes = body.gameObject.AddComponent<AstartesBody>();
            astartes.Initialize(body, newRun: false);
            AstartesPlugin.Log.LogInfo("已从存档恢复阿斯塔特身份。");
        }
        var organs = save[OrgansKey] as JObject;
        astartes.SecondHeartSpent = organs?["secondHeartSpent"] is JValue { Value: true };
        astartes.FurnaceSpent = organs?["furnaceSpent"] is JValue { Value: true };
        return save;
    }

    private static bool SaveBelongsToAstartes(JObject save)
    {
        if (save[SaveKey] is JToken identity)
            return identity.Type == JTokenType.String && (string?)identity == CharacterId;

        // 旧版丢失标记后，穿戴中的独有动力甲可用于识别该局。
        return save["items"] is JArray items && items.OfType<JObject>().Any(item =>
            item["slot"]?.Type == JTokenType.Integer && (int?)item["slot"] == -1 &&
            item["id"]?.Type == JTokenType.String && AstartesGear.IsPowerArmour((string?)item["id"]));
    }

    [HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.SaveGame))]
    private static class SaveIdentityPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceCall(instructions,
                AccessTools.Method(typeof(SaveSystem), nameof(SaveSystem.Zip)),
                AccessTools.Method(typeof(AstartesRun), nameof(ZipWithCharacter)));
        }
    }

    private static byte[] ZipWithCharacter(string json)
    {
        if (!PlayerCamera.main.body.TryGetComponent<AstartesBody>(out var astartes))
            return SaveSystem.Zip(json);

        var save = ParseSave(json);
        save[SaveKey] = CharacterId;
        save[OrgansKey] = new JObject
        {
            ["secondHeartSpent"] = astartes.SecondHeartSpent,
            ["furnaceSpent"] = astartes.FurnaceSpent
        };
        return SaveSystem.Zip(save.ToString(Formatting.None));
    }

    [HarmonyPatch(typeof(WorldGeneration), "SetGameWon")]
    private static class WinPatch
    {
        private static bool Prefix() => !AstartesBody.IsAstartes(PlayerCamera.main.body);
    }

    private static JObject ParseSave(string json)
    {
        using var text = new StringReader(json);
        using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None };
        return JObject.Load(reader);
    }

    private static List<CodeInstruction> ReplaceCall(IEnumerable<CodeInstruction> instructions,
        MethodInfo original, MethodInfo replacement)
    {
        var result = instructions.ToList();
        var matches = result.Where(instruction => instruction.Calls(original)).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"找不到唯一的 {original.DeclaringType?.Name}.{original.Name} 调用。");

        matches[0].opcode = OpCodes.Call;
        matches[0].operand = replacement;
        return result;
    }
}
