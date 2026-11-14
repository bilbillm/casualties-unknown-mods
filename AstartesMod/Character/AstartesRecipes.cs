using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstartesMod;

internal static class AstartesRecipes
{
    private const string SaveKey = "casualtiesunknown.astartes-recipes";
    private const Recipes.RecipeCategory Utilities = Recipes.RecipeCategory.Utilities;
    private const Recipes.RecipeCategory Tools = Recipes.RecipeCategory.Tools;
    private static Recipe[] registered = Array.Empty<Recipe>();

    [HarmonyPatch(typeof(Recipes), nameof(Recipes.SetUpRecipes))]
    private static class RegisterPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            registered = new[]
            {
                Define(AstartesBoltgun.MagazineId, 14, Utilities, 1, 0, false,
                    ("magazinebase", 1), ("scrappanel", 2), ("processedcopper", 1)),
                Define(AstartesBoltgun.RoundId, 14, Utilities, 5, 0, false,
                    ("casing", 5), ("flammablepowder", 1), ("scrappanel", 1)),
                Define(AstartesGear.ShouldersId, 16, Utilities, 1, 20, true,
                    ("titaniumsheet", 2), ("canvas", 1), ("plasticchunk", 1)),
                Define(AstartesGear.ArmsId, 17, Utilities, 1, 20, true,
                    ("titaniumsheet", 2), ("titaniumrod", 1), ("canvas", 1), ("bundleofwires", 1)),
                Define(AstartesGear.HelmetId, 18, Utilities, 1, 20, true,
                    ("riothelmet", 1), ("titaniumsheet", 1), ("flexiglass", 1), ("circuitboard", 1), ("bundleofwires", 1)),
                Define(AstartesGear.SwordId, 18, Tools, 1, 30, true,
                    ("titaniummachete", 1), ("titaniumsheet", 2), ("titaniumrod", 1), ("mediumbattery", 1), ("bundleofwires", 1)),
                Define(AstartesGear.CuirassId, 19, Utilities, 1, 40, true,
                    ("titaniumsheet", 5), ("titaniumrod", 2), ("canvas", 2)),
                Define(AstartesGear.LegsId, 19, Utilities, 1, 30, true,
                    ("titaniumsheet", 4), ("titaniumrod", 2), ("canvas", 2), ("bundleofwires", 2)),
                Define(AstartesGear.PackId, 20, Utilities, 1, 30, true,
                    ("titaniumsheet", 2), ("largebattery", 1), ("lantern", 1), ("circuitboard", 1), ("bundleofwires", 2)),
                Define(AstartesBoltgun.GunId, 20, Tools, 1, 30, true,
                    ("makeshiftrifle", 1), ("titaniumrod", 2), ("titaniumsheet", 2), ("scraptube", 2), ("circuitboard", 1))
            };

            foreach (var recipe in registered)
            {
                recipe.index = Recipes.recipes.Count;
                Recipes.recipes.Add(recipe);
            }
        }
    }

    private static Recipe Define(string result, int intelligence, Recipes.RecipeCategory category, int amount, int biochem,
        bool requiresCutting, params (string Id, int Count)[] materials)
    {
        var items = new List<RecipeItem>();
        foreach (var (id, count) in materials)
        {
            float condition = id is "riothelmet" or "titaniummachete" or "makeshiftrifle" ? 0.5f : 0f;
            for (int i = 0; i < count; i++)
                items.Add(new RecipeItem(condition) { specific = true, specificId = id, ignoredId = result });
        }
        if (biochem > 0)
            items.Add(new RecipeItem(biochem) { specific = true, specificId = "biochem", isLiquid = true });

        items.Add(Tool("hammering", result));
        if (requiresCutting)
            items.Add(Tool("cutting", result));

        return new Recipe
        {
            INT = intelligence, category = category, items = items,
            result = new RecipeResult { id = result, amount = amount }
        };
    }

    private static RecipeItem Tool(string quality, string result) => new(0.1f)
    {
        quality = new CraftingQuality(quality), destroyItem = false, ignoredId = result
    };

    internal static void SaveProgress(JObject save)
    {
        var progress = new JObject();
        foreach (var recipe in registered)
        {
            if (recipe.hasMadeBefore || recipe.INT == 0)
            {
                progress[recipe.result.id] = new JObject
                {
                    ["made"] = recipe.hasMadeBefore,
                    ["blueprint"] = recipe.INT == 0
                };
            }
        }
        save[SaveKey] = progress;

        // 原版的配方数组按下标存储。把末尾的模组进度按物品 ID 单独保存。
        if (save["savedRecipeData"]?["saved"] is not JArray native)
            return;
        for (int i = registered.Length - 1; i >= 0 && registered[i].index == native.Count - 1; i--)
            native.RemoveAt(native.Count - 1);
    }

    internal static void LoadProgress(JObject save)
    {
        if (save[SaveKey] is not JObject progress)
            return;
        foreach (var recipe in registered)
        {
            if (progress[recipe.result.id] is not JObject entry)
                continue;
            recipe.hasMadeBefore = entry["made"] is JValue { Value: true };
            if (entry["blueprint"] is JValue { Value: true })
                recipe.INT = 0;
        }
    }

    [HarmonyPatch(typeof(Recipe), "get_resultSprite")]
    private static class PreviewPatch
    {
        private static bool Prefix(Recipe __instance, ref (Sprite, Color) __result)
        {
            var prefab = AstartesPlugin.Gear.LoadCustomPrefab(__instance.result.id);
            if (!prefab)
                return true;
            __result = (prefab!.GetComponent<SpriteRenderer>().sprite, Color.white);
            return false;
        }
    }

    [HarmonyPatch(typeof(RecipeResult), nameof(RecipeResult.SpawnResult))]
    private static class SpawnPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var create = AccessTools.Method(typeof(Utils), nameof(Utils.Create),
                new[] { typeof(string), typeof(Vector2), typeof(float) });
            var replacement = AccessTools.Method(typeof(SpawnPatch), nameof(CreateResult));
            bool found = false;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(create))
                {
                    instruction.operand = replacement;
                    found = true;
                }
                yield return instruction;
            }
            if (!found)
                throw new InvalidOperationException("找不到原版配方产物的创建方法。");
        }

        private static GameObject CreateResult(string id, Vector2 position, float rotation)
        {
            var prefab = AstartesPlugin.Gear.LoadCustomPrefab(id);
            if (!prefab)
                return Utils.Create(id, position, rotation);

            var result = Object.Instantiate(prefab!, position, Quaternion.Euler(0f, 0f, rotation));
            AstartesBoltgun.ClearCraftedAmmo(id, result);
            return result;
        }
    }
}
