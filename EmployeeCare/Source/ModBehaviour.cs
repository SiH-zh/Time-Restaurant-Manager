using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

[assembly: System.Reflection.AssemblyTitle("时光餐厅餐厅管家")]
[assembly: System.Reflection.AssemblyVersion("1.1.2.0")]

namespace EmployeeCare
{
    public sealed class ModBehaviour : global::ModBehaviour
    {
        internal static ModBehaviour Current;
        internal CareSettings Settings = new CareSettings();
        internal CareView View;
        internal string Status = "设置已就绪";
        internal string StockStatus = "仅在点击补充按钮时执行，不会自动补充菜品。";
        private Harmony harmony;
        private GameObject entry;
        private MenuGroupUI menu;
        private Sprite icon;
        private Texture2D texture;
        private string settingsPath;
        private float nextMount;
        private bool stopped;

        protected override void OnAfterSetup()
        {
            if (Current != null) throw new InvalidOperationException("餐厅管家已加载，更新后请重启游戏。");
            Patches.CheckCompatibility();
            settingsPath = Path.Combine(Path.GetDirectoryName(GameWorld.GetCurrentSavePath()), "EmployeeCare", "settings.json");
            if (File.Exists(settingsPath))
            {
                try { Settings = JsonConvert.DeserializeObject<CareSettings>(File.ReadAllText(settingsPath)) ?? new CareSettings(); }
                catch (Exception ex) { Debug.LogWarning("[餐厅管家] 设置读取失败，使用默认关闭状态：" + ex.Message); }
            }
            DurationRules.Normalize(Settings);
            Current = this;
            try
            {
                harmony = new Harmony("EmployeeCare.Local.1");
                harmony.PatchAll(typeof(ModBehaviour).Assembly);
                View = gameObject.AddComponent<CareView>();
                View.Owner = this;
                EventManager.Register("ENTER_MAIN_PANEL", Leave);
                EventManager.Register("GUEST_MODE_CHANGE", Leave);
                Debug.Log("[餐厅管家] 1.1.2加载成功；自定义时长=" + Settings.CustomEnabled + "，喂食续期=" + Settings.SyncFoodHire + "，保安喂食=" + Settings.GuardFood + "，菜品按成本收费=" + Settings.ChargeDishCost);
            }
            catch
            {
                if (harmony != null) harmony.UnpatchAll(harmony.Id);
                Current = null;
                throw;
            }
        }

        internal bool Ready()
        {
            return !stopped && GameWorld.Instance != null && GameWorld.g_SQLiteSave != null
                && GameWorld.g_SQLiteSave.IsInitialized && !GameWorld.IsLoadingScene()
                && !GameWorld.IsExiting && !GameWorld.IsAppQuitting && !GuestSessionManager.IsInGuestSession();
        }

        internal bool Owns(EmployeeItem employee)
        {
            if (employee == null || !Ready() || !DurationRules.Eligible(true, employee.bIsSuperEmployee, (int)employee.eType)) return false;
            var list = Restaurant.GetEmployeeList();
            return list != null && list.Contains(employee);
        }

        internal long BaseHireSeconds(EmployeeItem employee)
        {
            return employee.bIsSystemEmployee
                ? EmployeeTierConfig.GetMaxWorkTimeSeconds(employee.eType, employee.nPrice)
                : checked((long)GameRule.nFriendHireHours * 3600L);
        }

