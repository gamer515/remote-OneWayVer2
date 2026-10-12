using System;

public static class BDPatternFactory
{
    public static IBattlePattern Create(int id)
    {
        switch (id)
        {
            case 1: return new BDPattern01();
            case 2: return new BDPattern02();
            case 3: return new BDPattern03();
            case 4: return new BDPattern04();
            case 5: return new BDPattern05();
            case 6: return new BDPattern06();
            case 7: return new BDPattern07();
            case 8: return new BDPattern08();
            case 9: return new BDPattern09();
            case 10: return new BDPattern10();

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(id), id, "등록되지 않은 BD 패턴입니다.");
        }
    }
}