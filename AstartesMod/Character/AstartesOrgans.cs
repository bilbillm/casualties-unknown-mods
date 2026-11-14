using HarmonyLib;
using UnityEngine;

namespace AstartesMod;

internal static class AstartesOrgans
{
    private const float CriticalBloodVolume = 30f;
    private const float CriticalBleedSpeed = 0.02f;
    private const float CriticalBrainHealth = 15f;
    private const float SevereHeadMuscleHealth = 40f;

    [HarmonyPatch(typeof(Body), "Update")]
    private static class BodyUpdatePatch
    {
        private static void Prefix(Body __instance) => CheckTrauma(__instance);
    }

    [HarmonyPatch(typeof(PlayerCamera), "HandleDeathScreen")]
    private static class DeathScreenPatch
    {
        private static void Prefix(PlayerCamera __instance)
        {
            // 伤害可在 Body.Update 之后结算。死亡界面弹出前再检查一次。
            if (!__instance.body.alive)
                CheckTrauma(__instance.body);
        }
    }

    private static void CheckTrauma(Body body)
    {
        if (!body.TryGetComponent<AstartesBody>(out var astartes))
            return;
        if (body.alive ? astartes.FurnaceSpent : astartes.SecondHeartSpent)
            return;

        var camera = PlayerCamera.main;
        var world = WorldGeneration.world;
        if (!camera || camera.body != body || camera.didDeathScreen || !world ||
            !world.worldExists || world.generatingWorld || world.cutsceneActive)
            return;

        bool furnaceFired = !astartes.FurnaceSpent && HasLethalTrauma(body);
        if (furnaceFired)
        {
            astartes.FurnaceSpent = true;
            TreatTrauma(body);
            AstartesPlugin.Log.LogInfo("贝利撒留炉已启动。");
        }

        if (!body.alive)
        {
            astartes.SecondHeartSpent = true;
            RestartCirculation(body);
            AstartesPlugin.Log.LogInfo("第二心脏已接管循环。");
            camera.DoAlert(AstartesVoice.UsesChinese
                ? (furnaceFired ? "第二心脏接管。贝利撒留炉已点燃。" : "第二心脏接管。")
                : (furnaceFired ? "Secondary heart engaged. Belisarian Furnace ignited." : "Secondary heart engaged."),
                important: true);
        }
        else if (furnaceFired)
        {
            camera.DoAlert(AstartesVoice.UsesChinese ? "贝利撒留炉已点燃。" : "Belisarian Furnace ignited.", important: true);
        }
    }

    private static bool HasLethalTrauma(Body body)
    {
        var head = body.limbs[0];
        if (body.brainHealth <= CriticalBrainHealth &&
            (head.muscleHealth < SevereHeadMuscleHealth || head.broken || head.hasShrapnel))
            return true;

        return body.bloodVolume < CriticalBloodVolume && NetBloodLoss(body) > CriticalBleedSpeed;
    }

    private static float NetBloodLoss(Body body)
    {
        // 与原版 HandleBody 的净失血速率一致，直接使用本帧伤口状态。
        float loss = Mathf.Clamp(body.internalBleeding, 0f, 25f) * 0.0057f;
        foreach (var limb in body.limbs)
        {
            if (!limb.dismembered)
                loss += limb.totalBleedAmount * limb.bleedSpeedMult;
        }
        return Mathf.Max(0f, loss - body.bloodRegenSpeed);
    }

    private static void TreatTrauma(Body body)
    {
        foreach (var limb in body.limbs)
        {
            if (limb.dismembered || limb.prostheticInfo.hasProsthetic)
                continue;

            limb.shrapnel = 0;
            limb.bleedAmount = 0f;
            limb.pain = Mathf.Min(limb.pain, 20f);
            if (limb.muscleHealth <= Limb.muscleDeathThreshold)
                continue;

            if (limb.broken)
            {
                limb.boneHealTimer = 0f;
                limb.MendBone();
            }
            if (limb.dislocated)
            {
                limb.dislocationTimer = 0f;
                limb.UnDislocate();
            }
        }

        body.internalBleeding = 0f;
        body.totalBleedSpeed = NetBloodLoss(body);
        body.painShock = 0f;
        body.shock = 0f;
    }

    private static void RestartCirculation(Body body)
    {
        body.brainHealth = 75f;
        body.consciousness = 70f;
        body.heartRate = 90f;
        body.heartRatePressureOffset = 0f;
        body.fibrillationProgress = 0f;
        body.fibrillationForced = false;
        body.bloodPressure = Mathf.Clamp(body.bloodPressure, 100f, 135f);
        body.bloodOxygen = Mathf.Max(body.bloodOxygen, 80f);
        body.respiratoryRate = Mathf.Max(body.respiratoryRate, 65f);
        body.breathing = true;
        // 原版血量字段为 60 时对应 80% 的血容量。
        body.bloodVolume = Mathf.Max(body.bloodVolume, 60f);
        body.shock = 0f;
        body.painShock = 0f;
        if (body.sleeping)
            body.WakeUp();
    }
}
