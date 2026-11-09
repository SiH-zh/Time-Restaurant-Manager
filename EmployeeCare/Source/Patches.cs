using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using TMPro;

namespace EmployeeCare
{
    internal static class Patches
    {
        internal static readonly FieldInfo CurrentEmployee = AccessTools.Field(typeof(EmployeeRoomPanel), "m_CurrentEmployee");
        private static readonly MethodInfo Refresh = AccessTools.Method(typeof(EmployeeRoomPanel), "RefreshUI");

        internal static void CheckCompatibility()
        {
            if (CurrentEmployee == null || Refresh == null
                || AccessTools.Method(typeof(EmployeeRoomPanel), "OnFoodClicked", new[] { typeof(int) }) == null
                || AccessTools.Method(typeof(EmployeeRoomPanel), "SetFoodButtonsVisible") == null
                || AccessTools.Field(typeof(MenuGroupUI), "m_btnRestaurant") == null
                || AccessTools.Method(typeof(EmployeeItem), "GetMaxStaminaSeconds") == null
                || AccessTools.Method(typeof(EmployeeData), "AddSystemEmployee") == null
                || AccessTools.Method(typeof(EmployeeData), "AddFriendEmployee") == null)
                throw new InvalidOperationException("当前游戏版本的雇员接口不兼容，请更新餐厅管家。");
        }

        internal static bool FeedingBlocked(EmployeeItem employee)
        {
            var mod = ModBehaviour.Current;
            return employee.bIsSystemEmployee && !(mod != null && mod.Settings.GuardFood
                && employee.eType == NpcType.Guard && mod.Owns(employee));
        }

        internal static void RefreshOpenPanels()
        {
            foreach (var panel in Resources.FindObjectsOfTypeAll<EmployeeRoomPanel>())
                if (panel.gameObject.scene.IsValid() && panel.gameObject.activeInHierarchy) Refresh.Invoke(panel, null);
        }
    }

    [HarmonyPatch(typeof(EmployeeItem), "GetMaxStaminaSeconds")]
    internal static class MaxStaminaPatch
    {
        private static bool Prefix(EmployeeItem __instance, ref long __result)
        {
            var mod = ModBehaviour.Current;
            if (mod == null || !mod.Settings.CustomEnabled || !mod.Owns(__instance)) return true;
            __result = mod.Settings.DurationSeconds;
            return false;
        }
    }

    [HarmonyPatch(typeof(EmployeeData), "AddSystemEmployee")]
    internal static class SystemHirePatch
    {
        private static void Postfix(EmployeeItem __result)
        { var mod = ModBehaviour.Current; if (mod != null) mod.ApplyNew(__result); }
    }

    [HarmonyPatch(typeof(EmployeeData), "AddFriendEmployee")]
    internal static class FriendHirePatch
    {
        private static void Postfix(EmployeeItem __result)
        { var mod = ModBehaviour.Current; if (mod != null) mod.ApplyNew(__result); }
    }

    [HarmonyPatch(typeof(EmployeeRoomPanel), "SetFoodButtonsVisible")]
    internal static class GuardFoodVisibilityPatch
    {
        private static void Prefix(EmployeeRoomPanel __instance, ref bool __0)
        {
            var mod = ModBehaviour.Current;
            var employee = Patches.CurrentEmployee.GetValue(__instance) as EmployeeItem;
            if (mod != null && employee != null && employee.eType == NpcType.Guard && mod.Owns(employee))
                __0 = mod.Settings.GuardFood;
        }
    }

    [HarmonyPatch(typeof(EmployeeRoomPanel), "RegisterFeedTips")]
    internal static class FoodTipsPatch
    {
        private static void Postfix(EmployeeRoomPanel __instance, long __0)
        {
            var mod = ModBehaviour.Current;
            var employee = Patches.CurrentEmployee.GetValue(__instance) as EmployeeItem;
            if (mod == null || !mod.Owns(employee) || Patches.FeedingBlocked(employee)) return;
            var getButton = AccessTools.Method(typeof(EmployeeRoomPanel), "GetFoodButton");
            var getCost = AccessTools.Method(typeof(EmployeeRoomPanel), "GetFoodCost");
            for (int i = 0; i < 5; i++)
            {
                var button = getButton.Invoke(__instance, new object[] { i }) as UnityEngine.UI.Button;
                if (button == null) continue;
                var tip = button.GetComponent<TooltipTrigger>();
                if (tip == null) continue;
                int cost = (int)getCost.Invoke(null, new object[] { i, __0 });
                string caption = cost + "金币，可补充体力" + ((i + 1) * 20) + "%"
                    + (mod.Settings.SyncFoodHire ? "\n同步延长雇期：以实际恢复的体力时长为准" : "\n喂食续期已关闭，仅补充体力");
                tip.SetTextCallback(delegate(string a, string b) { return caption; }, "", false);
            }
        }
    }