        internal void SaveSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            string temp = settingsPath + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(Settings, Formatting.Indented));
            if (File.Exists(settingsPath)) File.Replace(temp, settingsPath, settingsPath + ".bak");
            else File.Move(temp, settingsPath);
        }

        internal void PersistEmployees()
        {
            Restaurant.SaveImmediate();
            EventManager.DelayTrigger("EMPLOYEE_CHANGE");
        }

        internal void ApplyNew(EmployeeItem employee)
        {
            if (!Settings.CustomEnabled || !Owns(employee)) return;
            SetDuration(employee, TimeHelper.GetNowTimeStamp());
            PersistEmployees();
        }

        private void SetDuration(EmployeeItem employee, long now)
        {
            long end = checked(now + Settings.DurationSeconds);
            employee.nStartHireTime = DurationRules.HireStartForEnd(end, BaseHireSeconds(employee));
            employee.nStaminaExpireTime = end;
        }

        internal int EmployeeCount()
        {
            return Ready() ? Restaurant.GetEmployeeList().Count(Owns) : 0;
        }

        internal string EmployeeSummary()
        {
            var employees = Ready() ? Restaurant.GetEmployeeList().Where(Owns).ToList() : new List<EmployeeItem>();
            int guards = employees.Count(e => e.eType == NpcType.Guard);
            return (employees.Count - guards) + "人+" + guards + "保安";
        }

        internal void ApplyExisting()
        {
            if (!Settings.CustomEnabled || !Ready()) return;
            var employees = Restaurant.GetEmployeeList().Where(Owns).ToList();
            long now = TimeHelper.GetNowTimeStamp();
            var snapshots = employees.Select(e => new {
                身份 = e.szUID, 岗位 = e.eType.ToString(), 雇佣开始时间 = e.nStartHireTime, 体力到期时间 = e.nStaminaExpireTime
            }).ToList();
            string root = Path.Combine(Path.GetDirectoryName(GameWorld.GetCurrentSavePath()), "EmployeeCare", "Backups");
            Directory.CreateDirectory(root);
            string backup = Path.Combine(root, "时间修改前_" + TimeHelper.GetNowTime().ToString("yyyyMMdd_HHmmss_fff") + ".json");
            File.WriteAllText(backup, JsonConvert.SerializeObject(new { 存档 = GameWorld.GetCurrentSavePath(), 时间 = now, 原记录 = snapshots }, Formatting.Indented));
            foreach (var e in employees) SetDuration(e, now);
            PersistEmployees();
            Status = "已将当前 " + EmployeeSummary() + " 的体力和雇期统一设为 " + DurationRules.Format(Settings.DurationSeconds, Settings.DisplayDays) + (Settings.DisplayDays ? " 天" : " 小时");
            Debug.Log("[餐厅管家] " + Status + "；已保存修改前时间记录。");
        }

        private void Update()
        {
            if (stopped) return;
            try
            {
                if (Time.unscaledTime >= nextMount)
                {
                    nextMount = Time.unscaledTime + 1;
                    MountEntry();
                }
                if (View != null) View.Tick();
            }
            catch (Exception ex) { Debug.LogError("[餐厅管家] 界面更新失败：" + ex); nextMount = Time.unscaledTime + 5; }
        }

        private void MountEntry()
        {
            if (entry != null && menu != null) return;
            menu = Resources.FindObjectsOfTypeAll<MenuGroupUI>().FirstOrDefault(m => m.gameObject.scene.IsValid());
            if (menu == null) return;
            var original = AccessTools.Field(typeof(MenuGroupUI), "m_btnRestaurant").GetValue(menu) as Button;
            if (original == null) return;
            entry = Instantiate(original.gameObject, original.transform.parent, false);
            entry.name = "EmployeeCare_Entry";
            // 原右上角布局按兄弟顺序向左扩展。插在所有既有模组前面，避免占用其位置。
            int index = original.transform.GetSiblingIndex();
            var nativeButtons = new HashSet<Button>();
            foreach (string field in new[] { "m_btnRestaurant", "m_btnFarm", "m_btnRanch", "m_btnMarket", "m_btnWorkshop" })
            {
                var native = AccessTools.Field(typeof(MenuGroupUI), field).GetValue(menu) as Button;
                if (native != null) nativeButtons.Add(native);
            }
            while (index > 0)
            {
                var previous = original.transform.parent.GetChild(index - 1).GetComponent<Button>();
                if (previous == null || nativeButtons.Contains(previous)) break;
                index--;
            }
            entry.transform.SetSiblingIndex(index);
            var button = entry.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(delegate { if (Ready()) View.Open(); else MsgBox.Show("请先进入自己的餐厅。", 2f, null); });
            var cursor = entry.GetComponent<UICursorHandler>();
            if (cursor != null) Destroy(cursor);
            LoadIcon();
            var tip = entry.GetComponent<TooltipTrigger>();
            if (tip != null) tip.SetTextCallback(delegate(string a, string b) { return "餐厅管家\n雇员工时 · 喂食续期 · 菜品补充"; }, "", false);
            LayoutRebuilder.MarkLayoutForRebuild(menu.transform as RectTransform);
        }

        private void LoadIcon()
        {
            if (entry == null) return;
            string file = Path.Combine(Info.szPath, "icon.png");
            if (!File.Exists(file)) return;
            var nextTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(nextTexture, File.ReadAllBytes(file), true)) { Destroy(nextTexture); return; }
            nextTexture.filterMode = FilterMode.Bilinear;
            var nextIcon = Sprite.Create(nextTexture, new Rect(0, 0, nextTexture.width, nextTexture.height), new Vector2(.5f, .5f), 100);
            var image = entry.GetComponent<Image>();
            image.sprite = nextIcon; image.overrideSprite = null; image.type = Image.Type.Simple; image.preserveAspect = true;
            if (icon != null) Destroy(icon);
            if (texture != null) Destroy(texture);
            icon = nextIcon; texture = nextTexture;
        }

        private void Leave(object ignored) { if (View != null) View.Close(); }

        protected override void OnBeforeDeactivate()
        {
            if (stopped) return;
            stopped = true;
            if (harmony != null) harmony.UnpatchAll(harmony.Id);
            foreach (var layout in Resources.FindObjectsOfTypeAll<GuardDescriptionLayout>())
                layout.Restore();
            Patches.RefreshOpenPanels();
            EventManager.Unregister("ENTER_MAIN_PANEL", Leave);
            EventManager.Unregister("GUEST_MODE_CHANGE", Leave);
            if (View != null) { View.Shutdown(); Destroy(View); }
            if (entry != null) Destroy(entry);
            if (icon != null) Destroy(icon);
            if (texture != null) Destroy(texture);
            if (Current == this) Current = null;
        }
        private void OnDestroy() { if (Current == this) OnBeforeDeactivate(); }
    }
}
