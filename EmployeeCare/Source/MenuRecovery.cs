using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace EmployeeCare
{
    internal static class MenuRecovery
    {
        private const int OriginalPerCategory = 3;
        private const int ExpandedPerCategory = 6;
        private static readonly string[] CategoryNames = { "主菜", "配餐", "精品菜" };

        internal sealed class Plan
        {
            internal MenuData Menu;
            internal MenuSlot[] Source;
            internal ContentId[] DishIds;
            internal string SavePath;
            internal int[] Targets;
            internal List<int> Displaced;
        }

        internal static bool IsAvailable()
        {
            var menu = Restaurant.m_MenuData;
            var selling = SellingMenuUI.Instance;
            return menu != null && menu.m_MenuSlots != null && menu.m_MenuSlots.Length == StockRules.ExpandedSlotCount
                && !StockService.HasExpansionPatch() && selling != null && selling.SlotBoxArray != null
                && selling.SlotBoxArray.Length == StockRules.OriginalSlotCount;
        }

        internal static Plan Preview(ModBehaviour owner)
        {
            StockService.CheckContext(owner);
            if (!IsAvailable()) throw new InvalidOperationException("仅在菜槽扩展已停用、原版9槽界面已加载时可恢复。");
            var menu = Restaurant.m_MenuData;
            var slots = menu.m_MenuSlots;
            var plan = new Plan {
                Menu = menu, Source = slots, SavePath = GameWorld.GetCurrentSavePath(),
                DishIds = new ContentId[slots.Length], Targets = new int[StockRules.OriginalSlotCount],
                Displaced = new List<int>()
            };
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                plan.DishIds[i] = slot == null ? (ContentId)0 : slot.nDishID;
                if (slot == null) continue;
                string error = "现有菜品与已知18槽布局不一致，菜单保持原样。";
                if (slot.nDishCount < 0) throw new InvalidOperationException(error);
                if (slot.nDishID == (ContentId)0)
                {
                    if (slot.nDishCount != 0) throw new InvalidOperationException(error);
                    continue;
                }
                var dish = DishManager.GetDishConstData(slot.nDishID);
                if (dish == null) throw new InvalidOperationException("有已上架菜品的资料缺失，暂时无法恢复原版菜槽。");
                int category = DishFamilyManager.IsPremium(dish.nDishFamilyID) ? 2
                    : DishFamilyManager.IsDrink(dish.nDishFamilyID) ? 1 : 0;
                if (i / ExpandedPerCategory != category) throw new InvalidOperationException(error);
            }

            for (int category = 0; category < CategoryNames.Length; category++)
            {
                int originalStart = category * OriginalPerCategory;
                int expandedStart = category * ExpandedPerCategory;
                for (int j = 0; j < OriginalPerCategory; j++) plan.Targets[originalStart + j] = expandedStart + j;
                for (int i = expandedStart + OriginalPerCategory; i < expandedStart + ExpandedPerCategory; i++)
                {
                    if (plan.DishIds[i] == (ContentId)0) continue;
                    int free = originalStart;
                    while (free < originalStart + OriginalPerCategory
                        && (plan.DishIds[plan.Targets[free]] != (ContentId)0 || !menu.IsSlotUnlocked(free))) free++;
                    if (free == originalStart + OriginalPerCategory) plan.Displaced.Add(i);
                    else plan.Targets[free] = i;
                }
            }
            return plan;
        }

        internal static string Describe(Plan plan)
        {
            if (plan.Displaced.Count == 0)
                return "保留全部在售菜品和库存。现有菜品会按类别回到原版位置，扩展菜品可填入同类已解锁的空槽。";
            string impact = string.Join("、", CategoryNames.Select((name, category) => new {
                Name = name, Count = plan.Displaced.Count(i => i / ExpandedPerCategory == category)
            }).Where(x => x.Count > 0).Select(x => x.Count + "道" + x.Name).ToArray());
            var first = plan.Source[plan.Displaced[0]];
            var dish = DishManager.GetDishConstData(first.nDishID);
            string example = plan.Displaced.Count == 1 ? "「" + dish.szName + "」约 " + first.nDishCount.ToString("N0") + " 份"
                : impact + "，合计约 " + plan.Displaced.Sum(i => (long)plan.Source[i].nDishCount).ToString("N0") + " 份";
            return "原版空槽不足，" + example + "将从售卖菜单移出，清单会保存在存档目录供查询。\n\n原版菜槽中的菜品和库存会保留。确认后可直接补货。";
        }

        internal static string Apply(ModBehaviour owner, Plan approved)
        {
            var current = Preview(owner);
            if (!object.ReferenceEquals(current.Menu, approved.Menu) || !object.ReferenceEquals(current.Source, approved.Source)
                || current.SavePath != approved.SavePath || !current.DishIds.SequenceEqual(approved.DishIds)
                || !current.Targets.SequenceEqual(approved.Targets) || !current.Displaced.SequenceEqual(approved.Displaced))
                throw new InvalidOperationException("菜单或解锁状态已变化，请重新查看恢复预览。");

            // 复用原版数据模型，不改已学菜系。重设轮询起点使其重新按原版布局选菜。
            var foodCursor = AccessTools.Field(typeof(MenuData), "m_DishIndex");
            var drinkCursor = AccessTools.Field(typeof(MenuData), "m_nDrinkIndex");
            if (foodCursor == null || drinkCursor == null || foodCursor.FieldType != typeof(int) || drinkCursor.FieldType != typeof(int))
                throw new InvalidOperationException("当前游戏版本的菜单结构已变化，暂时无法恢复原版菜槽。");

            string record = null;
            if (current.Displaced.Count != 0)
            {
                string folder = Path.Combine(Path.GetDirectoryName(current.SavePath), "EmployeeCare", "菜槽恢复记录");
                Directory.CreateDirectory(folder);
                record = Path.Combine(folder, "移出菜品_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N") + ".json");
                var entries = current.Displaced.Select(i => new {
                    菜类 = CategoryNames[i / ExpandedPerCategory], 类内槽号 = i % ExpandedPerCategory + 1,
                    菜品编号 = current.Source[i].nDishID.ToString(),
                    菜名 = DishManager.GetDishConstData(current.Source[i].nDishID).szName,
                    剩余份数 = current.Source[i].nDishCount
                }).ToArray();
                File.WriteAllText(record, JsonConvert.SerializeObject(entries, Formatting.Indented));
            }

            var restored = current.Targets.Select(i => current.Source[i] ?? new MenuSlot()).ToArray();
            object oldFoodCursor = foodCursor.GetValue(current.Menu);
            object oldDrinkCursor = drinkCursor.GetValue(current.Menu);
            try
            {
                foodCursor.SetValue(current.Menu, 0);
                drinkCursor.SetValue(current.Menu, OriginalPerCategory);
                current.Menu.m_MenuSlots = restored;
            }
            catch
            {
                foodCursor.SetValue(current.Menu, oldFoodCursor);
                drinkCursor.SetValue(current.Menu, oldDrinkCursor);
                throw;
            }

            try { SellingMenuUI.RefreshAllSlot(); }
            catch (Exception ex) { Debug.LogWarning("[餐厅管家] 菜槽恢复后刷新售卖界面失败：" + ex); }
            // 让已打开的菜谱等订阅方也刷新，最多发送原版的9个有效下标。
            for (int i = 0; i < restored.Length; i++)
            {
                try { EventManager.Trigger("CHANGE_DISH", i); }
                catch (Exception ex) { Debug.LogWarning("[餐厅管家] 菜槽恢复后刷新失败：" + ex); }
            }

            string result = "已恢复原版9槽，可以补货。" + (record == null ? "" : "移出清单已保存到「菜槽恢复记录」。");
            const string saveWarning = "原版9槽已在游戏中恢复，但存档写入未确认，请正常保存后再退出。";
            owner.StockStatus = result;
            try
            {
                GameWorld.Active_SaveAll(delegate(bool ok) { if (!ok) owner.StockStatus = saveWarning; });
            }
            catch (Exception ex)
            {
                owner.StockStatus = saveWarning;
                Debug.LogWarning("[餐厅管家] 菜槽恢复后保存失败：" + ex);
            }
            return owner.StockStatus;
        }
    }
}
