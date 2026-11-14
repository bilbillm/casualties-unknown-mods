using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace AstartesMod;

[DefaultExecutionOrder(1000)]
internal sealed class AstartesBody : MonoBehaviour
{
    private SpriteRenderer[] renderers = Array.Empty<SpriteRenderer>();
    private AstartesLegPose? frontLeg;
    private AstartesLegPose? backLeg;
    internal float NextWarCryTime { get; set; }
    internal float NextScreamTime { get; set; }
    internal bool SecondHeartSpent { get; set; }
    internal bool FurnaceSpent { get; set; }

    internal void Initialize(Body body, bool newRun)
    {
        var profile = AstartesPlugin.Profile;
        if (newRun)
            profile.InitializeSkills(body.skills);
        AstartesPhysiology.Normalize(body);

        // 原版只序列化标记为 JsonProperty 的字段。恢复速率属于预制体属性。
        body.staminaGainMultiplier *= profile.StaminaRecovery;
        body.bonusLimbProtection += profile.NaturalProtection;
        var face = body.GetComponentInChildren<FacialExpression>(true);
        if (face)
        {
            // 原版的基础头部叫 HeadBack，眼睛是另一张独立的犬科眼睛精灵。
            var head = AstartesPlugin.Sprites.Head;
            face.defaultHead = head;
            face.defaultHeadMouth = head;
            face.defaultHeadMouthHalf = head;
            face.eyes.enabled = false;
        }
        renderers = transform.root.GetComponentsInChildren<SpriteRenderer>(true);
        LateUpdate();
        frontLeg = AstartesLegPose.Create(body, "F");
        backLeg = AstartesLegPose.Create(body, "B");
    }

    internal static bool IsAstartes(Body body) => body && body.TryGetComponent<AstartesBody>(out _);

    private void LateUpdate()
    {
        foreach (var renderer in renderers)
        {
            if (renderer && renderer.sprite && AstartesPlugin.Sprites.TryReplace(renderer.sprite, out var replacement))
                renderer.sprite = replacement;
        }
        frontLeg?.Update();
        backLeg?.Update();
    }

    private void OnDisable()
    {
        frontLeg?.Hide();
        backLeg?.Hide();
    }

    private void OnDestroy()
    {
        frontLeg?.Dispose();
        backLeg?.Dispose();
    }

    [HarmonyPatch(typeof(Body), nameof(Body.actualMaxSpeed), MethodType.Getter)]
    private static class RunSpeedPatch
    {
        private static void Postfix(Body __instance, ref float __result)
        {
            if (IsAstartes(__instance))
                __result *= AstartesPlugin.Profile.Speed;
        }
    }

    [HarmonyPatch(typeof(Body), nameof(Body.actualJumpSpeed), MethodType.Getter)]
    private static class JumpSpeedPatch
    {
        private static void Postfix(Body __instance, ref float __result)
        {
            if (IsAstartes(__instance))
                __result *= AstartesPlugin.Profile.Jump;
        }
    }

    [HarmonyPatch(typeof(Body), "HandlePeriodicChecks")]
    private static class ClottingPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var setter = AccessTools.PropertySetter(typeof(Body), nameof(Body.bleedClottingSpeed));
            if (code.Count(instruction => instruction.Calls(setter)) != 1)
                throw new InvalidOperationException("找不到唯一的原版凝血速度计算。");

            var scale = AccessTools.Method(typeof(ClottingPatch), nameof(ScaleClotting));
            foreach (var instruction in code)
            {
                if (instruction.Calls(setter))
                {
                    // 在原版提交计算值时应用阿斯塔特的凝血下限。
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction);
                    yield return new CodeInstruction(OpCodes.Call, scale);
                }
                yield return instruction;
            }
        }

        private static float ScaleClotting(float clotting, Body body) =>
            IsAstartes(body) ? Mathf.Max(2.5f, clotting * AstartesPlugin.Profile.Clotting) : clotting;
    }

    [HarmonyPatch(typeof(Body), nameof(Body.DoFurTuft))]
    private static class FurEffectPatch
    {
        private static bool Prefix(Body __instance) => !IsAstartes(__instance);
    }
}
