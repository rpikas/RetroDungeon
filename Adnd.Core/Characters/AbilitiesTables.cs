namespace Adnd.Core.Characters;
//souurce https://waynesbooks.games/2019/07/26/deities-and-demigods-the-extended-ability-scores-tables/

public static class AbilitiesTables
{
    // -----------------------------
    //  STRENGTH (AD&D 1e)
    // -----------------------------
    public static int StrengthDamageModifier(int strength, int? exceptional = null)
    {
        // Exceptional Strength handling (18/xx)
        if (strength == 18 && exceptional.HasValue)
        {
            return exceptional.Value switch
            {
                <= 50 => 3,
                <= 75 => 3,
                <= 90 => 4,
                <= 99 => 5,
                _ => 6 // 18/00
            };
        }

        // Normal Strength (AD&D 1e)
        return strength switch
        {
            <= 5 => -1,
            <= 12 => 0,
            <= 15 => 0,
            16 => 1,
            17 => 1,
            18 => 2,
            19 => 7,
            20 => 8,
            21 => 9,
            22 => 10,
            23 => 11,
            24 => 12,
            _ => 14 // 25
        };
    }

    public static int StrengthTHModifier(int strength, int? exceptional = null)
    {
        // Exceptional Strength handling (18/xx)
        if (strength == 18 && exceptional.HasValue)
        {
            return exceptional.Value switch
            {
                <= 50 => 1,
                <= 75 => 2,   // FIXED: 18/51–75 should give +2
                <= 90 => 2,
                <= 99 => 2,
                _ => 3        // 18/00
            };
        }

        // Normal Strength (AD&D 1e)
        return strength switch
        {
            1 => -5,
            2 => -4,
            3 => -3,
            <= 5 => -2,
            <= 7 => -1,
            <= 12 => 0,
            <= 16 => 0,
            17 => 1,
            18 => 1,
            19 => 3,
            20 => 3,
            21 => 4,
            22 => 4,
            23 => 5,
            24 => 6,
            _ => 7 // 25
        };
    }


    public static int StrengthWeightAllowanceModifier(int strength, int? exceptional = null)
    {
        if (strength == 18 && exceptional.HasValue)
        {
            return exceptional.Value switch
            {
                <= 50 => 1000,
                <= 75 => 1250,
                <= 90 => 1500,
                <= 99 => 2000,
                _ => 3000 // 18/00
            };
        }

        return strength switch
        {
            <= 3 => -350,
            <= 5 => -250,
            <= 7 => -150,
            <= 13 => 100,
            <= 15 => 200,
            <= 16 => 350,
            <= 17 => 500,
            <= 18 => 750,
            <= 19 => 4500,
            <= 20 => 5000,
            <= 21 => 6000,
            <= 22 => 7500,
            <= 23 => 9000,
            <= 24 => 12000,
            _ => 15000//
        };
    }

    public static int StrengthOpenDoors(int strength, int? exceptional = null)
    {
        // Exceptional Strength (18/xx)
        if (strength == 18 && exceptional.HasValue)
        {
            return exceptional.Value switch
            {
                <= 50 => 7,
                <= 75 => 8,
                <= 90 => 9,
                <= 99 => 10,
                _ => 11 // 18/00
            };
        }

        return strength switch
        {
            <= 3 => 1,
            <= 5 => 1,
            <= 8 => 2,
            <= 13 => 2,
            <= 15 => 3,
            <= 16 => 3,
            <= 17 => 3,
            <= 18 => 4,
            <= 19 => 5,//TODO: update numbers for according to D&D more exakt for 19-25
            <= 20 => 5,
            <= 21 => 6,
            <= 22 => 6,
            <= 23 => 6,
            <= 24 => 6,
            _ => 6//25
        };
    }

