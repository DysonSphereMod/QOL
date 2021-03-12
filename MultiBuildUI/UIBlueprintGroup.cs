using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using com.brokenmass.plugin.DSP.MultiBuildUI;

public class UIBlueprintGroup : MonoBehaviour
{
    // Internal values
    public bool isOpen;

    private UIBuildMenu menu;
    private UIButton button;

    public CanvasGroup mainGroup;
    private float alpha;

    // Use these fields to display info
    public Text infoTitle;
    public Text InfoText;

    public static Action onCreate;
    public static Action onRestore;
    public static Action onImport;
    public static Action onExport;

    public void Init(UIBuildMenu _menu, UIButton _button)
    {
        menu = _menu;
        button = _button;
    }

    public void _Open()
    {
        isOpen = true;
        menu.SetCurrentCategory(12);
        menu.childGroup.gameObject.SetActive(true);
        foreach (UIButton child in menu.childButtons)
        {
            if (child == null) continue;
            child.gameObject.SetActive(false);
        }

        button.highlighted = true;

        mainGroup.interactable = true;
        mainGroup.blocksRaycasts = true;

    }

    public void _Close()
    {
        isOpen = false;
        alpha = -0.5f;
        mainGroup.alpha = 0;
        mainGroup.interactable = false;
        button.highlighted = false;
        mainGroup.blocksRaycasts = false;

    }

    private void Update()
    {
        if (!isOpen) return;

        alpha += Time.deltaTime * 4f;
        mainGroup.alpha = Mathf.Clamp(alpha, -0.5f, 1f);
    }

    // These methods will be called when player presses one of the buttons.
    public void Create()
    {
        onCreate?.Invoke();
    }

    public void Restore()
    {
        onRestore?.Invoke();
    }

    public void Import()
    {
        onImport?.Invoke();
    }

    public void Export()
    {
        onExport?.Invoke();
    }
}

[HarmonyPatch]
static class UIFunctionPanelPatch
{
    private static bool blueprintPanelInit;

    public static UIBlueprintGroup blueprintGroup;

    [HarmonyPatch(typeof(UIFunctionPanel), "_OnClose")]
    [HarmonyPostfix]
    public static void Close(UIFunctionPanel __instance)
    {
        if (blueprintGroup != null)
        {
            blueprintGroup._Close();
        }
    }

    [HarmonyPatch(typeof(UIFunctionPanel), "_OnUpdate")]
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> UpdateConditions(IEnumerable<CodeInstruction> instructions)
    {
        CodeMatcher matcher = new CodeMatcher(instructions)
            .MatchForward(false,
                new CodeMatch(OpCodes.Ldarg_0),
                new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(GameMain), nameof(GameMain.data))),
                new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(GameData), "get_guideRunning"))
            )
            .Advance(1)
            .InsertAndAdvance(Transpilers.EmitDelegate<Action<UIFunctionPanel>>(panel =>
            {
                if (blueprintGroup != null && blueprintGroup.isOpen)
                {
                    panel.posWanted = 0f;
                    panel.widthWanted = 730f;
                }
            }))
            .InsertAndAdvance(new CodeInstruction(OpCodes.Ldarg_0));

        return matcher.InstructionEnumeration();
    }

    [HarmonyPatch(typeof(UIFunctionPanel), "_OnOpen")]
    [HarmonyPostfix]
    public static void Open(UIFunctionPanel __instance)
    {
        if (!blueprintPanelInit)
        {
            UIBuildMenu menu = __instance.buildMenu;
            Transform mainTrs = menu.gameObject.transform.Find("main-group");
            if (mainTrs == null) return;

            GameObject buttonPrefab = MultiBuildUI.bundle.LoadAsset<GameObject>("assets/blueprints/ui/button.prefab");
            GameObject button = Object.Instantiate(buttonPrefab, Vector3.zero, Quaternion.identity, mainTrs);
            button.transform.localPosition = new Vector3(260, 0, 0);
            menu.categoryButtons[10].transform.localPosition += new Vector3(52, 0, 0);
            menu.mainCanvas.transform.localPosition += new Vector3(-26, 0, 0);
            Button blueprintButton = button.GetComponent<Button>();

            GameObject prefab = MultiBuildUI.bundle.LoadAsset<GameObject>("assets/blueprints/ui/blueprint-group.prefab");
            GameObject group = Object.Instantiate(prefab, menu.transform, false);
            blueprintGroup = group.GetComponent<UIBlueprintGroup>();
            blueprintGroup.Init(menu, button.GetComponent<UIButton>());
            blueprintGroup._Close();

            blueprintButton.onClick.AddListener(() =>
            {
                if (blueprintGroup.isOpen)
                {
                    menu.SetCurrentCategory(0);
                }
                else
                {
                    blueprintGroup._Open();
                }
            });


            blueprintPanelInit = true;
        }
    }
}

[HarmonyPatch]
static class UIBuildMenuPatch
{
    [HarmonyPatch(typeof(UIBuildMenu), "SetCurrentCategory")]
    [HarmonyPostfix]
    public static void Close(UIBuildMenu __instance, int category)
    {
        if (category == 0 || category != 12)
        {
            UIFunctionPanelPatch.blueprintGroup._Close();
        }
    }
}

