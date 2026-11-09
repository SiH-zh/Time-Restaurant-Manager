using System;
using System.Globalization;

namespace EmployeeCare
{
    [Serializable]
    public sealed class CareSettings
    {
        public int Version = 1;
        public bool CustomEnabled;
        public bool SyncFoodHire;
        public bool GuardFood;
        public bool ChargeDishCost;
        public long DurationSeconds = 30L * 86400;
        public bool DisplayDays = true;
    }

    public static class DurationRules
    {
        public const long Minimum = 60;
        public const long Maximum = 3650L * 86400;

        public static bool TryParse(string text, bool days, out long seconds)
        {
            seconds = 0;
            decimal amount;
            if (text == null || text.Length > 24 || !decimal.TryParse(text.Trim(),
                NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount) || amount <= 0) return false;
            decimal raw;
            try { raw = amount * (days ? 86400m : 3600m); }
            catch (OverflowException) { return false; }
            if (raw < Minimum || raw > Maximum) return false;
            seconds = (long)decimal.Round(raw, 0, MidpointRounding.AwayFromZero);
            return true;
        }

        public static string Format(long seconds, bool days)
        {
            return (seconds / (days ? 86400m : 3600m)).ToString("0.######", CultureInfo.InvariantCulture);
        }

        public static long HireStartForEnd(long end, long originalDuration)
        {
            if (end <= 0 || originalDuration <= 0) throw new ArgumentOutOfRangeException();
            return checked(end - originalDuration);
        }

        public static long RestoredSeconds(long oldStaminaEnd, long newStaminaEnd, long now)
        {
            return Math.Max(0L, newStaminaEnd - Math.Max(oldStaminaEnd, now));
        }

        public static long ExtendedHireEnd(long oldHireEnd, long restored, long now)
        {
            return checked(Math.Max(oldHireEnd, now) + Math.Max(0L, restored));
        }

        public static bool Eligible(bool own, bool super, int job)
        {
            return own && !super && (job == 2 || job == 3 || job == 4);
        }

        public static void Normalize(CareSettings settings)
        {
            if (settings.DurationSeconds < Minimum || settings.DurationSeconds > Maximum)
            { settings.DurationSeconds = 30L * 86400; settings.CustomEnabled = false; }
        }
    }
}
