using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstartesMod;

internal sealed class AstartesGear : IDisposable
{
    internal const string HelmetId = "astartes_mkx_helmet";
    internal const string CuirassId = "astartes_mkx_cuirass";
    internal const string ShouldersId = "astartes_mkx_shoulders";
    internal const string ArmsId = "astartes_mkx_arms";
    internal const string LegsId = "astartes_mkx_legs";
    internal const string PackId = "astartes_mkx_pack";
    internal const string SwordId = "astartes_chainsword";
    internal const float DurabilityMultiplier = 3f;
    private const float PackRunSpeed = 1.15f;

    internal static bool IsPowerArmour(string? id) =>
        id is HelmetId or CuirassId or ShouldersId or ArmsId or LegsId or PackId;

    private readonly AstartesSprites sprites;
    private readonly Dictionary<string, GearItem> items;
    private GameObject? templates;
    private static bool IsChinese => Locale.currentLangName?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true;

    internal AstartesGear(AstartesSprites sprites)
    {
        this.sprites = sprites;
        var gear = new[]
        {
            Armour(HelmetId, "Mk X 动力头盔", "Mk X Power Helmet",
                "装有红色目镜的陶钢头盔。为头部提供重型防护。",
                "A red-lensed ceramite helmet. Provides heavy head protection.",
                "hat", "Head", "MkXHelmet", 4.5f, 1.0f, 0.16f, 8),
            Armour(CuirassId, "Mk X 胸腹甲", "Mk X Cuirass",
                "饰有金色帝国天鹰的陶钢胸腹甲。覆盖胸腔和腹部。",
                "Ceramite cuirass with a gilded Aquila. Protects the chest and abdomen.",
                "torsofront", "UpTorso", "MkXCuirass", 5.0f, 2.5f, 0.22f, 9, ("DownTorso", "MkXFauld")),
            Armour(ShouldersId, "Mk X 肩甲", "Mk X Pauldrons",
                "成对的金边肩甲，覆盖肩部与上臂。",
                "Paired gilded pauldrons protect the shoulders and upper arms.",
                "astartes_shoulders", "UpArmF", "MkXShoulder", 3.6f, 1.15f, 0.08f, 10, ("UpArmB", "MkXShoulderRear")),
            Armour(ArmsId, "Mk X 臂甲与手甲", "Mk X Vambraces and Gauntlets",
                "分节陶钢护臂与动力手甲。覆盖前臂和双手。",
                "Segmented ceramite bracers and powered gauntlets protect the forearms and hands.",
                "astartes_arms", "DownArmF", "MkXVambrace", 3.0f, 1.15f, 0.08f, 8,
                ("DownArmB", "MkXVambrace"), ("HandF", "MkXGauntlet"), ("HandB", "MkXGauntlet")),
            Armour(LegsId, "Mk X 腿甲与战靴", "Mk X Greaves and Sabatons",
                "覆盖大腿、小腿和脚掌。起跳助力提高 12%。",
                "Protects the thighs, shins and feet. Increases takeoff speed by 12%.",
                "feet", "FootF", "MkXSabaton", 3.5f, 2.0f, 0.18f, 8,
                ("ThighF", "MkXThigh"), ("ThighB", "MkXThigh"), ("CrusF", "MkXGreave"),
                ("CrusB", "MkXGreave"), ("FootB", "MkXSabaton")),
            Armour(PackId, "Mk X 动力背包", "Mk X Power Pack",
                "为伺服系统供电，外侧设有爆弹弹匣舱。穿戴时点亮广域照明灯，跑速提高 15%。",
                "Powers the servos and carries bolt magazines. Wear it to activate the floodlight and increase run speed by 15%.",
                "back", "UpTorso", "MkXPack", 0.65f, 2.2f, 0f, -18),
            new GearItem(SwordId, "machete", "Chainsword", "阿斯塔特链锯剑", "Astartes Chainsword",
                "动力链刃，可撕裂生物和障碍。挥砍命中会消耗耐久。",
                "A motor-driven chainblade that tears through creatures and obstacles. Hits wear down the blade.",
                new ItemInfo
                {
                    category = "tool", slotRotation = -90f, usable = true, usableWithLMB = true,
                    autoAttack = true, destroyAtZeroCondition = true, weight = 2.7f,
                    tags = "tool,backflip", value = 120, secret = true, untradeable = true,
                    rec = new Recognition(0), qualities = new List<CraftingQuality> { new("cutting", 400f) },
                    useAction = SwingChainsword
                }, Array.Empty<(string, string)>()),
            new GearItem(AstartesBoltgun.GunId, "rifle", "Boltgun", "阿斯塔特爆弹枪", "Astartes Boltgun",
                "阿斯塔特制式全自动爆弹枪。使用 60 发专用弹匣。",
                "Astartes-pattern automatic boltgun. Feeds from dedicated 60-round magazines.",
                new ItemInfo
                {
                    category = "utility", slotRotation = -90f, usable = true, usableWithLMB = true,
                    autoAttack = true, destroyAtZeroCondition = true, weight = 3.6f,
                    tags = "gun", value = 220, secret = true, untradeable = true, rec = new Recognition(0),
                    useAction = (_, item) => item.GetComponent<GunScript>().triggerPressed = true
                }, Array.Empty<(string, string)>()),
            new GearItem(AstartesBoltgun.MagazineId, "riflemagazine", "BoltMagazine", "爆弹弹匣", "Bolt Magazine",
                "可装填 60 发爆弹，适用于阿斯塔特爆弹枪。",
                "Holds up to 60 bolts for the Astartes boltgun.",
                new ItemInfo
                {
                    category = "custom", slotRotation = -90f, usable = true, weight = 0.55f,
                    tags = "belttool,astartes_bolt_ammo", value = 70, secret = true, untradeable = true,
                    rec = new Recognition(0), useAction = (_, item) => item.GetComponent<AmmoScript>().UnloadRound()
                }, Array.Empty<(string, string)>()),
            new GearItem(AstartesBoltgun.RoundId, "556round", null, ".75 口径爆弹", ".75 Calibre Bolt",
                "为阿斯塔特爆弹枪制造的穿甲弹，可装入爆弹弹匣。",
                "An armour-piercing round for the Astartes boltgun. Fits a bolt magazine.",
                new ItemInfo
                {
                    category = "custom", weight = 0.045f, tags = "bullet,astartes_bolt_ammo",
                    value = 2, secret = true, untradeable = true, rec = new Recognition(0)
                }, Array.Empty<(string, string)>())
        };
        items = gear.ToDictionary(item => item.Id, StringComparer.Ordinal);
    }

