using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace AstartesMod;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class AstartesPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "casualtiesunknown.astartes-mod";
    public const string PluginName = "Astartes Mod";
    public const string PluginVersion = "2.4.4";

    internal static AstartesProfile Profile { get; private set; } = null!;
    internal static AstartesSprites Sprites { get; private set; } = null!;
    internal static AstartesGear Gear { get; private set; } = null!;
    internal static AstartesVoice Voice { get; private set; } = null!;
    internal static ManualLogSource Log { get; private set; } = null!;
    private Harmony? harmony;

    private void Awake()
    {
        Log = Logger;
        try
        {
            Profile = new AstartesProfile(Config);
            var directory = Path.GetDirectoryName(Info.Location)!;
            Sprites = new AstartesSprites(Path.Combine(directory, "Sprites"));
            Gear = new AstartesGear(Sprites);
            Voice = new AstartesVoice();
            harmony = new Harmony(PluginGuid);
            harmony.PatchAll(typeof(AstartesPlugin).Assembly);
            // 引导器的宿主对象可能在首个场景切换时销毁。
            // 角色补丁与贴图随游戏进程存活。
            UnityEngine.Application.quitting += Shutdown;
            Log.LogInfo($"阿斯塔特角色 {PluginVersion} 已注册。初始 STR/RES/INT {Profile.Strength}/{Profile.Resilience}/{Profile.Intelligence}");
        }
        catch (Exception exception)
        {
            harmony?.UnpatchSelf();
            Voice?.Dispose();
            Gear?.Dispose();
            Sprites?.Dispose();
            Log.LogError(exception);
        }
    }

    private void Shutdown()
    {
        UnityEngine.Application.quitting -= Shutdown;
        harmony?.UnpatchSelf();
        Voice?.Dispose();
        Gear?.Dispose();
        Sprites?.Dispose();
    }
}

internal sealed class AstartesProfile
{
    internal int Strength { get; }
    internal int Resilience { get; }
    internal int Intelligence { get; }
    internal float Speed { get; }
    internal float Jump { get; }
    internal float StaminaRecovery { get; }
    internal float Clotting { get; }
    internal float NaturalProtection { get; }

    internal AstartesProfile(ConfigFile config)
    {
        var version = config.Bind("Astartes", "BalanceVersion", 0, "平衡配置迁移版本");
        bool upgrade = version.Value < 1;
        var skillRange = new AcceptableValueRange<int>(0, 36);
        var multiplierRange = new AcceptableValueRange<float>(0.1f, 6f);
        Strength = ReadBalance("StartingSTR", 24, 34, "初始力量等级", skillRange);
        Resilience = ReadBalance("StartingRES", 22, 32, "初始韧性等级", skillRange);
        Intelligence = ReadBalance("StartingINT", 12, 28, "初始智力等级", skillRange);
        Speed = ReadBalance("RunSpeed", 1.15f, 1.55f, "跑速倍率", multiplierRange);
        Jump = ReadBalance("JumpSpeed", 1.10f, 1.35f, "起跳速度倍率", multiplierRange);
        StaminaRecovery = ReadBalance("StaminaRecovery", 1.25f, 3.50f, "体力恢复倍率", multiplierRange);
        Clotting = ReadBalance("Clotting", 1.50f, 3.00f, "凝血速度倍率", multiplierRange);
        NaturalProtection = config.Bind("Astartes", "NaturalProtection", 1.25f,
            new ConfigDescription("裸装肢体防护系数", new AcceptableValueRange<float>(0f, 5f))).Value;
        if (upgrade)
            version.Value = 1;

        T ReadBalance<T>(string key, T oldDefault, T value, string description, AcceptableValueBase range)
        {
            var entry = config.Bind("Astartes", key, value, new ConfigDescription(description, range));
            if (upgrade && EqualityComparer<T>.Default.Equals(entry.Value, oldDefault))
                entry.Value = value;
            return entry.Value;
        }
    }

    internal void InitializeSkills(Skills skills)
    {
        skills.STR = Strength;
        skills.RES = Resilience;
        skills.INT = Intelligence;
        skills.UpdateExpBoundaries();
        skills.expSTR = skills.minSTR;
        skills.expRES = skills.minRES;
        skills.expINT = skills.minINT;
    }

}
