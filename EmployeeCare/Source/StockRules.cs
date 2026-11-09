using System;
using System.Collections.Generic;
using System.Globalization;

namespace EmployeeCare
{
    public sealed class StockBalanceException : InvalidOperationException
    {
        public readonly long Required;
        public readonly long Balance;
        public StockBalanceException(long required, long balance)
            : base("金币余额不足：需要 " + required.ToString("N0") + " 金币，当前只有 " + balance.ToString("N0") + "。未扣金币，也未补充菜品。")
        { Required = required; Balance = balance; }
    }
    public sealed class StockInputState
    {
        public string Text = "1000";
        public bool Faulted;
        public bool Accept(string value)
        {
            if (value == "") { Text = ""; Faulted = false; return false; }
            if (!StockRules.DigitsOnly(value) || value.Length > 6) { Faulted = true; return false; }
            Text = value;
            int amount;
            return !Faulted && StockRules.TryAmount(value, out amount);
        }
        public void SetShortcut(int amount) { Text = amount.ToString(CultureInfo.InvariantCulture); Faulted = false; }
    }

    public sealed class StockLine
    {
        public int Slot;
        public string Id;
        public string Name;
        public int Count;
        public long Cost;
    }

    public sealed class StockPlan
    {
        public StockLine[] Lines;
        public int Amount;
        public long Cost;
    }

    public static class StockRules
    {
        public const int MaxAddition = 100000;
        public const int MaxStock = 1000000;

        public static bool DigitsOnly(string text)
        {
            if (text == null) return false;
            foreach (char c in text) if (c < '0' || c > '9') return false;
            return true;
        }

        public static bool TryAmount(string text, out int amount)
        {
            amount = 0;
            return text != null && text.Length > 0 && text.Length <= 6 && DigitsOnly(text)
                && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out amount)
                && amount > 0 && amount <= MaxAddition;
        }

        public static StockPlan Plan(StockLine[] lines, int amount, bool charge)
        {
            if (amount < 1 || amount > MaxAddition) throw new InvalidOperationException("每道增加份数须为1至100000的整数。");
            if (lines == null || lines.Length == 0) throw new InvalidOperationException("当前没有已上架的菜品，请先上架菜品。");
            if (lines.Length > 9) throw new InvalidOperationException("菜槽结构与本版不兼容，未进行补充。");
            long total = 0;
            var slots = new HashSet<int>();
            foreach (var line in lines)
            {
                if (line == null || line.Slot < 0 || line.Slot >= 9 || !slots.Add(line.Slot))
                    throw new InvalidOperationException("菜槽数据异常，未进行补充。");
                if (line.Count < 0 || (long)line.Count + amount > MaxStock)
                    throw new InvalidOperationException((line.Name ?? "菜品") + "补充后会超过100万份，整次补充已取消。");
                if (line.Cost < 0) throw new InvalidOperationException("菜品成本异常，未进行补充。");
                if (charge) total = checked(total + checked(line.Cost * amount));
            }
            return new StockPlan { Lines = lines, Amount = amount, Cost = total };
        }

        public static void CheckBalance(StockPlan plan, long balance)
        {
            if (balance < plan.Cost) throw new StockBalanceException(plan.Cost, balance);
        }
    }
}