    public static int StrengthBendBars(int strength, int? exceptional = null)
    {
        // Exceptional Strength (18/xx)
        if (strength == 18 && exceptional.HasValue)
        {
            return exceptional.Value switch
            {
                <= 50 => 20,
                <= 75 => 25,
                <= 90 => 30,
                <= 99 => 35,
                _ => 40 // 18/00
            };
        }

        return strength switch
        {//TODO add 18/xx exceptional strength handling

            <= 8 => 1,
            <= 10 => 2,
            <= 12 => 4,
            <= 14 => 7,
            <= 16 => 10,
            <= 17 => 13,
            <= 18 => 16,
            <= 19 => 50,
            <= 20 => 60,
            <= 21 => 70,
            <= 22 => 80,
            <= 23 => 90,
            <= 24 => 100,
            _ => 100//25
        };
    }
    /*
    public static int StrengthOpenDoor(int strength)
    {
        return strength switch
        {//TODO add 18/xx exceptional strength handling
            <= 3 => -350,
            <= 5 => -250,
            <= 7 => -150,
            <= 13 => 100,
            <= 15 => 200,
            <= 16 => 350,
            <= 17 => 500,
            <= 18 => 750,
            <= 19 => 4500,
            <= 20 => 5000,
            <= 21 => 6000,
            <= 22 => 7500,
            <= 23 => 9000,
            <= 24 => 12000,
            _ => 15000//25
        };
    }*/
    public static int StrengthOpenDoors(int strength, int exceptional = 0)
    {
        // Exceptional Strength (18/xx)
        if (strength == 18 && exceptional > 0)
        {
            return exceptional switch
            {
                <= 50 => 7,
                <= 75 => 8,
                <= 90 => 9,
                <= 99 => 10,
                _ => 11 // 18/00
            };
        }

        // Normal Strength
        return strength switch
        {
            <= 2 => 1,
            <= 5 => 2,
            <= 8 => 2,
            <= 12 => 3,
            <= 15 => 4,
            16 => 5,
            17 => 6,
            18 => 6,
            19 => 12,
            20 => 13,
            21 => 14,
            22 => 15,
            23 => 16,
            24 => 17,
            _ => 18 // 25
        };
    }


    // -----------------------------
    //  DEXTERITY (AD&D 1e)
    // -----------------------------
    public static int DexterityReactionAdjustment(int dex)
    {
        return dex switch
        {
            <= 1 => -6,
            2 => -4,
            3 => -3,
            4 => -2,
            5 => -1,
            <= 14 => 0,
            15 => 1,
            16 => 2,
            17 => 3,
            18 => 4,
            19 => 4,
            20 => 4,
            21 => 5,
            22 => 5,
            23 => 6,
            24 => 6,
            _ => 7
        };
    }

    public static int DexterityAttackingAdjustment(int dex)
    {
        return dex switch
        {
            <= 1 => -4,
            2 => -3,
            3 => -2,
            4 => -1,
            5 => -1,
            <= 14 => 0,
            15 => 0,
            16 => 1,
            17 => 2,
            18 => 2,
            19 => 3,
            20 => 3,
            21 => 4,
            22 => 4,
            23 => 5,
            24 => 5,
            _ => 6
        };
    }

    public static int DexterityACModifier(int dex)
    {
        return dex switch
        {
            <= 1 => 5,
            2 => 4,
            3 => 3,
            4 => 2,
            5 => 1,
            <= 14 => 0,
            15 => -1,
            16 => -2,
            17 => -3,
            18 => -4,
            19 => -4,
            20 => -4,
            21 => -5,
            22 => -5,
            23 => -6,
            24 => -6,
            _ => -7
        };
    }
    public static int DexterityPickingPockets(int dex)
    {
        return dex switch
        {
            <= 9 => -15,
            10 => -10,
            11 => -5,
            12 => 0,
            13 => 0,
            14 => 0,
            15 => 0,
            16 => 0,
            17 => 5,
            18 => 10,
            19 => 15,
            20 => 20,
            21 => 25,
            22 => 30,
            23 => 35,
            24 => 40,
            _ => 45 // 25
        };
    }
    public static int DexterityOpenLocks(int dex) => dex switch
    {
        <= 9 => -10,
        10 => -5,
        11 => 0,
        12 => 0,
        13 => 0,
        14 => 0,
        15 => 5,
        16 => 10,
        17 => 15,
        18 => 15,
        19 => 20,
        20 => 25,
        21 => 30,
        22 => 35,
        23 => 40,
        24 => 45,
        _ => 50
    };
    public static int DexterityLocateRemoveTraps(int dex) => dex switch
    {
        <= 9 => -10,
        10 => -10,
        11 => -5,
        12 => 0,
        13 => 0,
        14 => 0,
        15 => 0,
        16 => 5,
        17 => 5,
        18 => 5,
        19 => 10,
        20 => 15,
        21 => 20,
        22 => 25,
        23 => 30,
        24 => 35,
        _ => 40
    };

