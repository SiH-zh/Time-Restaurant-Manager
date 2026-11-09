using System;
using System.Linq;
using EmployeeCare;
using Newtonsoft.Json;

public static class StockTests
{
    private static int passed;
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception("补货验证失败：" + name); passed++; Console.WriteLine("通过：" + name); }
    private static void Reject(Action action, string name)
    { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } catch (OverflowException) { rejected = true; } Check(rejected, name); }
    public static void Run()
    {
        int amount;
        var entry = new StockInputState();
        Check(entry.Accept("1"), "整数逐字输入可用");
        Check(!entry.Accept("1.") && entry.Text == "1", "拒绝小数点并保留原整数");
        Check(!entry.Accept("15") && entry.Faulted, "拒绝小数后输入下一位仍禁用操作，避免1.5误作15");
        entry.Accept(""); Check(entry.Accept("15") && !entry.Faulted, "清空后重新输入恢复可用");
        Check(!entry.Accept("1.5") && entry.Faulted, "一次粘贴小数也拒绝");
        entry.SetShortcut(100); Check(entry.Accept("100"), "主动选择快捷份数可清除输入错误");
        foreach (string text in new[] { "1", "10", "100", "1000", "10000", "100000" })
            Check(StockRules.TryAmount(text, out amount) && amount == int.Parse(text), "接受份数边界和快捷值：" + text);
        foreach (string text in new[] { "", "0", "-1", "+1", "1.5", "1e3", "１", "一", " 10", "10 ", "1,000", "100001", "2147483647", new string('9', 1000) })
            Check(!StockRules.TryAmount(text, out amount), "拒绝非法份数：" + (text.Length > 20 ? "超长数字" : text));
        var lines = new[] {
            new StockLine { Slot=0, Id="a", Name="主菜一", Count=9990, Cost=23 },
            new StockLine { Slot=1, Id="b", Name="主菜二", Count=0, Cost=31 },
            new StockLine { Slot=2, Id="c", Name="主菜三", Count=500, Cost=47 },
            new StockLine { Slot=3, Id="d", Name="配餐一", Count=20, Cost=2 },
            new StockLine { Slot=4, Id="e", Name="配餐二", Count=30, Cost=3 },
            new StockLine { Slot=5, Id="f", Name="配餐三", Count=40, Cost=5 },
            new StockLine { Slot=6, Id="g", Name="精品菜", Count=100, Cost=199 }
        };
        var plan = StockRules.Plan(lines, 1000, true);
        Check(plan.Cost == 310000 && plan.Lines.Length == 7 && plan.Amount == 1000, "三主菜三配餐一精品的真实单份成本累计");
        Check(StockRules.Plan(lines, 1000, false).Cost == 0, "关闭收费时费用为零");
        Check(plan.Lines.Any(x=>x.Count==0), "已上架售罄菜参与补充");
        Check(lines[0].Count == 9990 && lines[1].Count == 0, "预览不修改库存");
        Reject(delegate { StockRules.CheckBalance(plan, 309999); }, "余额差一金币也整次拒绝");
        Check(lines[0].Count == 9990 && lines[6].Count == 100, "余额不足后库存无变化");
        StockRules.CheckBalance(plan, 310000); Check(true, "余额恰好足够允许成交");
        StockRules.CheckBalance(StockRules.Plan(lines, 1000, false), 0); Check(true, "免费模式零余额可操作");
        Check(StockRules.Plan(new[] { new StockLine { Slot=8, Count=900000, Cost=0 } }, 100000, true).Cost == 0, "库存恰好达到一百万允许且零成本合法");
        Reject(delegate { StockRules.Plan(new[] { new StockLine { Slot=8, Count=900001 } }, 100000, false); }, "超过一百万拒绝而不截断");
        Reject(delegate { StockRules.Plan(new[] { new StockLine { Slot=0, Count=int.MaxValue } }, 1, false); }, "极端库存不会发生整数回绕");
        Reject(delegate { StockRules.Plan(new[] { new StockLine { Slot=0, Count=-1 } }, 1, false); }, "负库存拒绝");
        Reject(delegate { StockRules.Plan(new[] { new StockLine { Slot=0, Cost=-1 } }, 1, false); }, "负成本拒绝");
        Reject(delegate { StockRules.Plan(new[] { new StockLine { Slot=0, Cost=long.MaxValue } }, 10, true); }, "成本乘法溢出拒绝");
        Reject(delegate { StockRules.Plan(new[] { new StockLine { Slot=0, Cost=long.MaxValue }, new StockLine { Slot=1, Cost=1 } }, 1, true); }, "多菜成本求和溢出拒绝");
        Reject(delegate { StockRules.Plan(new StockLine[0], 10, false); }, "无在售菜时拒绝");
        Reject(delegate { StockRules.Plan(new[] { lines[0], lines[0] }, 10, false); }, "重复槽位拒绝");
        Check(StockRules.Plan(new[] { new StockLine { Slot=0, Id="same", Cost=1 }, new StockLine { Slot=1, Id="same", Cost=1 } }, 10, true).Cost == 20, "同菜不同槽位分别计费");
        var defaults = new CareSettings();
        Check(!defaults.CustomEnabled && !defaults.SyncFoodHire && !defaults.GuardFood && !defaults.ChargeDishCost, "初装全部开关关闭");
        var migrated = JsonConvert.DeserializeObject<CareSettings>("{\"CustomEnabled\":true,\"GuardFood\":true,\"SyncFoodHire\":true,\"DurationSeconds\":43200}");
        Check(migrated.CustomEnabled && migrated.GuardFood && migrated.SyncFoodHire && migrated.DurationSeconds==43200 && !migrated.ChargeDishCost, "旧配置升级保留原设置且新增收费默认关闭");
        Check(JsonConvert.DeserializeObject<CareSettings>(JsonConvert.SerializeObject(new CareSettings { ChargeDishCost=true })).ChargeDishCost, "收费设置可以持久保存");
        Console.WriteLine("菜品补充规则全部通过：" + passed + "项。");
    }
}
