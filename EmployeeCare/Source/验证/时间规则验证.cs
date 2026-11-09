using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EmployeeCare;
using HarmonyLib;

public static class CareTests
{
    private static int passed;
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception("验证失败：" + name); passed++; Console.WriteLine("通过：" + name); }
    public static void Run()
    {
        long seconds;
        Check(DurationRules.TryParse("30", true, out seconds) && seconds == 2592000, "30天单位换算");
        Check(DurationRules.TryParse("1.5", false, out seconds) && seconds == 5400, "支持小数小时");
        Check(DurationRules.TryParse("0.5", true, out seconds) && seconds == 43200, "支持半天");
        foreach (string invalid in new[] { "", "0", "-1", "NaN", "1e20", "1,000", "0.000001", "87601", new string('9', 24) })
            Check(!DurationRules.TryParse(invalid, false, out seconds), "拒绝非法小时值：" + invalid);
        Check(DurationRules.TryParse("3650", true, out seconds) && seconds == DurationRules.Maximum, "最大时长边界");
        Check(!DurationRules.TryParse("3650.1", true, out seconds), "拒绝超出最大天数");
        Check(DurationRules.Format(86400, true) == "1" && DurationRules.Format(86400, false) == "24", "天小时切换不改变时长");
        long now = 1794139200, end = now + 2592000;
        foreach (long duration in new[] { 21600L, 43200L, 57600L, 86400L })
            Check(DurationRules.HireStartForEnd(end, duration) + duration - now == 2592000, "不同原雇期均得到相同剩余30天：" + duration);
        Check(DurationRules.RestoredSeconds(now + 900, now + 1000, now) == 100, "体力触顶时只统计实际恢复的100秒");
        Check(DurationRules.RestoredSeconds(now - 500, now + 200, now) == 200, "体力耗尽后不把已经过期的时间算作恢复");
        Check(DurationRules.RestoredSeconds(now + 1000, now + 1000, now) == 0, "满体力或扣款失败不续期");
        Check(DurationRules.RestoredSeconds(now + 1000, now + 900, now) == 0, "没有恢复不倒扣雇期");
        Check(DurationRules.ExtendedHireEnd(now + 500, 200, now) == now + 700, "有效雇期延长实际恢复时长");
        Check(DurationRules.ExtendedHireEnd(now - 500, 200, now) == now + 200, "已到期雇期从当前时刻恢复");
        Check(!DurationRules.Eligible(false, false, 3), "排除好友餐厅人员");
        Check(!DurationRules.Eligible(true, true, 2), "排除永久超级员工");
        Check(DurationRules.Eligible(true, false, 4), "保安可以使用时长规则");
        var settings = new CareSettings { DurationSeconds = long.MaxValue, CustomEnabled = true };
        DurationRules.Normalize(settings);
        Check(!settings.CustomEnabled && settings.DurationSeconds == 2592000, "损坏设置关闭自定义并恢复安全值");

        var assembly = typeof(EmployeeCare.ModBehaviour).Assembly;
        var patches = assembly.GetType("EmployeeCare.Patches");
        patches.GetMethod("CheckCompatibility", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        Check(true, "实际游戏接口兼容性");
        var field = AccessTools.Field(typeof(EmployeeItem), "bIsSystemEmployee");
        var transpile = assembly.GetType("EmployeeCare.FoodPatch").GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic);
        var code = new CodeInstruction(OpCodes.Ldfld, field);
        var generator = new DynamicMethod("标签验证", typeof(void), Type.EmptyTypes).GetILGenerator();
        var label = generator.DefineLabel(); code.labels.Add(label);
        var result = ((IEnumerable<CodeInstruction>)transpile.Invoke(null, new object[] { new[] { code } })).ToList();
        Check(result.Count == 1 && result[0].opcode == OpCodes.Call && result[0].labels.Contains(label), "喂食补丁仅替换保安限制且保留跳转标签");
        foreach (int count in new[] { 0, 2 })
        {
            bool rejected = false;
            try { transpile.Invoke(null, new object[] { Enumerable.Range(0, count).Select(i => new CodeInstruction(OpCodes.Ldfld, field)).ToArray() }); }
            catch (TargetInvocationException ex) { rejected = ex.InnerException is InvalidOperationException; }
            Check(rejected, "接口结构改变时拒绝打补丁：匹配数" + count);
        }
        var system = new EmployeeItem { bIsSystemEmployee = true, eType = NpcType.Guard };
        Check((bool)patches.GetMethod("FeedingBlocked", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { system }), "模组未激活时保安保持原规则");
        Console.WriteLine("员工规则全部通过：" + passed + "项。真实界面的补丁安装需在游戏运行时验证。");
        StockTests.Run();
    }

    // 只能在已初始化的游戏运行时调用；普通系统运行时不提供游戏引擎内部接口。
    public static void VerifyInGameRuntime()
    {
        var assembly = typeof(EmployeeCare.ModBehaviour).Assembly;
        var harmony = new Harmony("EmployeeCare.IsolatedVerification");
        try
        {
            harmony.PatchAll(assembly);
            Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(EmployeeRoomPanel), "OnFoodClicked")).Owners.Contains(harmony.Id), "实际游戏喂食方法的补丁能够完整安装");
            Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(EmployeeRoomPanel), "SetFoodButtonsVisible")).Owners.Contains(harmony.Id), "保安食物显示补丁参数绑定正确");
            Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(EmployeeData), "AddFriendEmployee")).Owners.Contains(harmony.Id), "新雇员补丁参数绑定正确");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(EmployeeRoomPanel), "OnFoodClicked")) == null
            || !Harmony.GetPatchInfo(AccessTools.Method(typeof(EmployeeRoomPanel), "OnFoodClicked")).Owners.Contains(harmony.Id), "取消补丁能够清理挂接");
        Console.WriteLine("全部通过：" + passed + "项。");
    }
}
