namespace OneWay.Battle3D
{
    /// <summary>Converts the four Decision abilities into independent combat values.</summary>
    public static class BattlePlayerStatRules
    {
        public static void Apply(BattlePlayerController player, Battle3DStartData data)
        {
            if (player == null || data == null) return;

            var health = player.GetComponent<BattlePlayerHealth>();
            if (health != null) health.maxHealth = Value(data.Trait1, 70, 100, 130, 150);
            player.moveSpeed = Value(data.Trait2, 7, 10, 13, 15);

            var energy = player.GetComponent<DebugPlayerSpecialCombo>();
            if (energy != null)
                energy.ConfigureDecisionEnergy(
                    Value(data.Trait3, 70, 100, 130, 150),
                    Value(data.Trait4, 3, 5, 7, 10),
                    Value(data.Trait4, 20, 25, 27, 30));
        }

        static int Value(int ability, int low, int normal, int high, int highest)
        {
            if (ability <= 5) return low; // Zero uses the same band as 1..5.
            if (ability <= 10) return normal;
            if (ability <= 15) return high;
            return highest;
        }
    }
}