    [HarmonyPatch(typeof(EmployeeRoomPanel), "UpdateSysWorkerTips")]
    internal static class GuardWorkerTipsPatch
    {
        private static void Postfix(EmployeeRoomPanel __instance, EmployeeItem __0)
        {
            var tips = AccessTools.Field(typeof(EmployeeRoomPanel), "m_ObjSysWorkerTxt").GetValue(__instance) as GameObject;
            if (tips == null) return;
            var layout = __instance.GetComponent<GuardDescriptionLayout>();
            if (layout != null) layout.Restore();
            var mod = ModBehaviour.Current;
            if (mod == null || !mod.Settings.GuardFood || __0 == null
                || __0.eType != NpcType.Guard || !mod.Owns(__0)) return;
            if (layout == null)
            {
                layout = __instance.gameObject.AddComponent<GuardDescriptionLayout>();
                layout.Initialize(tips);
            }
            layout.ShowAboveFood();
        }
    }

    // 原说明完整保留，移至姓名与体力条之间；关闭功能或停用模组时还原布局。
    internal sealed class GuardDescriptionLayout : MonoBehaviour
    {
        private RectTransform group;
        private Vector2 position;
        private TMP_Text[] labels;
        private Vector2[] sizes;
        private bool[] wrapping;

        internal void Initialize(GameObject tips)
        {
            group = tips.GetComponent<RectTransform>();
            position = group.anchoredPosition;
            labels = tips.GetComponentsInChildren<TMP_Text>(true);
            sizes = labels.Select(t => t.rectTransform.sizeDelta).ToArray();
            wrapping = labels.Select(t => t.enableWordWrapping).ToArray();
        }

        internal void ShowAboveFood()
        {
            group.anchoredPosition = position + new Vector2(0, 470);
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i].rectTransform.sizeDelta = new Vector2(sizes[i].x, Mathf.Max(sizes[i].y, 100));
                labels[i].enableWordWrapping = true;
            }
        }

        internal void Restore()
        {
            if (group == null || labels == null) return;
            group.anchoredPosition = position;
            for (int i = 0; i < labels.Length; i++)
                if (labels[i] != null)
                {
                    labels[i].rectTransform.sizeDelta = sizes[i];
                    labels[i].enableWordWrapping = wrapping[i];
                }
        }
    }

    [HarmonyPatch(typeof(EmployeeRoomPanel), "OnFoodClicked")]
    internal static class FoodPatch
    {
        private sealed class FeedState
        {
            internal EmployeeItem Employee;
            internal long OldStaminaEnd;
            internal long OldHireEnd;
            internal long BaseSeconds;
            internal long Now;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            var field = AccessTools.Field(typeof(EmployeeItem), "bIsSystemEmployee");
            var method = AccessTools.Method(typeof(Patches), "FeedingBlocked");
            var codes = instructions.ToList();
            foreach (var code in codes)
            {
                if (code.opcode == OpCodes.Ldfld && Equals(code.operand, field))
                {
                    // 保留全部原版价格、扣款、食物比例、饱腹检查和动画，仅放行自己的保安。
                    code.opcode = OpCodes.Call; code.operand = method; replaced++;
                }
            }
            if (replaced != 1) throw new InvalidOperationException("雇员喂食接口已变化，停止加载补丁，避免错误扣款。");
            return codes;
        }

        private static void Prefix(EmployeeRoomPanel __instance, out FeedState __state)
        {
            __state = null;
            var mod = ModBehaviour.Current;
            var e = Patches.CurrentEmployee.GetValue(__instance) as EmployeeItem;
            if (mod == null || !mod.Settings.SyncFoodHire || !mod.Owns(e) || Patches.FeedingBlocked(e)) return;
            long now = TimeHelper.GetNowTimeStamp();
            long duration = mod.BaseHireSeconds(e);
            __state = new FeedState { Employee = e, OldStaminaEnd = e.nStaminaExpireTime,
                OldHireEnd = checked(e.nStartHireTime + duration), BaseSeconds = duration, Now = now };
        }

        private static void Postfix(EmployeeRoomPanel __instance, FeedState __state)
        {
            var mod = ModBehaviour.Current;
            if (__state == null || mod == null || !mod.Owns(__state.Employee)) return;
            long restored = DurationRules.RestoredSeconds(__state.OldStaminaEnd, __state.Employee.nStaminaExpireTime, __state.Now);
            if (restored <= 0) return;
            long end = DurationRules.ExtendedHireEnd(__state.OldHireEnd, restored, __state.Now);
            __state.Employee.nStartHireTime = DurationRules.HireStartForEnd(end, __state.BaseSeconds);
            Restaurant.SaveImmediate();
            // 原版先刷新文字再返回；续期后补刷一次，让雇期立即显示新值。
            AccessTools.Method(typeof(EmployeeRoomPanel), "UpdateHireTimeDisplay").Invoke(__instance, null);
            Debug.Log("[餐厅管家] 喂食成功，同步延长雇期 " + restored + " 秒。");
        }
    }
}
