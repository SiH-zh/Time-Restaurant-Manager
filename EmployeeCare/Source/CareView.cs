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
        private RectTransform employeePage, stockPage;
        private TMP_InputField stockInput;
        private TMP_Text chargeLabel, stockPreview, stockStatus;
        private Button employeeTab, stockTab;
        private GameObject alertObject;
        private TMP_Text alertTitle, alertBody;
        private Image stockNotice;
        private bool stockError;
        private Button stockApply;
        private readonly StockInputState stockEntry = new StockInputState();
        private bool invalidStockInput;
        private float nextStockRefresh;
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
        private string pendingRoster;

        internal void Open()
        {
            if (!Owner.Ready()) return;
            if (canvasObject == null) Build();
            visible = true;
            canvasObject.SetActive(true);
            if (!registered) { UIManager.RegisterEscHandler(this); registered = true; }
            input.SetTextWithoutNotify(DurationRules.Format(Owner.Settings.DurationSeconds, Owner.Settings.DisplayDays));
            CancelPending();
            ShowPage(false);
            Refresh();
        }

        internal void Close()
        {
            visible = false;
            if (alertObject != null) alertObject.SetActive(false);
            if (canvasObject != null) canvasObject.SetActive(false);
            if (registered) { UIManager.UnregisterEscHandler(this); registered = false; }
            CancelPending();
        }

        public bool OnEscapePressed()
        {
            if (!visible) return false;
            if (alertObject != null && alertObject.activeSelf) { alertObject.SetActive(false); return true; }
            Close(); return true;
        }
        internal void Shutdown()
        {
            Close();
            if (canvasObject != null) Destroy(canvasObject);
            // 材质仅归本模组所有；不改动也不销毁游戏共享字体材质。
            if (material != null) { Destroy(material); material = null; }
        }
        internal void Tick()
        {
            if (!visible) return;
            if (!Owner.Ready()) { Close(); return; }
            if (stockPage != null && stockPage.gameObject.activeSelf && Time.unscaledTime >= nextStockRefresh)
            { nextStockRefresh = Time.unscaledTime + 1; RefreshStock(); }
        }

        private void Build()
        {
            PrepareBodyFont();
            var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
            confirmSprite = sprites.FirstOrDefault(s => s.name == "Btn_Confirm");
            cancelSprite = sprites.FirstOrDefault(s => s.name == "Btn_Cancel");
            panelSprite = sprites.FirstOrDefault(s => s.name == "Tooltip_Bg");
            canvasObject = new GameObject("餐厅管家界面", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f); panel.sizeDelta = new Vector2(920, 840);
            var background = panel.gameObject.AddComponent<Image>(); background.sprite = panelSprite;
            background.type = panelSprite == null ? Image.Type.Simple : Image.Type.Sliced;
            background.color = panelSprite == null ? (Color)new Color32(255, 247, 225, 255) : Color.white;
            var blocker = panel.gameObject.AddComponent<Button>(); blocker.transition = Selectable.Transition.None; blocker.targetGraphic = background;
            Label(panel, "餐厅管家", 36, 25, 700, 54, 36);
            Label(panel, "工时、喂食续期与在售菜品补充", 38, 83, 650, 32, 21);
            Button(panel, "关闭", 774, 32, 108, 48, false, Close);

            employeeTab = Button(panel, "雇员管理", 40, 132, 410, 48, false, delegate { ShowPage(false); });
            stockTab = Button(panel, "菜品补充", 470, 132, 410, 48, false, delegate { ShowPage(true); });
            employeePage = Rect("雇员管理页", panel); Place(employeePage, 0, 62, 920, 778);
            Label(employeePage, "自定义工作时长", 38, 145, 580, 40, 27);
            customLabel = Button(employeePage, "", 696, 143, 184, 48, true, delegate {
                if (!CommitDuration()) return;
                Owner.Settings.CustomEnabled = !Owner.Settings.CustomEnabled;
                Owner.SaveSettings(); CancelPending(); Patches.RefreshOpenPanels(); Refresh();
            }).GetComponentInChildren<TMP_Text>();
            input = Input(employeePage, 40, 205, 250, 58);
            unit = Button(employeePage, "", 304, 205, 152, 58, false, delegate {
                if (!CommitDuration()) return;
                Owner.Settings.DisplayDays = !Owner.Settings.DisplayDays;
                input.SetTextWithoutNotify(DurationRules.Format(Owner.Settings.DurationSeconds, Owner.Settings.DisplayDays));
                Owner.SaveSettings(); Refresh();
            }).GetComponentInChildren<TMP_Text>();
            Button(employeePage, "保存时长", 476, 205, 186, 58, true, delegate {
                if (CommitDuration()) { Owner.Status = "已保存时长；不会自动重置现有员工"; Owner.SaveSettings(); Refresh(); }
            });
            Label(employeePage, "支持小数，范围为1分钟至3650天。点击单位可切换天／小时。", 40, 275, 840, 34, 19);
            Label(employeePage, "开启后，新雇员的体力与雇期使用此时长；现有员工需手动应用。", 40, 314, 840, 44, 21);
            applyButton = Button(employeePage, "", 40, 370, 840, 55, true, ApplyClicked);
            applyLabel = applyButton.GetComponentInChildren<TMP_Text>();
            hint = Label(employeePage, "", 40, 433, 840, 55, 19);

            Label(employeePage, "喂食同步延长雇期", 40, 500, 625, 40, 26);
            foodLabel = Button(employeePage, "", 696, 495, 184, 48, true, delegate {
                Owner.Settings.SyncFoodHire = !Owner.Settings.SyncFoodHire; Owner.SaveSettings(); Patches.RefreshOpenPanels(); Refresh();
            }).GetComponentInChildren<TMP_Text>();
            Label(employeePage, "仍按原价扣金币，按实际恢复的体力时长续期；未恢复时不续期。", 40, 548, 840, 38, 20);

            Label(employeePage, "保安也能用食物补充体力", 40, 598, 625, 40, 26);
            guardLabel = Button(employeePage, "", 696, 594, 184, 48, true, delegate {
                Owner.Settings.GuardFood = !Owner.Settings.GuardFood; Owner.SaveSettings(); Patches.RefreshOpenPanels(); Refresh();
            }).GetComponentInChildren<TMP_Text>();
            Label(employeePage, "在保安的雇员室中显示原来的五种食物，恢复比例与收费规则不变。", 40, 644, 840, 38, 20);

            status = Label(employeePage, "", 40, 694, 840, 36, 18);
            BuildStockPage();
            BuildAlert(root);
        }

        private void PrepareBodyFont()
        {
            // 正文常用字体优先，按使用次数及名称排序，避免取决于第一个被枚举的标题。
            var fonts = Resources.FindObjectsOfTypeAll<TMP_Text>()
                .Where(t => t.font != null && t.gameObject.scene.IsValid()
                    && (t.fontStyle & FontStyles.Bold) == 0)
                .GroupBy(t => t.font)
                .OrderByDescending(g => g.Key.HasCharacter('餐'))
                .ThenByDescending(g => g.Count())
                .ThenBy(g => g.Key.name, StringComparer.Ordinal)
                .Select(g => g.Key);
            font = fonts.FirstOrDefault() ?? TMP_Settings.defaultFontAsset;
            if (font == null) throw new InvalidOperationException("正文中文字体尚未加载，请稍后重新打开餐厅管家。");
            if (font.material == null) throw new InvalidOperationException("正文中文字体材质尚未加载，请稍后重新打开餐厅管家。");

            // 只复制字体资产的基础材质，不继承任意游戏文字的标题／描边预设。
            material = new Material(font.material);
            material.name = "EmployeeCare_正文独立材质";
            SetFontFloat("_FaceDilate", 0);
            SetFontFloat("_OutlineWidth", 0);
            SetFontFloat("_OutlineSoftness", 0);
            SetFontFloat("_WeightNormal", 0);
            SetFontFloat("_WeightBold", .3f);
            SetFontFloat("_UnderlayDilate", 0);
            SetFontFloat("_UnderlaySoftness", 0);
            SetFontFloat("_UnderlayOffsetX", 0);
            SetFontFloat("_UnderlayOffsetY", 0);
            SetFontFloat("_GlowOuter", 0);
            if (material.HasProperty("_FaceColor")) material.SetColor("_FaceColor", Color.white);
            if (material.HasProperty("_OutlineColor")) material.SetColor("_OutlineColor", Color.clear);
            if (material.HasProperty("_UnderlayColor")) material.SetColor("_UnderlayColor", Color.clear);
            material.DisableKeyword("UNDERLAY_ON");
            material.DisableKeyword("UNDERLAY_INNER");
            material.DisableKeyword("GLOW_ON");
            material.DisableKeyword("BEVEL_ON");
            Debug.Log("[餐厅管家] 正文字体=" + font.name + "；使用独立材质，字面扩张与描边已归零。");
        }

        private void SetFontFloat(string property, float value)
        { if (material.HasProperty(property)) material.SetFloat(property, value); }

        private void ShowPage(bool stock)
        {
            CancelPending();
            employeePage.gameObject.SetActive(!stock);
            stockPage.gameObject.SetActive(stock);
            SetButtonState(employeeTab, !stock);
            SetButtonState(stockTab, stock);
            Refresh();
            if (stock) RefreshStock();
        }

        private void BuildStockPage()
        {
            stockPage = Rect("菜品补充页", panel); Place(stockPage, 0, 204, 920, 620);
            Label(stockPage, "给每道当前在售菜品增加份数", 40, 0, 840, 40, 28);
            Label(stockPage, "包含主菜、配餐和精品菜；已上架但售罄的菜也能补充。", 40, 46, 840, 34, 21);
            Label(stockPage, "每次每道最多增加10万份，每道库存最多100万份。", 40, 82, 840, 34, 20);
            int[] amounts = { 10, 100, 1000, 10000 };
            for (int i = 0; i < amounts.Length; i++)
            {
                int amount = amounts[i];
                Button(stockPage, "+" + amount, 40 + i * 214, 133, 198, 58, true, delegate {
                    stockEntry.SetShortcut(amount); stockInput.SetTextWithoutNotify(stockEntry.Text);
                    invalidStockInput = false; AddStock(amount);
                });
            }
            Label(stockPage, "自定义每道增加份数（1至100000的整数）", 40, 210, 840, 36, 22);
            stockInput = Input(stockPage, 40, 258, 385, 58);
            stockInput.gameObject.name = "菜品份数输入";
            stockInput.contentType = TMP_InputField.ContentType.Standard;
            stockInput.characterLimit = 0; // 不截断粘贴内容，避免将过大数截成较小的合法数。
            stockInput.SetTextWithoutNotify(stockEntry.Text);
            stockInput.onValueChanged.AddListener(delegate(string value) {
                invalidStockInput = !stockEntry.Accept(value);
                stockError = invalidStockInput;
                stockInput.SetTextWithoutNotify(stockEntry.Text);
                Owner.StockStatus = stockEntry.Faulted
                    ? "输入已拒绝：只接受整数。请先清空输入框，再输入1至100000；也可点击快捷按钮。"
                    : (invalidStockInput ? "请输入1至100000的整数。" : "份数已填写；点击补充按钮后才会生效。");
                RefreshStock();
            });
            stockApply = Button(stockPage, "按输入份数补充", 448, 258, 432, 58, true, delegate {
                int amount;
                if (invalidStockInput || !StockRules.TryAmount(stockInput.text, out amount)) return;
                AddStock(amount);
            });
            Label(stockPage, "按真实成本扣金币", 40, 340, 620, 42, 26);
            chargeLabel = Button(stockPage, "", 696, 338, 184, 48, false, delegate {
                bool before = Owner.Settings.ChargeDishCost;
                Owner.Settings.ChargeDishCost = !before;
                try { Owner.SaveSettings(); stockError = false; Owner.StockStatus = "收费方式已保存；不会自动补充菜品。"; }
                catch (Exception ex) { Owner.Settings.ChargeDishCost = before; Owner.StockStatus = "设置未保存：" + ex.Message; }
                RefreshStock();
            }).GetComponentInChildren<TMP_Text>();
            Label(stockPage, "开启：按原版单份成本累计扣费，余额不足则整次取消。关闭：免费补充。", 40, 397, 840, 58, 20);
            stockPreview = Label(stockPage, "", 40, 459, 840, 100, 21);
            var notice = Rect("补充结果提示", stockPage); Place(notice, 34, 558, 852, 62);
            stockNotice = notice.gameObject.AddComponent<Image>(); stockNotice.raycastTarget = false;
            stockStatus = Label(notice, "", 10, 3, 832, 56, 21);
        }

        private void RefreshStock()
        {
            if (stockInput == null) return;
            chargeLabel.text = Owner.Settings.ChargeDishCost ? "已开启" : "已关闭";
            SetButtonState(chargeLabel.GetComponentInParent<Button>(true), Owner.Settings.ChargeDishCost);
            int amount;
            bool valid = !invalidStockInput && StockRules.TryAmount(stockInput.text, out amount);
            stockApply.interactable = valid;
            stockStatus.text = Owner.StockStatus;
            stockStatus.color = stockError ? (Color)new Color32(157, 35, 35, 255) : ink;
            stockNotice.color = stockError ? (Color)new Color32(255, 219, 210, 255) : new Color(0, 0, 0, 0);
            if (!valid) { stockPreview.text = "请输入有效份数后查看总费用；快捷按钮按其标注份数直接补充。"; return; }
            StockRules.TryAmount(stockInput.text, out amount);
            try
            {
                bool insufficient;
                stockPreview.text = StockService.Preview(Owner, amount, out insufficient);
                stockPreview.color = insufficient ? (Color)new Color32(177, 37, 31, 255) : ink;
                stockPreview.fontStyle = insufficient ? FontStyles.Bold : FontStyles.Normal;
            }
            catch (Exception ex) { stockPreview.text = ex.Message; stockPreview.color = ink; stockPreview.fontStyle = FontStyles.Normal; }
        }

        private void AddStock(int amount)
        {
            try { Owner.StockStatus = StockService.Apply(Owner, amount); stockError = false; }
            catch (Exception ex)
            {
                Owner.StockStatus = ex is OverflowException ? "金额计算超过安全范围，未扣金币，也未补充菜品。" : ex.Message;
                stockError = true;
                var shortage = ex as StockBalanceException;
                string body = Owner.StockStatus;
                if (shortage != null)
                    body = "本次需要：" + shortage.Required.ToString("N0") + " 金币\n当前余额：" + shortage.Balance.ToString("N0")
                        + " 金币\n还差：" + (checked((decimal)shortage.Required - shortage.Balance)).ToString("N0")
                        + " 金币\n\n未扣金币，也未补充任何菜品。\n可减少补充份数，或关闭按成本收费。";
                ShowAlert(shortage != null ? "金币余额不足" : "本次补充未完成", body);
                Debug.LogWarning("[餐厅管家] 补充未完成：" + ex);
            }
            RefreshStock();
        }

        private void BuildAlert(Transform root)
        {
            var overlay = Rect("重要操作提醒", root); Stretch(overlay); alertObject = overlay.gameObject;
            var shade = overlay.gameObject.AddComponent<Image>(); shade.color = new Color(0, 0, 0, .58f);
            var blocker = overlay.gameObject.AddComponent<Button>(); blocker.targetGraphic = shade; blocker.transition = Selectable.Transition.None;
            var box = Rect("提醒面板", overlay);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(.5f, .5f); box.sizeDelta = new Vector2(720, 445);
            var background = box.gameObject.AddComponent<Image>(); background.color = new Color32(255, 245, 224, 255);
            var border = Rect("提醒色条", box); Place(border, 0, 0, 720, 10);
            border.gameObject.AddComponent<Image>().color = new Color32(183, 48, 37, 255);
            alertTitle = Label(box, "", 34, 30, 652, 58, 35); alertTitle.color = new Color32(177, 37, 31, 255); alertTitle.fontStyle = FontStyles.Bold;
            alertBody = Label(box, "", 36, 99, 648, 240, 25);
            Button(box, "知道了", 212, 365, 296, 56, false, delegate { alertObject.SetActive(false); });
            alertObject.SetActive(false);
        }

        private void ShowAlert(string title, string body)
        {
            alertTitle.text = title; alertBody.text = body;
            alertObject.transform.SetAsLastSibling(); alertObject.SetActive(true);
        }

        private void SetButtonState(Button button, bool enabled)
        {
            var image = button.GetComponent<Image>();
            image.sprite = enabled ? confirmSprite : cancelSprite;
            image.color = image.sprite != null ? Color.white : (enabled ? (Color)new Color32(133, 181, 68, 255) : (Color)new Color32(245, 204, 103, 255));
            var label = button.GetComponentInChildren<TMP_Text>(true);
            label.fontStyle = FontStyles.Normal;
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
            string roster = Owner.EmployeeSummary();
            if (pendingContext != context || pendingDuration != Owner.Settings.DurationSeconds || pendingRoster != roster)
            {
                pendingContext = context; pendingDuration = Owner.Settings.DurationSeconds; pendingRoster = roster;
                applyLabel.text = "再次点击：确认重设当前 " + roster;
                hint.text = "将体力和剩余雇期都重设为指定时长，可能缩短现有时间。执行前自动备份原时间。";
                return;
            }
            try { Owner.ApplyExisting(); }
            catch (Exception ex) { Owner.Status = "未完成：" + ex.Message; Debug.LogError("[餐厅管家] " + ex); }
            CancelPending(); Refresh(); Patches.RefreshOpenPanels();
        }

        private void CancelPending() { pendingContext = null; pendingDuration = 0; pendingRoster = null; }
        private void Refresh()
        {
            if (canvasObject == null) return;
            CancelPending();
            customLabel.text = Owner.Settings.CustomEnabled ? "已开启" : "已关闭";
            foodLabel.text = Owner.Settings.SyncFoodHire ? "已开启" : "已关闭";
            guardLabel.text = Owner.Settings.GuardFood ? "已开启" : "已关闭";
            SetButtonState(customLabel.GetComponentInParent<Button>(true), Owner.Settings.CustomEnabled);
            SetButtonState(foodLabel.GetComponentInParent<Button>(true), Owner.Settings.SyncFoodHire);
            SetButtonState(guardLabel.GetComponentInParent<Button>(true), Owner.Settings.GuardFood);
            unit.text = Owner.Settings.DisplayDays ? "天（切换）" : "小时（切换）";
            applyLabel.text = "将当前 " + Owner.EmployeeSummary() + " 的体力与雇期设为 "
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
            label.fontStyle = FontStyles.Normal;
            label.fontWeight = FontWeight.Regular;
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
            var colors = button.colors; colors.disabledColor = new Color(.48f, .48f, .48f, .65f); button.colors = colors;
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
