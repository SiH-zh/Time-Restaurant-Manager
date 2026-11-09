using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EmployeeCare
{
    public sealed class CareView : MonoBehaviour, IEscHandler
    {
        internal ModBehaviour Owner;
        private GameObject canvasObject;
        private RectTransform panel;
        private TMP_FontAsset font;
        private Material material;
        private TMP_InputField input;
        private TMP_Text unit;
        private TMP_Text customLabel;
        private TMP_Text foodLabel;
        private TMP_Text guardLabel;
        private TMP_Text status;
        private TMP_Text applyLabel;
        private TMP_Text hint;
        private Button applyButton;
        private Sprite confirmSprite;
        private Sprite cancelSprite;
        private Sprite panelSprite;
        private readonly Color ink = new Color32(80, 62, 43, 255);
        private bool visible;
        private bool registered;
        private string pendingContext;
        private long pendingDuration;
        private int pendingCount;

        internal void Open()
        {
            if (!Owner.Ready()) return;
            if (canvasObject == null) Build();
            visible = true;
            canvasObject.SetActive(true);
            if (!registered) { UIManager.RegisterEscHandler(this); registered = true; }
            input.SetTextWithoutNotify(DurationRules.Format(Owner.Settings.DurationSeconds, Owner.Settings.DisplayDays));
            CancelPending();
            Refresh();
        }

        internal void Close()
        {
            visible = false;
            if (canvasObject != null) canvasObject.SetActive(false);
            if (registered) { UIManager.UnregisterEscHandler(this); registered = false; }
            CancelPending();
        }

        public bool OnEscapePressed() { if (!visible) return false; Close(); return true; }
        internal void Shutdown() { Close(); if (canvasObject != null) Destroy(canvasObject); }
        internal void Tick() { if (visible && !Owner.Ready()) Close(); }

        private void Build()
        {
            var sample = Resources.FindObjectsOfTypeAll<TMP_Text>().FirstOrDefault(t => t.font != null && t.gameObject.scene.IsValid());
            font = sample == null ? TMP_Settings.defaultFontAsset : sample.font;
            material = sample == null ? null : sample.fontSharedMaterial;
            var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
            confirmSprite = sprites.FirstOrDefault(s => s.name == "Btn_Confirm");
            cancelSprite = sprites.FirstOrDefault(s => s.name == "Btn_Cancel");
            panelSprite = sprites.FirstOrDefault(s => s.name == "Tooltip_Bg");
            canvasObject = new GameObject("雇员关怀界面", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(canvasObject);
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 3100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var root = Rect("遮罩", canvasObject.transform); Stretch(root);
            var shade = root.gameObject.AddComponent<Image>(); shade.color = new Color(0, 0, 0, .45f);
            var shadeButton = root.gameObject.AddComponent<Button>(); shadeButton.targetGraphic = shade;
            shadeButton.onClick.AddListener(Close);
            panel = Rect("设置面板", root);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f); panel.sizeDelta = new Vector2(920, 750);
            var background = panel.gameObject.AddComponent<Image>(); background.sprite = panelSprite;
            background.type = panelSprite == null ? Image.Type.Simple : Image.Type.Sliced;
            background.color = panelSprite == null ? (Color)new Color32(255, 247, 225, 255) : Color.white;
            var blocker = panel.gameObject.AddComponent<Button>(); blocker.transition = Selectable.Transition.None; blocker.targetGraphic = background;
            Label(panel, "雇员关怀", 36, 25, 700, 54, 36);
            Label(panel, "体力与雇期一起管理", 38, 83, 650, 32, 21);
            Button(panel, "关闭", 774, 32, 108, 48, false, Close);

            Label(panel, "自定义工作时长", 38, 145, 580, 40, 27);
            customLabel = Button(panel, "", 696, 143, 184, 48, true, delegate {
                if (!CommitDuration()) return;
                Owner.Settings.CustomEnabled = !Owner.Settings.CustomEnabled;
                Owner.SaveSettings(); CancelPending(); Patches.RefreshOpenPanels(); Refresh();
            }).GetComponentInChildren<TMP_Text>();
            input = Input(panel, 40, 205, 250, 58);
            unit = Button(panel, "", 304, 205, 152, 58, false, delegate {
                if (!CommitDuration()) return;
                Owner.Settings.DisplayDays = !Owner.Settings.DisplayDays;
                input.SetTextWithoutNotify(DurationRules.Format(Owner.Settings.DurationSeconds, Owner.Settings.DisplayDays));
                Owner.SaveSettings(); Refresh();
            }).GetComponentInChildren<TMP_Text>();
            Button(panel, "保存时长", 476, 205, 186, 58, true, delegate {
                if (CommitDuration()) { Owner.Status = "已保存时长；不会自动重置现有员工"; Owner.SaveSettings(); Refresh(); }
            });
            Label(panel, "支持小数，范围为1分钟至3650天。点击单位可切换天／小时。", 40, 275, 840, 34, 19);
            Label(panel, "开启后，新雇员的体力与雇期使用此时长；现有员工需手动应用。", 40, 314, 840, 44, 21);
            applyButton = Button(panel, "", 40, 370, 840, 55, true, ApplyClicked);
            applyLabel = applyButton.GetComponentInChildren<TMP_Text>();
            hint = Label(panel, "", 40, 433, 840, 55, 19);

            Label(panel, "喂食同步延长雇期", 40, 500, 625, 40, 26);
            foodLabel = Button(panel, "", 696, 495, 184, 48, true, delegate {
                Owner.Settings.SyncFoodHire = !Owner.Settings.SyncFoodHire; Owner.SaveSettings(); Patches.RefreshOpenPanels(); Refresh();
            }).GetComponentInChildren<TMP_Text>();
            Label(panel, "仍按原价扣金币，按实际恢复的体力时长续期；未恢复时不续期。", 40, 548, 840, 38, 20);

            Label(panel, "保安也能用食物补充体力", 40, 598, 625, 40, 26);
            guardLabel = Button(panel, "", 696, 594, 184, 48, true, delegate {
                Owner.Settings.GuardFood = !Owner.Settings.GuardFood; Owner.SaveSettings(); Patches.RefreshOpenPanels(); Refresh();
            }).GetComponentInChildren<TMP_Text>();
            Label(panel, "在保安的雇员室中显示原来的五种食物，恢复比例与收费规则不变。", 40, 644, 840, 38, 20);

            status = Label(panel, "", 40, 694, 840, 36, 18);
        }

        private bool CommitDuration()
        {
            long seconds;
            if (!DurationRules.TryParse(input.text, Owner.Settings.DisplayDays, out seconds))
            { status.text = "未保存：请输入有效正数，时长须为1分钟至3650天。"; return false; }
            if (Owner.Settings.DurationSeconds != seconds) CancelPending();
            Owner.Settings.DurationSeconds = seconds;
            return true;
        }

        private void ApplyClicked()
        {
            if (!Owner.Ready() || !Owner.Settings.CustomEnabled || !CommitDuration()) return;
            Owner.SaveSettings();
            string context = GameWorld.GetCurrentSavePath() + "|" + Player.m_szPlayerId;
            int count = Owner.EmployeeCount();
            if (pendingContext != context || pendingDuration != Owner.Settings.DurationSeconds || pendingCount != count)
            {
                pendingContext = context; pendingDuration = Owner.Settings.DurationSeconds; pendingCount = count;
                applyLabel.text = "再次点击：确认重设当前 " + count + " 人";
                hint.text = "将体力和剩余雇期都重设为指定时长，可能缩短现有时间。执行前自动备份原时间。";
                return;
            }
            try { Owner.ApplyExisting(); }
            catch (Exception ex) { Owner.Status = "未完成：" + ex.Message; Debug.LogError("[雇员关怀] " + ex); }
            CancelPending(); Refresh(); Patches.RefreshOpenPanels();
        }

        private void CancelPending() { pendingContext = null; pendingDuration = 0; pendingCount = 0; }
        private void Refresh()
        {
            if (canvasObject == null) return;
            CancelPending();
            customLabel.text = Owner.Settings.CustomEnabled ? "已开启" : "已关闭";
            foodLabel.text = Owner.Settings.SyncFoodHire ? "已开启" : "已关闭";
            guardLabel.text = Owner.Settings.GuardFood ? "已开启" : "已关闭";
            unit.text = Owner.Settings.DisplayDays ? "天（切换）" : "小时（切换）";
            applyLabel.text = "将当前 " + Owner.EmployeeCount() + " 人的体力与雇期设为 "
                + DurationRules.Format(Owner.Settings.DurationSeconds, Owner.Settings.DisplayDays) + (Owner.Settings.DisplayDays ? " 天" : " 小时");
            applyButton.interactable = Owner.Settings.CustomEnabled;
            hint.text = "保存时长和切换开关均不会自动重置已有员工；本次只影响你自己的餐厅。";
            status.text = Owner.Status;
        }

        private TMP_Text Label(Transform parent, string text, float x, float y, float w, float h, float size)
        {
            var rect = Rect("文字", parent); Place(rect, x, y, w, h);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) label.font = font;
            if (material != null) label.fontSharedMaterial = material;
            label.text = text; label.fontSize = size; label.color = ink;
            label.alignment = TextAlignmentOptions.MidlineLeft; label.enableWordWrapping = true;
            label.raycastTarget = false; label.richText = false; label.overflowMode = TextOverflowModes.Truncate;
            return label;
        }

        private Button Button(Transform parent, string caption, float x, float y, float w, float h, bool positive, Action action)
        {
            var rect = Rect(caption, parent); Place(rect, x, y, w, h);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = positive ? confirmSprite : cancelSprite;
            image.type = image.sprite == null ? Image.Type.Simple : Image.Type.Sliced;
            image.color = image.sprite != null ? Color.white : (positive ? (Color)new Color32(172, 199, 102, 255) : (Color)new Color32(247, 209, 115, 255));
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.onClick.AddListener(delegate { action(); });
            var nav = button.navigation; nav.mode = Navigation.Mode.None; button.navigation = nav;
            var label = Label(rect, caption, 10, 0, w - 20, h, 23); label.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private TMP_InputField Input(Transform parent, float x, float y, float w, float h)
        {
            var rect = Rect("工作时长输入", parent); Place(rect, x, y, w, h);
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color32(255, 255, 248, 255);
            var field = rect.gameObject.AddComponent<TMP_InputField>(); field.targetGraphic = image;
            var viewport = Rect("输入可视范围", rect); Stretch(viewport); viewport.offsetMin = new Vector2(12, 4); viewport.offsetMax = new Vector2(-12, -4);
            viewport.gameObject.AddComponent<RectMask2D>();
            var label = Label(viewport, "30", 0, 0, w - 24, h - 8, 29) as TextMeshProUGUI;
            Stretch(label.rectTransform);
            field.textViewport = viewport; field.textComponent = label;
            field.contentType = TMP_InputField.ContentType.DecimalNumber;
            field.lineType = TMP_InputField.LineType.SingleLine; field.characterLimit = 24;
            return field;
        }

        private static RectTransform Rect(string name, Transform parent)
        { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return go.GetComponent<RectTransform>(); }
        private static void Place(RectTransform rect, float x, float y, float w, float h)
        { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; }
    }
}
