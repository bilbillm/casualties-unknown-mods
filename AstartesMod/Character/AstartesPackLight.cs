using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AstartesMod;

internal sealed class AstartesPackLight : MonoBehaviour
{
    private Item pack = null!;
    private Light2D floodlight = null!;

    internal static void AddTo(GameObject template)
    {
        var light = template.AddComponent<Light2D>();
        light.enabled = false;
        // 禁用模板克隆后会保留序列化的渲染层，直接沿用原版提灯的层配置。
        var lantern = Resources.Load<GameObject>("lantern").GetComponent<Light2D>();
        var layers = AccessTools.Field(typeof(Light2D), "m_ApplyToSortingLayers");
        layers.SetValue(light, layers.GetValue(lantern));
        light.blendStyleIndex = lantern.blendStyleIndex;
        light.lightType = Light2D.LightType.Point;
        light.color = new Color32(232, 242, 255, 255);
        light.intensity = 1.1f;
        light.pointLightInnerAngle = 360f;
        light.pointLightOuterAngle = 360f;
        light.pointLightInnerRadius = 12f;
        light.pointLightOuterRadius = 36f;
        light.falloffIntensity = 0.6f;
        template.AddComponent<AstartesPackLight>();
    }

    private void Awake()
    {
        pack = GetComponent<Item>();
        floodlight = GetComponent<Light2D>();
    }

    private void LateUpdate()
    {
        // 原版穿戴物直接挂到肢体下，手持、存放和丢弃时的父节点不同。
        var parent = transform.parent;
        floodlight.enabled = pack.condition > 0f && parent && parent.TryGetComponent<Limb>(out _);
    }

    private void OnDisable()
    {
        if (floodlight)
            floodlight.enabled = false;
    }
}
