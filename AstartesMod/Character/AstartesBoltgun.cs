using HarmonyLib;
using UnityEngine;

namespace AstartesMod;

internal static class AstartesBoltgun
{
    internal const string GunId = "astartes_boltgun";
    internal const string MagazineId = "astartes_bolt_magazine";
    internal const string RoundId = "astartes_bolt_round";
    private const GunScript.AmmoType Bolt = (GunScript.AmmoType)3;
    private const int MagazineCapacity = 60;

    internal static void Configure(string id, GameObject template, AstartesSprites sprites)
    {
        if (id == GunId)
        {
            var gun = template.GetComponent<GunScript>();
            gun.ammoType = Bolt;
            gun.firingMode = GunScript.FiringMode.Auto;
            gun.feedType = GunScript.FeedType.Mag;
            gun.roundInChamber = GunScript.RoundInChamber.Round;
            gun.hasMag = true;
            gun.magCapacity = MagazineCapacity;
            gun.roundsInMag = MagazineCapacity - 1;
            gun.safe = gun.racked = gun.lastRacked = gun.triggerPressed = gun.firingPinStruck = false;
            gun.animalDamage = 600f;
            gun.structureDamage = 400f;
            gun.desiredGasTime = 0.09f;
            gun.knockBack = 1.7f;
            gun.verticalSpread = 0.013f;
            gun.loudness = 1f;
            gun.shotsPerFire = 1;
            gun.conditionLossPerShot = 0.04f / AstartesGear.DurabilityMultiplier;

            gun.normalSprite = gun.rackedSprite = sprites.Gear("Boltgun");
            gun.normalSpriteNoMag = gun.rackedSpriteNoMag = sprites.Gear("BoltgunNoMag");
            gun.barrel.position = template.transform.TransformPoint(new Vector3(gun.normalSprite.bounds.max.x, 0.45f));
            var flash = gun.muzzleParticle.transform;
            if (flash != template.transform && flash != gun.barrel && !flash.IsChildOf(gun.barrel))
                flash.position = gun.barrel.position;
        }
        else if (id == MagazineId || id == RoundId)
        {
            var ammo = template.GetComponent<AmmoScript>();
            ammo.ammoType = Bolt;
            ammo.maxRounds = ammo.rounds = id == MagazineId ? MagazineCapacity : 1;
        }
    }

    internal static void ClearCraftedAmmo(string id, GameObject item)
    {
        if (id == MagazineId)
        {
            item.GetComponent<AmmoScript>().rounds = 0;
        }
        else if (id == GunId)
        {
            var gun = item.GetComponent<GunScript>();
            gun.hasMag = false;
            gun.roundsInMag = 0;
            gun.roundInChamber = GunScript.RoundInChamber.None;
            item.GetComponent<SpriteRenderer>().sprite = gun.normalSpriteNoMag;
        }
    }

    [HarmonyPatch(typeof(AmmoScript), nameof(AmmoScript.AmmoTypeToItem))]
    private static class RoundIdPatch
    {
        private static void Postfix(GunScript.AmmoType ammo, ref string __result)
        {
            if (ammo == Bolt)
                __result = RoundId;
        }
    }

    [HarmonyPatch(typeof(AmmoScript), nameof(AmmoScript.AmmoTypeToMagazine))]
    private static class MagazineIdPatch
    {
        private static void Postfix(GunScript.AmmoType ammo, ref string __result)
        {
            if (ammo == Bolt)
                __result = MagazineId;
        }
    }

    [HarmonyPatch(typeof(GunScript), nameof(GunScript.JamChance))]
    private static class BoltgunReliabilityPatch
    {
        private static bool Prefix(GunScript __instance, ref float __result)
        {
            if (__instance.GetComponent<Item>().id != GunId)
                return true;
            __result = 0f;
            return false;
        }
    }

    [HarmonyPatch(typeof(Body), nameof(Body.HasHearingProtection))]
    private static class HelmetHearingPatch
    {
        private static void Postfix(Body __instance, ref bool __result)
        {
            __result |= AstartesGear.HasHelmet(__instance);
        }
    }
}