    public static int DexterityMoveSilently(int dex) => dex switch
    {
        <= 9 => -20,
        10 => -15,
        11 => -10,
        12 => -5,
        13 => 0,
        14 => 0,
        15 => 0,
        16 => 5,
        17 => 10,
        18 => 10,
        19 => 12,
        20 => 15,
        21 => 18,
        22 => 20,
        23 => 23,
        24 => 25,
        _ => 30
    };

    public static int DexterityHideInShadows(int dex) => dex switch
    {
        <= 9 => -10,
        10 => -5,
        11 => 0,
        12 => 0,
        13 => 0,
        14 => 0,
        15 => 0,
        16 => 5,
        17 => 0,
        18 => 10,
        19 => 12,
        20 => 15,
        21 => 18,
        22 => 20,
        23 => 23,
        24 => 25,
        _ => 30
    };


    // -----------------------------
    //  CONSTITUTION (AD&D 1e)
    // -----------------------------
    public static int ConstitutionHpBonus(int con, bool isFighter)
    {
        if (!isFighter)
        {
            return con switch
            {
                <= 6 => -1,
                <= 14 => 0,
                15 => 1,
                16 => 2,
                17 => 2,
                18 => 2,
                _ => 2
            };
        }

        return con switch
        {
            <= 6 => -1,
            <= 14 => 0,
            15 => 1,
            16 => 2,
            17 => 3,
            18 => 4,
            _ => 4
        };
    }

    public static int ConstitutionSystemShock(int con)
    {
        return con switch
        {
            <= 1 => 25,
            2 => 30,
            3 => 35,
            4 => 40,
            5 => 45,
            6 => 50,
            7 => 55,
            8 => 60,
            9 => 65,
            10 => 70,
            11 => 75,
            12 => 80,
            13 => 85,
            14 => 88,
            15 => 90,
            16 => 95,
            17 => 97,
            18 => 99,
            _ => 99
        };
    }

    public static int ConstitutionResurrectionSurvival(int con)
    {
        return con switch
        {
            <= 1 => 30,
            2 => 35,
            3 => 40,
            4 => 45,
            5 => 50,
            6 => 55,
            7 => 60,
            8 => 65,
            9 => 70,
            10 => 75,
            11 => 80,
            12 => 85,
            13 => 90,
            14 => 92,
            15 => 94,
            16 => 96,
            17 => 98,
            18 => 100,
            _ => 100
        };
    }

    // -----------------------------
    //  INTELLIGENCE (AD&D 1e)
    // -----------------------------
    public static int IntelligenceMinimumSpells(int intel)
    {
        return intel switch
        {
            <= 8 => 0,
            9 => 4,
            10 => 5,
            11 => 5,
            12 => 6,
            13 => 7,
            14 => 7,
            15 => 8,
            16 => 8,
            17 => 9,
            18 => 9,
            19 => 10,
            20 => 11,
            21 => 12,
            22 => 14,
            23 => 16,
            24 => 18,
            _ => 20
        };
    }

    public static int IntelligenceMaximumSpells(int intel)
    {
        return intel switch
        {
            <= 8 => 0,
            9 => 6,
            10 => 7,
            11 => 7,
            12 => 7,
            13 => 9,
            14 => 9,
            15 => 11,
            16 => 11,
            17 => 14,
            18 => 18,
            19 => 50,
            20 => 60,
            21 => 70,
            22 => 80,
            23 => 90,
            24 => 100,
            _ => 100
        };
    }

    public static int IntelligenceChanceToLearn(int intel)
    {
        return intel switch
        {
            <= 8 => 20,
            9 => 35,
            10 => 40,
            11 => 45,
            12 => 50,
            13 => 55,
            14 => 60,
            15 => 65,
            16 => 70,
            17 => 75,
            18 => 85,
            19 => 95,
            20 => 96,
            21 => 97,
            22 => 98,
            23 => 99,
            24 => 100,
            _ => 100
        };
    }

