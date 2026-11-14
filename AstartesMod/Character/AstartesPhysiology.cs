using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace AstartesMod;

internal static class AstartesPhysiology
{
    private const float BloodCapacityScale = 9f / 5f;
    private const float StaminaCapacityScale = 10f;
    private const float SicknessGainScale = 0.10f;
    private const float MoodFloor = 10f;
    private const float CurrentScale = 0.05f;

    internal static void Normalize(Body body)
    {
        body.venomTotal = body.venomCurrent = body.venomFake = body.septicShock = 0f;
        body.temperature = NormalTemperature(body.temperature);
        body.happiness = Mathf.Max(body.happiness, MoodFloor);
        body.bloodVolume = Mathf.Clamp(body.bloodVolume, -100f, 100f);
        body.stamina = Mathf.Clamp(body.stamina, 0f, 100f);
        foreach (var limb in body.limbs)
        {
            limb.infected = false;
            limb.infectionAmount = 0f;
        }
    }

    private static float NormalTemperature(float value) => Mathf.Clamp(value, 36.5f, 37.5f);

    private static void StoreBloodVolume(Body body, float value) => body.bloodVolume =
        AstartesBody.IsAstartes(body)
            ? Mathf.Clamp(body.bloodVolume + (value - body.bloodVolume) / BloodCapacityScale, -100f, 100f)
            : value;

    private static float ScaleStamina(Body body, float value) =>
        body.stamina + (value - body.stamina) / StaminaCapacityScale;

    private static void StoreStamina(Body body, float value) => body.stamina =
        AstartesBody.IsAstartes(body) ? Mathf.Clamp(ScaleStamina(body, value), 0f, 100f) : value;

    private static float ClampStamina(float value, float min, float max, Body body) =>
        Mathf.Clamp(AstartesBody.IsAstartes(body) ? ScaleStamina(body, value) : value, min, max);

    private static void StoreSickness(Body body, float value) => body.sicknessAmount =
        AstartesBody.IsAstartes(body) && value > body.sicknessAmount && value > 0f
            ? Mathf.Max(0f, body.sicknessAmount + (value - body.sicknessAmount) * SicknessGainScale)
            : value;

    private static void StoreMood(Body body, float value) => body.happiness =
        AstartesBody.IsAstartes(body) ? Mathf.Max(value, MoodFloor) : value;

    private static void StoreTemperature(Body body, float value) => body.temperature =
        AstartesBody.IsAstartes(body) ? NormalTemperature(value) : value;

    private static void StoreVenomTotal(Body body, float value) => body.venomTotal =
        AstartesBody.IsAstartes(body) ? 0f : value;

    private static void StoreVenomCurrent(Body body, float value) => body.venomCurrent =
        AstartesBody.IsAstartes(body) ? 0f : value;

    private static void StoreVenomFake(Body body, float value) => body.venomFake =
        AstartesBody.IsAstartes(body) ? 0f : value;

    private static void StoreSepsis(Body body, float value) => body.septicShock =
        AstartesBody.IsAstartes(body) ? 0f : value;

    private static void StoreInfected(Limb limb, bool value) => limb.infected =
        value && !AstartesBody.IsAstartes(limb.body);

    private static void StoreInfection(Limb limb, float value) => limb.infectionAmount =
        AstartesBody.IsAstartes(limb.body) ? 0f : value;

    [HarmonyPatch]
    private static class FieldUpdates
    {
        private static readonly FieldInfo blood = AccessTools.Field(typeof(Body), nameof(Body.bloodVolume));
        private static readonly FieldInfo stamina = AccessTools.Field(typeof(Body), nameof(Body.stamina));
        private static readonly MethodInfo staminaClamp = AccessTools.Method(typeof(Mathf), nameof(Mathf.Clamp),
            new[] { typeof(float), typeof(float), typeof(float) });
        private static readonly Dictionary<FieldInfo, MethodInfo> stores = new()
        {
            [blood] = Store(nameof(StoreBloodVolume)),
            [stamina] = Store(nameof(StoreStamina)),
            [AccessTools.Field(typeof(Body), nameof(Body.sicknessAmount))] = Store(nameof(StoreSickness)),
            [AccessTools.Field(typeof(Body), nameof(Body.happiness))] = Store(nameof(StoreMood)),
            [AccessTools.Field(typeof(Body), nameof(Body.temperature))] = Store(nameof(StoreTemperature)),
            [AccessTools.Field(typeof(Body), nameof(Body.venomTotal))] = Store(nameof(StoreVenomTotal)),
            [AccessTools.Field(typeof(Body), nameof(Body.venomCurrent))] = Store(nameof(StoreVenomCurrent)),
            [AccessTools.Field(typeof(Body), nameof(Body.venomFake))] = Store(nameof(StoreVenomFake)),
            [AccessTools.Field(typeof(Body), nameof(Body.septicShock))] = Store(nameof(StoreSepsis)),
            [AccessTools.Field(typeof(Limb), nameof(Limb.infected))] = Store(nameof(StoreInfected)),
            [AccessTools.Field(typeof(Limb), nameof(Limb.infectionAmount))] = Store(nameof(StoreInfection))
        };

