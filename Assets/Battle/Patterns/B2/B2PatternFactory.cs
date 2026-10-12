using System;

public static class B2PatternFactory
{
    public static IBattlePattern Create(int id)
    {
        switch (id)
        {
            case 1:
                return new B2Pattern01();

            // 두 번째 패턴을 만든 뒤 추가:
            case 2:
                return new B2Pattern02();

            case 3:
                return new B2Pattern03();

            case 4:
                return new B2Pattern04();

            case 5:
                return new B2Pattern05();

            case 6:
                return new B2Pattern06();

            case 7:
                return new B2Pattern07();

            case 8:
                return new B2Pattern08();

            case 9:
                return new B2Pattern09();

            case 10:
                return new B2Pattern10();

            case 11:
                return new B2Pattern11();

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(id),
                    id,
                    "등록되지 않은 B2 패턴입니다.");
        }
    }
}