using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstartesMod;

internal sealed class AstartesSprites : IDisposable
{
    private readonly List<Object> resources = new();
    private readonly Dictionary<string, Sprite> limbs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Sprite> equipment = new(StringComparer.Ordinal);
    internal Sprite Head { get; }
    internal Sprite Boot { get; }
    private readonly Sprite invisible;
    internal Sprite Selection { get; }
    internal Sprite LoadingPod { get; }
    internal Sprite Ending { get; }
    internal Sprite EndingCargo { get; }
    internal Sprite[] DeathScreens { get; }
    internal Sprite[] InteractionHands { get; }

    internal AstartesSprites(string directory)
    {
        try
        {
            // 缩小头部时保留颈部在衣领上的落点。
            Head = Load(directory, "Head", new Vector2(0.5f, 0.69f), 15f);
            Boot = Load(directory, "Boot", new Vector2(0.30f, 0.72f));
            Selection = Load(directory, "Selection");
            LoadingPod = Load(directory, "CutscenePod");
            Ending = Load(directory, "CutsceneEnd");
            EndingCargo = Load(directory, "CutsceneCargo");
            DeathScreens = new[]
            {
                Load(directory, "Death"),
                Load(directory, "DeathBleeding"),
                Load(directory, "DeathDrowning")
            };
            InteractionHands = LoadInteractionHands(directory);
            limbs.Add("experimentUpTorso", Load(directory, "UpperTorso"));
            limbs.Add("experimentDownTorso", Load(directory, "LowerTorso"));
            limbs.Add("experimentUpArm", Load(directory, "UpperArm"));
            limbs.Add("experimentDownArm", Load(directory, "Forearm"));
            limbs.Add("experimentHandF", Load(directory, "Fist"));
            limbs.Add("experimentHandB", Load(directory, "Hand"));
            limbs.Add("experimentThigh", Load(directory, "Thigh"));
            limbs.Add("experimentCrus", Load(directory, "Shin"));
            limbs.Add("experimentFoot", Load(directory, "Foot"));
            equipment.Add("MkXHelmet", Load(directory, "MkXHelmet", new Vector2(0.50f, 0.69f), 10f));
            equipment.Add("MkXCuirass", Load(directory, "MkXCuirass"));
            equipment.Add("MkXFauld", Load(directory, "MkXFauld"));
            equipment.Add("MkXPack", Load(directory, "MkXPack", new Vector2(0.91f, 0.38f)));
            equipment.Add("MkXShoulder", Load(directory, "MkXShoulder", new Vector2(0.50f, 0.35f)));
            equipment.Add("MkXShoulderRear", Load(directory, "MkXShoulderRear", new Vector2(0.50f, 0.35f)));
            equipment.Add("MkXVambrace", Load(directory, "MkXVambrace"));
            equipment.Add("MkXGauntlet", Load(directory, "MkXGauntlet"));
            equipment.Add("MkXThigh", Load(directory, "MkXThigh"));
            equipment.Add("MkXGreave", Load(directory, "MkXGreave"));
            equipment.Add("MkXSabaton", Load(directory, "MkXSabaton", new Vector2(0.30f, 0.63f)));
            equipment.Add("Chainsword", Load(directory, "Chainsword", new Vector2(0.50f, 0.13f)));
            var gunGrip = new Vector2(0.11f, 0.36f);
            equipment.Add("Boltgun", Load(directory, "Boltgun", gunGrip, 10f));
            equipment.Add("BoltgunNoMag", Load(directory, "BoltgunNoMag", gunGrip, 10f));
            equipment.Add("BoltMagazine", Load(directory, "BoltMagazine"));

            var clear = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            clear.SetPixel(0, 0, Color.clear);
            clear.Apply(false, true);
            ConfigureTexture(clear, "Invisible");
            invisible = MakeSprite(clear);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal Sprite Gear(string name) => equipment[name];

    internal bool TryReplace(Sprite source, out Sprite replacement)
    {
        string name = source.name;
        if (limbs.TryGetValue(name, out var limb))
        {
            replacement = limb;
            return true;
        }

        if (name.StartsWith("experimentHead", StringComparison.Ordinal))
        {
            replacement = Head;
            return true;
        }

        if (name == "experimentTail")
        {
            replacement = invisible;
            return true;
        }

        replacement = null!;
        return false;
    }

    private Sprite[] LoadInteractionHands(string directory)
    {
        const int columns = 4;
        const int rows = 4;
        var texture = LoadTexture(directory, "InteractionHands");
        if (texture.width % columns != 0 || texture.height % rows != 0)
            throw new InvalidDataException("阿斯塔特交互手势图应为 4×4 网格。");

        int width = texture.width / columns;
        int height = texture.height / rows;
        var hands = new Sprite[columns * rows];
        // 从左到右、从上到下对应原版 handSprites 的顺序。
        for (int i = 0; i < hands.Length; i++)
        {
            var rect = new Rect(i % columns * width, (rows - 1 - i / columns) * height, width, height);
            var sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 50f, 0, SpriteMeshType.FullRect);
            sprite.name = $"Astartes Interaction Hand {i}";
            resources.Add(sprite);
            hands[i] = sprite;
        }
        return hands;
    }

    private Sprite Load(string directory, string part, Vector2? pivot = null, float pixelsPerUnit = 8f) =>
        MakeSprite(LoadTexture(directory, part), pivot, pixelsPerUnit);

    private Texture2D LoadTexture(string directory, string part)
    {
        byte[] png = File.ReadAllBytes(Path.Combine(directory, part + ".png"));
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, png, true))
        {
            Object.Destroy(texture);
            throw new InvalidDataException($"无法加载阿斯塔特精灵 {part}。");
        }

        ConfigureTexture(texture, part);
        return texture;
    }

    private void ConfigureTexture(Texture2D texture, string part)
    {
        texture.name = "Astartes " + part;
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        resources.Add(texture);
    }

    private Sprite MakeSprite(Texture2D texture, Vector2? pivot = null, float pixelsPerUnit = 8f)
    {
        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            pivot ?? new Vector2(0.5f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.FullRect);
        sprite.name = texture.name;
        resources.Add(sprite);
        return sprite;
    }

    public void Dispose()
    {
        for (int i = resources.Count - 1; i >= 0; i--)
            Object.Destroy(resources[i]);

        resources.Clear();
    }
}