    private static GearItem Armour(string id, string zhName, string enName, string zhDesc, string enDesc,
        string slot, string limb, string sprite, float protection, float weight, float insulation,
        int sortingOffset, params (string Limb, string Sprite)[] secondary)
    {
        var info = new ItemInfo
        {
            category = "custom", wearable = true, desiredWearLimb = limb, wearSlotId = slot,
            wearableArmor = protection, wearableIsolation = insulation, wearableVisualOffset = sortingOffset,
            wearableHitDurabilityLossMultiplier = 0.10f / DurabilityMultiplier, weight = weight, destroyAtZeroCondition = true,
            value = 80, untradeable = true, secret = true, rec = new Recognition(0)
        };
        return new GearItem(id, "riothelmet", sprite, zhName, enName, zhDesc, enDesc, info, secondary);
    }

    private static void SwingChainsword(Body body, Item item)
    {
        if (item.condition <= 0f)
            return;

        var attack = new AttackInfo
        {
            damage = 120f, structuralDamage = 96f, distance = 7.2f, knockBack = 250f,
            // 原版只在命中生物时使用 attackCooldownMult。
            cooldown = 0.40f, attackCooldownMult = 0.66f / 5f, staminaUse = 2.2f, rotateAmount = 18f,
            swingSounds = new[] { "Saw" }, volume = 0.45f,
            attackAnim = Resources.Load<GameObject>("SwingAnim")
        };
        if (body.Attack(attack, body.handSlot))
            item.condition = Mathf.Max(0f, item.condition - 0.0005f / DurabilityMultiplier);
    }

