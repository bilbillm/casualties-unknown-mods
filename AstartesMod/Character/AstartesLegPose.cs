using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstartesMod;

/// <summary>在原版骨架上绘制人形腿。保留肢体的碰撞体、关节和伤势材质。</summary>
internal sealed class AstartesLegPose : IDisposable
{
    private const float NativeFootRestAngle = 38f;
    private const float NativeSoleOffset = 0.55f;
    private const float JointOverlap = 0.32f;

    private readonly Body body;
    private readonly Limb thigh;
    private readonly Limb shin;
    private readonly Limb foot;
    private readonly SpriteRenderer[] source;
    private readonly SpriteRenderer[] art;
    private readonly SpriteRenderer?[] armourArt = new SpriteRenderer?[3];
    private readonly SpriteRenderer?[] armourSource = new SpriteRenderer?[3];
    private bool showing;

    private AstartesLegPose(Body body, Limb thigh, Limb shin, Limb foot, SpriteRenderer[] source)
    {
        this.body = body;
        this.thigh = thigh;
        this.shin = shin;
        this.foot = foot;
        this.source = source;
        art = new[]
        {
            MakeRenderer(source[0], source[0].sprite),
            MakeRenderer(source[1], source[1].sprite),
            MakeRenderer(source[2], AstartesPlugin.Sprites.Boot)
        };
    }

    internal static AstartesLegPose? Create(Body body, string side)
    {
        var thigh = body.LimbByName("Thigh" + side);
        var shin = body.LimbByName("Crus" + side);
        var foot = body.LimbByName("Foot" + side);
        if (!thigh || !shin || !foot)
            return null;

        var thighRenderer = thigh.GetComponent<SpriteRenderer>();
        var shinRenderer = shin.GetComponent<SpriteRenderer>();
        var footRenderer = foot.GetComponent<SpriteRenderer>();
        if (!thighRenderer || !shinRenderer || !footRenderer ||
            !thighRenderer.sprite || !shinRenderer.sprite || !footRenderer.sprite)
            return null;

        return new AstartesLegPose(body, thigh, shin, foot, new[] { thighRenderer, shinRenderer, footRenderer });
    }

    internal void Update()
    {
        if (!CanDraw(thigh, source[0], out var thighArmour) ||
            !CanDraw(shin, source[1], out var shinArmour) ||
            !CanDraw(foot, source[2], out var footArmour))
        {
            Hide();
            return;
        }

        var root = body.transform;
        Vector2 hip = root.InverseTransformPoint(thigh.transform.TransformPoint(thigh.joint.anchor));
        Vector2 knee = root.InverseTransformPoint(shin.transform.TransformPoint(shin.joint.anchor));
        Vector2 ankle = root.InverseTransformPoint(foot.transform.TransformPoint(foot.joint.anchor));
        Vector2 direction = root.InverseTransformVector(foot.transform.TransformVector(Vector3.right));
        float bootAngle = Mathf.DeltaAngle(NativeFootRestAngle, Vector2.SignedAngle(Vector2.right, direction));

        if (body.standing)
        {
            bool restingFoot = body.grounded && Mathf.Abs(body.moveDir.x) < 0.1f;
            bootAngle = restingFoot ? Mathf.Clamp(bootAngle, -7f, 7f) : Mathf.Clamp(bootAngle, -45f, 35f);

            // 用原版脚底的轨迹保留落脚点，将小腿的关节移到人形靴子的踝部。
            Vector2 sole = root.InverseTransformPoint(foot.transform.TransformPoint(Vector2.down * NativeSoleOffset));
            var boot = art[2].sprite;
            ankle = sole + (Vector2)(Quaternion.Euler(0f, 0f, bootAngle) *
                new Vector2(-0.08f, boot.pivot.y / boot.pixelsPerUnit));

            if (!thigh.broken && !thigh.dislocated && !shin.broken && !shin.dislocated)
            {
                Vector2 midpoint = Vector2.Lerp(hip, ankle, 0.49f);
                Vector2 forward = Vector2.Perpendicular(ankle - hip).normalized;
                float bend = Vector2.Dot(knee - midpoint, forward);
                bend = Mathf.Clamp(bend * 0.52f, 0.04f, 0.36f) + Mathf.Clamp01(body.crouchAmount) * 0.32f;
                knee = midpoint + forward * bend;
            }
        }

        PlaceSegment(art[0], hip, knee, 1.06f);
        PlaceSegment(art[1], knee, ankle, 1.12f);
        art[2].transform.localPosition = ankle;
        art[2].transform.localRotation = Quaternion.Euler(0f, 0f, bootAngle);

        for (int i = 0; i < art.Length; i++)
        {
            var original = source[i];
            var adjusted = art[i];
            adjusted.sharedMaterial = original.sharedMaterial;
            adjusted.color = original.color;
            adjusted.sortingLayerID = original.sortingLayerID;
            adjusted.sortingOrder = original.sortingOrder;
            adjusted.enabled = true;
            original.forceRenderingOff = true;
        }
        DrawArmour(0, thighArmour);
        DrawArmour(1, shinArmour);
        DrawArmour(2, footArmour);
        showing = true;
    }

