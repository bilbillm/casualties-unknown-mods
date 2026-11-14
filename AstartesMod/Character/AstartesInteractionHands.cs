using HarmonyLib;
using UnityEngine;

namespace AstartesMod;

[HarmonyPatch(typeof(MinigameBase), nameof(MinigameBase.UpdateHandSprite))]
internal static class AstartesInteractionHands
{
    private static void Prefix(MinigameBase __instance, out Sprite[]? __state)
    {
        __state = null;
        if (!PlayerCamera.main || !AstartesBody.IsAstartes(PlayerCamera.main.body))
            return;

        // 原版逻辑仍决定使用哪一帧、左右手翻转和受伤色调。
        __state = __instance.handSprites;
        __instance.handSprites = AstartesPlugin.Sprites.InteractionHands;
    }

    private static void Postfix(MinigameBase __instance, Sprite[]? __state)
    {
        if (__state != null)
            __instance.handSprites = __state;
    }
}