    [HarmonyPatch]
    private static class EquipmentDamagePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(FanTrap), "OnWillRenderObject");
            yield return AccessTools.Method(typeof(SawbladeScript), "OnCollisionEnter2D");
            yield return AccessTools.Method(typeof(WorldGeneration), nameof(WorldGeneration.CreateExplosion));
            yield return AccessTools.Method(typeof(Damageable), nameof(Damageable.Damage));
            yield return AccessTools.Method(typeof(RecipeItem), nameof(RecipeItem.UseItem));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var setCondition = AccessTools.Method(typeof(Item), nameof(Item.SetCondition));
            var condition = AccessTools.Field(typeof(Item), nameof(Item.condition));
            var setAfterDamage = AccessTools.Method(typeof(EquipmentDamagePatch), nameof(SetAfterDamage));
            var storeAfterDamage = AccessTools.Method(typeof(EquipmentDamagePatch), nameof(StoreAfterDamage));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(setCondition))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = setAfterDamage;
                }
                else if (instruction.StoresField(condition))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = storeAfterDamage;
                }
                yield return instruction;
            }
        }

        private static void SetAfterDamage(Item item, float next) => item.SetCondition(AfterDamage(item, next));
        private static void StoreAfterDamage(Item item, float next) => item.condition = AfterDamage(item, next);

        private static float AfterDamage(Item item, float next)
        {
            if (next < item.condition && (IsPowerArmour(item.id) || item.id is SwordId or AstartesBoltgun.GunId))
                return item.condition + (next - item.condition) / DurabilityMultiplier;
            return next;
        }
    }

    internal static bool HasHelmet(Body body) => IsWornAndIntact(body, "hat", HelmetId);

    private static bool IsWornAndIntact(Body body, string slot, string id)
    {
        var piece = body.GetWearableBySlotID(slot);
        return piece && piece.id == id && piece.condition > 0f;
    }

    [HarmonyPatch(typeof(Body), nameof(Body.actualMaxSpeed), MethodType.Getter)]
    private static class PackServoPatch
    {
        private static void Postfix(Body __instance, ref float __result)
        {
            if (__instance.limbs != null && IsWornAndIntact(__instance, "back", PackId))
                __result *= PackRunSpeed;
        }
    }

    [HarmonyPatch(typeof(Body), nameof(Body.Jump))]
    private static class LegServoPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var speed = AccessTools.PropertyGetter(typeof(Body), nameof(Body.actualJumpSpeed));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(speed))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(LegServoPatch), nameof(PoweredJumpSpeed));
                }
                yield return instruction;
            }
        }

        private static float PoweredJumpSpeed(Body body) =>
            body.actualJumpSpeed * (IsWornAndIntact(body, "feet", LegsId) ? 1.12f : 1f);
    }

    private void RegisterItems()
    {
        bool chinese = IsChinese;
        foreach (var item in items.Values)
        {
            item.Info.fullName = chinese ? item.ZhName : item.EnName;
            item.Info.description = chinese ? item.ZhDesc : item.EnDesc;
            item.Info.SetTags();
            Item.GlobalItems.Add(item.Id, item.Info);
        }
    }

    private bool TryGetLocalizedText(string key, out string text)
    {
        bool description = key.EndsWith("dsc", StringComparison.Ordinal);
        string id = description ? key.Substring(0, key.Length - 3) : key;
        if (!items.TryGetValue(id, out var item))
        {
            text = "";
            return false;
        }
        bool chinese = IsChinese;
        text = description ? (chinese ? item.ZhDesc : item.EnDesc) : (chinese ? item.ZhName : item.EnName);
        return true;
    }

    private Object LoadPrefab(string id) => LoadCustomPrefab(id) ?? Resources.Load(id);

    internal GameObject? LoadCustomPrefab(string id)
    {
        if (!items.TryGetValue(id, out var item))
            return null;
        if (item.Prefab)
            return item.Prefab!;

        // 预制体停放在禁用的父物体下，等实例进入场景才运行原版 Awake。
        if (!templates)
        {
            templates = new GameObject("Astartes Equipment Prefabs");
            templates.SetActive(false);
            Object.DontDestroyOnLoad(templates);
        }
        var original = Resources.Load<GameObject>(item.VanillaPrefab);
        if (!original)
            throw new InvalidOperationException($"找不到装备模板 {item.VanillaPrefab}。");

        var clone = Object.Instantiate(original, templates!.transform, false);
        clone.name = id;
        clone.GetComponent<Item>().id = id;
        if (item.Sprite != null)
        {
            var sprite = sprites.Gear(item.Sprite);
            clone.GetComponent<SpriteRenderer>().sprite = sprite;
            FitCollider(clone, sprite);
        }

        if (item.Info.wearable)
        {
            if (clone.TryGetComponent<WearableSpriteSwap>(out var swap))
            {
                // 原版模板可能含不同角色的头盔贴图，统一使用本装备的贴图。
                swap.sprites = new List<Sprite>();
                swap.customScale = new List<float>();
            }
            var wearable = clone.GetComponent<Wearable>() ?? clone.AddComponent<Wearable>();
            wearable.secondaryLimbs = item.Secondary.Select(part => part.Limb).ToArray();
            wearable.secondaryLimbSprites = item.Secondary.Select(part => sprites.Gear(part.Sprite)).ToArray();
        }
        if (id == PackId)
        {
            var compartment = clone.AddComponent<Container>();
            compartment.maxWeight = 2f;
            compartment.maxWeightPerItem = 0.75f;
            compartment.tagRestriction = new[] { "astartes_bolt_ammo" };
            AstartesPackLight.AddTo(clone);
        }
        AstartesBoltgun.Configure(id, clone, sprites);
        item.Prefab = clone;
        return clone;
    }

    private static void FitCollider(GameObject instance, Sprite sprite)
    {
        Bounds bounds = sprite.bounds;
        if (instance.TryGetComponent<PolygonCollider2D>(out var polygon))
        {
            polygon.offset = Vector2.zero;
            polygon.pathCount = 1;
            polygon.SetPath(0, new Vector2[]
            {
                new(bounds.min.x, bounds.min.y), new(bounds.max.x, bounds.min.y),
                new(bounds.max.x, bounds.max.y), new(bounds.min.x, bounds.max.y)
            });
        }
        else if (instance.TryGetComponent<BoxCollider2D>(out var box))
        {
            box.offset = bounds.center;
            box.size = bounds.size;
        }
        else if (instance.TryGetComponent<CircleCollider2D>(out var circle))
        {
            circle.offset = bounds.center;
            circle.radius = Mathf.Min(bounds.extents.x, bounds.extents.y);
        }
    }

    private void EquipStartingGear(Body body)
    {
        Item Spawn(string id) => ((GameObject)Object.Instantiate(LoadPrefab(id),
            body.transform.position, Quaternion.identity)).GetComponent<Item>();

        foreach (var definition in items.Values.Where(item => item.Info.wearable))
            body.WearWearable(Spawn(definition.Id));

        EquipWeapon(AstartesBoltgun.GunId, 0, makeActive: true);
        EquipWeapon(SwordId, 1, makeActive: false);

        var pack = body.GetWearableBySlotID("back");
        var compartment = pack && pack.id == PackId ? pack.GetComponent<Container>() : null;
        for (int i = 0; i < 2; i++)
        {
            var magazine = Spawn(AstartesBoltgun.MagazineId);
            if (compartment)
                compartment!.LoadItem(magazine);
            else
                body.AutoPickUpItem(magazine);
        }
        PlayerCamera.main.UpdateWearables();
        AstartesPlugin.Log.LogInfo("阿斯塔特开局装备已发放。");

        void EquipWeapon(string id, int hand, bool makeActive)
        {
            var item = Spawn(id);
            int? slot = body.HoldingItem(hand) ? body.FirstEmptySlot() : hand;
            if (slot is int available)
            {
                body.PickUpItem(item, available, force: true);
                if (makeActive && body.slots[available].isHand && body.GetItem(available) == item)
                    body.handSlot = available;
            }
        }
    }

    public void Dispose()
    {
        if (templates)
            Object.Destroy(templates);
    }

    [HarmonyPatch(typeof(Item), nameof(Item.SetupItems))]
    private static class RegisterPatch
    {
        private static void Postfix() => AstartesPlugin.Gear.RegisterItems();
    }

    [HarmonyPatch(typeof(Locale), nameof(Locale.GetString))]
    private static class ItemTextPatch
    {
        private static void Postfix(string str, int type, ref string __result)
        {
            if (type == 0 && AstartesPlugin.Gear.TryGetLocalizedText(str, out var text))
                __result = text;
        }
    }

    [HarmonyPatch(typeof(WorldGeneration), "Start")]
    private static class StartingGearPatch
    {
        private static void Postfix()
        {
            var body = PlayerCamera.main.body;
            if (!SaveSystem.loadedRun && AstartesBody.IsAstartes(body))
                AstartesPlugin.Gear.EquipStartingGear(body);
        }
    }

    [HarmonyPatch]
    private static class RestoreGearPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SaveSystem), nameof(SaveSystem.TryLoadGame));
            yield return AccessTools.Method(typeof(GunScript), nameof(GunScript.UnloadMag));
            yield return AccessTools.Method(typeof(GunScript), "Update");
            yield return AccessTools.Method(typeof(AmmoScript), nameof(AmmoScript.UnloadRound));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var load = ((Func<string, Object>)Resources.Load).Method;
            var code = instructions.ToList();
            var matches = code.Where(instruction => instruction.Calls(load)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"找不到唯一的原版物品加载入口 {original.DeclaringType?.Name}.{original.Name}。");

            matches[0].opcode = OpCodes.Call;
            matches[0].operand = AccessTools.Method(typeof(RestoreGearPatch), nameof(Resolve));
            return code;
        }

        private static Object Resolve(string id) => AstartesPlugin.Gear.LoadPrefab(id);
    }

    private sealed class GearItem
    {
        internal string Id { get; }
        internal string VanillaPrefab { get; }
        internal string? Sprite { get; }
        internal string ZhName { get; }
        internal string EnName { get; }
        internal string ZhDesc { get; }
        internal string EnDesc { get; }
        internal ItemInfo Info { get; }
        internal (string Limb, string Sprite)[] Secondary { get; }
        internal GameObject? Prefab { get; set; }

        internal GearItem(string id, string vanillaPrefab, string? sprite, string zhName, string enName,
            string zhDesc, string enDesc, ItemInfo info, (string Limb, string Sprite)[] secondary)
        {
            Id = id;
            VanillaPrefab = vanillaPrefab;
            Sprite = sprite;
            ZhName = zhName;
            EnName = enName;
            ZhDesc = zhDesc;
            EnDesc = enDesc;
            Info = info;
            Secondary = secondary;
        }
    }
}