    private SpriteRenderer MakeRenderer(SpriteRenderer original, Sprite sprite)
    {
        var obj = new GameObject("Astartes " + original.name + " Visual", typeof(SpriteRenderer));
        obj.layer = original.gameObject.layer;
        obj.transform.SetParent(body.transform, false);
        var renderer = obj.GetComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.enabled = false;
        return renderer;
    }

    private static void PlaceSegment(SpriteRenderer renderer, Vector2 start, Vector2 end, float widthScale)
    {
        Vector2 axis = end - start;
        var part = renderer.transform;
        part.localPosition = (start + end) * 0.5f;
        part.localRotation = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.down, axis));
        float textureHeight = renderer.sprite.rect.height / renderer.sprite.pixelsPerUnit;
        part.localScale = new Vector3(widthScale, (axis.magnitude + JointOverlap) / textureHeight, 1f);
    }

    private bool CanDraw(Limb limb, SpriteRenderer renderer, out SpriteRenderer? armour)
    {
        armour = null;
        if (!limb || !limb.joint || !renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
            limb.dismembered || limb.prostheticInfo.hasProsthetic || (!showing && renderer.forceRenderingOff))
            return false;

        // 动力腿甲随人形部件绘制。其它穿戴物继续使用原版关节。
        var joint = limb.transform;
        for (int i = 0; i < joint.childCount; i++)
        {
            var child = joint.GetChild(i);
            Item? item;
            if (child.TryGetComponent<WearableExtension>(out var extension))
            {
                item = extension.reference;
                if (item && !item!.transform.parent)
                {
                    // 卸下后，原版会在帧尾销毁延伸物。先停画，避免旧战靴闪回原位。
                    if (item.id == AstartesGear.LegsId && child.TryGetComponent<SpriteRenderer>(out var removed))
                        removed.enabled = false;
                    continue;
                }
            }
            else if (!child.TryGetComponent<Item>(out item) || !item!.Stats.wearable)
            {
                continue;
            }

            if (!item || item!.id != AstartesGear.LegsId)
                return false;
            armour = child.GetComponent<SpriteRenderer>();
        }
        return true;
    }

    private void DrawArmour(int part, SpriteRenderer? original)
    {
        var previous = armourSource[part];
        if (previous != original)
        {
            if (previous)
                previous!.forceRenderingOff = false;
            armourSource[part] = original;
        }

        var overlay = armourArt[part];
        if (!original)
        {
            if (overlay)
                overlay!.enabled = false;
            return;
        }

        if (!overlay)
        {
            var obj = new GameObject("Mk X " + source[part].name, typeof(SpriteRenderer));
            obj.layer = art[part].gameObject.layer;
            obj.transform.SetParent(art[part].transform, false);
            overlay = obj.GetComponent<SpriteRenderer>();
            armourArt[part] = overlay;
        }
        overlay!.sprite = original!.sprite;
        overlay.sharedMaterial = original.sharedMaterial;
        overlay.color = original.color;
        overlay.sortingLayerID = original.sortingLayerID;
        overlay.sortingOrder = original.sortingOrder;
        overlay.enabled = original.enabled;
        original.forceRenderingOff = true;
    }

    internal void Hide()
    {
        if (!showing)
            return;

        for (int i = 0; i < art.Length; i++)
        {
            DrawArmour(i, null);
            if (art[i])
                art[i].enabled = false;
            if (source[i])
                source[i].forceRenderingOff = false;
        }
        showing = false;
    }

    public void Dispose()
    {
        Hide();
        foreach (var renderer in art)
            if (renderer)
                Object.Destroy(renderer.gameObject);
    }
}
