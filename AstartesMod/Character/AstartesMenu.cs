using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace AstartesMod;

internal sealed class AstartesMenu : MonoBehaviour
{
    private Image portrait = null!;
    private PreRunScript menu = null!;

    [HarmonyPatch(typeof(PreRunScript), "Start")]
    private static class AddCardPatch
    {
        private static void Postfix(PreRunScript __instance)
        {
            if (__instance.charImages.Count < 3)
                return;

            __instance.gameObject.AddComponent<AstartesMenu>().CreateCard(__instance);
        }
    }

    private void CreateCard(PreRunScript screen)
    {
        menu = screen;
        var original = screen.charImages[screen.charImages.Count - 1].rectTransform;
        var first = screen.charImages[0].rectTransform;
        var parent = original.parent;
        var position = original.localPosition;
        if (first.parent == parent && Mathf.Abs(position.x - first.localPosition.x) > 0.01f)
        {
            // 让四张卡片占用原有选角区域，并移动分离的内图和底座。
            int count = screen.charImages.Count;
            float originalSpacing = (position.x - first.localPosition.x) / (count - 1);
            float spacing = originalSpacing * count / (count + 1);
            float start = first.localPosition.x - (originalSpacing - spacing) * 0.5f;
            position.x = start + spacing * count;
            for (int i = 0; i < count; i++)
            {
                var originalCard = screen.charImages[i].rectTransform;
                float dx = start + spacing * i - originalCard.localPosition.x;
                Vector3 delta = parent.TransformVector(Vector3.right * dx);
                var inner = screen.charInnerImages[i].transform;
                var podium = screen.charPodiums[i].transform;
                MoveDetached(inner, originalCard, delta);
                MoveDetached(podium, originalCard, delta);
                originalCard.position += delta;
            }
        }
        else
        {
            position.x += original.rect.width * 0.85f;
        }

        var card = new GameObject("Astartes Character", typeof(RectTransform), typeof(Image), typeof(Button));
        var rect = card.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = original.anchorMin;
        rect.anchorMax = original.anchorMax;
        rect.pivot = original.pivot;
        rect.sizeDelta = original.sizeDelta;
        rect.localRotation = original.localRotation;
        rect.localScale = original.localScale;
        rect.localPosition = position;

        portrait = card.GetComponent<Image>();
        portrait.sprite = AstartesPlugin.Sprites.Selection;
        portrait.preserveAspect = true;
        // 矩形内的透明像素也接受点击，便于键鼠和导航选中。
        var button = card.GetComponent<Button>();
        button.targetGraphic = portrait;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => screen.SelectCharacter(AstartesRun.MenuSelection));

        UpdateSelection();
        AstartesPlugin.Log.LogInfo("阿斯塔特选角卡已加入新局界面。");
    }

    private static void MoveDetached(Transform item, Transform card, Vector3 delta)
    {
        if (item != card && !item.IsChildOf(card))
            item.position += delta;
    }

    private void LateUpdate()
    {
        if (portrait && menu)
            UpdateSelection();
    }

    private void UpdateSelection()
    {
        bool selected = PreRunScript.characterSelected == AstartesRun.MenuSelection;
        portrait.color = selected ? Color.white : new Color(0.5f, 0.5f, 0.5f);
    }

    private static bool IsChinese => Locale.currentLangName?.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase) == true;

    [HarmonyPatch(typeof(PreRunScript), "UpdateCharacterText")]
    private static class DetailsPatch
    {
        private static bool Prefix(PreRunScript __instance)
        {
            if (PreRunScript.characterSelected != AstartesRun.MenuSelection)
                return true;

            bool chinese = IsChinese;
            var profile = AstartesPlugin.Profile;
            __instance.charSelectName.text = chinese ? "[AST-01] 阿斯塔特" : "[AST-01] Astartes";
            __instance.charSelectNotes.text = chinese
                ? "[生物分类] — 改造人类\n[战团] — 极限战士\n[装备] — Mk X 动力甲、爆弹枪、链锯剑"
                : "[Species] — Augmented human\n[Chapter] — Ultramarines\n[Equipment] — Mk X armour, boltgun, chainsword";
            __instance.charSelectQuirks.text = chinese
                ? "[基因改造] — 血容量 9 L，体力上限为原版的 10 倍。\n[拉瑞曼器官] — 快速凝血，修复伤口。\n[强化代谢] — 免疫毒素与感染，患病增量为原版的 10%。"
                : "[Gene-forged] — 9 L blood capacity and 10× stamina.\n[Larraman's Organ] — Rapid clotting and wound repair.\n[Augmented metabolism] — Immune to venom and infection. Sickness builds at 10% of the normal rate.";
            __instance.charSelectStats.text = $"[STR] {profile.Strength}\n[RES] {profile.Resilience}\n[INT] {profile.Intelligence}";
            return false;
        }
    }
}
