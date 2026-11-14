using HarmonyLib;

namespace AstartesMod;

[HarmonyPatch(typeof(WorldGeneration), "Start")]
internal static class AstartesCutscenes
{
    private static void Postfix(WorldGeneration __instance)
    {
        var camera = PlayerCamera.main;
        var body = camera.body;
        if (!AstartesBody.IsAstartes(body))
            return;

        int character = body.charType;
        var sprites = AstartesPlugin.Sprites;
        __instance.podImages[character] = sprites.LoadingPod;
        __instance.podImages[character + __instance.podImages.Length / 2] = sprites.LoadingPod;
        __instance.cutsceneImages[character] = sprites.Ending;
        __instance.cutsceneImages[character + 3] = sprites.EndingCargo;
        // 原版死亡画面顺序为普通、失血、水中死亡。
        camera.deathScreenSprites = sprites.DeathScreens;
    }
}
