using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[assembly: AssemblyTitle(ModListMenu.ModListMenuPlugin.PluginName)]
[assembly: AssemblyProduct(ModListMenu.ModListMenuPlugin.PluginName)]
[assembly: AssemblyCopyright("Copyright (c) 2026 lumoren")]
[assembly: AssemblyVersion(ModListMenu.ModListMenuPlugin.PluginVersion)]
[assembly: AssemblyFileVersion(ModListMenu.ModListMenuPlugin.PluginVersion)]

namespace ModListMenu;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class ModListMenuPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "casualtiesunknown.mod-list-menu";
    public const string PluginName = "Mod List Menu";
    public const string PluginVersion = "1.0.0";

    private const string ButtonObjectName = "ModListMenuButton";
    private const string PanelObjectName = "ModListMenuPanel";

    private Harmony _harmony = null!;
    private GameObject? _buttonObject;
    private GameObject? _panelObject;
    private TMP_FontAsset? _font;
    private Material? _fontMaterial;
    private int _lastEnsureFrame;

    private void Awake()
    {
        _harmony = new Harmony(PluginGuid);
        SceneManager.sceneLoaded += OnSceneLoaded;
        Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        Logger.LogInfo("Loaded BepInEx plugins: " + Chainloader.PluginInfos.Count);
    }

    private void Start()
    {
        EnsureMenuUi();
    }

    private void Update()
    {
        if (Time.frameCount - _lastEnsureFrame < 60)
        {
            return;
        }

        _lastEnsureFrame = Time.frameCount;
        EnsureMenuUi();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        _harmony?.UnpatchSelf();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _buttonObject = null;
        _panelObject = null;
        _lastEnsureFrame = 0;
    }

    private void EnsureMenuUi()
    {
        if (_buttonObject != null)
        {
            return;
        }

        CacheFont();

        var source = FindAdaptiveMenuButton();
        if (source != null)
        {
            CreateClonedMenuButton(source);
            return;
        }

        if (!IsGameplayActive())
        {
            CreateFallbackMenuButton();
        }
    }

    private GameObject? FindAdaptiveMenuButton()
    {
        var buttons = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
            .Where(component => component != null
                && component.GetType().Name == "AdaptiveButton"
                && component.gameObject.activeInHierarchy
                && component.gameObject.scene.IsValid())
            .Select(component => component.gameObject)
            .Distinct()
            .ToList();

        if (buttons.Count == 0)
        {
            return null;
        }

        return buttons
            .OrderBy(button => button.transform.GetSiblingIndex())
            .LastOrDefault();
    }

    private void CreateClonedMenuButton(GameObject source)
    {
        var parent = source.transform.parent;
        if (parent == null)
        {
            return;
        }

        var clone = Instantiate(source, parent);
        clone.name = ButtonObjectName;
        clone.transform.SetAsLastSibling();
        DisableGameMenuComponents(clone);

        var label = T("button_mods");
        foreach (var text in clone.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            ApplyFont(text);
            text.text = label;
        }

        var rect = clone.GetComponent<RectTransform>();
        var sourceRect = source.GetComponent<RectTransform>();
        if (rect != null && sourceRect != null && parent.GetComponent<LayoutGroup>() == null)
        {
            var siblingRects = parent.GetComponentsInChildren<RectTransform>(false)
                .Where(item => item != rect && item.parent == parent)
                .ToList();
            var lowestY = siblingRects.Count > 0
                ? siblingRects.Min(item => item.anchoredPosition.y)
                : sourceRect.anchoredPosition.y;
            rect.anchoredPosition = new Vector2(sourceRect.anchoredPosition.x, lowestY - Math.Max(42f, sourceRect.rect.height * 1.15f));
        }

        HookButton(clone);
        _buttonObject = clone;
        Logger.LogInfo("Mod list menu button injected by cloning AdaptiveButton.");
    }

    private void DisableGameMenuComponents(GameObject clone)
    {
        foreach (var component in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            var typeName = component.GetType().Name;
            if (typeName == "AdaptiveButton" || typeName == "UILocalizer")
            {
                component.enabled = false;
                Destroy(component);
            }
        }
    }

    private void CreateFallbackMenuButton()
    {
        var canvas = FindActiveCanvas();
        if (canvas == null)
        {
            return;
        }

        var buttonObject = new GameObject(ButtonObjectName, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(canvas.transform, false);

        var rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-24f, -24f);
        rect.sizeDelta = new Vector2(132f, 42f);

        var image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.06f, 0.07f, 0.08f, 0.92f);
        image.raycastTarget = true;

        var text = CreateText("Label", buttonObject.transform, T("button_mods"), 22, TextAlignmentOptions.Center);
        var textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        HookButton(buttonObject);
        _buttonObject = buttonObject;
        Logger.LogInfo("Mod list fallback button injected.");
    }

    private void HookButton(GameObject buttonObject)
    {
        var button = buttonObject.GetComponent<Button>() ?? buttonObject.AddComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(TogglePanel);

        var graphic = buttonObject.GetComponent<Graphic>() ?? buttonObject.GetComponentInChildren<Graphic>(true);
        if (graphic == null)
        {
            graphic = buttonObject.AddComponent<Image>();
        }

        graphic.raycastTarget = true;
        button.targetGraphic = graphic;
    }

    private Canvas? FindActiveCanvas()
    {
        return Resources.FindObjectsOfTypeAll<Canvas>()
            .Where(canvas => canvas != null && canvas.isActiveAndEnabled && canvas.gameObject.scene.IsValid())
            .OrderByDescending(canvas => canvas.sortingOrder)
            .FirstOrDefault();
    }

    private bool IsGameplayActive()
    {
        var playerCameraType = Type.GetType("PlayerCamera, Assembly-CSharp");
        var mainField = playerCameraType?.GetField("main", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        return mainField?.GetValue(null) != null;
    }

    private void TogglePanel()
    {
        if (_panelObject == null)
        {
            CreatePanel();
            if (_panelObject != null)
            {
                _panelObject.SetActive(true);
            }

            return;
        }

        _panelObject.SetActive(!_panelObject.activeSelf);
    }

    private void CreatePanel()
    {
        var canvas = _buttonObject != null
            ? _buttonObject.GetComponentInParent<Canvas>()
            : FindActiveCanvas();
        if (canvas == null)
        {
            Logger.LogWarning("Cannot create mod list panel: no active Canvas found.");
            return;
        }

        var panel = new GameObject(PanelObjectName, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvas.transform, false);
        panel.transform.SetAsLastSibling();

        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(780f, 560f);

        var background = panel.GetComponent<Image>();
        background.color = new Color(0.02f, 0.025f, 0.03f, 0.96f);
        background.raycastTarget = true;

        var title = CreateText("Title", panel.transform, T("title"), 28, TextAlignmentOptions.Left);
        var titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(24f, -68f);
        titleRect.offsetMax = new Vector2(-92f, -20f);

        var closeButton = CreatePlainButton("CloseButton", panel.transform, T("close"));
        var closeRect = closeButton.GetComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(1f, 1f);
        closeRect.anchorMax = new Vector2(1f, 1f);
        closeRect.pivot = new Vector2(1f, 1f);
        closeRect.anchoredPosition = new Vector2(-20f, -20f);
        closeRect.sizeDelta = new Vector2(56f, 40f);
        closeButton.GetComponent<Button>().onClick.AddListener(() => panel.SetActive(false));

        var scrollObject = new GameObject("ScrollView", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollObject.transform.SetParent(panel.transform, false);
        var scrollRectTransform = scrollObject.GetComponent<RectTransform>();
        scrollRectTransform.anchorMin = new Vector2(0f, 0f);
        scrollRectTransform.anchorMax = new Vector2(1f, 1f);
        scrollRectTransform.offsetMin = new Vector2(24f, 24f);
        scrollRectTransform.offsetMax = new Vector2(-24f, -82f);
        scrollObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.18f);

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(scrollObject.transform, false);
        var viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        var viewportImage = viewport.GetComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.02f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = new Vector2(0f, 0f);
        contentRect.offsetMax = new Vector2(0f, 0f);

        var layout = content.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 10f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        var fitter = content.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scrollRect = scrollObject.GetComponent<ScrollRect>();
        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 28f;

        var entries = LoadPluginEntries();
        foreach (var entry in entries)
        {
            CreateEntry(content.transform, entry);
        }

        Logger.LogInfo("Rendered mod list entries: " + entries.Count);
        _panelObject = panel;
    }

    private Button CreatePlainButton(string name, Transform parent, string label)
    {
        var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        var image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.16f, 0.18f, 0.2f, 0.95f);
        image.raycastTarget = true;

        var text = CreateText("Label", buttonObject.transform, label, 20, TextAlignmentOptions.Center);
        var textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return buttonObject.GetComponent<Button>();
    }

    private TextMeshProUGUI CreateText(string name, Transform parent, string value, int fontSize, TextAlignmentOptions alignment)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        ApplyFont(text);
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = new Color(0.92f, 0.93f, 0.94f, 1f);
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    private void CreateEntry(Transform parent, ModEntry entry)
    {
        var item = new GameObject("ModEntry-" + SanitizeName(entry.Name), typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        item.transform.SetParent(parent, false);

        var image = item.GetComponent<Image>();
        image.color = new Color(0.08f, 0.09f, 0.105f, 0.92f);

        var layoutElement = item.GetComponent<LayoutElement>();
        layoutElement.minHeight = 132f;
        layoutElement.preferredHeight = 132f;

        var name = CreateText("Name", item.transform, entry.Name, 22, TextAlignmentOptions.Left);
        var nameRect = name.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0f, 1f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.offsetMin = new Vector2(14f, -42f);
        nameRect.offsetMax = new Vector2(-14f, -10f);

        var metaText = T("version") + " " + entry.Version;
        if (!string.IsNullOrWhiteSpace(entry.Author))
        {
            metaText += "  |  " + T("author") + " " + entry.Author;
        }

        if (!string.IsNullOrWhiteSpace(entry.Category))
        {
            metaText += "  |  " + T("category") + " " + entry.Category;
        }

        var meta = CreateText("Meta", item.transform, metaText, 16, TextAlignmentOptions.Left);
        meta.color = new Color(0.76f, 0.79f, 0.82f, 1f);
        var metaRect = meta.GetComponent<RectTransform>();
        metaRect.anchorMin = new Vector2(0f, 1f);
        metaRect.anchorMax = new Vector2(1f, 1f);
        metaRect.offsetMin = new Vector2(14f, -66f);
        metaRect.offsetMax = new Vector2(-14f, -42f);

        var description = CreateText("Description", item.transform, entry.Description, 17, TextAlignmentOptions.Left);
        var descriptionRect = description.GetComponent<RectTransform>();
        descriptionRect.anchorMin = new Vector2(0f, 0f);
        descriptionRect.anchorMax = new Vector2(1f, 1f);
        descriptionRect.offsetMin = new Vector2(14f, 34f);
        descriptionRect.offsetMax = new Vector2(-14f, -70f);

        var detail = CreateText("Detail", item.transform, T("guid") + " " + entry.Guid + "  |  " + T("path") + " " + entry.Location, 13, TextAlignmentOptions.Left);
        detail.color = new Color(0.58f, 0.62f, 0.66f, 1f);
        var detailRect = detail.GetComponent<RectTransform>();
        detailRect.anchorMin = new Vector2(0f, 0f);
        detailRect.anchorMax = new Vector2(1f, 0f);
        detailRect.offsetMin = new Vector2(14f, 8f);
        detailRect.offsetMax = new Vector2(-14f, 32f);
    }

    private List<ModEntry> LoadPluginEntries()
    {
        var entries = Chainloader.PluginInfos.Values
            .Select(CreateEntry)
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return entries;
    }

    private ModEntry CreateEntry(PluginInfo plugin)
    {
        var metadata = plugin.Metadata;
        var entry = new ModEntry
        {
            Guid = metadata.GUID,
            Name = metadata.Name,
            Version = metadata.Version.ToString(),
            Description = T("no_description"),
            Location = plugin.Location ?? string.Empty
        };

        var manifestPath = GetManifestPath(plugin.Location);
        if (manifestPath == null)
        {
            return entry;
        }

        try
        {
            var manifest = JsonConvert.DeserializeObject<ModInfoManifest>(File.ReadAllText(manifestPath));
            if (manifest == null)
            {
                Logger.LogWarning("Skipped empty modinfo.json: " + manifestPath);
                return entry;
            }

            if (!string.IsNullOrWhiteSpace(manifest.Name))
            {
                entry.Name = manifest.Name!;
            }

            if (!string.IsNullOrWhiteSpace(manifest.Description))
            {
                entry.Description = manifest.Description!;
            }

            entry.Author = manifest.Author ?? string.Empty;
            entry.Category = manifest.Category ?? string.Empty;
            Logger.LogInfo("Loaded modinfo.json: " + manifestPath);
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Failed to parse modinfo.json at " + manifestPath + ": " + ex.Message);
        }

        return entry;
    }

    private static string? GetManifestPath(string? pluginLocation)
    {
        if (string.IsNullOrWhiteSpace(pluginLocation))
        {
            return null;
        }

        var pluginDir = Path.GetDirectoryName(pluginLocation);
        if (string.IsNullOrWhiteSpace(pluginDir))
        {
            return null;
        }

        var root = Path.GetFullPath(Paths.PluginPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dir = Path.GetFullPath(pluginDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(root, dir, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var manifestPath = Path.Combine(dir, "modinfo.json");
        return File.Exists(manifestPath) ? manifestPath : null;
    }

    private void CacheFont()
    {
        if (_font != null)
        {
            return;
        }

        var text = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>()
            .FirstOrDefault(item => item != null && item.font != null);
        if (text == null)
        {
            return;
        }

        _font = text.font;
        _fontMaterial = text.fontMaterial;
    }

    private void ApplyFont(TextMeshProUGUI text)
    {
        if (_font != null)
        {
            text.font = _font;
        }

        if (_fontMaterial != null)
        {
            text.fontMaterial = _fontMaterial;
        }
    }

    private static string SanitizeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    private static bool IsChinese()
    {
        var lang = GetCurrentLanguageName();
        return lang.IndexOf("zh", StringComparison.OrdinalIgnoreCase) >= 0
            || lang.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) >= 0
            || lang.IndexOf("中文", StringComparison.OrdinalIgnoreCase) >= 0
            || lang.IndexOf("简体", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string GetCurrentLanguageName()
    {
        try
        {
            var localeType = Type.GetType("Locale, Assembly-CSharp");
            var currentName = localeType?.GetField("currentLangName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(currentName))
            {
                return currentName!;
            }

            var currentLang = localeType?.GetField("currentLang", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            var name = currentLang?.GetType().GetField("name", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(currentLang) as string;
            return name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string T(string key)
    {
        var zh = IsChinese();
        return key switch
        {
            "button_mods" => zh ? "模组" : "Mods",
            "title" => zh ? "已加载模组" : "Loaded Mods",
            "close" => zh ? "关闭" : "Close",
            "version" => zh ? "版本:" : "Version:",
            "author" => zh ? "作者:" : "Author:",
            "category" => zh ? "分类:" : "Category:",
            "guid" => zh ? "GUID:" : "GUID:",
            "path" => zh ? "路径:" : "Path:",
            "no_description" => zh ? "此模组没有提供说明文件。" : "This mod did not provide a description file.",
            _ => key
        };
    }

    private sealed class ModInfoManifest
    {
        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("description")]
        public string? Description { get; set; }

        [JsonProperty("author")]
        public string? Author { get; set; }

        [JsonProperty("category")]
        public string? Category { get; set; }
    }

    private sealed class ModEntry
    {
        public string Guid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
    }
}
