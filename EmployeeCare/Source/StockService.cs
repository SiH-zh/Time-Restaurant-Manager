using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace EmployeeCare
{
    internal static class StockService
    {
        private static bool busy;

        private static void CheckContext(ModBehaviour owner)
        {
            if (!owner.Ready() || !GameSession.IsOwner || GameWorld.IsBusinessSaveFrozen()
                || DecorateManager.IsInDecorateMode() || RestaurantExpandManager.IsInExpandPreview()
                || SubAreaExpandManager.IsInSubAreaPreview())
                throw new InvalidOperationException("请在自己餐厅的正常营业界面补充菜品。");
        }

        internal static StockLine[] Read(ModBehaviour owner, out string summary)
        {
            CheckContext(owner);
            var menu = Restaurant.m_MenuData;
            if (menu == null || menu.m_MenuSlots == null || menu.m_MenuSlots.Length != 9)
                throw new InvalidOperationException("菜槽结构与本版不兼容，未进行补充。");
            var lines = new List<StockLine>();
            int main = 0, side = 0, premium = 0;
            for (int i = 0; i < menu.m_MenuSlots.Length; i++)
            {
                var slot = menu.m_MenuSlots[i];
                if (!menu.IsSlotUnlocked(i) || slot == null || slot.nDishID == (ContentId)0) continue;
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
            string summary;
            var plan = StockRules.Plan(Read(owner, out summary), amount, owner.Settings.ChargeDishCost);
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
                string summary;
                var plan = StockRules.Plan(Read(owner, out summary), amount, owner.Settings.ChargeDishCost);
                StockRules.CheckBalance(plan, Player.m_nGold);
                var menu = Restaurant.m_MenuData;
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
                if (!System.Object.ReferenceEquals(menu, Restaurant.m_MenuData)) throw new InvalidOperationException("餐厅已切换，请重新操作。");
                foreach (var line in plan.Lines)
                {
                    var slot = menu.m_MenuSlots[line.Slot];
                    if (!menu.IsSlotUnlocked(line.Slot) || slot == null || slot.nDishID.ToString() != line.Id || slot.nDishCount != line.Count)
                        throw new InvalidOperationException("在售菜品已变化，请重新操作。");
                }
                StockRules.CheckBalance(plan, Player.m_nGold);
                gold = Player.m_nGold;
                try
                {
                    if (plan.Cost > 0 && !Player.CostGold(plan.Cost))
                        throw new InvalidOperationException("金币扣款失败，未进行补充。");
                    // 同一主线程内先完成全部数据，再通知界面，避免逐槽事件打断。
                    foreach (var line in plan.Lines) menu.m_MenuSlots[line.Slot].nDishCount = checked(line.Count + amount);
                }
                catch
                {
                    foreach (var line in plan.Lines) menu.m_MenuSlots[line.Slot].nDishCount = line.Count;
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
    }
}