        // 食物、药物与环境的写入分散在原版方法和编译器生成的回调中。
        // 按字段定位实际写入点，在伤害与呕吐判定读到数值之前换算。
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var assembly = typeof(Body).Assembly;
            var tokens = new HashSet<int>(stores.Keys.Select(field => field.MetadataToken));
            foreach (var type in assembly.GetTypes())
            foreach (var method in AccessTools.GetDeclaredMethods(type))
            {
                if (method.ContainsGenericParameters)
                    continue;
                var il = method.GetMethodBody()?.GetILAsByteArray();
                if (il != null && ContainsStoreToken(il, tokens))
                    yield return method;
            }
        }

        private static bool ContainsStoreToken(byte[] il, HashSet<int> tokens)
        {
            for (int index = 0; index + sizeof(int) < il.Length; index++)
            {
                if (il[index] == OpCodes.Stfld.Value && tokens.Contains(BitConverter.ToInt32(il, index + 1)))
                    return true;
            }
            return false;
        }

        private static MethodInfo Store(string name) => AccessTools.Method(typeof(AstartesPhysiology), name);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var code = instructions.ToList();
            for (int index = 1; index < code.Count; index++)
            {
                var instruction = code[index];
                if (instruction.opcode != OpCodes.Stfld || instruction.operand is not FieldInfo field ||
                    !stores.TryGetValue(field, out var store))
                    continue;

                var previous = code[index - 1];
                if (original.DeclaringType == typeof(Body))
                {
                    // 治疗复位和原版硬边界直接赋值。体力的常规更新先换算再钳制。
                    if ((field == blood || field == stamina) &&
                        (original.Name == nameof(Body.Heal) || original.Name == nameof(Body.TryLastStand)))
                        continue;
                    if (field == blood && original.Name == "HandleVitalProcesses" && previous.opcode == OpCodes.Ldc_R4)
                        continue;
                    if (field == stamina && original.Name == "HandleBody" && previous.Calls(staminaClamp))
                    {
                        code.Insert(index - 1, new CodeInstruction(OpCodes.Ldarg_0)
                            .MoveLabelsFrom(previous).MoveBlocksFrom(previous));
                        previous.operand = Store(nameof(ClampStamina));
                        index++;
                        continue;
                    }
                }

                instruction.opcode = OpCodes.Call;
                instruction.operand = store;
            }
            return code;
        }
    }

    [HarmonyPatch(typeof(SaveSystem), nameof(SaveSystem.TryLoadGame))]
    private static class RestoreBodyPatch
    {
        private static void Postfix()
        {
            var body = PlayerCamera.main.body;
            if (SaveSystem.loadedRun && AstartesBody.IsAstartes(body))
                Normalize(body);
        }
    }

    [HarmonyPatch(typeof(Body), nameof(Body.bloodToLitersBody))]
    private static class BloodVolumeDisplayPatch
    {
        private static void Postfix(Body __instance, ref float __result)
        {
            if (AstartesBody.IsAstartes(__instance))
                __result *= BloodCapacityScale;
        }
    }

    [HarmonyPatch(typeof(Body), nameof(Body.totalHappiness), MethodType.Getter)]
    private static class MoodPatch
    {
        private static void Postfix(Body __instance, ref float __result)
        {
            if (AstartesBody.IsAstartes(__instance))
                __result = Mathf.Max(__result, MoodFloor);
        }
    }

    private static void HealTissue(Limb limb, float pointsPerSecond, ref float rate)
    {
        if (AstartesBody.IsAstartes(limb.body) && limb.body.alive && !limb.dismembered &&
            !limb.prostheticInfo.hasProsthetic && !limb.hasShrapnel)
        {
            rate = Mathf.Max(rate, Time.deltaTime * pointsPerSecond * WorldGeneration.GetRunSettingFloat("healingrate"));
        }
    }

    [HarmonyPatch(typeof(Limb), nameof(Limb.SkinHealRate), MethodType.Getter)]
    private static class SkinHealPatch
    {
        private static void Postfix(Limb __instance, ref float __result) => HealTissue(__instance, 1.8f, ref __result);
    }

    [HarmonyPatch(typeof(Limb), nameof(Limb.MuscleHealRate), MethodType.Getter)]
    private static class MuscleHealPatch
    {
        private static void Postfix(Limb __instance, ref float __result) => HealTissue(__instance, 1.2f, ref __result);
    }

    [HarmonyPatch]
    private static class FractureHealingPatch
    {
        private const float HealSpeedMultiplier = 10f;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Limb), "Update");
            yield return AccessTools.Method(typeof(SplintLimb), "Update");
            yield return AccessTools.PropertyGetter(typeof(Limb), nameof(Limb.injuryHealTime));
            yield return AccessTools.Method(typeof(WatchScript), "Update");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var speed = AccessTools.Field(typeof(Limb), nameof(Limb.boneHealSpeed));
            var limbSpeed = AccessTools.Method(typeof(FractureHealingPatch), nameof(LimbHealSpeed));
            var watchSpeed = AccessTools.Method(typeof(FractureHealingPatch), nameof(WatchHealSpeed));
            var splintedLimb = AccessTools.Field(typeof(SplintLimb), "limb");
            var code = instructions.ToList();
            if (!code.Any(instruction => instruction.LoadsField(speed)))
                throw new InvalidOperationException($"找不到 {original.DeclaringType?.Name}.{original.Name} 中的骨折愈合速度。");

            // 换算愈合速度，沿用原版骨折进度和夹板结算。
            foreach (var instruction in code)
            {
                if (!instruction.LoadsField(speed))
                {
                    yield return instruction;
                    continue;
                }

                if (original.DeclaringType == typeof(WatchScript))
                {
                    yield return new CodeInstruction(OpCodes.Call, watchSpeed)
                        .MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                    continue;
                }

                yield return new CodeInstruction(OpCodes.Ldarg_0)
                    .MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                if (original.DeclaringType == typeof(SplintLimb))
                    yield return new CodeInstruction(OpCodes.Ldfld, splintedLimb);
                yield return new CodeInstruction(OpCodes.Call, limbSpeed);
            }
        }

        private static float LimbHealSpeed(Limb limb) =>
            AstartesBody.IsAstartes(limb.body) && !limb.prostheticInfo.hasProsthetic
                ? Limb.boneHealSpeed * HealSpeedMultiplier : Limb.boneHealSpeed;

        private static float WatchHealSpeed() => AstartesBody.IsAstartes(PlayerCamera.main.body)
            ? Limb.boneHealSpeed * HealSpeedMultiplier : Limb.boneHealSpeed;
    }

    [HarmonyPatch(typeof(WaterPusher), nameof(WaterPusher.OnTriggerStay2D))]
    private static class CurrentPatch
    {
        private readonly struct CurrentState
        {
            internal readonly Rigidbody2D Rigidbody;
            internal readonly Body? Body;
            internal readonly Vector2 Velocity;
            internal readonly float Slip;

            internal CurrentState(Rigidbody2D rigidbody, Body? body)
            {
                Rigidbody = rigidbody;
                Body = body;
                Velocity = rigidbody.velocity;
                Slip = body ? body!.liquidSlipTime : 0f;
            }
        }

        [HarmonyPriority(Priority.First)]
        private static void Prefix(Collider2D collision, out CurrentState __state)
        {
            __state = default;
            if (!PlayerCamera.main || !collision || !collision.attachedRigidbody)
                return;

            var body = PlayerCamera.main.body;
            var rigidbody = collision.attachedRigidbody;
            if (!AstartesBody.IsAstartes(body) ||
                (rigidbody != body.rb && !collision.transform.IsChildOf(body.transform)))
                return;

            bool pushesBody = collision.gameObject == body.gameObject;
            __state = new CurrentState(rigidbody, pushesBody ? body : null);
            // 临时增大平衡槽，让原版或 QoL 的冲倒判定直接使用换算后的容量。
            if (pushesBody)
                body.liquidRagdollBar /= CurrentScale;
        }

        private static void Finalizer(CurrentState __state)
        {
            var rigidbody = __state.Rigidbody;
            if (rigidbody)
                rigidbody.velocity = Vector2.Lerp(__state.Velocity, rigidbody.velocity, CurrentScale);
            var body = __state.Body;
            if (body)
            {
                body!.liquidSlipTime = Mathf.Lerp(__state.Slip, body.liquidSlipTime, CurrentScale);
                body.liquidRagdollBar *= CurrentScale;
            }
        }
    }
}
