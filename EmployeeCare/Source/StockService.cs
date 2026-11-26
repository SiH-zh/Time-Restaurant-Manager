using System;
using System.IO;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace EmployeeCare
{
    internal static class StockService
    {
        private const string ExpansionPatchOwner = "DishSlotCuisineExpansion";
        private static bool busy;

        internal static void CheckContext(ModBehaviour owner)
        {
            if (!owner.Ready() || !GameSession.IsOwner || GameWorld.IsBusinessSaveFrozen()
                || DecorateManager.IsInDecorateMode() || RestaurantExpandManager.IsInExpandPreview()
                || SubAreaExpandManager.IsInSubAreaPreview())
                throw new InvalidOperationException("请在自己餐厅的正常营业界面补充菜品。");
        }

        internal static bool HasExpansionPatch()
        {
            // 仅凭存档数组长度无法判断扩展是否仍负责解锁与界面刷新。
            var unlock = Harmony.GetPatchInfo(AccessTools.Method(typeof(MenuData), "IsSlotUnlocked"));
            if (unlock == null) return false;
            foreach (var patch in unlock.Prefixes)
                if (patch.owner == ExpansionPatchOwner) return true;
            return false;
        }

        private static MenuSlot[] GetSlots(MenuData menu)
        {
            var slots = menu == null ? null : menu.m_MenuSlots;
            if (slots == null) throw new InvalidOperationException("菜槽尚未加载，请稍后重试。");
            if (slots.Length == StockRules.OriginalSlotCount)
            {
                if (HasExpansionPatch())
                    throw new InvalidOperationException("「百味食光」已加载，但18槽菜单尚未就绪，请完全退出游戏后重开。");
                return slots;
            }
            if (slots.Length != StockRules.ExpandedSlotCount)
                throw new InvalidOperationException("无法识别当前 " + slots.Length + " 槽的菜品布局，未进行补充。");
            if (!HasExpansionPatch())
                throw new InvalidOperationException("存档仍使用18槽，但菜槽扩展未加载。要继续使用原版，请先选择「恢复原版9槽」。");
            var selling = SellingMenuUI.Instance;
            if (selling == null || selling.SlotBoxArray == null || selling.SlotBoxArray.Length != slots.Length)
                throw new InvalidOperationException("18槽售卖界面尚未就绪，请完全退出游戏后重开。");
            return slots;
        }

        private static StockLine[] Read(MenuData menu, MenuSlot[] slots, out string summary)
        {
            var lines = new List<StockLine>();
            int main = 0, side = 0, premium = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null || slot.nDishID == (ContentId)0 || !menu.IsSlotUnlocked(i)) continue;
                var dish = DishManager.GetDishConstData(slot.nDishID);
                if (dish == null) throw new InvalidOperationException("有已上架菜品的资料缺失，未进行补充。");
                if (DishFamilyManager.IsPremium(dish.nDishFamilyID)) premium++;
                else if (DishFamilyManager.IsDrink(dish.nDishFamilyID)) side++;
                else main++;
                lines.Add(new StockLine { Slot = i, Id = slot.nDishID.ToString(), Name = dish.szName, Count = slot.nDishCount, Cost = dish.nCost });
            }
            summary = main + "道主菜 + " + side + "道配餐 + " + premium + "道精品菜";
            return lines.ToArray();
        }

        internal static string Preview(ModBehaviour owner, int amount, out bool insufficient)
        {
            CheckContext(owner);
            var menu = Restaurant.m_MenuData;
            var slots = GetSlots(menu);
            string summary;
            var plan = StockRules.Plan(Read(menu, slots, out summary), amount, owner.Settings.ChargeDishCost, slots.Length);
            insufficient = Player.m_nGold < plan.Cost;
            return summary + "\n每道增加 " + amount.ToString("N0") + " 份，共 " + ((long)amount * plan.Lines.Length).ToString("N0")
                + " 份\n本次费用：" + plan.Cost.ToString("N0") + " 金币；当前余额：" + Player.m_nGold.ToString("N0")
                + (insufficient ? "（余额不足，无法补充）" : "");
        }

        internal static string Apply(ModBehaviour owner, int amount)
        {
            if (busy) throw new InvalidOperationException("正在补充，请稍后。");
            busy = true;
            try
            {
                CheckContext(owner);
                var menu = Restaurant.m_MenuData;
                var slots = GetSlots(menu);
                string summary;
                var plan = StockRules.Plan(Read(menu, slots, out summary), amount, owner.Settings.ChargeDishCost, slots.Length);
                StockRules.CheckBalance(plan, Player.m_nGold);
                long gold = Player.m_nGold;
                string root = Path.Combine(Path.GetDirectoryName(GameWorld.GetCurrentSavePath()), "EmployeeCare", "Backups");
                Directory.CreateDirectory(root);
                string backup = Path.Combine(root, "菜品补充前_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(backup, JsonConvert.SerializeObject(new {
                    存档 = GameWorld.GetCurrentSavePath(), 原金币 = gold, 每道增加 = amount,
                    本次费用 = plan.Cost, 菜槽原记录 = plan.Lines
                }, Formatting.Indented));
                // 备份写入后重新核对现场，失败则不扣款、不补货。
                CheckContext(owner);
                CheckMenu(menu, slots, plan.Lines);
                StockRules.CheckBalance(plan, Player.m_nGold);
                gold = Player.m_nGold;
                try
                {
                    if (plan.Cost > 0 && !Player.CostGold(plan.Cost))
                        throw new InvalidOperationException("金币扣款失败，未进行补充。");
                    // 扣款事件的监听方可能切换菜单，写入前再核对一次。
                    if (plan.Cost > 0)
                    {
                        CheckContext(owner);
                        CheckMenu(menu, slots, plan.Lines);
                    }
                    // 同一主线程内先完成全部数据，再通知界面，避免逐槽事件打断。
                    foreach (var line in plan.Lines) slots[line.Slot].nDishCount = line.Count + amount;
                }
                catch
                {
                    Player.m_nGold = gold;
                    try { EventManager.Trigger("PLAYER_GOLD_CHANGE", 0L); } catch { }
                    throw;
                }
                foreach (var line in plan.Lines)
                {
                    try { EventManager.Trigger("CHANGE_DISH", line.Slot); }
                    catch (Exception ex) { Debug.LogError("[餐厅管家] 菜品界面刷新失败：" + ex); }
                }
                string result = "已为" + summary + "各增加 " + amount.ToString("N0") + " 份，扣除 " + plan.Cost.ToString("N0") + " 金币。";
                try
                {
                    // 沿用游戏正常保存入口；它将玩家金币和餐厅库存一并排入保存队列。
                    GameWorld.Active_SaveAll(delegate(bool ok) {
                        if (!ok) { owner.StockStatus = "菜品已补充，但存档写入未确认，请正常保存游戏并保留备份。"; Debug.LogError("[餐厅管家] " + owner.StockStatus); }
                    });
                }
                catch (Exception ex) { result += " 保存发生异常，请正常保存游戏。"; Debug.LogError("[餐厅管家] " + ex); }
                Debug.Log("[餐厅管家] " + result + " 修改前记录：" + backup);
                return result;
            }
            finally { busy = false; }
        }

        private static void CheckMenu(MenuData menu, MenuSlot[] slots, StockLine[] lines)
        {
            if (!object.ReferenceEquals(menu, Restaurant.m_MenuData) || !object.ReferenceEquals(slots, GetSlots(menu)))
                throw new InvalidOperationException("餐厅或菜槽布局已变化，请重新操作。");
            foreach (var line in lines)
            {
                var slot = slots[line.Slot];
                if (slot == null || slot.nDishID.ToString() != line.Id || slot.nDishCount != line.Count || !menu.IsSlotUnlocked(line.Slot))
                    throw new InvalidOperationException("在售菜品已变化，请重新操作。");
            }
        }
    }
}
