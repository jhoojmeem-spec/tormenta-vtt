using System;
using TormentaVTT.Models;

namespace TormentaVTT.Tests
{
    public static class QuickChecks
    {
        public static void Run()
        {
            Console.WriteLine("QuickChecks: starting...");
            var sheet = new CharacterSheet();
            sheet.HP = 50;
            sheet.SetResistance("fire", 3);
            sheet.SetVulnerability("ice", 2);

            var d1 = sheet.GetDamageAfterTypeModifiers(10, "fire");
            var d2 = sheet.GetDamageAfterTypeModifiers(10, "ice");
            var d3 = sheet.GetDamageAfterTypeModifiers(5, "bludgeoning");

            Console.WriteLine($"fire 10 -> {d1}");
            Console.WriteLine($"ice 10 -> {d2}");
            Console.WriteLine($"bludgeoning 5 -> {d3}");

            if (d1 != 7) throw new Exception("fire calculation failed");
            if (d2 != 12) throw new Exception("ice calculation failed");
            if (d3 != 5) throw new Exception("bludgeoning calculation failed");

            Console.WriteLine("QuickChecks: all tests passed.");
        }
    }
}