    // -----------------------------
    //  WISDOM (AD&D 1e)
    // -----------------------------
    /*
    public readonly struct WisdomSpellBonus
    {
        public int L1 { get; }
        public int L2 { get; }
        public int L3 { get; }
        public int L4 { get; }

        public WisdomSpellBonus(int l1, int l2, int l3, int l4)
        {
            L1 = l1; L2 = l2; L3 = l3; L4 = l4;
        }

        public override string ToString()
            => $"1st:{L1}, 2nd:{L2}, 3rd:{L3}, 4th:{L4}";
    }
    */
    public readonly struct WisdomSpellBonus
    {
        public int L1 { get; }
        public int L2 { get; }
        public int L3 { get; }
        public int L4 { get; }

        public WisdomSpellBonus(int l1, int l2, int l3, int l4)
        {
            L1 = l1;
            L2 = l2;
            L3 = l3;
            L4 = l4;
        }

        public override string ToString()
            => $"{L1}/{L2}/{L3}/{L4}";
    }



    public static WisdomSpellBonus WisdomBonus(int wis)
    {
        return wis switch
        {
            < 13 => new(0, 0, 0, 0),
            13 => new(1, 0, 0, 0),
            14 => new(2, 0, 0, 0),
            15 => new(2, 1, 0, 0),
            16 => new(2, 2, 0, 0),
            17 => new(2, 2, 1, 0),
            18 => new(2, 2, 2, 0),
            19 => new(3, 2, 2, 1),
            20 => new(3, 3, 2, 2),
            21 => new(3, 3, 3, 2),
            22 => new(4, 3, 3, 2),
            23 => new(4, 4, 3, 3),
            24 => new(4, 4, 4, 3),
            _ => new(5, 4, 4, 4)
        };
    }

    public static int WisdomSpellFailure(int wis)
    {
        return wis switch
        {
            <= 3 => 50,
            4 => 45,
            5 => 40,
            6 => 35,
            7 => 30,
            8 => 25,
            9 => 20,
            10 => 15,
            11 => 10,
            12 => 5,
            _ => 0
        };
    }

    public static int WisdomMagicAttackAdjustment(int wis)
    {
        return wis switch
        {
            3 => -3,
            4 => -2,
            5 => -1,
            6 => -1,
            7 => -1,
            8 => 0,
            <= 14 => 0,
            15 => 1,
            16 => 2,
            17 => 3,
            18 => 4,
            19 => 4,
            20 => 4,
            21 => 5,
            22 => 5,
            23 => 6,
            24 => 6,
            _ => 7
        };
    }

    // -----------------------------
    //  CHARISMA (AD&D 1e)
    // -----------------------------
    public static int CharismaReactionBonus(int cha)
    {
        return cha switch
        {
            <= 3 => -25,
            4 => -20,
            5 => -15,
            6 => -10,
            7 => -5,
            8 => 0,
            <= 12 => 0,
            13 => 5,
            14 => 10,
            15 => 15,
            16 => 25,
            17 => 30,
            18 => 35,
            19 => 40,
            20 => 45,
            21 => 50,
            22 => 55,
            23 => 60,
            24 => 65,
            _ => 70 // 25
        };
    }

    /*
    //source: https://adndhintsntips.fandom.com/wiki/ADND-1E-OSRIC-CHARISMA-TABLE
    */
    public static int CharismaMaxHenchmen(int cha)
    {
        return cha switch
        {
            <= 3 => 1,
            4 => 1,
            5 => 2,
            6 => 2,
            7 => 3,
            8 => 3,
            <= 11 => 4,
            <= 13 => 5,
            14 => 6,
            15 => 7,
            16 => 8,
            17 => 10,
            18 => 15,
            19 => 20,
            20 => 25,
            21 => 30,
            22 => 35,
            23 => 40,
            24 => 45,
            _ => 50 // 25
        };
    }

    public static int CharismaLoyaltyBonus(int cha)
    {
        return cha switch
        {
            <= 3 => -30,
            4 => -25,
            5 => -20,
            6 => -15,
            7 => -10,
            8 => -5,
            <= 13 => 0,
            14 => 5,
            15 => 15,
            16 => 20,
            17 => 30,
            18 => 40,
            19 => 50,
            20 => 60,
            21 => 70,
            22 => 80,
            23 => 90,
            >= 24 => 100
        };
    }


}
